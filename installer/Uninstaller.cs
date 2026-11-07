using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Uninstall Discord Shield")]
[assembly: AssemblyDescription("Discord Shield Uninstaller")]
[assembly: AssemblyProduct("Discord Shield")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const int IDYES = 6;

    [STAThread]
    private static void Main()
    {
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
            "Are you sure you want to completely remove Discord Shield and all of its components from your computer?",
            "Uninstall Discord Shield",
            MB_YESNO | MB_ICONQUESTION
        );

        if (response != IDYES) return;

        KillProcess("DiscordTunnel");
        KillProcess("DiscordShieldEngine");
        KillProcess("ciadpi");
        KillProcess("goodbyedpi");
        StopService("WinDivert");
        CleanupDiscordBridge();
        CleanupRegistryAndShortcuts();

        MessageBoxW(
            IntPtr.Zero,
            "Discord Shield has been successfully uninstalled.",
            "Uninstall Discord Shield",
            MB_ICONINFORMATION
        );

        SelfDeleteDirectory(baseDir);
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

            string appDataCore = Path.Combine(localAppData, "DiscordTunnel");
            if (Directory.Exists(appDataCore))
            {
                try { Directory.Delete(appDataCore, true); } catch { }
            }
        }
        catch { }
    }

    private static void CleanupRegistryAndShortcuts()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\DiscordShield", false);
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

    private static void SelfDeleteDirectory(string targetDir)
    {
        try
        {
            string cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = cmdPath,
                Arguments = "/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"" + targetDir + "\"",
                WorkingDirectory = Path.GetTempPath(),
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }
}
