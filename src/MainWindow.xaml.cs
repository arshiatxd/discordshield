using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DiscordTunnel.Models;
using DiscordTunnel.Services;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace DiscordTunnel
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "1.0.1";
        private const string RegistryPath = @"Software\DiscordShield";
        private const string RegistryRegionValue = "Region";

        private readonly IReadOnlyList<RegionOption> _regions = RegionCatalog.All;
        private readonly GatewayVerifier _verifier = new();

        private bool _isConnected;
        private bool _shutdownComplete;
        private int _isProcessing;
        private int _selectedRegionIndex;
        private Process? _tunnelProcess;
        private Process? _proxyProcess;
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private string _coreDir = string.Empty;
        private string _enginePath = string.Empty;
        private string _proxyPath = string.Empty;
        private string _activeProfileName = "Auto-Detect";
        private CancellationTokenSource? _pingCts;
        private CancellationTokenSource? _connectCts;

        public MainWindow()
        {
            InitializeComponent();
            InitializeVisuals();

            RegionList.ItemsSource = _regions;
            SelectRegion(LoadSelectedRegion(), persist: false);

            InitializeCoreEngine();
            SetupSystemTray();
            Task.Run(SystemUtilities.CleanLegacyDohOverrides);

            Closing += MainWindow_Closing;
            Loaded += (_, _) => PlayEntrance();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (OverlayLayer.Visibility == Visibility.Visible || PrivacyModal.Visibility == Visibility.Visible) return;

            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            if (PrivacyModal.Visibility == Visibility.Visible)
            {
                HidePrivacy();
                e.Handled = true;
            }
            else if (_openPanel != null)
            {
                ClosePanel();
                e.Handled = true;
            }
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayCheckBox.IsChecked == true)
            {
                Hide();
                if (_isConnected)
                {
                    _trayIcon?.ShowBalloonTip(1200, "Discord Shield", "Running in the tray. Discord traffic is protected.", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayCheckBox.IsChecked == true) Hide();
            else Close();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e) => Shutdown();

        private void ForceExit()
        {
            Shutdown();
            Application.Current.Shutdown();
        }

        private void Shutdown()
        {
            if (_shutdownComplete) return;
            _shutdownComplete = true;

            _isConnected = false;
            StopBackgroundPing();
            CancelConnection();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            StopAllEnginesSync();
            _verifier.Dispose();
        }

        private void SetupSystemTray()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = $"Discord Shield v{AppVersion} - Disconnected",
                Icon = LoadTrayIcon(),
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Show Discord Shield", null, (_, _) => RestoreWindow()));
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Connect", null, (_, _) => Dispatcher.Invoke(() => ConnectBtn_Click(this, new RoutedEventArgs()))));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Exit", null, (_, _) => Dispatcher.Invoke(ForceExit)));

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => RestoreWindow();
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                string localIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(localIcon)) return new System.Drawing.Icon(localIcon, 32, 32);

                var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
                if (stream != null) return new System.Drawing.Icon(stream, 32, 32);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException) { }

            return System.Drawing.SystemIcons.Shield;
        }

        private void UpdateTrayState(string tooltip, string toggleLabel)
        {
            if (_trayIcon == null) return;

            _trayIcon.Text = tooltip;
            if (_trayIcon.ContextMenuStrip is { Items.Count: > 1 } strip) strip.Items[1].Text = toggleLabel;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void InitializeCoreEngine()
        {
            try
            {
                string localCore = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core");
                _coreDir = Directory.Exists(localCore)
                    ? localCore
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordShield", "Core");

                Directory.CreateDirectory(_coreDir);
                _enginePath = Path.Combine(_coreDir, "DiscordShieldEngine.exe");
                _proxyPath = Path.Combine(_coreDir, "ciadpi.exe");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Failed to initialize core engine:\n{ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private int LoadSelectedRegion()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key?.GetValue(RegistryRegionValue) is string code)
                {
                    for (int i = 0; i < _regions.Count; i++)
                    {
                        if (_regions[i].Code == code) return i;
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }

            return RegionCatalog.DefaultIndex;
        }

        private static void SaveSelectedRegion(string code)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(RegistryRegionValue, code, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }

        private void SelectRegion(int index, bool persist)
        {
            if (index < 0 || index >= _regions.Count) index = RegionCatalog.DefaultIndex;

            _selectedRegionIndex = index;
            for (int i = 0; i < _regions.Count; i++) _regions[i].IsSelected = i == index;

            var option = _regions[index];
            RegionSelector.Content = option;

            if (persist) SaveSelectedRegion(option.Code);

            if (!_isConnected && Volatile.Read(ref _isProcessing) == 0)
            {
                SetText(StatusSubLabel, $"Ready for {option.ShortName}", animate: persist);
            }
        }

        private void RegionSelector_Click(object sender, RoutedEventArgs e)
        {
            if (_openPanel == RegionDropdown)
            {
                ClosePanel();
                return;
            }

            Point anchor = RegionSelector.TransformToAncestor(RootContainer).Transform(new Point(0, 0));
            double height = RegionDropdown.ActualHeight > 0 ? RegionDropdown.ActualHeight : (_regions.Count * 40 + 12);
            double below = RootContainer.ActualHeight - (anchor.Y + RegionSelector.ActualHeight) - 16;
            bool openBelow = below >= height + 6;
            double top = openBelow ? anchor.Y + RegionSelector.ActualHeight + 6 : Math.Max(56, anchor.Y - height - 6);

            RegionSelector.Tag = "Open";
            OpenPanel(RegionDropdown, RegionDropdownScale, anchor.X, top, new Point(0.5, openBelow ? 0 : 1));
        }

        private void RegionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: RegionOption option })
            {
                SelectRegion(RegionCatalog.IndexOf(option), persist: true);
            }

            ClosePanel();
        }

        private void MenuBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_openPanel == AppMenu)
            {
                ClosePanel();
                return;
            }

            Point anchor = MenuBtn.TransformToAncestor(RootContainer).Transform(new Point(0, 0));
            double left = anchor.X + MenuBtn.ActualWidth - AppMenu.Width;
            OpenPanel(AppMenu, AppMenuScale, left, anchor.Y + MenuBtn.ActualHeight + 6, new Point(1, 0));
        }

        private void PrivacyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ClosePanel();
            ShowPrivacy();
        }

        private void PrivacyCloseBtn_Click(object sender, RoutedEventArgs e) => HidePrivacy();

        private void PrivacyModal_BackgroundMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == PrivacyModal) HidePrivacy();
        }

        private void OverlayLayer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource != OverlayLayer) return;

            ClosePanel();
            e.Handled = true;
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _isProcessing, 1) == 1) return;

            try
            {
                if (_isConnected) await StopTunnelAsync();
                else await StartTunnelAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }

        private async void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelBtn.IsEnabled = false;
            if (_isConnected)
            {
                await StopTunnelAsync();
                return;
            }

            ConnectBtn.Content = "Cancelling...";
            CancelConnection();
        }

        private void CancelConnection()
        {
            try { _connectCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private async Task StartTunnelAsync()
        {
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            var token = _connectCts.Token;

            try
            {
                BeginConnectingUi();

                if (!File.Exists(_enginePath) || !File.Exists(_proxyPath)) InitializeCoreEngine();

                await Task.Run(() =>
                {
                    StopAllEnginesSync();
                    Thread.Sleep(200);
                    SystemUtilities.FlushDnsCache();
                }, token);

                var profiles = await ResolveProfilesAsync(token);
                bool autoMode = profiles.Count > 1;
                string failure = "Discord Gateway unreachable.";
                long latency = 0;
                bool connected = false;

                for (int i = 0; i < profiles.Count && !connected; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var profile = profiles[i];
                    SetText(StatusSubLabel, autoMode
                        ? $"Testing profile ({i + 1}/{profiles.Count}): {profile.ShortName}"
                        : $"Applying {profile.ShortName} profile");
                    SetPill(true, profile.Description);

                    LaunchEngines(profile);
                    await Task.Delay(autoMode ? 650 : 850, token);

                    if (!IsEngineAlive(_proxyProcess) && !IsEngineAlive(_tunnelProcess))
                    {
                        await Task.Run(StopAllEnginesSync);
                        continue;
                    }

                    ConnectBtn.Content = "Verifying...";
                    var result = await _verifier.VerifyAsync(autoMode ? 2 : 3, token);

                    if (result.Success)
                    {
                        connected = true;
                        latency = result.LatencyMs;
                        _activeProfileName = autoMode ? $"Auto ({profile.ShortName})" : profile.ShortName;
                    }
                    else
                    {
                        failure = result.Detail;
                        await Task.Run(() =>
                        {
                            StopAllEnginesSync();
                            Thread.Sleep(200);
                            SystemUtilities.FlushDnsCache();
                        });
                    }
                }

                token.ThrowIfCancellationRequested();

                Process? watched = IsEngineAlive(_proxyProcess) ? _proxyProcess : IsEngineAlive(_tunnelProcess) ? _tunnelProcess : null;
                if (connected && watched != null)
                {
                    AttachEngineExitWatcher(watched);
                    SetUiState(true, latency, _activeProfileName);
                    StartBackgroundPing();
                }
                else
                {
                    await StopTunnelAsync();
                    SetUiFailedState(failure);
                }
            }
            catch (OperationCanceledException)
            {
                await StopTunnelAsync("Connection cancelled");
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
            {
                await StopTunnelAsync();
                SetUiFailedState($"Connection error: {ex.Message}");
            }
            finally
            {
                ShowCancel(false);
                ConnectBtn.IsEnabled = true;
            }
        }

        private async Task<IReadOnlyList<RegionOption>> ResolveProfilesAsync(CancellationToken token)
        {
            var selected = _regions[_selectedRegionIndex];
            if (selected.Code != RegionCatalog.AutoCode) return new[] { selected };

            SetText(StatusSubLabel, "Detecting your network");
            return await RegionCatalog.BuildAutoQueueAsync(token);
        }

        private void LaunchEngines(RegionOption profile)
        {
            if (File.Exists(_proxyPath))
            {
                _proxyProcess = Process.Start(CreateHiddenStartInfo(_proxyPath, profile.ProxyArguments));
            }

            if (File.Exists(_enginePath))
            {
                try { _tunnelProcess = Process.Start(CreateHiddenStartInfo(_enginePath, profile.Arguments)); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }

        private ProcessStartInfo CreateHiddenStartInfo(string fileName, string arguments) => new()
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _coreDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        private static bool IsEngineAlive(Process? process)
        {
            try { return process != null && !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        private void AttachEngineExitWatcher(Process process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (sender, _) => Dispatcher.BeginInvoke(async () =>
                {
                    if (_isConnected && (ReferenceEquals(sender, _proxyProcess) || ReferenceEquals(sender, _tunnelProcess)))
                    {
                        await StopTunnelAsync();
                        SetUiFailedState("Tunnel engine terminated unexpectedly.");
                    }
                });
            }
            catch (InvalidOperationException) { }
        }

        private void StartBackgroundPing()
        {
            _pingCts?.Cancel();
            _pingCts?.Dispose();
            _pingCts = new CancellationTokenSource();
            var token = _pingCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(15000, token).ConfigureAwait(false);
                        if (!_isConnected) continue;

                        long latency = await _verifier.PingAsync(token).ConfigureAwait(false);
                        if (latency <= 0) continue;

                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            if (!_isConnected) return;
                            SetText(PingBadgeText, $"{latency} ms", animate: false);
                            SetText(StatusSubLabel, $"Gateway verified  \u2022  {latency} ms latency", animate: false);
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }, token);
        }

        private void StopBackgroundPing()
        {
            try { _pingCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private async Task StopTunnelAsync(string subStatus = "Discord traffic is direct")
        {
            _isConnected = false;
            CancelConnection();
            StopBackgroundPing();
            SetUiState(false, subStatus: subStatus);

            await Task.Run(() =>
            {
                StopAllEnginesSync();
                Thread.Sleep(200);
            });
        }

        private void StopAllEnginesSync()
        {
            StopProcess(ref _proxyProcess);
            StopProcess(ref _tunnelProcess);

            SystemUtilities.TerminateProcessByName("ciadpi");
            SystemUtilities.TerminateProcessByName("DiscordShieldEngine");
            SystemUtilities.TerminateProcessByName("goodbyedpi");
            SystemUtilities.StopDriverService();
        }

        private static void StopProcess(ref Process? process)
        {
            var target = Interlocked.Exchange(ref process, null);
            if (target == null) return;

            try
            {
                target.EnableRaisingEvents = false;
                if (!target.HasExited)
                {
                    target.Kill();
                    target.WaitForExit(300);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            finally
            {
                target.Dispose();
            }
        }
    }
}
