using System;
using System.Diagnostics;
using System.IO;

namespace DiscordTunnel.Services
{
    public static class SystemUtilities
    {
        private static readonly string[] LegacyDohServers = { "1.1.1.1", "8.8.8.8", "1.0.0.1", "8.8.4.4" };

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
