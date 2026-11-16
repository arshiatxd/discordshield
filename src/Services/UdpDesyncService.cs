namespace DiscordTunnel.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class UdpDesyncService : IDisposable
{
    private const string CaptureFilter =
        "outbound and !impostor and !loopback and ip and udp and (udp.DstPort == 3478 or udp.DstPort == 19302 or udp.PayloadLength == 74 or (udp.DstPort >= 19294 and udp.DstPort <= 19344) or (udp.DstPort >= 49152 and udp.DstPort <= 65535))";

    private const int WinDivertLayerNetwork = 0;
    private const int WinDivertShutdownBoth = 3;
    private const int PacketBufferSize = 65536;
    private const int DecoyBufferSize = 2048;
    private const int AddressBufferSize = 80;
    private const int HeaderPeekSize = 160;
    private const long DiscoveryCooldownMs = 400;
    private const long FlowTimeoutMs = 180000;

    private static readonly byte[] StunDecoyPayload = Convert.FromHexString(
        "000100502112a4425a4b4b7568477865706e64300006000951427a503a746e576b000000c0570004000003e7802a0008b6bcef7bb3f7289800250000002400046e001eff00080014aa4f94f91ff5cebedfec9e66add3dd9bb795ed6e802800042b1fb635");
    private static readonly byte[] ZeroControlPayload = new byte[] { 0x00 };
    private static readonly byte[] OneControlPayload = new byte[] { 0x01 };

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate IntPtr WinDivertOpenDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        int layer,
        short priority,
        ulong flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WinDivertRecvDelegate(
        IntPtr handle,
        IntPtr pPacket,
        uint packetLen,
        out uint pRecvLen,
        IntPtr pAddr);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WinDivertSendDelegate(
        IntPtr handle,
        IntPtr pPacket,
        uint packetLen,
        out uint pSendLen,
        IntPtr pAddr);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate uint WinDivertHelperCalcChecksumsDelegate(
        IntPtr pPacket,
        uint packetLen,
        IntPtr pAddr,
        ulong flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WinDivertShutdownDelegate(IntPtr handle, int how);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WinDivertCloseDelegate(IntPtr handle);

    private readonly object _syncRoot = new();
    private IntPtr _moduleHandle;
    private WinDivertOpenDelegate? _winDivertOpen;
    private WinDivertRecvDelegate? _winDivertRecv;
    private WinDivertSendDelegate? _winDivertSend;
    private WinDivertHelperCalcChecksumsDelegate? _winDivertCalcChecksums;
    private WinDivertShutdownDelegate? _winDivertShutdown;
    private WinDivertCloseDelegate? _winDivertClose;

    private IntPtr _divertHandle = new(-1);
    private CancellationTokenSource? _cts;
    private Thread? _workerThread;
    private byte[] _quicDecoyPayload = BuildFallbackQuicPayload();
    private long _desyncedPackets;
    private int _lastErrorCode;
    private bool _isActive;
    private bool _disposed;

    public bool IsActive => Volatile.Read(ref _isActive);

    public long DesyncedPackets => Interlocked.Read(ref _desyncedPackets);

    public int LastErrorCode => Volatile.Read(ref _lastErrorCode);

    public bool Start(string coreDir)
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            Volatile.Write(ref _lastErrorCode, 0);

            if (!EnsureNativeLoaded(coreDir))
            {
                return false;
            }

            _quicDecoyPayload = LoadQuicPayload(coreDir);
            Interlocked.Exchange(ref _desyncedPackets, 0);

            IntPtr opened = OpenHandle(out int lastError);
            if (!IsValidHandle(opened))
            {
                Volatile.Write(ref _lastErrorCode, lastError);
                return false;
            }

            _divertHandle = opened;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _workerThread = new Thread(() => WorkerLoop(opened, token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "DiscordShield-UdpDesync"
            };

            Volatile.Write(ref _isActive, true);
            _workerThread.Start();
            return true;
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
        }
    }

    private void StopCore()
    {
        Volatile.Write(ref _isActive, false);

        var cts = _cts;
        _cts = null;
        if (cts != null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        IntPtr handle = _divertHandle;
        _divertHandle = new IntPtr(-1);
        if (IsValidHandle(handle))
        {
            try { _winDivertShutdown?.Invoke(handle, WinDivertShutdownBoth); }
            catch { }

            try { _winDivertClose?.Invoke(handle); }
            catch { }
        }

        var thread = _workerThread;
        _workerThread = null;
        if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
        {
            thread.Join(1500);
        }

        cts?.Dispose();
    }

    private IntPtr OpenHandle(out int lastError)
    {
        lastError = 0;
        if (_winDivertOpen == null)
        {
            return new IntPtr(-1);
        }

        for (short priority = 510; priority <= 525; priority++)
        {
            IntPtr handle = _winDivertOpen(CaptureFilter, WinDivertLayerNetwork, priority, 0);
            if (IsValidHandle(handle))
            {
                lastError = 0;
                return handle;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != 0)
            {
                lastError = err;
            }
        }

        return new IntPtr(-1);
    }

    private bool EnsureNativeLoaded(string coreDir)
    {
        if (_moduleHandle != IntPtr.Zero && _winDivertOpen != null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(coreDir))
        {
            return false;
        }

        string dllPath = Path.Combine(coreDir, "WinDivert.dll");
        if (!File.Exists(dllPath))
        {
            return false;
        }

        if (!NativeLibrary.TryLoad(dllPath, out IntPtr handle))
        {
            return false;
        }

        if (!TryBindExport(handle, "WinDivertOpen", out _winDivertOpen)
            || !TryBindExport(handle, "WinDivertRecv", out _winDivertRecv)
            || !TryBindExport(handle, "WinDivertSend", out _winDivertSend)
            || !TryBindExport(handle, "WinDivertHelperCalcChecksums", out _winDivertCalcChecksums)
            || !TryBindExport(handle, "WinDivertShutdown", out _winDivertShutdown)
            || !TryBindExport(handle, "WinDivertClose", out _winDivertClose))
        {
            return false;
        }

        _moduleHandle = handle;
        return true;
    }

    private static bool TryBindExport<TDelegate>(IntPtr moduleHandle, string exportName, out TDelegate? del)
        where TDelegate : Delegate
    {
        del = null;
        if (!NativeLibrary.TryGetExport(moduleHandle, exportName, out IntPtr proc))
        {
            return false;
        }

        del = Marshal.GetDelegateForFunctionPointer<TDelegate>(proc);
        return true;
    }

    private static bool IsValidHandle(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    private static byte[] LoadQuicPayload(string coreDir)
    {
        try
        {
            string packetPath = Path.Combine(coreDir, "drover-packet.bin");
            if (File.Exists(packetPath))
            {
                byte[] bytes = File.ReadAllBytes(packetPath);
                if (bytes.Length >= 64 && bytes.Length <= 1400)
                {
                    return bytes;
                }
            }
        }
        catch { }

        return BuildFallbackQuicPayload();
    }

    private static byte[] BuildFallbackQuicPayload()
    {
        byte[] payload = new byte[1200];
        payload[0] = 0xC5;
        payload[4] = 0x01;
        payload[5] = 0x08;
        Random.Shared.NextBytes(payload.AsSpan(6));
        return payload;
    }

    private void WorkerLoop(IntPtr handle, CancellationToken token)
    {
        var recv = _winDivertRecv;
        var send = _winDivertSend;
        var calcChecksums = _winDivertCalcChecksums;
        if (recv == null || send == null || calcChecksums == null)
        {
            Volatile.Write(ref _isActive, false);
            return;
        }

        IntPtr packetBuf = IntPtr.Zero;
        IntPtr decoyBuf = IntPtr.Zero;
        IntPtr addrBuf = IntPtr.Zero;
        IntPtr decoyAddrBuf = IntPtr.Zero;

        byte[] headerPeek = new byte[HeaderPeekSize];
        byte[] addrScratch = new byte[AddressBufferSize];
        byte[] quicPayload = _quicDecoyPayload;
        byte[] quicFrame = new byte[28 + quicPayload.Length];
        byte[] stunFrame = new byte[28 + StunDecoyPayload.Length];
        byte[] controlFrame = new byte[28 + 1];
        var activeFlows = new Dictionary<VoiceFlowKey, FlowState>();

        try
        {
            packetBuf = Marshal.AllocHGlobal(PacketBufferSize);
            decoyBuf = Marshal.AllocHGlobal(DecoyBufferSize);
            addrBuf = Marshal.AllocHGlobal(AddressBufferSize);
            decoyAddrBuf = Marshal.AllocHGlobal(AddressBufferSize);

            while (!token.IsCancellationRequested)
            {
                if (!recv(handle, packetBuf, PacketBufferSize, out uint recvLen, addrBuf))
                {
                    break;
                }

                if (recvLen == 0)
                {
                    continue;
                }

                int peekLen = Math.Min((int)recvLen, HeaderPeekSize);
                if (peekLen >= 28)
                {
                    Marshal.Copy(packetBuf, headerPeek, 0, peekLen);
                    TryDesyncVoicePacket(
                        handle,
                        send,
                        calcChecksums,
                        headerPeek,
                        peekLen,
                        (int)recvLen,
                        addrBuf,
                        decoyAddrBuf,
                        decoyBuf,
                        addrScratch,
                        quicPayload,
                        quicFrame,
                        stunFrame,
                        controlFrame,
                        activeFlows);
                }

                send(handle, packetBuf, recvLen, out _, addrBuf);
            }
        }
        catch { }
        finally
        {
            Volatile.Write(ref _isActive, false);

            if (packetBuf != IntPtr.Zero) Marshal.FreeHGlobal(packetBuf);
            if (decoyBuf != IntPtr.Zero) Marshal.FreeHGlobal(decoyBuf);
            if (addrBuf != IntPtr.Zero) Marshal.FreeHGlobal(addrBuf);
            if (decoyAddrBuf != IntPtr.Zero) Marshal.FreeHGlobal(decoyAddrBuf);
        }
    }

    private void TryDesyncVoicePacket(
        IntPtr handle,
        WinDivertSendDelegate send,
        WinDivertHelperCalcChecksumsDelegate calcChecksums,
        byte[] headerPeek,
        int peekLen,
        int recvLen,
        IntPtr addrBuf,
        IntPtr decoyAddrBuf,
        IntPtr decoyBuf,
        byte[] addrScratch,
        byte[] quicPayload,
        byte[] quicFrame,
        byte[] stunFrame,
        byte[] controlFrame,
        Dictionary<VoiceFlowKey, FlowState> activeFlows)
    {
        int ipVersion = (headerPeek[0] >> 4) & 0x0F;
        if (ipVersion != 4 || headerPeek[9] != 17)
        {
            return;
        }

        int ihl = (headerPeek[0] & 0x0F) * 4;
        if (ihl < 20 || ihl + 8 > peekLen || ihl + 8 > recvLen)
        {
            return;
        }

        int payloadOffset = ihl + 8;
        int udpPayloadLen = recvLen - payloadOffset;
        int availablePayload = peekLen - payloadOffset;
        if (udpPayloadLen <= 0 || availablePayload <= 0)
        {
            return;
        }

        uint dstIp = (uint)((headerPeek[16] << 24) | (headerPeek[17] << 16) | (headerPeek[18] << 8) | headerPeek[19]);
        ushort srcPort = (ushort)((headerPeek[ihl] << 8) | headerPeek[ihl + 1]);
        ushort dstPort = (ushort)((headerPeek[ihl + 2] << 8) | headerPeek[ihl + 3]);

        bool isDiscordDiscovery = udpPayloadLen >= 70
            && udpPayloadLen <= 80
            && availablePayload >= 4
            && headerPeek[payloadOffset] == 0x00
            && headerPeek[payloadOffset + 1] == 0x01
            && ((headerPeek[payloadOffset + 2] == 0x00 && headerPeek[payloadOffset + 3] == 0x46) || udpPayloadLen == 74);

        bool isStun = udpPayloadLen >= 20
            && availablePayload >= 8
            && (headerPeek[payloadOffset] & 0xC0) == 0x00
            && headerPeek[payloadOffset + 4] == 0x21
            && headerPeek[payloadOffset + 5] == 0x12
            && headerPeek[payloadOffset + 6] == 0xA4
            && headerPeek[payloadOffset + 7] == 0x42;

        long nowMs = Environment.TickCount64;
        var flowKey = new VoiceFlowKey(dstIp, srcPort, dstPort);

        if (isDiscordDiscovery || isStun)
        {
            bool shouldDesync;
            if (activeFlows.TryGetValue(flowKey, out var state))
            {
                shouldDesync = (nowMs - state.LastDesyncMs) >= DiscoveryCooldownMs;
                if (shouldDesync)
                {
                    state.LastDesyncMs = nowMs;
                }
                state.ExpiresAtMs = nowMs + FlowTimeoutMs;
            }
            else
            {
                PruneExpiredFlows(activeFlows, nowMs);
                activeFlows[flowKey] = new FlowState
                {
                    LastDesyncMs = nowMs,
                    ExpiresAtMs = nowMs + FlowTimeoutMs
                };
                shouldDesync = true;
            }

            if (!shouldDesync)
            {
                return;
            }

            Marshal.Copy(addrBuf, addrScratch, 0, AddressBufferSize);
            ushort baseIpId = (ushort)((headerPeek[4] << 8) | headerPeek[5]);

            if (isDiscordDiscovery)
            {
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, ZeroControlPayload, controlFrame, 1, baseIpId);
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, OneControlPayload, controlFrame, 1, (ushort)(baseIpId + 1));
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, quicPayload, quicFrame, 4, (ushort)(baseIpId + 2));
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, StunDecoyPayload, stunFrame, 2, (ushort)(baseIpId + 6));
            }
            else
            {
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, ZeroControlPayload, controlFrame, 1, baseIpId);
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, OneControlPayload, controlFrame, 1, (ushort)(baseIpId + 1));
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, StunDecoyPayload, stunFrame, 4, (ushort)(baseIpId + 2));
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, quicPayload, quicFrame, 2, (ushort)(baseIpId + 6));
            }

            return;
        }

        bool isVoicePort = dstPort >= 49152
            || (dstPort >= 19294 && dstPort <= 19344)
            || dstPort == 3478
            || dstPort == 19302;

        if (isVoicePort
            && udpPayloadLen >= 13
            && availablePayload >= 2
            && headerPeek[payloadOffset] == 0x16
            && headerPeek[payloadOffset + 1] == 0xFE)
        {
            bool hasFlow = activeFlows.TryGetValue(flowKey, out var activeState);
            bool cooldownElapsed = hasFlow && (nowMs - activeState!.LastDesyncMs) >= 2000;
            if (!hasFlow || !activeState!.DtlsDesynced || nowMs >= activeState.ExpiresAtMs || cooldownElapsed)
            {
                if (!hasFlow)
                {
                    PruneExpiredFlows(activeFlows, nowMs);
                    activeState = new FlowState
                    {
                        LastDesyncMs = nowMs,
                        ExpiresAtMs = nowMs + FlowTimeoutMs,
                        DtlsDesynced = true
                    };
                    activeFlows[flowKey] = activeState;
                }
                else
                {
                    activeState!.DtlsDesynced = true;
                    activeState.LastDesyncMs = nowMs;
                    activeState.ExpiresAtMs = nowMs + FlowTimeoutMs;
                }

                Marshal.Copy(addrBuf, addrScratch, 0, AddressBufferSize);
                ushort baseIpId = (ushort)((headerPeek[4] << 8) | headerPeek[5]);
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, ZeroControlPayload, controlFrame, 1, baseIpId);
                SendDecoyBurst(handle, send, calcChecksums, headerPeek, ihl, decoyBuf, decoyAddrBuf, addrScratch, quicPayload, quicFrame, 3, (ushort)(baseIpId + 1));
            }
        }
    }

    private void SendDecoyBurst(
        IntPtr handle,
        WinDivertSendDelegate send,
        WinDivertHelperCalcChecksumsDelegate calcChecksums,
        byte[] headerPeek,
        int ihl,
        IntPtr decoyBuf,
        IntPtr decoyAddrBuf,
        byte[] addrScratch,
        byte[] payload,
        byte[] frame,
        int count,
        ushort startIpId)
    {
        int totalLen = 28 + payload.Length;
        if (totalLen > DecoyBufferSize)
        {
            return;
        }

        Buffer.BlockCopy(headerPeek, 0, frame, 0, 20);
        frame[0] = 0x45;
        frame[2] = (byte)(totalLen >> 8);
        frame[3] = (byte)totalLen;
        frame[6] = 0x00;
        frame[7] = 0x00;
        if (frame[8] == 0)
        {
            frame[8] = 64;
        }
        frame[9] = 17;

        frame[20] = headerPeek[ihl];
        frame[21] = headerPeek[ihl + 1];
        frame[22] = headerPeek[ihl + 2];
        frame[23] = headerPeek[ihl + 3];

        ushort udpLen = (ushort)(8 + payload.Length);
        frame[24] = (byte)(udpLen >> 8);
        frame[25] = (byte)udpLen;

        Buffer.BlockCopy(payload, 0, frame, 28, payload.Length);

        for (int i = 0; i < count; i++)
        {
            ushort ipId = unchecked((ushort)(startIpId + 1 + i));
            frame[4] = (byte)(ipId >> 8);
            frame[5] = (byte)ipId;
            frame[10] = 0;
            frame[11] = 0;
            frame[26] = 0;
            frame[27] = 0;

            Marshal.Copy(addrScratch, 0, decoyAddrBuf, AddressBufferSize);
            Marshal.Copy(frame, 0, decoyBuf, totalLen);
            calcChecksums(decoyBuf, (uint)totalLen, decoyAddrBuf, 0);
            if (send(handle, decoyBuf, (uint)totalLen, out _, decoyAddrBuf))
            {
                Interlocked.Increment(ref _desyncedPackets);
            }
        }
    }

    private static void PruneExpiredFlows(Dictionary<VoiceFlowKey, FlowState> activeFlows, long nowMs)
    {
        if (activeFlows.Count < 256)
        {
            return;
        }

        var stale = new List<VoiceFlowKey>();
        foreach (var kvp in activeFlows)
        {
            if (nowMs >= kvp.Value.ExpiresAtMs)
            {
                stale.Add(kvp.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            activeFlows.Remove(stale[i]);
        }

        if (activeFlows.Count >= 512)
        {
            activeFlows.Clear();
        }
    }

    private readonly record struct VoiceFlowKey(uint DstIp, ushort SrcPort, ushort DstPort);

    private sealed class FlowState
    {
        public long LastDesyncMs;
        public long ExpiresAtMs;
        public bool DtlsDesynced;
    }
}
