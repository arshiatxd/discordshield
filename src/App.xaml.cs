using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using DiscordTunnel.Services;

namespace DiscordTunnel
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = @"Local\DiscordShield_SingleInstance_Mutex";
        private const string EventName = @"Local\DiscordShield_SingleInstance_Event";

        private Mutex? _mutex;
        private EventWaitHandle? _restoreEvent;
        private RegisteredWaitHandle? _waitHandle;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch
            {
                createdNew = false;
            }

            if (!createdNew)
            {
                try
                {
                    using var existingEvent = EventWaitHandle.OpenExisting(EventName);
                    existingEvent.Set();
                }
                catch { }

                try
                {
                    SystemUtilities.PostMessage(SystemUtilities.HwndBroadcast, SystemUtilities.WmShowAppMessage, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                try
                {
                    var current = Process.GetCurrentProcess();
                    foreach (var proc in Process.GetProcessesByName(current.ProcessName))
                    {
                        if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
                        {
                            SystemUtilities.ShowWindow(proc.MainWindowHandle, 9);
                            SystemUtilities.SetForegroundWindow(proc.MainWindowHandle);
                            break;
                        }
                    }
                }
                catch { }

                Shutdown();
                return;
            }

            try
            {
                _restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                _waitHandle = ThreadPool.RegisterWaitForSingleObject(
                    _restoreEvent,
                    (_, _) => Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (Current?.MainWindow is MainWindow window)
                        {
                            window.RestoreWindow();
                        }
                    }),
                    null,
                    -1,
                    false
                );
            }
            catch { }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _waitHandle?.Unregister(null);
            _restoreEvent?.Dispose();
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
