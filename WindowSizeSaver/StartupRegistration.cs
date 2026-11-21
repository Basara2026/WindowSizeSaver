using System.IO;
using System.Runtime.InteropServices;

namespace WindowSizeSaver;

/// <summary>
/// Windows 起動時の自動起動(スタートアップ)の登録・解除。
/// 現在のユーザーのスタートアップフォルダ(shell:startup)に、このアプリのショートカットを作る/削除する。
/// </summary>
static class StartupRegistration
{
    const string ShortcutName = "WindowSizeSaver.lnk";

    static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    /// <summary>このアプリ(現在の実行ファイル)のショートカットがスタートアップにあるか</summary>
    public static bool IsRegistered()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !File.Exists(ShortcutPath)) return false;

        try
        {
            return string.Equals(ReadTarget(ShortcutPath), exe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;   // リンク先を読み取れない場合は、ショートカットがあれば登録済みとみなす
        }
    }

    /// <summary>登録(true)または解除(false)する。成功すれば true。</summary>
    public static bool Set(bool enable)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return false;

        try
        {
            if (enable) CreateShortcut(ShortcutPath, exe);
            else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            return true;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- ショートカットの作成・読み取り(WScript.Shell を使用)

    static void CreateShortcut(string linkPath, string exePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell を利用できません。");
        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = ((dynamic)shell).CreateShortcut(linkPath);
            try
            {
                link.TargetPath = exePath;
                link.WorkingDirectory = Path.GetDirectoryName(exePath);
                link.Description = "WindowSizeSaver";
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject((object)link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    static string? ReadTarget(string linkPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return null;

        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = ((dynamic)shell).CreateShortcut(linkPath);
            try { return (string?)link.TargetPath; }
            finally { Marshal.FinalReleaseComObject((object)link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
