using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTunnel.Services
{
    public sealed class GatewayVerifier : IDisposable
    {
        private const string GatewayUrl = "https://discord.com/api/v9/gateway";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DiscordShield/1.0.1";
        private static readonly string[] FallbackCloudflareEdges =
        {
            "162.159.135.232",
            "162.159.137.232",
            "162.159.136.232",
            "162.159.138.232"
        };

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
                Timeout = TimeSpan.FromSeconds(3.0)
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
                    latency = await Task.Run(() => MeasureWithCurl(token, attempt), token).ConfigureAwait(false);
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

            return await Task.Run(() => MeasureWithCurl(token, 1), token).ConfigureAwait(false);
        }

        private async Task<long> MeasureProxiedHttpAsync(CancellationToken token)
        {
            try
            {
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

        private static long MeasureWithCurl(CancellationToken token, int attempt)
        {
            token.ThrowIfCancellationRequested();

            string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            if (!File.Exists(curl)) return 0;

            int safeAttempt = Math.Max(1, attempt);
            int idx1 = (safeAttempt - 1) * 2 % FallbackCloudflareEdges.Length;
            int idx2 = ((safeAttempt - 1) * 2 + 1) % FallbackCloudflareEdges.Length;

            string edge1 = FallbackCloudflareEdges[idx1];
            long resolved1 = RunCurlProbe(curl, $"-s -m 3 --socks5-hostname 127.0.0.1:1080 --resolve discord.com:443:{edge1} {GatewayUrl}", token);
            if (resolved1 > 0) return resolved1;

            token.ThrowIfCancellationRequested();

            string edge2 = FallbackCloudflareEdges[idx2];
            long resolved2 = RunCurlProbe(curl, $"-s -m 3 --socks5-hostname 127.0.0.1:1080 --resolve discord.com:443:{edge2} {GatewayUrl}", token);
            if (resolved2 > 0) return resolved2;

            token.ThrowIfCancellationRequested();
            return RunCurlProbe(curl, $"-s -m 3 --socks5-hostname 127.0.0.1:1080 {GatewayUrl}", token);
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
                process.WaitForExit(3400);
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch
                {
                }
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

        private static bool IsGatewayPayload(string body) =>
            body.Contains("gateway.discord.gg", StringComparison.Ordinal) || body.Contains("\"url\"", StringComparison.Ordinal);

        public void Dispose() => _proxied.Dispose();
    }
}
