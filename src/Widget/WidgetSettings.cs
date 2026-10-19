using System.IO;
using System.Text.Json;

namespace CodexUsageWidgetApp;

internal sealed class WidgetSettings
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageWidget");
    internal static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string CodexPath { get; set; } = "";
    public int RefreshSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }

    internal static WidgetSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 30, 3600);
            return settings;
        }
        catch { return new(); }
    }

    internal void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }

    internal void SetStartup()
    {
        string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AI Usage Widget.lnk");
        if (!StartWithWindows) { File.Delete(shortcut); return; }
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("无法创建启动快捷方式。");
        object shell = Activator.CreateInstance(shellType)!;
        object? linkObject = null;
        try
        {
            dynamic dynamicShell = shell;
            linkObject = dynamicShell.CreateShortcut(shortcut);
            dynamic link = linkObject;
            link.TargetPath = Environment.ProcessPath!;
            link.WorkingDirectory = AppContext.BaseDirectory;
            link.Description = "AI 用量浮窗";
            link.Save();
        }
        finally
        {
            if (linkObject is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(linkObject);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
