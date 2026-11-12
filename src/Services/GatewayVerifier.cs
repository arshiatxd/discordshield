using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTunnel.Services
{
    public sealed class GatewayVerifier : IDisposable
    {
        private const string GatewayUrl = "https://discord.com/api/v9/gateway";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DiscordShield/1.0.1";
        private static readonly string[] FallbackCloudflareEdges = { "162.159.135.232", "162.159.137.232" };

        private readonly HttpClient _proxied;

        public GatewayVerifier()
        {
            _proxied = new HttpClient(new SocketsHttpHandler
            {
                Proxy = new WebProxy("socks5://127.0.0.1:1080"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(2.5)
            })
            {
                Timeout = TimeSpan.FromSeconds(2.8)
            };
        }

        public async Task<(bool Success, long LatencyMs, string Detail)> VerifyAsync(int maxAttempts, CancellationToken token)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                long latency = await MeasureProxiedHttpAsync(token).ConfigureAwait(false);
                if (latency <= 0)
                {
                    latency = await Task.Run(() => MeasureWithCurl(token), token).ConfigureAwait(false);
                }

                if (latency > 0)
                {
                    return (true, latency, "Gateway verified");
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(300, token).ConfigureAwait(false);
                }
            }

            return (false, 0, "Discord Gateway unreachable (network filtered)");
        }

        public async Task<long> PingAsync(CancellationToken token)
        {
            long latency = await MeasureProxiedHttpAsync(token).ConfigureAwait(false);
            if (latency > 0) return latency;

            return await Task.Run(() => MeasureWithCurl(token), token).ConfigureAwait(false);
        }

        private async Task<long> MeasureProxiedHttpAsync(CancellationToken token)
        {
            try
            {
                if (await IsSystemDnsPoisonedAsync(token).ConfigureAwait(false)) return 0;

                var timer = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Get, GatewayUrl);
                request.Headers.Add("User-Agent", UserAgent);

                using var response = await _proxied.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;

                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                timer.Stop();
                return IsGatewayPayload(body) ? Math.Max(1, timer.ElapsedMilliseconds) : 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return 0;
            }
        }

        private static long MeasureWithCurl(CancellationToken token)
        {
            if (token.IsCancellationRequested) return 0;

            string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            if (!File.Exists(curl)) return 0;

            foreach (string edgeIp in FallbackCloudflareEdges)
            {
                if (token.IsCancellationRequested) return 0;
                long resolvedSocks = RunCurlProbe(curl, $"-s -m 2 --socks5-hostname 127.0.0.1:1080 --resolve discord.com:443:{edgeIp} {GatewayUrl}", token);
                if (resolvedSocks > 0) return resolvedSocks;
            }

            if (token.IsCancellationRequested) return 0;
            return RunCurlProbe(curl, $"-s -m 2 --socks5-hostname 127.0.0.1:1080 {GatewayUrl}", token);
        }

        private static long RunCurlProbe(string curlPath, string arguments, CancellationToken token)
        {
            try
            {
                var timer = Stopwatch.StartNew();
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = curlPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                if (process == null) return 0;

                using var registration = token.Register(() =>
                {
                    try { process.Kill(); }
                    catch { }
                });

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2400);
                timer.Stop();
                token.ThrowIfCancellationRequested();
                return IsGatewayPayload(output) ? Math.Max(1, timer.ElapsedMilliseconds) : 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return 0;
            }
        }

        private static async Task<bool> IsSystemDnsPoisonedAsync(CancellationToken token)
        {
            try
            {
                using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                dnsCts.CancelAfter(1000);

                var addresses = await Dns.GetHostAddressesAsync("discord.com", dnsCts.Token).ConfigureAwait(false);
                if (addresses.Length == 0) return true;

                foreach (var addr in addresses)
                {
                    if (IPAddress.IsLoopback(addr) || addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any)) return true;

                    byte[] bytes = addr.GetAddressBytes();
                    if (addr.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
                    {
                        if (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsGatewayPayload(string body) =>
            body.Contains("gateway.discord.gg", StringComparison.Ordinal) || body.Contains("\"url\"", StringComparison.Ordinal);

        public void Dispose() => _proxied.Dispose();
    }
}
