namespace DiscordTunnel.Services;

using System;
using System.Collections.Concurrent;
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
        "discordapp.net",
        "discordcdn.com",
        "discord.co",
        "discord.gift",
        "discord.new",
        "discord.design",
        "discord.dev",
        "discordstatus.com",
        "dis.gd",
        "inv.wtf",
        "discordsays.com"
    };

    private static readonly string[] DiscordEdgeIps = new[]
    {
        "162.159.135.232",
        "162.159.137.232",
        "162.159.136.232",
        "162.159.138.232",
        "162.159.135.233",
        "162.159.137.233"
    };

    private static readonly string[] DohEndpoints = new[]
    {
        "https://1.1.1.1/dns-query",
        "https://1.0.0.1/dns-query",
        "https://8.8.8.8/dns-query"
    };

    private readonly ConcurrentDictionary<string, (IPAddress Ip, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, HttpClient> _httpClients = new();
    private long _dohResolutionCount;
    private long _edgeFallbackCount;
    private int _edgeRoundRobinIndex;
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

        if (b0 == 0) return true;
        if (b0 == 10) return true;
        if (b0 == 100 && b1 >= 64 && b1 <= 127) return true;
        if (b0 == 127) return true;
        if (b0 == 169 && b1 == 254) return true;
        if (b0 == 172 && b1 >= 16 && b1 <= 31) return true;
        if (b0 == 192 && b1 == 0 && b2 == 0) return true;
        if (b0 == 192 && b1 == 0 && b2 == 2) return true;
        if (b0 == 192 && b1 == 168) return true;
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
        if (packet is null || packet.Length < 12)
        {
            return null;
        }

        bool qr = (packet[2] & 0x80) != 0;
        int rcode = packet[3] & 0x0F;
        if (!qr || rcode != 0)
        {
            return null;
        }

        int qdCount = (packet[4] << 8) | packet[5];
        int anCount = (packet[6] << 8) | packet[7];
        if (anCount == 0)
        {
            return null;
        }

        int offset = 12;
        for (int i = 0; i < qdCount; i++)
        {
            if (!TrySkipName(packet, ref offset))
            {
                return null;
            }

            if (offset + 4 > packet.Length)
            {
                return null;
            }

            offset += 4;
        }

        for (int i = 0; i < anCount; i++)
        {
            if (!TrySkipName(packet, ref offset))
            {
                return null;
            }

            if (offset + 10 > packet.Length)
            {
                return null;
            }

            ushort type = (ushort)((packet[offset] << 8) | packet[offset + 1]);
            ushort cls = (ushort)((packet[offset + 2] << 8) | packet[offset + 3]);
            ushort rdlength = (ushort)((packet[offset + 8] << 8) | packet[offset + 9]);
            offset += 10;

            if (offset + rdlength > packet.Length)
            {
                return null;
            }

            if (type == 1 && cls == 1 && rdlength == 4)
            {
                var ip = new IPAddress(packet.AsSpan(offset, 4));
                if (!IsPoisonedOrInvalidIPv4(ip))
                {
                    return ip;
                }
            }

            offset += rdlength;
        }

        return null;
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

    public static bool IsDiscordEdgeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalized = host.Trim().TrimEnd('.');
        for (int i = 0; i < DiscordEdgeDomains.Length; i++)
        {
            var domain = DiscordEdgeDomains[i];
            if (normalized.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IPAddress?> ResolveIPv4Async(string host, int backendSocksPort, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(host))
        {
            _lastDohSummary = "failed";
            return null;
        }

        var normalized = host.Trim().TrimEnd('.');

        if (IPAddress.TryParse(normalized, out var directIp))
        {
            return !IsPoisonedOrInvalidIPv4(directIp) ? directIp : null;
        }

        if (_cache.TryGetValue(normalized, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Ip;
        }

        if (backendSocksPort > 0)
        {
            var client = GetOrCreateHttpClient(backendSocksPort);
            byte[] query;
            try
            {
                query = BuildDnsQuery(normalized);
            }
            catch (ArgumentException)
            {
                _lastDohSummary = "failed";
                return null;
            }

            for (int i = 0; i < DohEndpoints.Length; i++)
            {
                var endpoint = DohEndpoints[i];
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
                var content = new ByteArrayContent(query);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
                request.Content = content;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                linkedCts.CancelAfter(TimeSpan.FromMilliseconds(1800));

                try
                {
                    using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
                        var parsedIp = ParseFirstCleanIPv4(body);
                        if (parsedIp is not null)
                        {
                            Interlocked.Increment(ref _dohResolutionCount);
                            var uriHost = new Uri(endpoint).Host;
                            _lastDohSummary = $"ok ({uriHost})";
                            _cache[normalized] = (parsedIp, DateTimeOffset.UtcNow.AddMinutes(5));
                            return parsedIp;
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                }
            }
        }

        try
        {
            using var osCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            osCts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            var osIps = await Dns.GetHostAddressesAsync(normalized, osCts.Token).ConfigureAwait(false);
            for (int i = 0; i < osIps.Length; i++)
            {
                var osIp = osIps[i];
                if (!IsPoisonedOrInvalidIPv4(osIp))
                {
                    _lastDohSummary = "system-dns";
                    _cache[normalized] = (osIp, DateTimeOffset.UtcNow.AddMinutes(5));
                    return osIp;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }

        if (IsDiscordEdgeHost(normalized))
        {
            var index = (int)((uint)Interlocked.Increment(ref _edgeRoundRobinIndex) % (uint)DiscordEdgeIps.Length);
            var edgeIp = IPAddress.Parse(DiscordEdgeIps[index]);
            Interlocked.Increment(ref _edgeFallbackCount);
            _lastDohSummary = "edge-fallback";
            _cache[normalized] = (edgeIp, DateTimeOffset.UtcNow.AddSeconds(30));
            return edgeIp;
        }

        _lastDohSummary = "failed";
        return null;
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
        foreach (var client in _httpClients.Values)
        {
            client.Dispose();
        }

        _httpClients.Clear();
        _cache.Clear();
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
                ConnectTimeout = TimeSpan.FromMilliseconds(1500)
            };
            return new HttpClient(handler, disposeHandler: true);
        });
    }
}
