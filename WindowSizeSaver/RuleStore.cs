using System.IO;
using System.Text.Json;

namespace WindowSizeSaver;

/// <summary>有効になっている(適用済みの)ルールと、その保存</summary>
static class RuleStore
{
    /// <summary>設定ファイル: 実行ファイルと同じフォルダ</summary>
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "WindowSizeSaver.settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly object Lock = new();

    public static List<Rule> Rules { get; private set; } = Load();
    public static string? LastError { get; private set; }

    /// <summary>ホットキーで登録・更新されたルール(設定画面の表示更新用)</summary>
    public static event Action<IReadOnlyList<Rule>>? Registered;

    static List<Rule> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<Rule>>(File.ReadAllText(FilePath));
                if (list != null) return list;
            }
        }
        catch { }
        return new List<Rule>();
    }

    public static bool Save()
    {
        lock (Lock)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Rules, JsonOptions));
                File.Move(tmp, FilePath, true);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    /// <summary>設定画面で OK/適用 された内容に置き換えて保存する</summary>
    public static bool Replace(IEnumerable<Rule> rules)
    {
        Rules = rules.Select(r => r.Clone()).ToList();
        return Save();
    }

    // 新規ルールの遅延(ms)の初期値。Firefox は起動後に自分で位置を戻すので長めに待つ
    static int DefaultDelay(string process) =>
        process.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase) ? 1000 : 100;

    /// <summary>ホットキー登録:「*」行とタイトル行の2行を追加(同一キーは位置・サイズを上書き)</summary>
    public static void Register(string process, string title, int x, int y, int w, int h)
    {
        var affected = new List<Rule>();

        void Upsert(string t)
        {
            var r = Rules.FirstOrDefault(a =>
                a.Process.Equals(process, StringComparison.OrdinalIgnoreCase) && a.Title == t);
            if (r == null) { r = new Rule { Process = process, Title = t, Delay = DefaultDelay(process) }; Rules.Add(r); }
            r.X = x; r.Y = y; r.Width = w; r.Height = h;
            affected.Add(r);
        }

        Upsert(Rule.AnyTitle);
        if (!string.IsNullOrEmpty(title) && title != Rule.AnyTitle) Upsert(title);
        Save();
        Registered?.Invoke(affected);
    }

    public static List<Rule> EnabledFor(string process) =>
        Rules.Where(r => r.Enabled &&
            r.Process.Equals(process, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>タイトル指定の行を優先し、なければ「*」の行</summary>
    public static Rule? Pick(List<Rule> candidates, string title) =>
        candidates.FirstOrDefault(r => !r.IsAny && r.Title == title)
        ?? candidates.FirstOrDefault(r => r.IsAny);
}
