using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DiscordTunnel
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = @"Local\DiscordShield_SingleInstance_Mutex";
        private const string EventName = @"Local\DiscordShield_SingleInstance_Event";
        private const string PipeName = "DiscordShield_Activate_v1";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private Mutex? _mutex;
        private EventWaitHandle? _restoreEvent;
        private RegisteredWaitHandle? _waitHandle;
        private CancellationTokenSource? _pipeCts;

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
                SignalExistingInstance();
                Shutdown(0);
                return;
            }

            StartActivationListeners();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            base.OnStartup(e);
        }

        private static void SignalExistingInstance()
        {
            bool signaled = false;

            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(600);
                byte[] msg = Encoding.UTF8.GetBytes("SHOW");
                pipe.Write(msg, 0, msg.Length);
                pipe.Flush();
                signaled = true;
            }
            catch { }

            if (!signaled)
            {
                try
                {
                    using var evt = EventWaitHandle.OpenExisting(EventName);
                    evt.Set();
                    signaled = true;
                }
                catch { }
            }

            if (!signaled)
            {
                try
                {
                    IntPtr hWnd = FindWindowW(null, "Discord Shield");
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, 9);
                        SetForegroundWindow(hWnd);
                    }
                }
                catch { }
            }
        }

        private void StartActivationListeners()
        {
            try
            {
                _restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                _waitHandle = ThreadPool.RegisterWaitForSingleObject(
                    _restoreEvent,
                    (_, _) => DispatchRestore(),
                    null,
                    -1,
                    false
                );
            }
            catch { }

            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous
                        );
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                        byte[] buffer = new byte[16];
                        int read = await server.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (read > 0)
                        {
                            DispatchRestore();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                }
            }, token);
        }

        private void DispatchRestore()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.RestoreWindow();
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _pipeCts?.Cancel(); } catch { }
            _pipeCts?.Dispose();
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
