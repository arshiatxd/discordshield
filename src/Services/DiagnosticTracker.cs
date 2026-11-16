namespace DiscordTunnel.Services;

using System;
using System.Collections.Generic;
using System.Text;

public sealed class DiagnosticTracker
{
    private readonly object _syncRoot = new();
    private readonly List<StepRecord> _steps = new();
    private string _appVersion = "unknown";
    private string _regionCode = "none";
    private bool _isElevated;
    private DateTime _startedUtc = DateTime.UtcNow;
    private string _systemDnsStatus = "pending";
    private string _dohStatus = "pending";
    private long _totalConnections;
    private long _dohResolutions;
    private long _edgeFallbacks;
    private bool _udpDesyncActive;
    private long _udpDesyncPackets;
    private int _udpDesyncError;

    public void BeginSession(string appVersion, string regionCode, bool isElevated)
    {
        lock (_syncRoot)
        {
            var cleanedVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion.Trim();
            if (cleanedVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                cleanedVersion = cleanedVersion.Substring(1);
            }

            _appVersion = cleanedVersion;
            _regionCode = string.IsNullOrWhiteSpace(regionCode) ? "none" : regionCode.Trim();
            _isElevated = isElevated;
            _startedUtc = DateTime.UtcNow;
            _systemDnsStatus = "pending";
            _dohStatus = "pending";
            _totalConnections = 0;
            _dohResolutions = 0;
            _edgeFallbacks = 0;
            _udpDesyncActive = false;
            _udpDesyncPackets = 0;
            _udpDesyncError = 0;
            _steps.Clear();
        }
    }

    public void SetDnsProbe(string systemDnsStatus, string dohStatus)
    {
        lock (_syncRoot)
        {
            _systemDnsStatus = string.IsNullOrWhiteSpace(systemDnsStatus) ? "unknown" : systemDnsStatus.Trim();
            _dohStatus = string.IsNullOrWhiteSpace(dohStatus) ? "unknown" : dohStatus.Trim();
        }
    }

    public void RecordStep(string regionCode, string strategyLabel, bool engineEnabled, bool engineAlive, bool proxyAlive, long elapsedMs, bool success, string detail, string? engineState = null)
    {
        var sanitizedDetail = Sanitize(detail);
        var safeRegion = string.IsNullOrWhiteSpace(regionCode) ? "none" : regionCode.Trim();
        var safeStrategy = string.IsNullOrWhiteSpace(strategyLabel) ? "none" : strategyLabel.Trim();
        var resolvedEngineState = string.IsNullOrWhiteSpace(engineState) ? (engineAlive ? "alive" : "down") : engineState.Trim();

        lock (_syncRoot)
        {
            while (_steps.Count >= 24)
            {
                _steps.RemoveAt(0);
            }

            _steps.Add(new StepRecord(
                safeRegion,
                safeStrategy,
                engineEnabled,
                resolvedEngineState,
                proxyAlive,
                elapsedMs,
                success,
                sanitizedDetail));
        }
    }

    public void SetRelayStats(long totalConnections, long dohResolutions, long edgeFallbacks)
    {
        lock (_syncRoot)
        {
            _totalConnections = totalConnections;
            _dohResolutions = dohResolutions;
            _edgeFallbacks = edgeFallbacks;
        }
    }

    public void SetUdpDesync(bool isActive, long desyncedPackets, int errorCode = 0)
    {
        lock (_syncRoot)
        {
            _udpDesyncActive = isActive;
            _udpDesyncPackets = desyncedPackets;
            _udpDesyncError = isActive ? 0 : errorCode;
        }
    }

    public string BuildReport()
    {
        lock (_syncRoot)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Discord Shield Diagnostics");
            sb.AppendLine($"Version: v{_appVersion}");
            sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"Elevated: {_isElevated}");
            sb.AppendLine($"Region: {_regionCode}");
            sb.AppendLine($"Started: {_startedUtc:u}");
            sb.AppendLine($"System DNS: {_systemDnsStatus}");
            sb.AppendLine($"DoH: {_dohStatus}");
            sb.AppendLine($"Relay: connections={_totalConnections}, doh={_dohResolutions}, edge_fallback={_edgeFallbacks}");
            string udpErrSuffix = !_udpDesyncActive && _udpDesyncError != 0 ? $", err=0x{_udpDesyncError:x8}" : string.Empty;
            sb.AppendLine($"UDP Desync: {(_udpDesyncActive ? "active" : "inactive")} (packets={_udpDesyncPackets}{udpErrSuffix})");
            sb.AppendLine("Steps:");

            if (_steps.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var step in _steps)
                {
                    var outcome = step.Success ? "[PASS]" : "[FAIL]";
                    var proxyStatus = step.ProxyAlive ? "alive" : "down";
                    var suffix = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : $" - {step.Detail}";
                    sb.AppendLine($"  {outcome} {step.RegionCode}/{step.StrategyLabel} engine={step.EngineState}(en={step.EngineEnabled}) proxy={proxyStatus} {step.ElapsedMs}ms{suffix}");
                }
            }

            return sb.ToString().TrimEnd();
        }
    }

    private static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cleaned = input.Trim();

        var userName = Environment.UserName;
        if (!string.IsNullOrEmpty(userName) && userName.Length > 1)
        {
            cleaned = cleaned.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        var machineName = Environment.MachineName;
        if (!string.IsNullOrEmpty(machineName) && machineName.Length > 1)
        {
            cleaned = cleaned.Replace(machineName, "<machine>", StringComparison.OrdinalIgnoreCase);
        }

        var userDomain = Environment.UserDomainName;
        if (!string.IsNullOrEmpty(userDomain) && userDomain.Length > 1)
        {
            cleaned = cleaned.Replace(userDomain, "<domain>", StringComparison.OrdinalIgnoreCase);
        }

        return cleaned;
    }

    private sealed record StepRecord(
        string RegionCode,
        string StrategyLabel,
        bool EngineEnabled,
        string EngineState,
        bool ProxyAlive,
        long ElapsedMs,
        bool Success,
        string Detail);
}
