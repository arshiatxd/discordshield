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
        private const string UpdaterProbeUrl = "https://stable.dl2.discordapp.net/";
        private const string FallbackUpdaterIp = "34.126.226.51";
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
                ConnectTimeout = TimeSpan.FromSeconds(3.2)
            })
            {
                Timeout = TimeSpan.FromSeconds(4.2)
            };
        }

        public async Task<(bool Success, long LatencyMs, string Detail)> VerifyAsync(int maxAttempts, CancellationToken token)
        {
            string lastDetail = "Discord Gateway unreachable (network filtered)";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                var gatewayTask = MeasureGatewayAsync(token, attempt);
                var updaterTask = VerifyUpdaterReachableAsync(token);
                await Task.WhenAll(gatewayTask, updaterTask).ConfigureAwait(false);

                long latency = gatewayTask.Result;
                bool updaterOk = updaterTask.Result;

                if (latency > 0 && updaterOk)
                {
                    return (true, latency, "Gateway and Updater verified");
                }

                lastDetail = latency > 0
                    ? "Discord Updater CDN unreachable (stable.dl2.discordapp.net)"
                    : "Discord Gateway unreachable (network filtered)";

                if (attempt < maxAttempts)
                {
                    await Task.Delay(300, token).ConfigureAwait(false);
                }
            }

            return (false, 0, lastDetail);
        }

        public async Task<long> PingAsync(CancellationToken token) =>
            await MeasureGatewayAsync(token, 1).ConfigureAwait(false);

        private async Task<long> MeasureGatewayAsync(CancellationToken token, int attempt)
        {
            long latency = await MeasureProxiedHttpAsync(token).ConfigureAwait(false);
            if (latency > 0) return latency;

            return await Task.Run(() => MeasureWithCurl(token, attempt), token).ConfigureAwait(false);
        }

        private async Task<bool> VerifyUpdaterReachableAsync(CancellationToken token)
        {
            if (await VerifyUpdaterHttpAsync(token).ConfigureAwait(false))
            {
                return true;
            }

            return await Task.Run(() => VerifyUpdaterWithCurl(token), token).ConfigureAwait(false);
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

        private async Task<bool> VerifyUpdaterHttpAsync(CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, UpdaterProbeUrl);
                request.Headers.Add("User-Agent", UserAgent);

                using var response = await _proxied.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                return IsValidHttpStatus((int)response.StatusCode);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
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
            var (out1, ms1) = RunCurlCommand(curl, $"-s -m 3 --socks5 127.0.0.1:1080 --resolve discord.com:443:{edge1} {GatewayUrl}", token);
            if (ms1 > 0 && IsGatewayPayload(out1)) return ms1;

            token.ThrowIfCancellationRequested();

            string edge2 = FallbackCloudflareEdges[idx2];
            var (out2, ms2) = RunCurlCommand(curl, $"-s -m 3 --socks5 127.0.0.1:1080 --resolve discord.com:443:{edge2} {GatewayUrl}", token);
            if (ms2 > 0 && IsGatewayPayload(out2)) return ms2;

            token.ThrowIfCancellationRequested();

            var (outRelay, msRelay) = RunCurlCommand(curl, $"-s -m 3 --socks5-hostname 127.0.0.1:1080 {GatewayUrl}", token);
            return msRelay > 0 && IsGatewayPayload(outRelay) ? msRelay : 0;
        }

        private static bool VerifyUpdaterWithCurl(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            if (!File.Exists(curl)) return false;

            var (relayOut, relayMs) = RunCurlCommand(curl, $"-s -o NUL -w \"%{{http_code}}\" -m 3 --socks5-hostname 127.0.0.1:1080 {UpdaterProbeUrl}", token);
            if (relayMs > 0 && int.TryParse(relayOut.Trim(), out int relayCode) && IsValidHttpStatus(relayCode))
            {
                return true;
            }

            token.ThrowIfCancellationRequested();

            var (pinnedOut, pinnedMs) = RunCurlCommand(curl, $"-s -o NUL -w \"%{{http_code}}\" -m 3 --socks5 127.0.0.1:1080 --resolve stable.dl2.discordapp.net:443:{FallbackUpdaterIp} {UpdaterProbeUrl}", token);
            return pinnedMs > 0 && int.TryParse(pinnedOut.Trim(), out int pinnedCode) && IsValidHttpStatus(pinnedCode);
        }

        private static (string Output, long ElapsedMs) RunCurlCommand(string curlPath, string arguments, CancellationToken token)
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
                if (process == null) return (string.Empty, 0);

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
                return (output, Math.Max(1, timer.ElapsedMilliseconds));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return (string.Empty, 0);
            }
        }

        private static bool IsValidHttpStatus(int statusCode) =>
            statusCode is >= 200 and < 500 and not 451;

        private static bool IsGatewayPayload(string body) =>
            body.Contains("gateway.discord.gg", StringComparison.Ordinal) || body.Contains("\"url\"", StringComparison.Ordinal);

        public void Dispose() => _proxied.Dispose();
    }
}
