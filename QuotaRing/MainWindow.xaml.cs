using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuotaRing.Controls;
using QuotaRing.Models;
using QuotaRing.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace QuotaRing;

public partial class MainWindow : Window
{
    private readonly QuotaProviders _providers = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _codexClaudeTimer;
    private readonly DispatcherTimer _antigravityTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Icon _trayIconHandle;

    private readonly Dictionary<string, ProviderSnapshot> _snapshots = new(StringComparer.Ordinal);
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        Topmost = _settings.Topmost;
        RestorePosition();

        _snapshots["antigravity"] = LoadingSnapshot("antigravity", "Antigravity", "#6366F1");
        _snapshots["codex"] = LoadingSnapshot("codex", "Codex", "#10B981");
        _snapshots["claude"] = LoadingSnapshot("claude", "Claude", "#F97316");

        _trayIconHandle = CreateTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconHandle,
            Text = "QuotaRing - AI 剩余额度",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ToggleVisibility);
            }
        };

        ApplyTheme();
        RenderAll();

        _codexClaudeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _codexClaudeTimer.Tick += async (_, _) => await RefreshProvidersAsync(includeAntigravity: false);
        _codexClaudeTimer.Start();

        _antigravityTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _antigravityTimer.Tick += async (_, _) => await RefreshProvidersAsync(includeAntigravity: true);
        _antigravityTimer.Start();

        Loaded += async (_, _) => await RefreshProvidersAsync(includeAntigravity: true);
        LocationChanged += (_, _) => SavePosition();
    }

    private static ProviderSnapshot LoadingSnapshot(string id, string name, string accent) =>
        new(id, name, accent, null, ProviderStatus.Loading, [], "加载中...", DateTimeOffset.Now);

    private void RestorePosition()
    {
        var workArea = SystemParameters.WorkArea;
        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            var left = Math.Clamp(_settings.Left.Value, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            var top = Math.Clamp(_settings.Top.Value, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - 160));
            Left = left;
            Top = top;
        }
        else
        {
            Left = workArea.Right - Width - 16;
            Top = workArea.Top + 48;
        }
    }

    private void SavePosition()
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.Left = Left;
        _settings.Top = Top;
        SettingsStore.Save(_settings);
    }

    private async Task RefreshProvidersAsync(bool includeAntigravity)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdatedText.Text = " · 刷新中";

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        try
        {
            var tasks = new List<Task<ProviderSnapshot>>
            {
                _providers.FetchCodexAsync(ct),
                _providers.FetchClaudeAsync(ct)
            };

            if (includeAntigravity)
            {
                tasks.Add(_providers.FetchAntigravityAsync(ct));
            }

            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks);
                tasks.Remove(done);
                var snap = await done;
                _snapshots[snap.ProviderId] = snap;
                RenderAll();
            }

            UpdatedText.Text = $" · {DateTime.Now:HH:mm}";
        }
        catch (OperationCanceledException)
        {
            UpdatedText.Text = " · 已取消";
        }
        catch (Exception ex)
        {
            UpdatedText.Text = " · 刷新异常";
            System.Diagnostics.Debug.WriteLine($"Refresh failed: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplyTheme()
    {
        var dark = _settings.DarkTheme;
        RootBorder.Background = CreateBrush(dark ? "#EE131720" : "#F5FFFFFF");
        RootBorder.BorderBrush = CreateBrush(dark ? "#26FFFFFF" : "#220F172A");
        HeaderTitle.Foreground = CreateBrush(dark ? "#F8FAFC" : "#0F172A");
        UpdatedText.Foreground = CreateBrush(dark ? "#94A3B8" : "#64748B");

        var iconFg = CreateBrush(dark ? "#CBD5E1" : "#475569");
        RefreshBtn.Foreground = iconFg;
        ThemeBtn.Foreground = iconFg;
        HideBtn.Foreground = iconFg;
        PinBtn.Foreground = _settings.Topmost ? CreateBrush("#10B981") : iconFg;
        PinBtn.ToolTip = _settings.Topmost ? "已置顶（点击取消）" : "未置顶（点击置顶）";
    }

    private void RenderAll()
    {
        ProvidersPanel.Children.Clear();
        var now = DateTimeOffset.UtcNow;

        foreach (var id in new[] { "antigravity", "codex", "claude" })
        {
            if (_snapshots.TryGetValue(id, out var snapshot))
            {
                ProvidersPanel.Children.Add(BuildProviderCard(snapshot, now));
            }
        }
    }

    private Border BuildProviderCard(ProviderSnapshot snap, DateTimeOffset now)
    {
        var dark = _settings.DarkTheme;
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, snap.ProviderId == "claude" ? 0 : 8),
            Padding = new Thickness(10, 8, 10, 9),
            CornerRadius = new CornerRadius(12),
            Background = CreateBrush(dark ? "#16FFFFFF" : "#0C0F172A"),
            BorderBrush = CreateBrush(dark ? "#14FFFFFF" : "#120F172A"),
            BorderThickness = new Thickness(1)
        };

        var rootStack = new StackPanel();

        // Title row
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftPanel.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = CreateBrush(snap.AccentHex),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        leftPanel.Children.Add(new TextBlock
        {
            Text = snap.DisplayName,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = CreateBrush(dark ? "#E2E8F0" : "#1E293B")
        });

        if (!string.IsNullOrWhiteSpace(snap.PlanLabel))
        {
            leftPanel.Children.Add(new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(5, 1, 5, 1),
                CornerRadius = new CornerRadius(4),
                Background = CreateBrush(dark ? "#20FFFFFF" : "#140F172A"),
                Child = new TextBlock
                {
                    Text = snap.PlanLabel,
                    FontSize = 9.5,
                    Foreground = CreateBrush(dark ? "#CBD5E1" : "#475569")
                }
            });
        }

        titleRow.Children.Add(leftPanel);

        if (!string.IsNullOrWhiteSpace(snap.StatusMessage))
        {
            var msgColor = snap.Status == ProviderStatus.Error
                ? "#F87171"
                : (dark ? "#94A3B8" : "#64748B");

            var msgBlock = new TextBlock
            {
                Text = snap.StatusMessage,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = CreateBrush(msgColor)
            };
            DockPanel.SetDock(msgBlock, Dock.Right);
            titleRow.Children.Add(msgBlock);
        }

        rootStack.Children.Add(titleRow);

        // Rings row
        var ringsGrid = new UniformGrid { Rows = 1 };
        if (snap.Status == ProviderStatus.Ready && snap.Rings.Count > 0)
        {
            ringsGrid.Columns = snap.Rings.Count;
            foreach (var ringItem in snap.Rings)
            {
                var gauge = new QuotaRingGauge();
                gauge.SetData(ringItem, dark, now);
                ringsGrid.Children.Add(gauge);
            }
        }
        else
        {
            var placeholders = snap.ProviderId == "antigravity"
                ? new[] { "Gemini 5h", "Gemini 7d", "3P 5h", "3P 7d" }
                : new[] { "5h", "7d" };
            var hint = snap.Status switch
            {
                ProviderStatus.Loading => "同步中",
                ProviderStatus.NotSignedIn => "未登录",
                _ => "读取失败"
            };

            ringsGrid.Columns = placeholders.Length;
            foreach (var label in placeholders)
            {
                var gauge = new QuotaRingGauge();
                gauge.SetEmpty(label, hint, dark);
                ringsGrid.Children.Add(gauge);
            }
        }

        rootStack.Children.Add(ringsGrid);
        card.Child = rootStack;
        return card;
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏浮窗", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("立即刷新额度", null, (_, _) => Dispatcher.Invoke(async () => await RefreshProvidersAsync(true)));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var themeItem = new Forms.ToolStripMenuItem("深色模式") { Checked = _settings.DarkTheme, CheckOnClick = true };
        themeItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.DarkTheme = themeItem.Checked;
            SettingsStore.Save(_settings);
            ApplyTheme();
            RenderAll();
        });
        menu.Items.Add(themeItem);

        var pinItem = new Forms.ToolStripMenuItem("窗口置顶") { Checked = _settings.Topmost, CheckOnClick = true };
        pinItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.Topmost = pinItem.Checked;
            Topmost = _settings.Topmost;
            SettingsStore.Save(_settings);
            ApplyTheme();
        });
        menu.Items.Add(pinItem);

        var lockItem = new Forms.ToolStripMenuItem("锁定窗口位置") { Checked = _settings.Locked, CheckOnClick = true };
        lockItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.Locked = lockItem.Checked;
            SettingsStore.Save(_settings);
        });
        menu.Items.Add(lockItem);

        var autostartItem = new Forms.ToolStripMenuItem("开机自动启动")
        {
            Checked = SettingsStore.IsAutostartEnabled(),
            CheckOnClick = true
        };
        autostartItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            SettingsStore.SetAutostart(autostartItem.Checked);
        });
        menu.Items.Add(autostartItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 QuotaRing", null, (_, _) => Dispatcher.Invoke(ExitApp));
        return menu;
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var trackPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(120, 148, 163, 184), 4.5f);
            g.DrawEllipse(trackPen, 5, 5, 22, 22);
            using var arcPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 16, 185, 129), 4.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawArc(arcPen, 5, 5, 22, 22, -90, 265);
        }

        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private void ExitApp()
    {
        _refreshCts?.Cancel();
        _codexClaudeTimer.Stop();
        _antigravityTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconHandle.Dispose();
        _providers.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.Locked && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) =>
        await RefreshProvidersAsync(includeAntigravity: true);

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        SettingsStore.Save(_settings);
        ApplyTheme();
        RenderAll();
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.Topmost = !_settings.Topmost;
        Topmost = _settings.Topmost;
        SettingsStore.Save(_settings);
        ApplyTheme();
    }

    private void HideBtn_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();
        base.OnClosed(e);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
