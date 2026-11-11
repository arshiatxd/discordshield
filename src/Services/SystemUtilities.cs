using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DiscordTunnel.Services
{
    public static class SystemUtilities
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionRefresh = 37;
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private const string HostsHeader = "# Discord Shield Managed Hosts";
        private const string HostsFooter = "# End Discord Shield Managed Hosts";
        private const string HostsEntries =
            "# Discord Shield Managed Hosts\r\n" +
            "162.159.137.232 discord.com www.discord.com canary.discord.com ptb.discord.com\r\n" +
            "162.159.137.232 discord.gg www.discord.gg discordapp.com www.discordapp.com\r\n" +
            "162.159.137.232 discordapp.net cdn.discordapp.com media.discordapp.net gateway.discord.gg\r\n" +
            "162.159.137.232 latency.discord.media status.discord.com updates.discord.com dl.discordapp.net\r\n" +
            "# End Discord Shield Managed Hosts";

        private static readonly string[] LegacyDohServers = { "1.1.1.1", "8.8.8.8", "1.0.0.1", "8.8.4.4" };

        private static HttpListener? _pacListener;
        private static CancellationTokenSource? _pacCts;

        public static void TerminateProcessByName(string name)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(400);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static void StopDriverService() => RunSystemTool("sc.exe", "stop WinDivert", 800);

        public static void FlushDnsCache() => RunSystemTool("ipconfig.exe", "/flushdns", 800);

        public static void CleanLegacyDohOverrides()
        {
            foreach (string server in LegacyDohServers)
            {
                RunSystemTool("netsh.exe", $"dns delete encryption server={server}", 300);
            }
        }

        public static void ApplyDiscordHostsMapping()
        {
            try
            {
                string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                if (!File.Exists(hostsPath)) return;

                string current = File.ReadAllText(hostsPath);
                if (current.Contains(HostsHeader, StringComparison.Ordinal)) return;

                string updated = current.TrimEnd() + Environment.NewLine + Environment.NewLine + HostsEntries + Environment.NewLine;
                File.WriteAllText(hostsPath, updated);
                FlushDnsCache();
            }
            catch { }
        }

        public static void RemoveDiscordHostsMapping()
        {
            try
            {
                string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                if (!File.Exists(hostsPath)) return;

                string current = File.ReadAllText(hostsPath);
                int start = current.IndexOf(HostsHeader, StringComparison.Ordinal);
                if (start < 0) return;

                int end = current.IndexOf(HostsFooter, StringComparison.Ordinal);
                if (end >= 0) end += HostsFooter.Length;
                else end = current.Length;

                string updated = current.Remove(start, end - start).TrimEnd();
                File.WriteAllText(hostsPath, string.IsNullOrEmpty(updated) ? string.Empty : updated + Environment.NewLine);
                FlushDnsCache();
            }
            catch { }
        }

        public static void EnableBrowserProxy()
        {
            StartPacServer();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
                key?.SetValue("AutoConfigURL", "http://127.0.0.1:1085/discord-shield.pac", RegistryValueKind.String);
                RefreshWinINet();
            }
            catch { }
        }

        public static void DisableBrowserProxy()
        {
            StopPacServer();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
                if (key?.GetValue("AutoConfigURL") is string url && url.Contains("127.0.0.1:1085", StringComparison.Ordinal))
                {
                    key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                    RefreshWinINet();
                }
            }
            catch { }
        }

        private static void RefreshWinINet()
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }

        private static void StartPacServer()
        {
            if (_pacListener != null) return;

            try
            {
                _pacCts = new CancellationTokenSource();
                var token = _pacCts.Token;

                _pacListener = new HttpListener();
                _pacListener.Prefixes.Add("http://127.0.0.1:1085/");
                _pacListener.Start();

                byte[] pacBytes = Encoding.UTF8.GetBytes(
                    "function FindProxyForURL(url, host) {\n" +
                    "  if (shExpMatch(host, \"*discord*\") || shExpMatch(host, \"*dis.gd\") || shExpMatch(host, \"*inv.wtf\")) {\n" +
                    "    return \"SOCKS5 127.0.0.1:1080; SOCKS 127.0.0.1:1080; DIRECT\";\n" +
                    "  }\n" +
                    "  return \"DIRECT\";\n" +
                    "}\n");

                Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested && _pacListener is { IsListening: true })
                    {
                        try
                        {
                            var ctx = await _pacListener.GetContextAsync().ConfigureAwait(false);
                            ctx.Response.ContentType = "application/x-ns-proxy-autoconfig";
                            ctx.Response.ContentLength64 = pacBytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(pacBytes, 0, pacBytes.Length, token).ConfigureAwait(false);
                            ctx.Response.Close();
                        }
                        catch (OperationCanceledException) { break; }
                        catch (ObjectDisposedException) { break; }
                        catch { }
                    }
                }, token);
            }
            catch { }
        }

        private static void StopPacServer()
        {
            try { _pacCts?.Cancel(); } catch { }
            try { _pacListener?.Stop(); } catch { }
            try { _pacListener?.Close(); } catch { }
            _pacListener = null;
            _pacCts?.Dispose();
            _pacCts = null;
        }

        private static void RunSystemTool(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, fileName);
                if (!File.Exists(path)) return;

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                process?.WaitForExit(timeoutMs);
            }
            catch { }
        }
    }
}
