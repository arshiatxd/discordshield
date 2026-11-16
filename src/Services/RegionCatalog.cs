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
                Description = "TLS Record Split & UDP Voice Desync",
                Strategies = new List<BypassStrategy>
                {
                    new()
                    {
                        Label = "Primary",
                        EngineArguments = "-f 2 -e 2 --native-frag --frag-by-sni -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -L 1 -r 1+s -s 1+s -M h,d -A torst,ssl_err -r 1+s -s 1+s -d 3+s -M h,d -A torst,ssl_err -r 1+s -s 1+s -f -1+s -t 6 -Q rand -n speed.cloudflare.com -M h,d -A torst,ssl_err -r 1+s -o 1 -M h,d"
                    },
                    new()
                    {
                        Label = "Native Split",
                        EngineArguments = "-f 2 -e 2 --native-frag --frag-by-sni -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -r 1+s -s 1+s -M h,d"
                    },
                    new()
                    {
                        Label = "Proxy Split (No Driver)",
                        EngineArguments = string.Empty,
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -L 1 -r 1+s -s 1+s -M h,d -A torst,ssl_err -r 1+s -s 1+s -d 3+s -M h,d -A torst,ssl_err -r 1+s -o 1 -M h,d"
                    },
                    new()
                    {
                        Label = "Forward Fake SNI",
                        EngineArguments = string.Empty,
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -L 1 -r 1+s -s 1+s -f -1+s -t 8 -Q rand -n www.google.com -A t,s -r 1+s -s 1+s -f -1+s -t 10 -Q rand -n cdn.jsdelivr.net -M h,d"
                    },
                    new()
                    {
                        Label = "Adaptive Disorder",
                        EngineArguments = "-5 --wrong-chksum --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -L 1 -s 1+s -d 3+s -r 1+s -M h,d -A torst,ssl_err -r 1+s -s 1+s -f -1+s -t 6 -Q rand -n speed.cloudflare.com -M h,d"
                    },
                    new()
                    {
                        Label = "Direct Safe Mode",
                        EngineArguments = "-9 --fake-gen 10 --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 2 -L 1 -r 1+s -s 1+s -d 2+s -M h,d,r -A torst,ssl_err --fake -1+s --fake-sni speed.cloudflare.com --ttl 7 -A torst,ssl_err -o 1 -r 1+s"
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
                        EngineArguments = "-5 --set-ttl 5 -q --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d -T 1 --auto=torst,ssl_err -L 1 --tlsrec 1+s --auto=torst,ssl_err -r 1+s -s 1+s -M h,d"
                    },
                    new()
                    {
                        Label = "Adaptive Disorder",
                        EngineArguments = "-9 --fake-gen 10 --wrong-seq --wrong-chksum --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 1 -L 1 -r 1+s -s 1+s -d 3+s -M h,d --auto=torst,ssl_err --oob 1 -M h,d"
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
                        EngineArguments = "-9 --auto-ttl 1-4-10 -q --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d -T 1 --auto=torst --tlsrec 1+s --ttl 5"
                    },
                    new()
                    {
                        Label = "Adaptive TSPU",
                        EngineArguments = "-9 --fake-gen 10 --auto-ttl 1-4-10 --wrong-seq --wrong-chksum -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 1 -L 1 -r 1+s -s 1+s -d 3+s -M h,d --auto=torst,ssl_err --oob 1 -M h,d"
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
                        EngineArguments = "-7 -q --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d -T 1 --auto=torst --tlsrec 1+s"
                    },
                    new()
                    {
                        Label = "Adaptive Fake SNI",
                        EngineArguments = "-9 --fake-gen 10 --wrong-chksum --reverse-frag -q --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 1 -L 1 -r 1+s -s 1+s -d 3+s -M h,d --auto=torst,ssl_err --oob 1 --fake -1+s --fake-sni www.cloudflare.com --ttl 6 -M h,d"
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
                        EngineArguments = "-5 -q --auto-ttl 1-4-10 --blacklist discord-list.txt",
                        ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d -T 1 --auto=torst --tlsrec 1+s"
                    },
                    new()
                    {
                        Label = "Adaptive OOB",
                        EngineArguments = "-6 -q --reverse-frag --blacklist discord-list.txt",
                        ProxyArguments = "-I 0.0.0.0 -K t,h -T 1 -L 1 -r 1+s -s 1+s -d 3+s -M h,d --auto=torst,ssl_err --oob 1 -M h,d"
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
            if (!string.Equals(localGeo, "GL", StringComparison.OrdinalIgnoreCase))
            {
                return localGeo;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromMilliseconds(800));

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://1.1.1.1/cdn-cgi/trace");
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
