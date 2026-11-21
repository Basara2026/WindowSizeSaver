using System.IO;
using System.Text.Json;

namespace WindowSizeSaver;

/// <summary>列幅(幅の種類は "Pixel" / "Star")</summary>
sealed class ColumnWidth
{
    public double Value { get; set; }
    public string Unit { get; set; } = "";
}

/// <summary>設定画面の位置・サイズ・列幅</summary>
sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public List<ColumnWidth>? Columns { get; set; }
}

/// <summary>画面表示まわりの設定。実行ファイルと同じフォルダに保存する。</summary>
static class UiSettings
{
    static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "WindowSizeSaver.ui-settings.json");
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static WindowBounds? LoadSettingsWindow()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(FilePath));
        }
        catch { }
        return null;
    }

    public static void SaveSettingsWindow(WindowBounds bounds)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(bounds, JsonOptions));
        }
        catch { }
    }
}
