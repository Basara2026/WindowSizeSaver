namespace WindowSizeSaver;

static class AppInfo
{
    public const string Name = "WindowSizeSaver";

    /// <summary>
    /// バージョン: 更新日時(年月日時分)をそのまま数字にする。更新のたびに書き換える。
    /// </summary>
    public const string Version = "202611211159";

    public static string VersionText => $"{Name} Version {Version}";
}
