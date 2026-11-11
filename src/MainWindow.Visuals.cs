using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace DiscordTunnel
{
    public partial class MainWindow
    {
        private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction EaseOutStrong = new QuinticEase { EasingMode = EasingMode.EaseOut };

        private readonly SolidColorBrush _statusBrush = new(Hex("#F2F3F5"));
        private readonly SolidColorBrush _subBrush = new(Hex("#949BA4"));
        private readonly SolidColorBrush _buttonBrush = new(Hex("#5865F2"));

        private FrameworkElement? _openPanel;
        private int _panelVersion;
        private int _privacyVersion;
        private bool _cancelVisible;

        private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);

        private void InitializeVisuals()
        {
            StatusLabel.Foreground = _statusBrush;
            StatusSubLabel.Foreground = _subBrush;
            ConnectBtn.Background = _buttonBrush;
        }

        private void PlayEntrance()
        {
            var scale = new ScaleTransform(0.97, 0.97);
            WindowFrame.RenderTransformOrigin = new Point(0.5, 0.5);
            WindowFrame.RenderTransform = scale;

            WindowFrame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = EaseOutStrong });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = EaseOutStrong });
        }

        private static void AnimateColor(SolidColorBrush brush, string hex, int milliseconds = 240)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Hex(hex), TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut });
        }

        private static void SetText(TextBlock target, string text, bool animate = true)
        {
            if (target.Text == text) return;

            target.Text = text;
            if (animate)
            {
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
            }
        }

        private void SetPill(bool show, string? text = null)
        {
            if (text != null) SecurityPillText.Text = text;
            SecurityPill.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseOut });
        }

        private static void FadeElement(UIElement element, bool show)
        {
            if (show)
            {
                element.Visibility = Visibility.Visible;
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
                return;
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut };
            fade.Completed += (_, _) =>
            {
                if (element.Opacity <= 0.01) element.Visibility = Visibility.Collapsed;
            };
            element.BeginAnimation(OpacityProperty, fade);
        }

        private void ShowCancel(bool show)
        {
            if (_cancelVisible == show) return;
            _cancelVisible = show;

            if (show)
            {
                CancelHost.Visibility = Visibility.Visible;
                CancelHost.BeginAnimation(WidthProperty, new DoubleAnimation(48, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOutStrong });
                CancelBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
                return;
            }

            var shrink = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOutStrong };
            shrink.Completed += (_, _) =>
            {
                if (!_cancelVisible) CancelHost.Visibility = Visibility.Collapsed;
            };
            CancelHost.BeginAnimation(WidthProperty, shrink);
            CancelBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut });
        }

        private ScaleTransform PanelScale(FrameworkElement panel) => panel == RegionDropdown ? RegionDropdownScale : AppMenuScale;

        private void OpenPanel(FrameworkElement panel, ScaleTransform scale, double left, double top, Point origin)
        {
            if (_openPanel != null && _openPanel != panel) CollapsePanelNow(_openPanel);

            _panelVersion++;
            _openPanel = panel;

            OverlayLayer.Visibility = Visibility.Visible;
            panel.Margin = new Thickness(left, top, 0, 0);
            panel.RenderTransformOrigin = origin;
            panel.Visibility = Visibility.Visible;

            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOutStrong });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOutStrong });
        }

        private void ClosePanel()
        {
            var panel = _openPanel;
            if (panel == null) return;

            _openPanel = null;
            int version = ++_panelVersion;
            RegionSelector.Tag = null;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut };
            fade.Completed += (_, _) =>
            {
                if (version != _panelVersion) return;
                panel.Visibility = Visibility.Collapsed;
                OverlayLayer.Visibility = Visibility.Collapsed;
            };

            var scale = PanelScale(panel);
            panel.BeginAnimation(OpacityProperty, fade);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut });
        }

        private void CollapsePanelNow(FrameworkElement panel)
        {
            var scale = PanelScale(panel);
            panel.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            panel.Opacity = 0;
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.9;
            panel.Visibility = Visibility.Collapsed;
            RegionSelector.Tag = null;
        }

        private void ShowPrivacy()
        {
            _privacyVersion++;
            PrivacyModal.Visibility = Visibility.Visible;

            PrivacyModal.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
            PrivacyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOutStrong });
            PrivacyScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOutStrong });
        }

        private void HidePrivacy()
        {
            int version = ++_privacyVersion;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut };
            fade.Completed += (_, _) =>
            {
                if (version == _privacyVersion) PrivacyModal.Visibility = Visibility.Collapsed;
            };

            PrivacyModal.BeginAnimation(OpacityProperty, fade);
            PrivacyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut });
            PrivacyScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut });
        }

        private void BeginConnectingUi()
        {
            ClosePanel();
            RegionSelector.IsEnabled = false;
            ConnectBtn.IsEnabled = false;
            ConnectBtn.Content = "Connecting...";

            SetText(StatusLabel, "CONNECTING...");
            AnimateColor(_statusBrush, "#FEE75C");
            AnimateColor(_subBrush, "#949BA4");
            SetText(StatusSubLabel, "Starting Discord Shield engine");
            SetPill(true, "Initializing bypass engine");

            CancelBtn.IsEnabled = true;
            ShowCancel(true);
        }

        private void SetUiState(bool connected, long latencyMs = 0, string profileName = "", string subStatus = "Discord traffic is direct")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetUiState(connected, latencyMs, profileName, subStatus));
                return;
            }

            _isConnected = connected;

            var fadeDuration = TimeSpan.FromMilliseconds(480);
            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var purpleFade = new DoubleAnimation(connected ? 1.0 : 0.0, fadeDuration) { EasingFunction = cubicEase };
            PurpleBackground.BeginAnimation(UIElement.OpacityProperty, purpleFade);

            AnimateLock(connected, fadeDuration);

            if (connected)
            {
                RegionSelector.IsEnabled = false;

                SetText(StatusLabel, "CONNECTED");
                AnimateColor(_statusBrush, "#FFFFFF");
                AnimateColor(_subBrush, "#E3E5E8");
                SetText(StatusSubLabel, $"Gateway verified  \u2022  {latencyMs} ms latency");

                SetText(PingBadgeText, $"{latencyMs} ms", animate: false);
                FadeElement(PingBadge, true);

                string displayName = string.IsNullOrEmpty(profileName) ? _activeProfileName : profileName;
                SetPill(true, $"{displayName}  \u2022  Bypass active");

                ConnectBtn.Content = "Disconnect";
                AnimateColor(_buttonBrush, "#DA373C");
                BtnShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, new ColorAnimation(Hex("#DA373C"), TimeSpan.FromMilliseconds(240)));
                BtnShadow.Opacity = 0.50;

                UpdateTrayState($"Discord Shield v{AppVersion} - Connected ({latencyMs} ms)", "Disconnect");
            }
            else
            {
                RegionSelector.IsEnabled = true;

                SetText(StatusLabel, "DISCONNECTED");
                AnimateColor(_statusBrush, "#F2F3F5");
                AnimateColor(_subBrush, "#949BA4");
                SetText(StatusSubLabel, subStatus);

                FadeElement(PingBadge, false);
                SetPill(false);

                ConnectBtn.Content = "Connect";
                AnimateColor(_buttonBrush, "#5865F2");
                BtnShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, new ColorAnimation(Hex("#5865F2"), TimeSpan.FromMilliseconds(240)));
                BtnShadow.Opacity = 0.45;

                UpdateTrayState($"Discord Shield v{AppVersion} - Disconnected", "Connect");
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

            RegionSelector.IsEnabled = true;

            SetText(StatusLabel, "CONNECTION FAILED");
            AnimateColor(_statusBrush, "#EF4444");
            AnimateColor(_subBrush, "#B5BAC1");
            SetText(StatusSubLabel, errorReason);

            FadeElement(PingBadge, false);
            SetPill(true, "Verification failed  \u2022  Engine reset");

            ConnectBtn.Content = "Retry Connection";
            AnimateColor(_buttonBrush, "#5865F2");
            BtnShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, new ColorAnimation(Hex("#5865F2"), TimeSpan.FromMilliseconds(240)));
            BtnShadow.Opacity = 0.45;
        }

        private void AnimateLock(bool connected, TimeSpan fadeDuration)
        {
            if (connected)
            {
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
            }
            else
            {
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
            }
        }
    }
}
