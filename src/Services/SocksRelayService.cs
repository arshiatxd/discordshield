namespace DiscordTunnel.Services;

using System;
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
            var clientStream = client.GetStream();

            byte[] greetingHeader = new byte[2];
            await clientStream.ReadExactlyAsync(greetingHeader, token).ConfigureAwait(false);
            byte ver = greetingHeader[0];
            byte nmethods = greetingHeader[1];

            if (ver != 5 || nmethods == 0)
            {
                return;
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
                return;
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
                return;
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
                    return;
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
                return;
            }

            int backendPort = Volatile.Read(ref _backendPort);
            if (backendPort <= 0)
            {
                byte[] failReply = new byte[] { 0x05, 0x01, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
                await clientStream.WriteAsync(failReply, token).ConfigureAwait(false);
                await clientStream.FlushAsync(token).ConfigureAwait(false);
                return;
            }

            Interlocked.Increment(ref _totalConnections);

            byte[] forwardedRequest;
            if (cmd == 0x01 && atyp == 0x03 && domain is not null)
            {
                IPAddress? resolved = await _dnsService.ResolveIPv4Async(domain, backendPort, token).ConfigureAwait(false);
                if (resolved is { AddressFamily: AddressFamily.InterNetwork })
                {
                    Span<byte> ipBytes = stackalloc byte[4];
                    resolved.TryWriteBytes(ipBytes, out _);
                    forwardedRequest = new byte[]
                    {
                        0x05,
                        0x01,
                        0x00,
                        0x01,
                        ipBytes[0],
                        ipBytes[1],
                        ipBytes[2],
                        ipBytes[3],
                        portHi,
                        portLo
                    };
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
            }
            else if (atyp == 0x01)
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

            upstream = new TcpClient();
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
