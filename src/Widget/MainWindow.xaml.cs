using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;

namespace CodexUsageWidgetApp;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _mouseTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IntPtr _hwnd;
    private GlassBackdrop? _glassBackdrop;
    private double _reflectionX = .3;
    private double _reflectionY = .15;
    private bool _passThrough;
    private bool _dragging;
    private bool _refreshing;
    private double _sessionRemaining;
    private double _weeklyRemaining;
    private DateTime _nextRefresh = DateTime.MinValue;
    private readonly WidgetSettings _settings = WidgetSettings.Load();
    private bool _settingsOpen;

    public MainWindow()
    {
        InitializeComponent();
        if (_settings.Left is double left && _settings.Top is double top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        }
        _clock.Tick += Clock_Tick;
        _mouseTimer.Tick += (_, _) => UpdateMousePassThrough();
        LocationChanged += (_, _) => _glassBackdrop?.Sync(this);
        SizeChanged += (_, _) => _glassBackdrop?.Sync(this);
        Activated += (_, _) => _glassBackdrop?.Sync(this);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _glassBackdrop = new GlassBackdrop(_hwnd);
        var tint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.85, 1) };
        tint.GradientStops.Add(new GradientStop(Color.FromArgb(148, 255, 255, 255), 0));
        tint.GradientStops.Add(new GradientStop(Color.FromArgb(140, 237, 245, 255), .45));
        tint.GradientStops.Add(new GradientStop(Color.FromArgb(152, 241, 238, 255), 1));
        GlassShell.Background = tint;
        double smoothedLight = .4;
        byte lastAlpha = 140;
        _glassBackdrop.FrameReady += (frame, light) =>
        {
            if (!ReferenceEquals(DesktopTexture.ImageSource, frame)) DesktopTexture.ImageSource = frame;
            smoothedLight += (light - smoothedLight) * .1;
            byte alpha = (byte)Math.Clamp(100 + 65 * (1 - smoothedLight), 100, 165);
            if (alpha == lastAlpha) return;
            lastAlpha = alpha;
            tint.GradientStops[0].Color = Color.FromArgb((byte)(alpha + 8), 255, 255, 255);
            tint.GradientStops[1].Color = Color.FromArgb(alpha, 237, 245, 255);
            tint.GradientStops[2].Color = Color.FromArgb((byte)(alpha + 12), 241, 238, 255);
        };
        UpdateMousePassThrough();
        _mouseTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _glassBackdrop?.Sync(this);
        StartShimmer(SessionTrack, SessionShimmer, 0);
        StartShimmer(WeeklyTrack, WeeklyShimmer, 0.75);
        _clock.Start();
        if (((App)Application.Current).ShowSettingsOnStart) OpenSettings();
        await RefreshAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _clock.Stop();
        _mouseTimer.Stop();
        _glassBackdrop?.Dispose();
        try { _settings.Left = Left; _settings.Top = Top; _settings.Save(); } catch { }
    }

    private void UpdateMousePassThrough()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var pos)) return;
        var local = PointFromScreen(new Point(pos.X, pos.Y));
        double desiredX = Math.Clamp(local.X / Math.Max(1, ActualWidth), 0, 1);
        double desiredY = Math.Clamp(local.Y / Math.Max(1, ActualHeight), 0, 1);
        _reflectionX += (desiredX - _reflectionX) * .07;
        _reflectionY += (desiredY - _reflectionY) * .07;
        double idle = Math.Sin(Environment.TickCount64 / 3800.0) * .035;
        ReflectionBrush.Center = new Point(.1 + _reflectionX * .75 + idle, -.15 + _reflectionY * .55);
        ReflectionBrush.GradientOrigin = new Point(.1 + _reflectionX * .75 + idle, -.22 + _reflectionY * .55);
        bool insideHeader = local.X >= 0 && local.X <= ActualWidth && local.Y >= 0 && local.Y <= 38;
        bool shouldPassThrough = !insideHeader && !_dragging && !_settingsOpen;
        if (shouldPassThrough == _passThrough) return;

        long style = NativeMethods.GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        style = shouldPassThrough ? style | WsExTransparent : style & ~WsExTransparent;
        NativeMethods.SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        _passThrough = shouldPassThrough;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        UpdateMousePassThrough();
        try
        {
            double currentTop = Top;
            BeginAnimation(TopProperty, null);
            Top = currentTop;
            DragMove();
            var workArea = SystemParameters.WorkArea;
            if (Top <= workArea.Top + 28)
            {
                var snap = new DoubleAnimation(workArea.Top + 6, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(TopProperty, snap);
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            _dragging = false;
            UpdateMousePassThrough();
        }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    internal void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        UpdateMousePassThrough();
        try
        {
            var window = new SettingsWindow(_settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                _nextRefresh = DateTime.MinValue;
                _ = RefreshAsync();
            }
        }
        finally { _settingsOpen = false; UpdateMousePassThrough(); }
    }

    private async void Clock_Tick(object? sender, EventArgs e)
    {
        UpdateCountdowns();
        if (!_refreshing && DateTime.Now >= _nextRefresh) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        StatusText.Text = "正在刷新用量…";
        try
        {
            var json = await Task.Run(RunDataHelper);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Session", out var session) && session.ValueKind == JsonValueKind.Object)
                ApplyQuota(session, SessionPercent, SessionTrack, SessionFill, true);
            if (root.TryGetProperty("Weekly", out var weekly) && weekly.ValueKind == JsonValueKind.Object)
                ApplyQuota(weekly, WeeklyPercent, WeeklyTrack, WeeklyFill, false);
            if (root.TryGetProperty("DeepSeek", out var deepSeek)) ApplyDeepSeek(deepSeek);
            StatusText.Text = root.TryGetProperty("CodexError", out var codexError)
                ? "Codex待连接 · 点击⚙设置" : "已更新 · 自动刷新";
            StatusText.ToolTip = root.TryGetProperty("CodexError", out codexError) ? codexError.GetString() : null;
            UpdateCountdowns();
            _nextRefresh = DateTime.Now.AddSeconds(_settings.RefreshSeconds);
        }
        catch (Exception ex)
        {
            StatusText.Text = "读取失败 · 将自动重试";
            StatusText.ToolTip = ex.Message;
            _nextRefresh = DateTime.Now.AddSeconds(20);
        }
        finally { _refreshing = false; }
    }

    private string RunDataHelper()
    {
        string appDir = AppContext.BaseDirectory;
        string helper = Path.Combine(appDir, "Get-CodexUsage.ps1");
        if (!File.Exists(helper)) throw new FileNotFoundException("找不到用量读取组件。", helper);
        string output = Path.Combine(Path.GetTempPath(), $"CodexUsageWidget-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(ps)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(helper);
        start.ArgumentList.Add("-OutputPath");
        start.ArgumentList.Add(output);
        string? codex = string.IsNullOrWhiteSpace(_settings.CodexPath) ? SettingsWindow.FindCodex() : _settings.CodexPath;
        if (!string.IsNullOrWhiteSpace(codex))
        {
            start.ArgumentList.Add("-CodexPath");
            start.ArgumentList.Add(codex);
        }
        string? key = LocalCredentials.Read();
        if (!string.IsNullOrWhiteSpace(key)) start.Environment["DEEPSEEK_API_KEY"] = key;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隐藏的数据读取进程。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("用量查询超时。");
        }
        string stderr = stderrTask.GetAwaiter().GetResult();
        try
        {
            if (!File.Exists(output)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "读取组件没有返回数据。" : stderr.Trim());
            return File.ReadAllText(output);
        }
        finally { try { File.Delete(output); } catch { } }
    }

    private void ApplyQuota(JsonElement quota, TextBlock percent, Grid track, Border fill, bool isSession)
    {
        if (quota.TryGetProperty("usedPercent", out var used))
        {
            double remaining = Math.Clamp(100 - used.GetDouble(), 0, 100);
            if (isSession) _sessionRemaining = remaining; else _weeklyRemaining = remaining;
            percent.Text = $"{remaining:0}%";
            SetFillWidth(track, fill, remaining);
        }
        if (quota.TryGetProperty("resetsAt", out var resetAt) && resetAt.ValueKind == JsonValueKind.Number)
        {
            long timestamp = resetAt.GetInt64();
            if (isSession) _sessionResetAt = timestamp; else _weeklyResetAt = timestamp;
        }
    }

    private void ApplyDeepSeek(JsonElement data)
    {
        if (data.TryGetProperty("Configured", out var configured) && !configured.GetBoolean())
        {
            DeepSeekBalance.Text = "—";
            DeepSeekStatus.Text = "点击顶部 ⚙ 配置密钥";
            DeepSeekStatus.ToolTip = "在设置中填写自己的 DeepSeek API 密钥；也兼容 DEEPSEEK_API_KEY 用户环境变量。";
            return;
        }
        if (data.TryGetProperty("Error", out var error))
        {
            DeepSeekBalance.Text = "读取失败";
            DeepSeekStatus.Text = "DeepSeek API 暂不可用";
            DeepSeekStatus.ToolTip = error.GetString();
            return;
        }
        if (!data.TryGetProperty("BalanceInfos", out var infos) || infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0)
        {
            DeepSeekBalance.Text = "—";
            DeepSeekStatus.Text = "未返回余额数据";
            return;
        }
        var balances = new List<string>();
        var details = new List<string>();
        foreach (var item in infos.EnumerateArray())
        {
            string currency = item.GetProperty("currency").GetString() ?? "USD";
            string symbol = currency == "CNY" ? "¥" : "$";
            decimal total = decimal.Parse(item.GetProperty("total_balance").GetString() ?? "0", CultureInfo.InvariantCulture);
            decimal granted = decimal.Parse(item.GetProperty("granted_balance").GetString() ?? "0", CultureInfo.InvariantCulture);
            decimal topped = decimal.Parse(item.GetProperty("topped_up_balance").GetString() ?? "0", CultureInfo.InvariantCulture);
            balances.Add($"{symbol}{total:N2}");
            details.Add($"{currency}: 总额 {symbol}{total:N2}，赠金 {symbol}{granted:N2}，充值 {symbol}{topped:N2}");
        }
        DeepSeekBalance.Text = string.Join(" · ", balances);
        DeepSeekStatus.Text = data.TryGetProperty("IsAvailable", out var available) && available.GetBoolean() ? "API 可用" : "余额不足";
        DeepSeekStatus.ToolTip = string.Join(Environment.NewLine, details);
    }

    private static void SetFillWidth(Grid track, Border fill, double remaining)
    {
        if (track.ActualWidth <= 0) return;
        var animation = new DoubleAnimation(track.ActualWidth * remaining / 100, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fill.BeginAnimation(WidthProperty, animation);
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid track && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            double radius = e.NewSize.Height / 2;
            track.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
        }
        if (ReferenceEquals(sender, SessionTrack)) SetFillWidth(SessionTrack, SessionFill, _sessionRemaining);
        if (ReferenceEquals(sender, WeeklyTrack)) SetFillWidth(WeeklyTrack, WeeklyFill, _weeklyRemaining);
    }

    private static void StartShimmer(Grid track, Border shimmer, double delaySeconds)
    {
        if (shimmer.RenderTransform is not TranslateTransform transform) return;
        var animation = new DoubleAnimation(-44, Math.Max(180, track.ActualWidth), TimeSpan.FromSeconds(2.8))
        {
            BeginTime = TimeSpan.FromSeconds(delaySeconds),
            RepeatBehavior = RepeatBehavior.Forever
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void UpdateCountdowns()
    {
        if (_sessionResetAt > 0) SessionReset.Text = FormatCountdown(_sessionResetAt, true);
        if (_weeklyResetAt > 0) WeeklyReset.Text = FormatCountdown(_weeklyResetAt, false);
    }

    private long _sessionResetAt;
    private long _weeklyResetAt;
    private string FormatCountdown(long resetAt, bool isSession)
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(resetAt).ToLocalTime();
        long left = Math.Max(0, (long)Math.Floor((reset - DateTimeOffset.Now).TotalSeconds));
        if (left <= 0) return "额度重置中…";
        long seconds = left % 60;
        long minutes = left / 60 % 60;
        long hours = isSession ? left / 3600 : left / 3600 % 24;
        long days = isSession ? 0 : left / 86400;
        var parts = new List<string>();
        if (days > 0) parts.Add($"{days}天");
        if (hours > 0) parts.Add($"{hours}小时");
        if (minutes > 0) parts.Add($"{minutes}分");
        if (seconds > 0) parts.Add($"{seconds}秒");
        return $"{string.Concat(parts)}  ·  {reset:MM-dd HH:mm}";
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern IntPtr GetWindowLong32(IntPtr hwnd, int index);
        internal static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern IntPtr SetWindowLong32(IntPtr hwnd, int index, IntPtr value);
        internal static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : SetWindowLong32(hwnd, index, value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
