namespace DiscordTunnel.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class SocksRelayService : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly CleanDnsService _dnsService;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _backendPort;
    private long _totalConnections;
    private bool _isListening;
    private bool _disposed;
    private const int BackendAttemptTimeoutMs = 4500;
    private const int MaxCandidateAttempts = 3;

    public SocksRelayService(CleanDnsService dnsService, int listenPort = 1080)
    {
        _dnsService = dnsService ?? throw new ArgumentNullException(nameof(dnsService));
        if (listenPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(listenPort), "Listen port must be between 1 and 65535.");
        }

        ListenPort = listenPort;
    }

    public int ListenPort { get; }

    public int BackendPort
    {
        get => Volatile.Read(ref _backendPort);
        set => Volatile.Write(ref _backendPort, value);
    }

    public long TotalConnections => Interlocked.Read(ref _totalConnections);

    public bool IsListening
    {
        get
        {
            lock (_syncRoot)
            {
                return _isListening;
            }
        }
    }

    public static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isListening)
            {
                return;
            }

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, ListenPort);
                listener.Start();
                _listener = listener;
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"Local port {ListenPort} is already in use.", ex);
            }

            _cts = new CancellationTokenSource();
            _isListening = true;
            _ = AcceptLoopAsync(_listener, _cts.Token);
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _listener?.Stop();
            }
            catch (Exception)
            {
            }

            _listener = null;

            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, token));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        TcpClient? upstream = null;
        try
        {
            client.NoDelay = true;
            var clientStream = client.GetStream();

            byte[] greetingHeader = new byte[2];
            await clientStream.ReadExactlyAsync(greetingHeader, token).ConfigureAwait(false);

            if (greetingHeader[0] == 0x05)
            {
                upstream = await HandleSocks5HandshakeAsync(clientStream, greetingHeader[1], token).ConfigureAwait(false);
            }
            else if (IsHttpMethodStart(greetingHeader[0]))
            {
                upstream = await HandleHttpHandshakeAsync(clientStream, greetingHeader, token).ConfigureAwait(false);
            }

            if (upstream is null)
            {
                return;
            }

            using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var clientToUpstream = PumpAsync(client, upstream, relayCts, relayCts.Token);
            var upstreamToClient = PumpAsync(upstream, client, relayCts, relayCts.Token);
            await Task.WhenAll(clientToUpstream, upstreamToClient).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            try
            {
                upstream?.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                client.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task<TcpClient?> HandleSocks5HandshakeAsync(NetworkStream clientStream, byte nmethods, CancellationToken token)
    {
        if (nmethods == 0)
        {
            return null;
        }

        byte[] methods = new byte[nmethods];
        await clientStream.ReadExactlyAsync(methods, token).ConfigureAwait(false);

        bool supportsNoAuth = false;
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i] == 0x00)
            {
                supportsNoAuth = true;
                break;
            }
        }

        if (!supportsNoAuth)
        {
            byte[] noAcceptable = new byte[] { 0x05, 0xFF };
            await clientStream.WriteAsync(noAcceptable, token).ConfigureAwait(false);
            await clientStream.FlushAsync(token).ConfigureAwait(false);
            return null;
        }

        byte[] authChoice = new byte[] { 0x05, 0x00 };
        await clientStream.WriteAsync(authChoice, token).ConfigureAwait(false);
        await clientStream.FlushAsync(token).ConfigureAwait(false);

        byte[] requestHeader = new byte[4];
        await clientStream.ReadExactlyAsync(requestHeader, token).ConfigureAwait(false);
        byte reqVer = requestHeader[0];
        byte cmd = requestHeader[1];
        byte rsv = requestHeader[2];
        byte atyp = requestHeader[3];

        if (reqVer != 5)
        {
            byte[] unsupportedReply = new byte[] { 0x05, 0x08, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            await clientStream.WriteAsync(unsupportedReply, token).ConfigureAwait(false);
            await clientStream.FlushAsync(token).ConfigureAwait(false);
            return null;
        }

        byte[] addrAndPort;
        string? domain = null;
        byte portHi;
        byte portLo;

        if (atyp == 0x01)
        {
            addrAndPort = new byte[6];
            await clientStream.ReadExactlyAsync(addrAndPort, token).ConfigureAwait(false);
            portHi = addrAndPort[4];
            portLo = addrAndPort[5];
        }
        else if (atyp == 0x04)
        {
            addrAndPort = new byte[18];
            await clientStream.ReadExactlyAsync(addrAndPort, token).ConfigureAwait(false);
            portHi = addrAndPort[16];
            portLo = addrAndPort[17];
        }
        else if (atyp == 0x03)
        {
            byte[] lenBuf = new byte[1];
            await clientStream.ReadExactlyAsync(lenBuf, token).ConfigureAwait(false);
            byte domainLen = lenBuf[0];
            if (domainLen == 0)
            {
                byte[] unsupportedReply = new byte[] { 0x05, 0x08, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
                await clientStream.WriteAsync(unsupportedReply, token).ConfigureAwait(false);
                await clientStream.FlushAsync(token).ConfigureAwait(false);
                return null;
            }

            addrAndPort = new byte[domainLen + 2];
            await clientStream.ReadExactlyAsync(addrAndPort, token).ConfigureAwait(false);
            domain = Encoding.ASCII.GetString(addrAndPort, 0, domainLen);
            portHi = addrAndPort[domainLen];
            portLo = addrAndPort[domainLen + 1];
        }
        else
        {
            byte[] unsupportedReply = new byte[] { 0x05, 0x08, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            await clientStream.WriteAsync(unsupportedReply, token).ConfigureAwait(false);
            await clientStream.FlushAsync(token).ConfigureAwait(false);
            return null;
        }

        int backendPort = Volatile.Read(ref _backendPort);
        if (backendPort <= 0)
        {
            byte[] failReply = new byte[] { 0x05, 0x01, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            await clientStream.WriteAsync(failReply, token).ConfigureAwait(false);
            await clientStream.FlushAsync(token).ConfigureAwait(false);
            return null;
        }

        Interlocked.Increment(ref _totalConnections);

        if (cmd == 0x01 && atyp == 0x03 && domain is not null)
        {
            try
            {
                (TcpClient? upstream, byte[]? socksReply) = await ConnectBackendWithFailoverAsync(
                    domain,
                    (ushort)((portHi << 8) | portLo),
                    cmd,
                    rsv,
                    backendPort,
                    token).ConfigureAwait(false);

                if (upstream is not null && socksReply is not null)
                {
                    await clientStream.WriteAsync(socksReply, token).ConfigureAwait(false);
                    await clientStream.FlushAsync(token).ConfigureAwait(false);
                    return upstream;
                }

                if (clientStream.CanWrite)
                {
                    byte[] failureReply = socksReply is { Length: >= 4 }
                        ? socksReply
                        : new byte[] { 0x05, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
                    await clientStream.WriteAsync(failureReply, token).ConfigureAwait(false);
                    await clientStream.FlushAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
            }

            return null;
        }

        byte[] forwardedRequest;
        if (atyp == 0x01)
        {
            forwardedRequest = new byte[4 + 6];
            forwardedRequest[0] = 0x05;
            forwardedRequest[1] = cmd;
            forwardedRequest[2] = rsv;
            forwardedRequest[3] = 0x01;
            Buffer.BlockCopy(addrAndPort, 0, forwardedRequest, 4, 6);
        }
        else if (atyp == 0x04)
        {
            forwardedRequest = new byte[4 + 18];
            forwardedRequest[0] = 0x05;
            forwardedRequest[1] = cmd;
            forwardedRequest[2] = rsv;
            forwardedRequest[3] = 0x04;
            Buffer.BlockCopy(addrAndPort, 0, forwardedRequest, 4, 18);
        }
        else
        {
            forwardedRequest = new byte[4 + 1 + addrAndPort.Length];
            forwardedRequest[0] = 0x05;
            forwardedRequest[1] = cmd;
            forwardedRequest[2] = rsv;
            forwardedRequest[3] = 0x03;
            forwardedRequest[4] = (byte)(addrAndPort.Length - 2);
            Buffer.BlockCopy(addrAndPort, 0, forwardedRequest, 5, addrAndPort.Length);
        }

        try
        {
            return await ConnectToBackendSocksAsync(backendPort, forwardedRequest, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            try
            {
                if (clientStream.CanWrite)
                {
                    byte[] connRefused = new byte[] { 0x05, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
                    await clientStream.WriteAsync(connRefused, token).ConfigureAwait(false);
                    await clientStream.FlushAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }

            return null;
        }
    }

    private static bool IsHttpMethodStart(byte firstByte) =>
        firstByte is (byte)'C' or (byte)'G' or (byte)'H' or (byte)'P' or (byte)'D' or (byte)'O';

    private async Task<TcpClient?> HandleHttpHandshakeAsync(NetworkStream clientStream, byte[] initialTwoBytes, CancellationToken token)
    {
        byte[] headerBuffer = new byte[16384];
        headerBuffer[0] = initialTwoBytes[0];
        headerBuffer[1] = initialTwoBytes[1];
        int totalLen = 2;
        int terminatorIdx = -1;

        while (totalLen < headerBuffer.Length)
        {
            int read = await clientStream.ReadAsync(headerBuffer.AsMemory(totalLen, headerBuffer.Length - totalLen), token).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            totalLen += read;
            terminatorIdx = headerBuffer.AsSpan(0, totalLen).IndexOf("\r\n\r\n"u8);
            if (terminatorIdx >= 0)
            {
                break;
            }
        }

        if (terminatorIdx < 0)
        {
            await WriteHttpErrorAsync(clientStream, "431 Request Header Fields Too Large", token).ConfigureAwait(false);
            return null;
        }

        int headerEnd = terminatorIdx + 4;
        string headerText = Encoding.ASCII.GetString(headerBuffer, 0, terminatorIdx);
        int lineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd >= 0 ? headerText[..lineEnd] : headerText;
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            await WriteHttpErrorAsync(clientStream, "400 Bad Request", token).ConfigureAwait(false);
            return null;
        }

        string method = parts[0].ToUpperInvariant();
        string target = parts[1];
        string httpVersion = parts[2];
        bool isConnect = method == "CONNECT";

        string host;
        ushort port;
        string? rewrittenRequest = null;

        if (isConnect)
        {
            if (!TryParseHostPort(target, out host, out port))
            {
                await WriteHttpErrorAsync(clientStream, "400 Bad Request", token).ConfigureAwait(false);
                return null;
            }
        }
        else if (TryBuildForwardHttpRequest(method, target, httpVersion, lineEnd >= 0 ? headerText[(lineEnd + 2)..] : string.Empty, out host, out port, out rewrittenRequest))
        {
        }
        else
        {
            await WriteHttpErrorAsync(clientStream, "400 Bad Request", token).ConfigureAwait(false);
            return null;
        }

        int backendPort = Volatile.Read(ref _backendPort);
        if (backendPort <= 0)
        {
            await WriteHttpErrorAsync(clientStream, "502 Bad Gateway", token).ConfigureAwait(false);
            return null;
        }

        Interlocked.Increment(ref _totalConnections);

        TcpClient? upstream = null;
        try
        {
            (upstream, _) = await ConnectBackendWithFailoverAsync(host, port, 0x01, 0x00, backendPort, token).ConfigureAwait(false);
            if (upstream is null)
            {
                await WriteHttpErrorAsync(clientStream, "502 Bad Gateway", token).ConfigureAwait(false);
                return null;
            }

            var upstreamStream = upstream.GetStream();

            if (isConnect)
            {
                byte[] established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                await clientStream.WriteAsync(established, token).ConfigureAwait(false);
                await clientStream.FlushAsync(token).ConfigureAwait(false);
            }
            else if (rewrittenRequest is not null)
            {
                byte[] forwardHeaderBytes = Encoding.ASCII.GetBytes(rewrittenRequest);
                await upstreamStream.WriteAsync(forwardHeaderBytes, token).ConfigureAwait(false);
            }

            if (totalLen > headerEnd)
            {
                await upstreamStream.WriteAsync(headerBuffer.AsMemory(headerEnd, totalLen - headerEnd), token).ConfigureAwait(false);
            }

            if (!isConnect || totalLen > headerEnd)
            {
                await upstreamStream.FlushAsync(token).ConfigureAwait(false);
            }

            return upstream;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            upstream?.Dispose();
            return null;
        }
        catch (Exception)
        {
            upstream?.Dispose();
            try
            {
                if (clientStream.CanWrite)
                {
                    await WriteHttpErrorAsync(clientStream, "502 Bad Gateway", token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }

            return null;
        }
    }

    private static bool TryBuildForwardHttpRequest(
        string method,
        string target,
        string httpVersion,
        string rawHeaders,
        out string host,
        out ushort port,
        out string rewrittenRequest)
    {
        host = string.Empty;
        port = 80;
        rewrittenRequest = string.Empty;

        if (method is not ("GET" or "HEAD" or "POST" or "PUT" or "DELETE" or "OPTIONS" or "PATCH"))
        {
            return false;
        }

        string relativePath = "/";
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            host = uri.Host;
            port = uri.Port > 0 ? (ushort)uri.Port : (ushort)80;
            relativePath = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        }
        else if (target.StartsWith('/'))
        {
            relativePath = target;
        }
        else
        {
            return false;
        }

        var sb = new StringBuilder();
        sb.Append(method).Append(' ').Append(relativePath).Append(' ').Append(httpVersion).Append("\r\n");

        string? hostHeaderAuthority = null;
        if (!string.IsNullOrEmpty(rawHeaders))
        {
            foreach (string line in rawHeaders.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string name = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();

                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    hostHeaderAuthority = value;
                }

                if (name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("Connection: close\r\n");
                    continue;
                }

                sb.Append(line).Append("\r\n");
            }
        }

        if (string.IsNullOrEmpty(host))
        {
            if (string.IsNullOrWhiteSpace(hostHeaderAuthority))
            {
                return false;
            }

            if (TryParseHostPort(hostHeaderAuthority, out string parsedHost, out ushort parsedPort))
            {
                host = parsedHost;
                port = parsedPort;
            }
            else
            {
                host = hostHeaderAuthority.Trim();
                port = 80;
            }
        }

        sb.Append("\r\n");
        rewrittenRequest = sb.ToString();
        return !string.IsNullOrEmpty(host);
    }

    private async Task<(TcpClient? Upstream, byte[]? Socks5Reply)> ConnectBackendWithFailoverAsync(
        string host,
        ushort port,
        byte cmd,
        byte rsv,
        int backendPort,
        CancellationToken token)
    {
        byte portHi = (byte)(port >> 8);
        byte portLo = (byte)(port & 0xFF);
        bool isDirectIpv4 = IPAddress.TryParse(host, out var directIp) && directIp.AddressFamily == AddressFamily.InterNetwork;

        IPAddress[] candidates = await _dnsService.ResolveIPv4CandidatesAsync(host, backendPort, token).ConfigureAwait(false);
        var attempts = new List<(IPAddress? Ip, byte[] Request)>(MaxCandidateAttempts + 1);

        int count = Math.Min(candidates.Length, MaxCandidateAttempts);
        for (int i = 0; i < count; i++)
        {
            IPAddress ip = candidates[i];
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                attempts.Add((ip, BuildSocks5IpRequest(cmd, rsv, ip, portHi, portLo)));
            }
        }

        if (!isDirectIpv4)
        {
            attempts.Add((null, BuildSocks5DomainRequest(cmd, rsv, host, portHi, portLo)));
        }

        byte[]? lastReply = null;
        for (int i = 0; i < attempts.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (candidateIp, requestBytes) = attempts[i];
            TcpClient? upstream = null;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            attemptCts.CancelAfter(BackendAttemptTimeoutMs);

            try
            {
                upstream = await ConnectToBackendSocksAsync(backendPort, requestBytes, attemptCts.Token).ConfigureAwait(false);
                byte[]? replyBytes = await ReadSocks5ConnectReplyBytesAsync(upstream.GetStream(), attemptCts.Token).ConfigureAwait(false);
                if (replyBytes is { Length: >= 4 } && replyBytes[0] == 0x05 && replyBytes[1] == 0x00)
                {
                    return (upstream, replyBytes);
                }

                lastReply = replyBytes;
                upstream.Dispose();
                if (candidateIp is not null)
                {
                    _dnsService.ReportIpFailure(host, candidateIp);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                upstream?.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or InvalidOperationException)
            {
                upstream?.Dispose();
                if (candidateIp is not null)
                {
                    _dnsService.ReportIpFailure(host, candidateIp);
                }
            }
        }

        return (null, lastReply);
    }

    private static byte[] BuildSocks5IpRequest(byte cmd, byte rsv, IPAddress ip, byte portHi, byte portLo)
    {
        Span<byte> ipBytes = stackalloc byte[4];
        ip.TryWriteBytes(ipBytes, out _);
        return new byte[]
        {
            0x05,
            cmd,
            rsv,
            0x01,
            ipBytes[0],
            ipBytes[1],
            ipBytes[2],
            ipBytes[3],
            portHi,
            portLo
        };
    }

    private static byte[] BuildSocks5DomainRequest(byte cmd, byte rsv, string host, byte portHi, byte portLo)
    {
        byte[] hostBytes = Encoding.ASCII.GetBytes(host);
        int hostLen = Math.Min(hostBytes.Length, 255);
        byte[] request = new byte[4 + 1 + hostLen + 2];
        request[0] = 0x05;
        request[1] = cmd;
        request[2] = rsv;
        request[3] = 0x03;
        request[4] = (byte)hostLen;
        Buffer.BlockCopy(hostBytes, 0, request, 5, hostLen);
        request[5 + hostLen] = portHi;
        request[6 + hostLen] = portLo;
        return request;
    }

    private static async Task<TcpClient> ConnectToBackendSocksAsync(int backendPort, byte[] forwardedRequest, CancellationToken token)
    {
        var upstream = new TcpClient { NoDelay = true };
        try
        {
            await upstream.ConnectAsync(IPAddress.Loopback, backendPort, token).ConfigureAwait(false);
            var upstreamStream = upstream.GetStream();

            byte[] upstreamGreeting = new byte[] { 0x05, 0x01, 0x00 };
            await upstreamStream.WriteAsync(upstreamGreeting, token).ConfigureAwait(false);
            await upstreamStream.FlushAsync(token).ConfigureAwait(false);

            byte[] upstreamChoice = new byte[2];
            await upstreamStream.ReadExactlyAsync(upstreamChoice, token).ConfigureAwait(false);
            if (upstreamChoice[0] != 0x05 || upstreamChoice[1] != 0x00)
            {
                throw new InvalidOperationException("Upstream SOCKS5 handshake rejected.");
            }

            await upstreamStream.WriteAsync(forwardedRequest, token).ConfigureAwait(false);
            await upstreamStream.FlushAsync(token).ConfigureAwait(false);
            return upstream;
        }
        catch
        {
            upstream.Dispose();
            throw;
        }
    }

    private static async Task<byte[]?> ReadSocks5ConnectReplyBytesAsync(NetworkStream upstreamStream, CancellationToken token)
    {
        byte[] replyHeader = new byte[4];
        await upstreamStream.ReadExactlyAsync(replyHeader, token).ConfigureAwait(false);
        byte atyp = replyHeader[3];

        if (atyp == 0x01)
        {
            byte[] full = new byte[4 + 6];
            Buffer.BlockCopy(replyHeader, 0, full, 0, 4);
            await upstreamStream.ReadExactlyAsync(full.AsMemory(4, 6), token).ConfigureAwait(false);
            return full;
        }

        if (atyp == 0x04)
        {
            byte[] full = new byte[4 + 18];
            Buffer.BlockCopy(replyHeader, 0, full, 0, 4);
            await upstreamStream.ReadExactlyAsync(full.AsMemory(4, 18), token).ConfigureAwait(false);
            return full;
        }

        if (atyp == 0x03)
        {
            byte[] lenBuf = new byte[1];
            await upstreamStream.ReadExactlyAsync(lenBuf, token).ConfigureAwait(false);
            int restLen = lenBuf[0] + 2;
            byte[] full = new byte[4 + 1 + restLen];
            Buffer.BlockCopy(replyHeader, 0, full, 0, 4);
            full[4] = lenBuf[0];
            await upstreamStream.ReadExactlyAsync(full.AsMemory(5, restLen), token).ConfigureAwait(false);
            return full;
        }

        return null;
    }

    private static bool TryParseHostPort(string authority, out string host, out ushort port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        if (authority.StartsWith('['))
        {
            int closeIdx = authority.IndexOf(']');
            if (closeIdx <= 1)
            {
                return false;
            }

            host = authority[1..closeIdx];
            if (closeIdx + 1 >= authority.Length)
            {
                port = 443;
                return true;
            }

            if (authority[closeIdx + 1] != ':')
            {
                return false;
            }

            return ushort.TryParse(authority[(closeIdx + 2)..], out port) && port > 0;
        }

        int colonIdx = authority.LastIndexOf(':');
        if (colonIdx <= 0 || colonIdx == authority.Length - 1)
        {
            return false;
        }

        host = authority[..colonIdx].Trim();
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        return ushort.TryParse(authority[(colonIdx + 1)..], out port) && port > 0;
    }

    private static async Task WriteHttpErrorAsync(NetworkStream clientStream, string statusLine, CancellationToken token)
    {
        byte[] bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusLine}\r\nConnection: close\r\n\r\n");
        await clientStream.WriteAsync(bytes, token).ConfigureAwait(false);
        await clientStream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task PumpAsync(TcpClient src, TcpClient dst, CancellationTokenSource relayCts, CancellationToken token)
    {
        bool normalEof = false;
        try
        {
            await src.GetStream().CopyToAsync(dst.GetStream(), token).ConfigureAwait(false);
            normalEof = true;
        }
        catch (Exception)
        {
        }
        finally
        {
            try
            {
                if (dst.Connected)
                {
                    dst.Client.Shutdown(SocketShutdown.Send);
                }
            }
            catch (Exception)
            {
            }

            if (!normalEof)
            {
                try
                {
                    relayCts.Cancel();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
