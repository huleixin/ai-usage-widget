using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CodexUsageWidgetApp;

public partial class SettingsWindow : Window
{
    private readonly WidgetSettings _settings;
    internal SettingsWindow(WidgetSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        CodexPathBox.Text = settings.CodexPath;
        RefreshBox.Text = settings.RefreshSeconds.ToString();
        StartupBox.IsChecked = settings.StartWithWindows;
        KeyStatus.Text = string.IsNullOrEmpty(LocalCredentials.Read())
            ? "可留空；也兼容已有的 DEEPSEEK_API_KEY 用户环境变量。"
            : "本机已保存密钥。留空可保留；不会显示或导出已有密钥。";
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Codex 程序 (codex.exe)|codex.exe|可执行程序 (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) CodexPathBox.Text = picker.FileName;
    }
    private void CodexDocs_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://developers.openai.com/codex/cli/") { UseShellExecute = true });
    private void Login_Click(object sender, RoutedEventArgs e)
    {
        string path = CodexPathBox.Text.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) path = FindCodex() ?? "";
        if (!File.Exists(path)) { ErrorText.Text = "请先安装 Codex，或浏览选择 codex.exe。"; return; }
        try { Process.Start(new ProcessStartInfo(path, "login") { UseShellExecute = true }); }
        catch { ErrorText.Text = "无法打开登录。请在终端中运行 codex login。"; }
    }
    internal static string? FindCodex()
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { string candidate = Path.Combine(directory.Trim().Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        string bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(bundled))
        {
            string? found = Directory.EnumerateFiles(bundled, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (found is not null) return found;
        }
        string npmScope = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        if (Directory.Exists(npmScope))
            foreach (string package in Directory.EnumerateDirectories(npmScope, "codex*"))
            {
                string? found = Directory.EnumerateFiles(package, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        return null;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text, out int interval) || interval is < 30 or > 3600)
        { ErrorText.Text = "刷新间隔需要填写 30–3600 之间的整数。"; return; }
        string path = CodexPathBox.Text.Trim().Trim('"');
        if (path.Length > 0 && (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        { ErrorText.Text = "请选择有效的 codex.exe 路径，或留空自动查找。"; return; }
        try
        {
            if (RemoveKeyBox.IsChecked == true) LocalCredentials.Delete();
            else if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password)) LocalCredentials.Save(ApiKeyBox.Password);
            _settings.CodexPath = path;
            _settings.RefreshSeconds = interval;
            _settings.StartWithWindows = StartupBox.IsChecked == true;
            _settings.SetStartup();
            _settings.Save();
            ApiKeyBox.Clear();
            DialogResult = true;
        }
        catch { ErrorText.Text = "未能保存设置，请检查本机文件或凭据管理器的访问权限。"; }
    }
}
