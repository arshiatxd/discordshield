using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace DiscordTunnel
{
    public sealed class RegionOption
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string ProxyArguments { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        private bool _isConnected = false;
        private int _isProcessing = 0;
        private Process? _tunnelProcess = null;
        private Process? _proxyProcess = null;
        private NotifyIcon? _trayIcon = null;
        private string _coreDir = string.Empty;
        private string _exePath = string.Empty;
        private string _proxyExePath = string.Empty;
        private string _activeProfileName = "Auto-Detect";

        private readonly List<RegionOption> _regionOptions = new()
        {
            new RegionOption
            {
                Code = "AUTO",
                Name = "Auto-Detect",
                ShortName = "Auto-Detect",
                Description = "Automatic Route Verification",
                Arguments = string.Empty,
                ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
            },
            new RegionOption
            {
                Code = "IR",
                Name = "Iran",
                ShortName = "Iran",
                Description = "OOB Desync and Fake SNI Tunnel",
                Arguments = "-9 --fake-gen 10 --wrong-seq --wrong-chksum --reverse-frag -q --dns-addr 77.88.8.8 --dns-port 1253 --blacklist discord-list.txt",
                ProxyArguments = "--oob 1 --tlsrec 1+s --split 1 --disorder 3+s --auto=torst,ssl_err -s 1 -d 3+s --ttl 4 --fake -1+s --fake-sni www.cloudflare.com -M h,d -p 1080"
            },
            new RegionOption
            {
                Code = "TR",
                Name = "Turkey",
                ShortName = "Turkey",
                Description = "TLS Record Split and SNI Desync",
                Arguments = "-5 --set-ttl 5 -q --blacklist discord-list.txt",
                ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
            },
            new RegionOption
            {
                Code = "RU",
                Name = "Russia",
                ShortName = "Russia",
                Description = "Dynamic TTL and TCP Segmentation",
                Arguments = "-9 --auto-ttl 1-4-10 -q --blacklist discord-list.txt",
                ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s --ttl 5 -p 1080"
            },
            new RegionOption
            {
                Code = "AE",
                Name = "UAE / Middle East",
                ShortName = "UAE / Middle East",
                Description = "VoIP and WebRTC Desync Shield",
                Arguments = "-7 -q --blacklist discord-list.txt",
                ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
            },
            new RegionOption
            {
                Code = "GL",
                Name = "Global",
                ShortName = "Global",
                Description = "Deep Packet Splitting Shield",
                Arguments = "-5 -q --blacklist discord-list.txt",
                ProxyArguments = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s -p 1080"
            }
        };

        private readonly HttpClient _directClient;
        private readonly HttpClient _socksClient;
        private CancellationTokenSource? _pingCts;
        private CancellationTokenSource? _connectCts;

        public MainWindow()
        {
            InitializeComponent();
            RegionCombo.ItemsSource = _regionOptions;
            RegionCombo.SelectedIndex = LoadSelectedRegion();
            InitializeCoreEngine();
            SetupSystemTray();
            CleanDnsSecurityOverrides();

            var directHandler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(2.5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            };
            _directClient = new HttpClient(directHandler) { Timeout = TimeSpan.FromSeconds(3.0) };

            var socksHandler = new SocketsHttpHandler
            {
                Proxy = new WebProxy("socks5://127.0.0.1:1080"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(3.0),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            };
            _socksClient = new HttpClient(socksHandler) { Timeout = TimeSpan.FromSeconds(3.5) };

            Closing += MainWindow_Closing;
        }

        private void MenuBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new System.Windows.Controls.ContextMenu();

            var privacyItem = new System.Windows.Controls.MenuItem
            {
                Header = "Privacy Policy",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2F3F5")),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1F22")),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            privacyItem.Click += (s, ev) =>
            {
                PrivacyModal.Visibility = Visibility.Visible;
            };

            menu.Items.Add(privacyItem);
            menu.PlacementTarget = MenuBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void PrivacyCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            PrivacyModal.Visibility = Visibility.Collapsed;
        }

        private void PrivacyModal_BackgroundMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == PrivacyModal)
            {
                PrivacyModal.Visibility = Visibility.Collapsed;
            }
        }

        private void RegionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RegionCombo?.SelectedItem is RegionOption option)
            {
                SaveSelectedRegion(RegionCombo.SelectedIndex);
                if (!_isConnected && StatusLabel != null && StatusLabel.Text != "CONNECTING...")
                {
                    StatusSubLabel.Text = $"Ready for {option.ShortName}";
                }
            }
        }

        private void SaveSelectedRegion(int index)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\DiscordShield");
                key?.SetValue("SelectedRegion", index, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        private int LoadSelectedRegion()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\DiscordShield");
                var val = key?.GetValue("SelectedRegion");
                if (val is int index && index >= 0 && index < _regionOptions.Count)
                {
                    return index;
                }
            }
            catch { }

            return 0;
        }

        private void InitializeCoreEngine()
        {
            try
            {
                string localCore = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core");
                if (Directory.Exists(localCore))
                {
                    _coreDir = localCore;
                }
                else
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _coreDir = Path.Combine(appData, "DiscordShield", "Core");
                    Directory.CreateDirectory(_coreDir);
                }

                _exePath = Path.Combine(_coreDir, "DiscordShieldEngine.exe");
                _proxyExePath = Path.Combine(_coreDir, "ciadpi.exe");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize core engine:\n{ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void CleanDnsSecurityOverrides()
        {
            try
            {
                string netshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
                if (!File.Exists(netshPath)) return;

                string[] commands = new[]
                {
                    "dns delete encryption server=1.1.1.1",
                    "dns delete encryption server=8.8.8.8",
                    "dns delete encryption server=1.0.0.1",
                    "dns delete encryption server=8.8.4.4"
                };

                foreach (string cmd in commands)
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = netshPath,
                        Arguments = cmd,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    proc?.WaitForExit(300);
                }
            }
            catch { }
        }

        private void SetupSystemTray()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "Discord Shield v1.0.1 - Disconnected",
                Icon = LoadTrayIcon(),
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Show Discord Shield", null, (s, e) => RestoreWindow());
            var toggleItem = new ToolStripMenuItem("Connect", null, (s, e) => Dispatcher.Invoke(() => ConnectBtn_Click(this, new RoutedEventArgs())));
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Dispatcher.Invoke(ForceExit));

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(toggleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => RestoreWindow();
        }

        private Icon LoadTrayIcon()
        {
            try
            {
                string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(localIco))
                {
                    return new Icon(localIco, 32, 32);
                }

                var uri = new Uri("pack://application:,,,/app.ico");
                var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                {
                    return new Icon(stream, 32, 32);
                }
            }
            catch { }
            return SystemIcons.Shield;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayCheckBox.IsChecked == true)
            {
                Hide();
                if (_isConnected)
                {
                    _trayIcon?.ShowBalloonTip(1200, "Discord Shield", "Running smoothly in tray. Discord traffic is protected.", ToolTipIcon.Info);
                }
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayCheckBox.IsChecked == true)
            {
                Hide();
            }
            else
            {
                Close();
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isConnected = false;
            StopBackgroundPing();
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = null;

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            Task.Run(StopAllEnginesSync);

            _directClient.Dispose();
            _socksClient.Dispose();
        }

        private void ForceExit()
        {
            _isConnected = false;
            StopBackgroundPing();
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = null;

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            Task.Run(StopAllEnginesSync);

            System.Windows.Application.Current.Shutdown();
        }

        private void StopAllEnginesSync()
        {
            try
            {
                if (_proxyProcess != null)
                {
                    _proxyProcess.EnableRaisingEvents = false;
                    if (!_proxyProcess.HasExited)
                    {
                        _proxyProcess.Kill();
                        _proxyProcess.WaitForExit(300);
                    }
                    _proxyProcess.Dispose();
                    _proxyProcess = null;
                }
            }
            catch { }

            try
            {
                if (_tunnelProcess != null)
                {
                    _tunnelProcess.EnableRaisingEvents = false;
                    if (!_tunnelProcess.HasExited)
                    {
                        _tunnelProcess.Kill();
                        _tunnelProcess.WaitForExit(300);
                    }
                    _tunnelProcess.Dispose();
                    _tunnelProcess = null;
                }
            }
            catch { }

            TerminateProcessByName("ciadpi");
            TerminateProcessByName("DiscordShieldEngine");
            TerminateProcessByName("goodbyedpi");
            StopDriverService();
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _isProcessing, 1) == 1) return;

            try
            {
                if (!_isConnected)
                {
                    await StartTunnelWithVerificationAsync();
                }
                else
                {
                    await StopTunnelAsync();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }

        private async Task StartTunnelWithVerificationAsync()
        {
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            var token = _connectCts.Token;

            try
            {
                ConnectBtn.IsEnabled = false;
                RegionCombo.IsEnabled = false;
                ConnectBtn.Content = "Connecting...";

                StatusLabel.Text = "CONNECTING...";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE75C"));
                StatusSubLabel.Text = "Starting Discord Shield Engine...";
                SecurityPill.Visibility = Visibility.Visible;
                SecurityPillText.Text = "Initializing Bypass Engine...";

                if (!File.Exists(_exePath) || !File.Exists(_proxyExePath))
                {
                    InitializeCoreEngine();
                }

                await Task.Run(() =>
                {
                    StopAllEnginesSync();
                    Thread.Sleep(200);
                    FlushDnsCache();
                }, token);

                if (token.IsCancellationRequested) return;

                var profilesToTry = new List<RegionOption>();
                int selectedIndex = RegionCombo.SelectedIndex;

                if (selectedIndex > 0 && selectedIndex < _regionOptions.Count)
                {
                    profilesToTry.Add(_regionOptions[selectedIndex]);
                }
                else
                {
                    profilesToTry.Add(_regionOptions[1]);
                    profilesToTry.Add(_regionOptions[4]);
                    profilesToTry.Add(_regionOptions[2]);
                    profilesToTry.Add(_regionOptions[3]);
                    profilesToTry.Add(_regionOptions[5]);
                }

                bool isAutoMode = profilesToTry.Count > 1;
                bool connectedSuccessfully = false;
                long verifiedLatency = 0;
                string lastFailureDetail = "Discord Gateway unreachable.";

                for (int i = 0; i < profilesToTry.Count; i++)
                {
                    if (token.IsCancellationRequested) return;

                    var profile = profilesToTry[i];
                    if (isAutoMode)
                    {
                        StatusSubLabel.Text = $"Testing profile ({i + 1}/{profilesToTry.Count}): {profile.ShortName}...";
                        SecurityPillText.Text = profile.Description;
                    }
                    else
                    {
                        StatusSubLabel.Text = $"Applying {profile.ShortName} bypass tunnel...";
                        SecurityPillText.Text = profile.Description;
                    }

                    if (File.Exists(_proxyExePath))
                    {
                        var proxyInfo = new ProcessStartInfo
                        {
                            FileName = _proxyExePath,
                            Arguments = profile.ProxyArguments,
                            WorkingDirectory = _coreDir,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        _proxyProcess = new Process { StartInfo = proxyInfo };
                        _proxyProcess.Start();
                    }

                    if (File.Exists(_exePath))
                    {
                        try
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = _exePath,
                                Arguments = profile.Arguments,
                                WorkingDirectory = _coreDir,
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            _tunnelProcess = new Process { StartInfo = startInfo };
                            _tunnelProcess.Start();
                        }
                        catch { }
                    }

                    await Task.Delay(isAutoMode ? 650 : 850, token);

                    bool proxyAlive = _proxyProcess != null && !_proxyProcess.HasExited;
                    bool driverAlive = _tunnelProcess != null && !_tunnelProcess.HasExited;

                    if (!proxyAlive && !driverAlive)
                    {
                        await Task.Run(StopAllEnginesSync);
                        continue;
                    }

                    ConnectBtn.Content = "Verifying...";
                    int attempts = isAutoMode ? 2 : 3;
                    var (success, latencyMs, detail) = await VerifyDiscordGatewayPingAsync(token, attempts);

                    if (token.IsCancellationRequested) return;

                    if (success)
                    {
                        connectedSuccessfully = true;
                        verifiedLatency = latencyMs;
                        _activeProfileName = profile.ShortName;
                        break;
                    }
                    else
                    {
                        lastFailureDetail = detail;
                        await Task.Run(() =>
                        {
                            StopAllEnginesSync();
                            Thread.Sleep(200);
                            FlushDnsCache();
                        });
                    }
                }

                bool anyEngineRunning = (_proxyProcess != null && !_proxyProcess.HasExited) || (_tunnelProcess != null && !_tunnelProcess.HasExited);
                if (connectedSuccessfully && anyEngineRunning)
                {
                    if (_proxyProcess != null && !_proxyProcess.HasExited)
                    {
                        AttachEngineExitWatcher(_proxyProcess);
                    }
                    else if (_tunnelProcess != null && !_tunnelProcess.HasExited)
                    {
                        AttachEngineExitWatcher(_tunnelProcess);
                    }

                    SetUiState(true, verifiedLatency, _activeProfileName);
                    StartBackgroundPing();
                }
                else
                {
                    await StopTunnelAsync();
                    SetUiFailedState(lastFailureDetail);
                }
            }
            catch (OperationCanceledException)
            {
                SetUiState(false);
            }
            catch (Exception ex)
            {
                await StopTunnelAsync();
                SetUiFailedState($"Connection error: {ex.Message}");
            }
            finally
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => ConnectBtn.IsEnabled = true);
                }
                else
                {
                    ConnectBtn.IsEnabled = true;
                }
            }
        }

        private void AttachEngineExitWatcher(Process process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        if (_isConnected && (ReferenceEquals(sender, _proxyProcess) || ReferenceEquals(sender, _tunnelProcess)))
                        {
                            await StopTunnelAsync();
                            SetUiFailedState("Tunnel engine terminated unexpectedly.");
                        }
                    });
                };
            }
            catch { }
        }

        private async Task<(bool success, long latencyMs, string detail)> VerifyDiscordGatewayPingAsync(CancellationToken token, int maxAttempts = 3)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) break;

                try
                {
                    var sw = Stopwatch.StartNew();
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/gateway");
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DiscordShield/1.0.1");

                    using var response = await _socksClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    sw.Stop();

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync(token);
                        if (content.Contains("wss://gateway.discord.gg") || content.Contains("\"url\""))
                        {
                            return (true, Math.Max(1, sw.ElapsedMilliseconds), "Gateway verified");
                        }
                    }
                }
                catch { }

                try
                {
                    var sw = Stopwatch.StartNew();
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/gateway");
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DiscordShield/1.0.1");

                    using var response = await _directClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    sw.Stop();

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync(token);
                        if (content.Contains("wss://gateway.discord.gg") || content.Contains("\"url\""))
                        {
                            return (true, Math.Max(1, sw.ElapsedMilliseconds), "Gateway verified");
                        }
                    }
                }
                catch { }

                var curlCheck = await Task.Run(() => VerifyWithNativeCurlSni(), token);
                if (curlCheck.success)
                {
                    return (true, curlCheck.latencyMs, "Gateway verified");
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(400, token);
                }
            }

            return (false, 0, "Discord Gateway unreachable (network filtered)");
        }

        private static (bool success, long latencyMs) VerifyWithNativeCurlSni()
        {
            try
            {
                string curlPath = Path.Combine(Environment.SystemDirectory, "curl.exe");
                if (!File.Exists(curlPath)) return (false, 0);

                var sw = Stopwatch.StartNew();
                var psi = new ProcessStartInfo
                {
                    FileName = curlPath,
                    Arguments = "-s -m 3 --socks5-hostname 127.0.0.1:1080 --resolve discord.com:443:162.159.135.232 https://discord.com/api/v9/gateway",
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3200);
                    sw.Stop();

                    if (output.Contains("gateway.discord.gg") || output.Contains("\"url\""))
                    {
                        return (true, Math.Max(1, sw.ElapsedMilliseconds));
                    }
                }
            }
            catch { }
            return (false, 0);
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
                        if (!_isConnected || token.IsCancellationRequested) continue;

                        long latency = 0;
                        try
                        {
                            var sw = Stopwatch.StartNew();
                            using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/gateway");
                            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DiscordShield/1.0.1");

                            using var response = await _socksClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                            sw.Stop();

                            if (response.IsSuccessStatusCode)
                            {
                                latency = Math.Max(1, sw.ElapsedMilliseconds);
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch
                        {
                            var curlCheck = VerifyWithNativeCurlSni();
                            if (curlCheck.success) latency = curlCheck.latencyMs;
                        }

                        if (latency > 0)
                        {
                            _ = Dispatcher.BeginInvoke(() =>
                            {
                                if (_isConnected)
                                {
                                    PingBadgeText.Text = $"{latency} ms";
                                    StatusSubLabel.Text = $"Gateway verified • {latency} ms latency";
                                }
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private void StopBackgroundPing()
        {
            _pingCts?.Cancel();
            _pingCts?.Dispose();
            _pingCts = null;
        }

        private async Task StopTunnelAsync()
        {
            _isConnected = false;
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = null;

            StopBackgroundPing();
            SetUiState(false);

            await Task.Run(() =>
            {
                StopAllEnginesSync();
                Thread.Sleep(200);
            });
        }

        private void SetUiState(bool connected, long latencyMs = 0, string profileName = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetUiState(connected, latencyMs, profileName));
                return;
            }

            _isConnected = connected;

            var fadeDuration = TimeSpan.FromMilliseconds(480);
            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var purpleFade = new DoubleAnimation(connected ? 1.0 : 0.0, fadeDuration) { EasingFunction = cubicEase };
            PurpleBackground.BeginAnimation(UIElement.OpacityProperty, purpleFade);

            if (connected)
            {
                RegionCombo.IsEnabled = false;

                var liftUp = new DoubleAnimation(-64, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut }
                };
                ShackleTranslate.BeginAnimation(TranslateTransform.YProperty, liftUp);

                var swingOpen = new DoubleAnimation(-28, TimeSpan.FromMilliseconds(460))
                {
                    BeginTime = TimeSpan.FromMilliseconds(140),
                    EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                };
                ShackleRotate.BeginAnimation(RotateTransform.AngleProperty, swingOpen);

                var bodyPulse = new DoubleAnimationUsingKeyFrames();
                bodyPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bodyPulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                bodyPulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                LockBodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, bodyPulse);
                LockBodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, bodyPulse);

                var glowBreath = new DoubleAnimation(0.55, 0.88, TimeSpan.FromSeconds(1.8))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                LockGlow.BeginAnimation(UIElement.OpacityProperty, glowBreath);

                LockBodyBorder.Background = (LinearGradientBrush)FindResource("PurpleLockBodyGradient");
                LockBodyBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5B4FC"));

                var keyholeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5B4FC"));
                KeyholeTop.Fill = keyholeBrush;
                KeyholeBottom.Fill = keyholeBrush;

                StatusLabel.Text = "CONNECTED";
                StatusLabel.Foreground = Brushes.White;
                StatusSubLabel.Text = $"Gateway verified • {latencyMs} ms latency";
                StatusSubLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3E5E8"));

                PingBadge.Visibility = Visibility.Visible;
                PingBadgeText.Text = $"{latencyMs} ms";

                SecurityPill.Visibility = Visibility.Visible;
                string displayProf = string.IsNullOrEmpty(profileName) ? _activeProfileName : profileName;
                SecurityPillText.Text = $"{displayProf} • Bypass Active";

                ConnectBtn.Content = "Disconnect";
                ConnectBtn.Background = (LinearGradientBrush)FindResource("ButtonConnectedGradient");
                BtnShadow.Color = (Color)ColorConverter.ConvertFromString("#EF4444");
                BtnShadow.Opacity = 0.50;

                if (_trayIcon != null)
                {
                    _trayIcon.Text = $"Discord Shield v1.0.1 - Connected ({latencyMs} ms)";
                    if (_trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.Items.Count > 1)
                    {
                        _trayIcon.ContextMenuStrip.Items[1].Text = "Disconnect";
                    }
                }
            }
            else
            {
                RegionCombo.IsEnabled = true;

                var swingShut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                ShackleRotate.BeginAnimation(RotateTransform.AngleProperty, swingShut);

                var snapDown = new DoubleAnimation(0, TimeSpan.FromMilliseconds(380))
                {
                    BeginTime = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new BounceEase { Bounces = 1, Bounciness = 2.8, EasingMode = EasingMode.EaseOut }
                };
                ShackleTranslate.BeginAnimation(TranslateTransform.YProperty, snapDown);

                var bodyRecoil = new DoubleAnimationUsingKeyFrames();
                bodyRecoil.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
                bodyRecoil.KeyFrames.Add(new EasingDoubleKeyFrame(0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bodyRecoil.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                LockBodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, bodyRecoil);
                LockBodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, bodyRecoil);

                LockGlow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, fadeDuration));

                LockBodyBorder.Background = (LinearGradientBrush)FindResource("GreyLockBodyGradient");
                LockBodyBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4E5058"));

                var keyholeDarkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#232428"));
                KeyholeTop.Fill = keyholeDarkBrush;
                KeyholeBottom.Fill = keyholeDarkBrush;

                StatusLabel.Text = "DISCONNECTED";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2F3F5"));
                StatusSubLabel.Text = "Discord traffic is direct";
                StatusSubLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#949BA4"));

                PingBadge.Visibility = Visibility.Collapsed;
                SecurityPill.Visibility = Visibility.Collapsed;

                ConnectBtn.Content = "Connect";
                ConnectBtn.Background = (LinearGradientBrush)FindResource("ButtonIdleGradient");
                BtnShadow.Color = (Color)ColorConverter.ConvertFromString("#5865F2");
                BtnShadow.Opacity = 0.45;

                if (_trayIcon != null)
                {
                    _trayIcon.Text = "Discord Shield v1.0.1 - Disconnected";
                    if (_trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.Items.Count > 1)
                    {
                        _trayIcon.ContextMenuStrip.Items[1].Text = "Connect";
                    }
                }
            }
        }

        private void SetUiFailedState(string errorReason)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetUiFailedState(errorReason));
                return;
            }

            _isConnected = false;
            StopBackgroundPing();

            RegionCombo.IsEnabled = true;

            StatusLabel.Text = "CONNECTION FAILED";
            StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            StatusSubLabel.Text = errorReason;
            StatusSubLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B5BAC1"));

            PingBadge.Visibility = Visibility.Collapsed;
            SecurityPill.Visibility = Visibility.Visible;
            SecurityPillText.Text = "Verification Failed • Safe Engine Reset";

            ConnectBtn.Content = "Retry Connection";
            ConnectBtn.Background = (LinearGradientBrush)FindResource("ButtonIdleGradient");
            BtnShadow.Color = (Color)ColorConverter.ConvertFromString("#5865F2");
            BtnShadow.Opacity = 0.45;
        }

        private static void TerminateProcessByName(string name)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(400);
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch { }
        }

        private static void StopDriverService()
        {
            try
            {
                string scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
                if (!File.Exists(scPath)) return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = scPath,
                    Arguments = "stop WinDivert",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(startInfo);
                process?.WaitForExit(800);
            }
            catch { }
        }

        private static void FlushDnsCache()
        {
            try
            {
                string ipconfigPath = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
                if (!File.Exists(ipconfigPath)) return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = ipconfigPath,
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(startInfo);
                process?.WaitForExit(800);
            }
            catch { }
        }
    }
}
