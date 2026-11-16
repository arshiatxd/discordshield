namespace DiscordTunnel.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class CleanDnsService : IDisposable
{
    private static readonly string[] DiscordEdgeDomains = new[]
    {
        "discord.com",
        "discord.gg",
        "discordapp.com",
        "latency.discord.media",
        "discordcdn.com",
        "discord.co",
        "discord.gift",
        "discord.new",
        "discord.design",
        "discord.dev",
        "discordstatus.com",
        "dis.gd",
        "inv.wtf",
        "discordsays.com",
        "media.discordapp.net",
        "images-ext-1.discordapp.net",
        "images-ext-2.discordapp.net"
    };

    private static readonly string[] DiscordStaticDomains = new[]
    {
        "static.discord.com"
    };

    private static readonly string[] DiscordStaticEdgeDomains = new[]
    {
        "static-edge.discord.com"
    };

    private static readonly string[] DiscordUpdaterGcpDomains = new[]
    {
        "dl2.discordapp.net",
        "stable.dl2.discordapp.net"
    };

    private static readonly string[] DiscordLegacyDlDomains = new[]
    {
        "dl.discordapp.net"
    };

    private static readonly string[] DiscordCloudflareEdgeIps = new[]
    {
        "162.159.135.232",
        "162.159.136.232",
        "162.159.137.232",
        "162.159.138.232"
    };

    private static readonly string[] DiscordStaticIps = new[]
    {
        "34.54.86.23"
    };

    private static readonly string[] DiscordStaticEdgeIps = new[]
    {
        "34.126.224.98"
    };

    private static readonly string[] DiscordUpdaterGcpIps = new[]
    {
        "34.126.226.51"
    };

    private static readonly string[] DiscordLegacyDlIps = new[]
    {
        "104.18.48.115",
        "104.18.52.172"
    };

    private static readonly IPEndPoint[] PublicUdpDnsEndpoints = new[]
    {
        new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
        new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53),
        new IPEndPoint(IPAddress.Parse("1.0.0.1"), 53),
        new IPEndPoint(IPAddress.Parse("8.8.4.4"), 53),
        new IPEndPoint(IPAddress.Parse("9.9.9.9"), 53),
        new IPEndPoint(IPAddress.Parse("77.88.8.8"), 1253),
        new IPEndPoint(IPAddress.Parse("208.67.222.222"), 5353)
    };

    private static readonly IPEndPoint[] PublicTcpDnsEndpoints = new[]
    {
        new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
        new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53),
        new IPEndPoint(IPAddress.Parse("77.88.8.8"), 1253),
        new IPEndPoint(IPAddress.Parse("208.67.222.222"), 5353)
    };

    private static readonly string[] DohEndpoints = new[]
    {
        "https://1.1.1.1/dns-query",
        "https://1.0.0.1/dns-query",
        "https://8.8.8.8/dns-query"
    };

    private readonly ConcurrentDictionary<string, (IPAddress[] Ips, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<IPAddress[]>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, HttpClient> _httpClients = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private long _dohResolutionCount;
    private long _edgeFallbackCount;
    private int _edgeRoundRobinIndex;
    private int _dnsQueryId;
    private volatile string _lastDohSummary = "pending";
    private bool _disposed;

    public string LastDohSummary => _lastDohSummary;
    public long DohResolutionCount => Interlocked.Read(ref _dohResolutionCount);
    public long EdgeFallbackCount => Interlocked.Read(ref _edgeFallbackCount);

    public static bool IsPoisonedOrInvalidIPv4(IPAddress address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[4];
        if (!address.TryWriteBytes(bytes, out _))
        {
            return true;
        }

        byte b0 = bytes[0];
        byte b1 = bytes[1];
        byte b2 = bytes[2];
        byte b3 = bytes[3];

        if (b0 == 0) return true;
        if (b0 == 10) return true;
        if (b0 == 100 && b1 >= 64 && b1 <= 127) return true;
        if (b0 == 127) return true;
        if (b0 == 169 && b1 == 254) return true;
        if (b0 == 172 && b1 >= 16 && b1 <= 31) return true;
        if (b0 == 192 && b1 == 0 && b2 == 0) return true;
        if (b0 == 192 && b1 == 0 && b2 == 2) return true;
        if (b0 == 192 && b1 == 168) return true;
        if (b0 == 195 && b1 == 175 && b2 == 254 && (b3 == 2 || b3 == 3)) return true;
        if (b0 == 198 && (b1 == 18 || b1 == 19)) return true;
        if (b0 == 198 && b1 == 51 && b2 == 100) return true;
        if (b0 == 203 && b1 == 0 && b2 == 113) return true;
        if (b0 >= 224) return true;

        return false;
    }

    public static byte[] BuildDnsQuery(string host, ushort id = 0x1A2B)
    {
        if (host is null)
        {
            throw new ArgumentException("Host cannot be null.", nameof(host));
        }

        var normalized = host.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Host cannot be empty.", nameof(host));
        }

        if (Encoding.ASCII.GetByteCount(normalized) > 253)
        {
            throw new ArgumentException("Host name exceeds 253 bytes.", nameof(host));
        }

        var labels = normalized.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0)
            {
                throw new ArgumentException("Host contains an empty label.", nameof(host));
            }

            if (Encoding.ASCII.GetByteCount(label) > 63)
            {
                throw new ArgumentException("Host label exceeds 63 bytes.", nameof(host));
            }
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)(id >> 8));
        writer.Write((byte)(id & 0xFF));
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        foreach (var label in labels)
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)labelBytes.Length);
            writer.Write(labelBytes);
        }

        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);

        return stream.ToArray();
    }

    public static IPAddress? ParseFirstCleanIPv4(byte[] packet)
    {
        var list = ParseCleanIPv4List(packet, expectedTransactionId: null);
        return list.Length > 0 ? list[0] : null;
    }

    public static IPAddress? ParseFirstCleanIPv4(byte[] packet, ushort expectedTransactionId)
    {
        var list = ParseCleanIPv4List(packet, (ushort?)expectedTransactionId);
        return list.Length > 0 ? list[0] : null;
    }

    public static IPAddress[] ParseCleanIPv4List(byte[] packet, ushort? expectedTransactionId = null)
    {
        if (packet is null || packet.Length < 12)
        {
            return Array.Empty<IPAddress>();
        }

        if (expectedTransactionId.HasValue)
        {
            ushort id = (ushort)((packet[0] << 8) | packet[1]);
            if (id != expectedTransactionId.Value)
            {
                return Array.Empty<IPAddress>();
            }
        }

        ushort flags = (ushort)((packet[2] << 8) | packet[3]);
        bool qr = (flags & 0x8000) != 0;
        int rcode = flags & 0x000F;
        if (!qr || rcode != 0)
        {
            return Array.Empty<IPAddress>();
        }

        int qdCount = (packet[4] << 8) | packet[5];
        int anCount = (packet[6] << 8) | packet[7];
        if (anCount == 0)
        {
            return Array.Empty<IPAddress>();
        }

        int offset = 12;
        for (int i = 0; i < qdCount; i++)
        {
            if (!TrySkipName(packet, ref offset))
            {
                return Array.Empty<IPAddress>();
            }

            if (offset + 4 > packet.Length)
            {
                return Array.Empty<IPAddress>();
            }

            offset += 4;
        }

        var results = new List<IPAddress>(Math.Min(anCount, 8));
        for (int i = 0; i < anCount; i++)
        {
            if (!TrySkipName(packet, ref offset))
            {
                break;
            }

            if (offset + 10 > packet.Length)
            {
                break;
            }

            ushort type = (ushort)((packet[offset] << 8) | packet[offset + 1]);
            ushort cls = (ushort)((packet[offset + 2] << 8) | packet[offset + 3]);
            ushort rdlength = (ushort)((packet[offset + 8] << 8) | packet[offset + 9]);
            offset += 10;

            if (offset + rdlength > packet.Length)
            {
                break;
            }

            if (type == 1 && cls == 1 && rdlength == 4)
            {
                var ip = new IPAddress(packet.AsSpan(offset, 4));
                if (!IsPoisonedOrInvalidIPv4(ip) && !results.Contains(ip))
                {
                    results.Add(ip);
                    if (results.Count >= 8)
                    {
                        break;
                    }
                }
            }

            offset += rdlength;
        }

        return results.Count > 0 ? results.ToArray() : Array.Empty<IPAddress>();
    }

    private static bool IsAuthoritativeNoDataResponse(byte[] packet)
    {
        if (packet is null || packet.Length < 12)
        {
            return false;
        }

        ushort flags = (ushort)((packet[2] << 8) | packet[3]);
        bool qr = (flags & 0x8000) != 0;
        int rcode = flags & 0x000F;
        int qdCount = (packet[4] << 8) | packet[5];
        int anCount = (packet[6] << 8) | packet[7];
        return qr && qdCount >= 1 && anCount == 0 && (rcode == 0 || rcode == 3);
    }

    private static bool TrySkipName(byte[] packet, ref int offset)
    {
        while (true)
        {
            if (offset >= packet.Length)
            {
                return false;
            }

            byte b = packet[offset];
            if (b == 0)
            {
                offset++;
                return true;
            }

            int marker = b & 0xC0;
            if (marker == 0xC0)
            {
                if (offset + 2 > packet.Length)
                {
                    return false;
                }

                int pointerTarget = ((b & 0x3F) << 8) | packet[offset + 1];
                if (pointerTarget >= packet.Length)
                {
                    return false;
                }

                offset += 2;
                return true;
            }

            if (marker != 0)
            {
                return false;
            }

            int labelLen = b;
            offset++;
            if (offset + labelLen > packet.Length)
            {
                return false;
            }

            offset += labelLen;
        }
    }

    public static bool IsDiscordEdgeHost(string host) => GetFallbackPool(host) is not null;

    public bool TryGetFallbackIp(string host, out IPAddress? fallbackIp)
    {
        if (TryGetFallbackPool(host, out var ips) && ips.Length > 0)
        {
            fallbackIp = ips[0];
            return true;
        }

        fallbackIp = null;
        return false;
    }

    public bool TryGetFallbackPool(string host, out IPAddress[] fallbackIps)
    {
        var pool = GetFallbackPool(host);
        if (pool is null || pool.Length == 0)
        {
            fallbackIps = Array.Empty<IPAddress>();
            return false;
        }

        int start = (int)((uint)Interlocked.Increment(ref _edgeRoundRobinIndex) % (uint)pool.Length);
        var ordered = new IPAddress[pool.Length];
        for (int i = 0; i < pool.Length; i++)
        {
            ordered[i] = IPAddress.Parse(pool[(start + i) % pool.Length]);
        }

        fallbackIps = ordered;
        return true;
    }

    private static string[]? GetFallbackPool(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().TrimEnd('.');
        if (MatchesAnyDomain(normalized, DiscordStaticDomains))
        {
            return DiscordStaticIps;
        }

        if (MatchesAnyDomain(normalized, DiscordStaticEdgeDomains))
        {
            return DiscordStaticEdgeIps;
        }

        if (MatchesAnyDomain(normalized, DiscordUpdaterGcpDomains))
        {
            return DiscordUpdaterGcpIps;
        }

        if (MatchesAnyDomain(normalized, DiscordLegacyDlDomains))
        {
            return DiscordLegacyDlIps;
        }

        if (MatchesAnyDomain(normalized, DiscordEdgeDomains))
        {
            return DiscordCloudflareEdgeIps;
        }

        return null;
    }

    private static bool MatchesAnyDomain(string normalizedHost, string[] domains)
    {
        for (int i = 0; i < domains.Length; i++)
        {
            var domain = domains[i];
            if (normalizedHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || normalizedHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void ReportIpFailure(string host, IPAddress failedIp)
    {
        if (_disposed || string.IsNullOrWhiteSpace(host) || failedIp is null)
        {
            return;
        }

        var normalized = host.Trim().TrimEnd('.');
        if (!_cache.TryGetValue(normalized, out var entry) || entry.Ips.Length == 0)
        {
            return;
        }

        if (entry.Ips.Length == 1)
        {
            if (entry.Ips[0].Equals(failedIp))
            {
                _cache.TryRemove(normalized, out _);
            }
            return;
        }

        var rotated = new List<IPAddress>(entry.Ips.Length);
        bool found = false;
        for (int i = 0; i < entry.Ips.Length; i++)
        {
            if (entry.Ips[i].Equals(failedIp))
            {
                found = true;
                continue;
            }
            rotated.Add(entry.Ips[i]);
        }

        if (!found)
        {
            return;
        }

        rotated.Add(failedIp);
        _cache[normalized] = (rotated.ToArray(), entry.ExpiresAt);
    }

    public async Task<IPAddress?> ResolveIPv4Async(string host, int backendSocksPort, CancellationToken token)
    {
        var candidates = await ResolveIPv4CandidatesAsync(host, backendSocksPort, token).ConfigureAwait(false);
        return candidates.Length > 0 ? candidates[0] : null;
    }

    public async Task<IPAddress[]> ResolveIPv4CandidatesAsync(string host, int backendSocksPort, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(host))
        {
            _lastDohSummary = "failed";
            return Array.Empty<IPAddress>();
        }

        var normalized = host.Trim().TrimEnd('.');

        if (IPAddress.TryParse(normalized, out var directIp))
        {
            return !IsPoisonedOrInvalidIPv4(directIp) ? new[] { directIp } : Array.Empty<IPAddress>();
        }

        if (_cache.TryGetValue(normalized, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Ips;
        }

        string inflightKey = $"{normalized}|{backendSocksPort}";
        var entry = _inflight.GetOrAdd(
            inflightKey,
            _ => new Lazy<Task<IPAddress[]>>(() => ResolveAndRemoveAsync(normalized, backendSocksPort, inflightKey)));

        try
        {
            return await entry.Value.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task<IPAddress[]> ResolveAndRemoveAsync(string normalized, int backendSocksPort, string inflightKey)
    {
        try
        {
            return await ResolveCoreAsync(normalized, backendSocksPort, _disposeCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(inflightKey, out _);
        }
    }

    private sealed record DnsResolutionResult(IPAddress[] Ips, bool AuthoritativeEmpty, string Summary);

    private async Task<IPAddress[]> ResolveCoreAsync(string normalized, int backendSocksPort, CancellationToken token)
    {
        if (_cache.TryGetValue(normalized, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Ips;
        }

        byte[] query;
        ushort queryId = (ushort)(((uint)Interlocked.Increment(ref _dnsQueryId) & 0xFFFFu) | 1u);
        try
        {
            query = BuildDnsQuery(normalized, queryId);
        }
        catch (ArgumentException)
        {
            _lastDohSummary = "failed";
            return Array.Empty<IPAddress>();
        }

        bool dohAuthoritativeEmpty = false;

        if (backendSocksPort > 0)
        {
            using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            byte[] dohQuery = BuildDnsQuery(normalized);
            var winnerTcs = new TaskCompletionSource<DnsResolutionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var publicTask = RunDnsCandidateAsync(() => QueryPublicDnsAsync(query, queryId, raceCts.Token), winnerTcs, raceCts);
            var dohTask = QueryDohWithNegativeAsync(dohQuery, backendSocksPort, winnerTcs, raceCts);

            await Task.WhenAll(publicTask, dohTask).ConfigureAwait(false);
            if (winnerTcs.Task.IsCompletedSuccessfully && winnerTcs.Task.Result is { Ips.Length: > 0 } winner)
            {
                Interlocked.Increment(ref _dohResolutionCount);
                _lastDohSummary = winner.Summary;
                _cache[normalized] = (winner.Ips, DateTimeOffset.UtcNow.AddMinutes(10));
                return winner.Ips;
            }

            dohAuthoritativeEmpty = dohTask.IsCompletedSuccessfully && dohTask.Result;
        }
        else
        {
            var publicResult = await QueryPublicDnsAsync(query, queryId, token).ConfigureAwait(false);
            if (publicResult is { Ips.Length: > 0 })
            {
                Interlocked.Increment(ref _dohResolutionCount);
                _lastDohSummary = publicResult.Summary;
                _cache[normalized] = (publicResult.Ips, DateTimeOffset.UtcNow.AddMinutes(10));
                return publicResult.Ips;
            }
        }

        if (dohAuthoritativeEmpty)
        {
            _lastDohSummary = "nxdomain";
            _cache[normalized] = (Array.Empty<IPAddress>(), DateTimeOffset.UtcNow.AddSeconds(30));
            return Array.Empty<IPAddress>();
        }

        if (TryGetFallbackPool(normalized, out var edgeIps) && edgeIps.Length > 0)
        {
            Interlocked.Increment(ref _edgeFallbackCount);
            _lastDohSummary = "edge-fallback";
            _cache[normalized] = (edgeIps, DateTimeOffset.UtcNow.AddSeconds(30));
            return edgeIps;
        }

        try
        {
            using var osCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            osCts.CancelAfter(TimeSpan.FromMilliseconds(900));
            var osAddresses = await Dns.GetHostAddressesAsync(normalized, osCts.Token).ConfigureAwait(false);
            var cleanOs = new List<IPAddress>(Math.Min(osAddresses.Length, 8));
            for (int i = 0; i < osAddresses.Length; i++)
            {
                var osIp = osAddresses[i];
                if (!IsPoisonedOrInvalidIPv4(osIp) && !cleanOs.Contains(osIp))
                {
                    cleanOs.Add(osIp);
                    if (cleanOs.Count >= 8)
                    {
                        break;
                    }
                }
            }

            if (cleanOs.Count > 0)
            {
                var arr = cleanOs.ToArray();
                _lastDohSummary = "system-dns";
                _cache[normalized] = (arr, DateTimeOffset.UtcNow.AddMinutes(5));
                return arr;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }

        _lastDohSummary = "failed";
        return Array.Empty<IPAddress>();
    }

    private static async Task RunDnsCandidateAsync(
        Func<Task<DnsResolutionResult?>> candidate,
        TaskCompletionSource<DnsResolutionResult?> winnerTcs,
        CancellationTokenSource raceCts)
    {
        try
        {
            var result = await candidate().ConfigureAwait(false);
            if (result is { Ips.Length: > 0 } && winnerTcs.TrySetResult(result))
            {
                try { raceCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task<bool> QueryDohWithNegativeAsync(
        byte[] query,
        int backendSocksPort,
        TaskCompletionSource<DnsResolutionResult?> winnerTcs,
        CancellationTokenSource raceCts)
    {
        try
        {
            var result = await QueryDohParallelAsync(query, backendSocksPort, raceCts.Token).ConfigureAwait(false);
            if (result is { Ips.Length: > 0 })
            {
                if (winnerTcs.TrySetResult(result))
                {
                    try { raceCts.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                return false;
            }

            if (result is { AuthoritativeEmpty: true })
            {
                try { raceCts.Cancel(); }
                catch (ObjectDisposedException) { }
                return true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }

        return false;
    }

    private static async Task<DnsResolutionResult?> QueryPublicDnsAsync(byte[] query, ushort expectedId, CancellationToken token)
    {
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        raceCts.CancelAfter(TimeSpan.FromMilliseconds(850));

        var winnerTcs = new TaskCompletionSource<DnsResolutionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task>(PublicUdpDnsEndpoints.Length + PublicTcpDnsEndpoints.Length);

        foreach (var endpoint in PublicUdpDnsEndpoints)
        {
            tasks.Add(QueryUdpEndpointAsync(endpoint, query, expectedId, winnerTcs, raceCts));
        }

        foreach (var endpoint in PublicTcpDnsEndpoints)
        {
            tasks.Add(QueryTcpEndpointAsync(endpoint, query, expectedId, winnerTcs, raceCts));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return winnerTcs.Task.IsCompletedSuccessfully ? winnerTcs.Task.Result : null;
    }

    private static async Task QueryUdpEndpointAsync(
        IPEndPoint endpoint,
        byte[] query,
        ushort expectedId,
        TaskCompletionSource<DnsResolutionResult?> winnerTcs,
        CancellationTokenSource raceCts)
    {
        var ct = raceCts.Token;
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            await udp.SendAsync(query, endpoint, ct).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            if (!response.RemoteEndPoint.Address.Equals(endpoint.Address))
            {
                return;
            }

            var parsed = ParseCleanIPv4List(response.Buffer, expectedId);
            if (parsed.Length > 0 && winnerTcs.TrySetResult(new DnsResolutionResult(parsed, false, $"dns ({endpoint.Address}:{endpoint.Port})")))
            {
                try { raceCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private static async Task QueryTcpEndpointAsync(
        IPEndPoint endpoint,
        byte[] query,
        ushort expectedId,
        TaskCompletionSource<DnsResolutionResult?> winnerTcs,
        CancellationTokenSource raceCts)
    {
        var ct = raceCts.Token;
        try
        {
            await Task.Delay(150, ct).ConfigureAwait(false);
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(endpoint.Address, endpoint.Port, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();

            byte[] framed = new byte[2 + query.Length];
            framed[0] = (byte)(query.Length >> 8);
            framed[1] = (byte)(query.Length & 0xFF);
            Buffer.BlockCopy(query, 0, framed, 2, query.Length);
            await stream.WriteAsync(framed, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            byte[] lenBuf = new byte[2];
            await stream.ReadExactlyAsync(lenBuf, ct).ConfigureAwait(false);
            int msgLen = (lenBuf[0] << 8) | lenBuf[1];
            if (msgLen < 12 || msgLen > 4096)
            {
                return;
            }

            byte[] body = new byte[msgLen];
            await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
            var parsed = ParseCleanIPv4List(body, expectedId);
            if (parsed.Length > 0 && winnerTcs.TrySetResult(new DnsResolutionResult(parsed, false, $"dns-tcp ({endpoint.Address}:{endpoint.Port})")))
            {
                try { raceCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task<DnsResolutionResult?> QueryDohParallelAsync(byte[] query, int backendSocksPort, CancellationToken token)
    {
        var client = GetOrCreateHttpClient(backendSocksPort);
        using var dohCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        dohCts.CancelAfter(TimeSpan.FromMilliseconds(2100));

        var winnerTcs = new TaskCompletionSource<DnsResolutionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task<bool>>(DohEndpoints.Length);

        foreach (var endpoint in DohEndpoints)
        {
            tasks.Add(QuerySingleDohAsync(client, endpoint, query, winnerTcs, dohCts));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (winnerTcs.Task.IsCompletedSuccessfully)
        {
            return winnerTcs.Task.Result;
        }

        for (int i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].IsCompletedSuccessfully && tasks[i].Result)
            {
                return new DnsResolutionResult(Array.Empty<IPAddress>(), true, "nxdomain");
            }
        }

        return null;
    }

    private static async Task<bool> QuerySingleDohAsync(
        HttpClient client,
        string endpoint,
        byte[] query,
        TaskCompletionSource<DnsResolutionResult?> winnerTcs,
        CancellationTokenSource dohCts)
    {
        var ct = dohCts.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            var content = new ByteArrayContent(query);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Content = content;

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var parsedIps = ParseCleanIPv4List(body);
            if (parsedIps.Length > 0)
            {
                var uriHost = new Uri(endpoint).Host;
                if (winnerTcs.TrySetResult(new DnsResolutionResult(parsedIps, false, $"ok ({uriHost})")))
                {
                    try { dohCts.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                return false;
            }

            return IsAuthoritativeNoDataResponse(body);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }

        return false;
    }

    public async Task<(bool IsClean, string Summary)> ProbeSystemDnsAsync(string host, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(host))
        {
            return (false, "empty");
        }

        var normalized = host.Trim().TrimEnd('.');
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            var addresses = await Dns.GetHostAddressesAsync(normalized, cts.Token).ConfigureAwait(false);
            if (addresses is null || addresses.Length == 0)
            {
                return (false, "empty");
            }

            bool hasClean = false;
            for (int i = 0; i < addresses.Length; i++)
            {
                if (!IsPoisonedOrInvalidIPv4(addresses[i]))
                {
                    hasClean = true;
                    break;
                }
            }

            if (hasClean)
            {
                return (true, "clean");
            }

            return (false, "poisoned (private/reserved IP)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"error ({ex.GetType().Name})");
        }
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { _disposeCts.Cancel(); }
        catch (ObjectDisposedException) { }
        _disposeCts.Dispose();

        foreach (var client in _httpClients.Values)
        {
            client.Dispose();
        }

        _httpClients.Clear();
        _inflight.Clear();
        _cache.Clear();
    }

    private static readonly string[] PrewarmHosts = new[]
    {
        "discord.com",
        "static.discord.com",
        "static-edge.discord.com",
        "gateway.discord.gg",
        "cdn.discordapp.com",
        "media.discordapp.net",
        "images-ext-1.discordapp.net",
        "latency.discord.media",
        "updates.discord.com",
        "stable.dl2.discordapp.net",
        "dl.discordapp.net",
        "sentry.io",
        "o64374.ingest.sentry.io",
        "cloudflare-ech.com"
    };

    public async Task PrewarmAsync(int backendSocksPort, CancellationToken token)
    {
        if (_disposed || backendSocksPort <= 0)
        {
            return;
        }

        var tasks = new List<Task>(PrewarmHosts.Length);
        foreach (var host in PrewarmHosts)
        {
            tasks.Add(ResolveIPv4Async(host, backendSocksPort, token));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private HttpClient GetOrCreateHttpClient(int backendSocksPort)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _httpClients.GetOrAdd(backendSocksPort, port =>
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromMilliseconds(1800),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 8
            };
            return new HttpClient(handler, disposeHandler: true);
        });
    }
}
