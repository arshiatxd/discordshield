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
        private const string ShieldRegistryKey = @"Software\DiscordShield";
        private const string SavedPacValueName = "SavedAutoConfigURL";
        private const string PacPrefix = "http://127.0.0.1:1085/";
        private const string PacEndpoint = "http://127.0.0.1:1085/discord-shield.pac";

        private static readonly string[] DiscordFlavors = { "Discord", "DiscordPTB", "DiscordCanary" };

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

        public static void DeployDiscordBridge(string coreDir)
        {
            try
            {
                string versionDllSource = Path.Combine(coreDir, "version.dll");
                string droverIniSource = Path.Combine(coreDir, "drover.ini");
                if (!File.Exists(versionDllSource) || !File.Exists(droverIniSource)) return;

                long sourceDllLength = new FileInfo(versionDllSource).Length;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                foreach (string flavor in DiscordFlavors)
                {
                    string basePath = Path.Combine(localAppData, flavor);
                    if (!Directory.Exists(basePath)) continue;

                    foreach (string appDir in Directory.GetDirectories(basePath, "app-*"))
                    {
                        try
                        {
                            string targetDll = Path.Combine(appDir, "version.dll");
                            string targetIni = Path.Combine(appDir, "drover.ini");

                            if (!File.Exists(targetDll) || new FileInfo(targetDll).Length != sourceDllLength)
                            {
                                File.Copy(versionDllSource, targetDll, true);
                            }

                            if (!File.Exists(targetIni))
                            {
                                File.Copy(droverIniSource, targetIni, true);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static void CleanupDiscordBridge()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                foreach (string flavor in DiscordFlavors)
                {
                    string basePath = Path.Combine(localAppData, flavor);
                    if (!Directory.Exists(basePath)) continue;

                    foreach (string appDir in Directory.GetDirectories(basePath, "app-*"))
                    {
                        DeleteFileQuietly(Path.Combine(appDir, "drover.ini"));
                        DeleteFileQuietly(Path.Combine(appDir, "version.dll"));
                    }
                }
            }
            catch { }
        }

        public static void CleanupLegacyArtifacts()
        {
            try
            {
                string hostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                if (!File.Exists(hostsPath)) return;

                string current = File.ReadAllText(hostsPath);
                const string header = "# Discord Shield Managed Hosts";
                const string footer = "# End Discord Shield Managed Hosts";
                int start = current.IndexOf(header, StringComparison.Ordinal);
                if (start < 0) return;

                int end = current.IndexOf(footer, StringComparison.Ordinal);
                end = end >= 0 ? end + footer.Length : current.Length;

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
                using var internetKey = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
                if (internetKey == null) return;

                if (internetKey.GetValue("AutoConfigURL") is string currentUrl &&
                    !string.IsNullOrWhiteSpace(currentUrl) &&
                    !currentUrl.Contains("127.0.0.1:1085", StringComparison.OrdinalIgnoreCase))
                {
                    using var appKey = Registry.CurrentUser.CreateSubKey(ShieldRegistryKey);
                    appKey?.SetValue(SavedPacValueName, currentUrl, RegistryValueKind.String);
                }

                internetKey.SetValue("AutoConfigURL", PacEndpoint, RegistryValueKind.String);
                RefreshWinINet();
            }
            catch { }
        }

        public static void DisableBrowserProxy()
        {
            StopPacServer();

            try
            {
                using var internetKey = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
                if (internetKey == null) return;

                if (internetKey.GetValue("AutoConfigURL") is string currentUrl &&
                    currentUrl.Contains("127.0.0.1:1085", StringComparison.OrdinalIgnoreCase))
                {
                    using var appKey = Registry.CurrentUser.OpenSubKey(ShieldRegistryKey, writable: true);
                    if (appKey?.GetValue(SavedPacValueName) is string previousUrl &&
                        !string.IsNullOrWhiteSpace(previousUrl))
                    {
                        internetKey.SetValue("AutoConfigURL", previousUrl, RegistryValueKind.String);
                        appKey.DeleteValue(SavedPacValueName, throwOnMissingValue: false);
                    }
                    else
                    {
                        internetKey.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                    }

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
                _pacListener.Prefixes.Add(PacPrefix);
                _pacListener.Start();

                byte[] pacBytes = Encoding.UTF8.GetBytes(
                    "function FindProxyForURL(url, host) {\n" +
                    "  var h = host.toLowerCase();\n" +
                    "  var domains = [\n" +
                    "    \"discord.com\", \"discord.gg\", \"discordapp.com\", \"discordapp.net\",\n" +
                    "    \"discord.media\", \"discordcdn.com\", \"discord.co\", \"discord.gift\",\n" +
                    "    \"discord.new\", \"discord.design\", \"discord.dev\", \"discordstatus.com\",\n" +
                    "    \"dis.gd\", \"inv.wtf\", \"discordsays.com\"\n" +
                    "  ];\n" +
                    "  for (var i = 0; i < domains.length; i++) {\n" +
                    "    var d = domains[i];\n" +
                    "    if (h === d || dnsDomainIs(h, \".\" + d)) {\n" +
                    "      return \"SOCKS5 127.0.0.1:1080; SOCKS 127.0.0.1:1080; DIRECT\";\n" +
                    "    }\n" +
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
                            ctx.Response.Headers["Cache-Control"] = "no-store";
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

        private static void DeleteFileQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
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
