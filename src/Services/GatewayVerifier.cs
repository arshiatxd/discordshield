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

        private readonly HttpClient _direct;
        private readonly HttpClient _proxied;

        public GatewayVerifier()
        {
            _direct = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2.5) })
            {
                Timeout = TimeSpan.FromSeconds(3.0)
            };

            _proxied = new HttpClient(new SocketsHttpHandler
            {
                Proxy = new WebProxy("socks5://127.0.0.1:1080"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(3.0)
            })
            {
                Timeout = TimeSpan.FromSeconds(3.5)
            };
        }

        public async Task<(bool Success, long LatencyMs, string Detail)> VerifyAsync(int maxAttempts, CancellationToken token)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                long latency = await MeasureAsync(_proxied, token);
                if (latency <= 0) latency = await MeasureAsync(_direct, token);
                if (latency <= 0) latency = await Task.Run(MeasureWithCurl, token);

                if (latency > 0) return (true, latency, "Gateway verified");

                if (attempt < maxAttempts) await Task.Delay(400, token);
            }

            return (false, 0, "Discord Gateway unreachable (network filtered)");
        }

        public async Task<long> PingAsync(CancellationToken token)
        {
            long latency = await MeasureAsync(_proxied, token);
            return latency > 0 ? latency : await Task.Run(MeasureWithCurl, token);
        }

        private static async Task<long> MeasureAsync(HttpClient client, CancellationToken token)
        {
            try
            {
                var timer = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Get, GatewayUrl);
                request.Headers.Add("User-Agent", UserAgent);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
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

        private static long MeasureWithCurl()
        {
            try
            {
                string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
                if (!File.Exists(curl)) return 0;

                var timer = Stopwatch.StartNew();
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = curl,
                    Arguments = $"-s -m 3 --socks5-hostname 127.0.0.1:1080 --resolve discord.com:443:162.159.135.232 {GatewayUrl}",
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                if (process == null) return 0;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3200);
                timer.Stop();
                return IsGatewayPayload(output) ? Math.Max(1, timer.ElapsedMilliseconds) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsGatewayPayload(string body) =>
            body.Contains("gateway.discord.gg", StringComparison.Ordinal) || body.Contains("\"url\"", StringComparison.Ordinal);

        public void Dispose()
        {
            _direct.Dispose();
            _proxied.Dispose();
        }
    }
}
