using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DiscordTunnel.Models;

namespace DiscordTunnel.Services
{
    public static class RegionCatalog
    {
        public const string AutoCode = "AUTO";
        public const int DefaultIndex = 0;

        private const string YandexDnsArgs = "--dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253";
        private const string OpenDnsArgs = "--dns-addr 208.67.222.222 --dns-port 5353";

        private static readonly HttpClient GeoClient = new() { Timeout = TimeSpan.FromSeconds(1.5) };

        public static IReadOnlyList<RegionOption> All { get; } = new List<RegionOption>
        {
            new()
            {
                Code = AutoCode,
                Name = "Auto-Detect",
                ShortName = "Auto-Detect",
                Description = "Geo-Aware Route Verification",
                Strategies = new List<BypassStrategy>()
            },
            new()
            {
                Code = "IR",
                Name = "Iran",
                ShortName = "Iran",
                Description = "OOB Desync & TLS Record Split",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = $"-9 --fake-gen 10 -q {YandexDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --auto=torst,ssl_err -s 1 -d 3+s --ttl 5 --fake -1+s --fake-sni www.cloudflare.com -M h,d -p 1080"
                    },
                    new()
                    {
                        Label = "Adaptive OOB",
                        EngineArguments = $"-5 --wrong-chksum --reverse-frag -q {OpenDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--split 1+s --disorder 3+s --oob 1 --tlsrec 1+s --auto=torst,ssl_err,redirect -L 3 -M h,d -p 1080"
                    },
                    new()
                    {
                        Label = "Direct DNS Mode",
                        EngineArguments = "-9 --fake-gen 10 --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --split 1+s --disorder 2+s --auto=torst,ssl_err -L 3 --fake -1+s --fake-sni speed.cloudflare.com --ttl 7 -M h,d,r -p 1080"
                    }
                }
            },
            new()
            {
                Code = "TR",
                Name = "Turkey",
                ShortName = "Turkey",
                Description = "SNI Split & Packet Desync Tunnel",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = $"-5 --set-ttl 5 {YandexDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
                    },
                    new()
                    {
                        Label = "Adaptive Disorder",
                        EngineArguments = "-9 --fake-gen 10 --wrong-seq --wrong-chksum --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --split 1+s --disorder 3+s --auto=torst,ssl_err -L 3 -M h,d -p 1080"
                    }
                }
            },
            new()
            {
                Code = "RU",
                Name = "Russia",
                ShortName = "Russia",
                Description = "Dynamic Auto-TTL & Segmentation",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = $"-9 --auto-ttl 1-4-10 -q {YandexDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s --ttl 5 -p 1080"
                    },
                    new()
                    {
                        Label = "Adaptive TSPU",
                        EngineArguments = "-9 --fake-gen 10 --auto-ttl 1-4-10 --wrong-seq --wrong-chksum -q --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --split 1+s --disorder 3+s --auto=torst,ssl_err -L 3 -M h,d -p 1080"
                    }
                }
            },
            new()
            {
                Code = "AE",
                Name = "UAE / Middle East",
                ShortName = "UAE / Middle East",
                Description = "VoIP & WebRTC Desync Shield",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = $"-7 -q {YandexDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
                    },
                    new()
                    {
                        Label = "Adaptive Fake SNI",
                        EngineArguments = "-9 --fake-gen 10 --wrong-chksum --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --split 1+s --disorder 3+s --auto=torst,ssl_err -L 3 --fake -1+s --fake-sni www.cloudflare.com --ttl 6 -M h,d -p 1080"
                    }
                }
            },
            new()
            {
                Code = "GL",
                Name = "Global",
                ShortName = "Global",
                Description = "Deep Packet Splitting Tunnel",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = $"-5 -q --auto-ttl 1-4-10 {YandexDnsArgs} --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
                    },
                    new()
                    {
                        Label = "Adaptive OOB",
                        EngineArguments = "-6 -q --reverse-frag --blacklist discord-list.txt",
                        ProxyArguments = "--oob 1 --tlsrec 1+s --split 1+s --disorder 3+s --auto=torst,ssl_err -L 3 -M h,d -p 1080"
                    }
                }
            }
        };

        public static RegionOption? Find(string code) => All.FirstOrDefault(r => r.Code == code);

        public static int IndexOf(RegionOption option)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (ReferenceEquals(All[i], option)) return i;
            }
            return DefaultIndex;
        }

        public static async Task<IReadOnlyList<RegionOption>> BuildAutoQueueAsync(CancellationToken token)
        {
            string country = await DetectCountryCodeAsync(token).ConfigureAwait(false);

            string[] order = country switch
            {
                "IR" => new[] { "IR", "AE", "TR", "GL", "RU" },
                "TR" => new[] { "TR", "GL", "AE", "IR", "RU" },
                "RU" or "BY" => new[] { "RU", "TR", "GL", "AE", "IR" },
                "AE" or "SA" or "OM" or "QA" or "KW" or "BH" => new[] { "AE", "IR", "TR", "GL", "RU" },
                _ => new[] { "GL", "TR", "IR", "AE", "RU" }
            };

            return order.Select(Find).OfType<RegionOption>().ToList();
        }

        private static async Task<string> DetectCountryCodeAsync(CancellationToken token)
        {
            string localGeo = DetectLocalWindowsRegion();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromMilliseconds(1200));

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloudflare.com/cdn-cgi/trace");
                request.Headers.Add("User-Agent", "DiscordShield/1.0.1");
                using var response = await GeoClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                string trace = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                foreach (string line in trace.Split('\n'))
                {
                    if (!line.StartsWith("loc=", StringComparison.OrdinalIgnoreCase)) continue;
                    string code = line[4..].Trim().ToUpperInvariant();
                    if (code.Length == 2) return code;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return localGeo;
        }

        private static string DetectLocalWindowsRegion()
        {
            try
            {
                string tz = TimeZoneInfo.Local.Id;
                if (ContainsAny(tz, "Iran", "Tehran")) return "IR";
                if (ContainsAny(tz, "Turkey", "Istanbul")) return "TR";
                if (ContainsAny(tz, "Russian", "Moscow", "Ekaterinburg", "Novosibirsk", "Vladivostok")) return "RU";
                if (ContainsAny(tz, "Arabian", "Dubai", "Abu Dhabi", "Arab Standard")) return "AE";
            }
            catch { }

            try
            {
                string iso = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
                if (iso.Length == 2) return iso;
            }
            catch { }

            return "GL";
        }

        private static bool ContainsAny(string source, params string[] needles) =>
            needles.Any(n => source.Contains(n, StringComparison.OrdinalIgnoreCase));
    }
}
