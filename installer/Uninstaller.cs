using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Uninstall Discord Shield")]
[assembly: AssemblyDescription("Discord Shield Uninstaller")]
[assembly: AssemblyCompany("Discord Shield Contributors")]
[assembly: AssemblyProduct("Discord Shield")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const int IDYES = 6;
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string ShieldRegistryKey = @"Software\DiscordShield";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            RunFullCleanup();
            return;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        string uninsExe = Path.Combine(baseDir, "unins000.exe");

        if (File.Exists(uninsExe))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uninsExe,
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                });
                return;
            }
            catch { }
        }

        int response = MessageBoxW(
            IntPtr.Zero,
            "Are you sure you want to remove Discord Shield components and settings?",
            "Uninstall Discord Shield",
            MB_YESNO | MB_ICONQUESTION
        );

        if (response != IDYES) return;

        RunFullCleanup();

        MessageBoxW(
            IntPtr.Zero,
            "Discord Shield components and settings have been removed.",
            "Uninstall Discord Shield",
            MB_ICONINFORMATION
        );
    }

    private static void RunFullCleanup()
    {
        KillProcess("DiscordTunnel");
        KillProcess("DiscordShieldEngine");
        KillProcess("ciadpi");
        KillProcess("goodbyedpi");
        StopService("WinDivert");
        DisableBrowserProxy();
        CleanupLegacyHosts();
        CleanupDiscordBridge();
        CleanupRegistryAndShortcuts();
    }

    private static void KillProcess(string name)
    {
        try
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(500);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
    }

    private static void StopService(string serviceName)
    {
        try
        {
            string scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
            if (!File.Exists(scPath)) return;
            using (Process proc = Process.Start(new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = "stop " + serviceName,
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                if (proc != null) proc.WaitForExit(1000);
            }
        }
        catch { }
    }

    private static void DisableBrowserProxy()
    {
        try
        {
            using (var internetKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
            {
                if (internetKey == null) return;
                object val = internetKey.GetValue("AutoConfigURL");
                string currentUrl = val as string;
                if (string.IsNullOrEmpty(currentUrl) || currentUrl.IndexOf("127.0.0.1:1085", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                string previousUrl = null;
                using (var appKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ShieldRegistryKey, false))
                {
                    if (appKey != null) previousUrl = appKey.GetValue("SavedAutoConfigURL") as string;
                }

                if (!string.IsNullOrWhiteSpace(previousUrl))
                {
                    internetKey.SetValue("AutoConfigURL", previousUrl, Microsoft.Win32.RegistryValueKind.String);
                }
                else
                {
                    internetKey.DeleteValue("AutoConfigURL", false);
                }

                InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
            }
        }
        catch { }
    }

    private static void CleanupLegacyHosts()
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
        }
        catch { }
    }

    private static void CleanupDiscordBridge()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string flavor in new string[] { "Discord", "DiscordPTB", "DiscordCanary" })
            {
                string basePath = Path.Combine(localAppData, flavor);
                if (!Directory.Exists(basePath)) continue;

                foreach (string appDir in Directory.GetDirectories(basePath, "app-*"))
                {
                    TryDeleteFile(Path.Combine(appDir, "drover.ini"));
                    TryDeleteFile(Path.Combine(appDir, "version.dll"));
                }
            }

            foreach (string folder in new string[] { "DiscordTunnel", "DiscordShield" })
            {
                string appDataCore = Path.Combine(localAppData, folder);
                if (Directory.Exists(appDataCore))
                {
                    try { Directory.Delete(appDataCore, true); } catch { }
                }
            }
        }
        catch { }
    }

    private static void CleanupRegistryAndShortcuts()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ShieldRegistryKey, false);
        }
        catch { }

        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Discord Shield.lnk"));
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Discord Shield.lnk"));
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Discord Shield.lnk"));
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Discord Shield.lnk"));
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Discord Shield.lnk"));

        string groupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Discord Shield");
        if (Directory.Exists(groupDir))
        {
            try { Directory.Delete(groupDir, true); } catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
