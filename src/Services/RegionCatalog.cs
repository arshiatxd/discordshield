using System;
using System.Collections.Generic;
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

        private const string EngineArguments = "-9 --fake-gen 10 --wrong-seq --wrong-chksum --reverse-frag -q --dns-addr 1.1.1.1 --dns-port 53 --blacklist discord-list.txt";
        private const string ProxyBase = "--oob 1 --tlsrec 1+s --split 1 --disorder 3+s --auto=torst,ssl_err -s 1 -d 3+s";
        private const string ProxyFake = "--fake -1+s --fake-sni www.cloudflare.com -M h,d -p 1080";

        private static readonly HttpClient GeoClient = new() { Timeout = TimeSpan.FromSeconds(1.5) };

        public static IReadOnlyList<RegionOption> All { get; } = new List<RegionOption>
        {
            new()
            {
                Code = AutoCode,
                Name = "Auto-Detect",
                ShortName = "Auto-Detect",
                Description = "Automatic Route Verification",
                ProxyArguments = $"{ProxyBase} --ttl 4 {ProxyFake}"
            },
            new()
            {
                Code = "IR",
                Name = "Iran",
                ShortName = "Iran",
                Description = "OOB Desync and Fake SNI Tunnel",
                Arguments = EngineArguments,
                ProxyArguments = $"{ProxyBase} --ttl 4 {ProxyFake}"
            },
            new()
            {
                Code = "TR",
                Name = "Turkey",
                ShortName = "Turkey",
                Description = "Fake SNI, OOB Desync, and BTK Shield",
                Arguments = EngineArguments,
                ProxyArguments = $"{ProxyBase} --ttl 4 {ProxyFake}"
            },
            new()
            {
                Code = "RU",
                Name = "Russia",
                ShortName = "Russia",
                Description = "Dynamic TTL and TCP Segmentation",
                Arguments = "-9 --fake-gen 10 --wrong-seq --wrong-chksum --reverse-frag --auto-ttl 1-4-10 -q --dns-addr 1.1.1.1 --dns-port 53 --blacklist discord-list.txt",
                ProxyArguments = $"{ProxyBase} --ttl 5 {ProxyFake}"
            },
            new()
            {
                Code = "AE",
                Name = "UAE / Middle East",
                ShortName = "UAE / Middle East",
                Description = "VoIP and WebRTC Desync Shield",
                Arguments = EngineArguments,
                ProxyArguments = $"{ProxyBase} --ttl 4 {ProxyFake}"
            },
            new()
            {
                Code = "GL",
                Name = "Global",
                ShortName = "Global",
                Description = "Deep Packet Splitting Shield",
                Arguments = EngineArguments,
                ProxyArguments = $"{ProxyBase} --ttl 4 {ProxyFake}"
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
                "TR" => new[] { "TR", "GL", "AE", "RU", "IR" },
                "RU" or "BY" => new[] { "RU", "TR", "GL", "AE", "IR" },
                "IR" => new[] { "IR", "AE", "TR", "RU", "GL" },
                "AE" or "SA" or "OM" or "QA" or "KW" or "BH" => new[] { "AE", "TR", "GL", "IR", "RU" },
                _ => new[] { "GL", "TR", "AE", "RU", "IR" }
            };

            return order.Select(Find).OfType<RegionOption>().ToList();
        }

        public static int DefaultIndex => 0;

        private static async Task<string> DetectCountryCodeAsync(CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloudflare.com/cdn-cgi/trace");
                request.Headers.Add("User-Agent", "DiscordShield/1.0.1");
                using var response = await GeoClient.SendAsync(request, token).ConfigureAwait(false);
                string trace = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

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

            string zone = TimeZoneInfo.Local.Id + " " + TimeZoneInfo.Local.DisplayName;
            if (ContainsAny(zone, "Turkey", "Istanbul")) return "TR";
            if (ContainsAny(zone, "Iran", "Tehran")) return "IR";
            if (ContainsAny(zone, "Russia", "Moscow")) return "RU";
            if (ContainsAny(zone, "Arabian", "Dubai", "Abu Dhabi")) return "AE";

            try
            {
                return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
            }
            catch
            {
                return "GL";
            }
        }

        private static bool ContainsAny(string source, params string[] needles) =>
            needles.Any(n => source.Contains(n, StringComparison.OrdinalIgnoreCase));
    }
}
