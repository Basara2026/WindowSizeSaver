using System.Windows.Threading;

namespace WindowSizeSaver;

/// <summary>
/// ウインドウの表示を監視し、ルールに一致したら位置・サイズを適用する。
///
/// ・アプリは表示の直後に自前で位置を復元することが多い(エクスプローラー、Firefox など)ため、
///   表示から数秒間は位置を監視し、ずれていたら補正し直す。
/// ・ただし補正は「位置が落ち着いてから」「回数制限つき」で行い、アプリと奪い合って
///   ウインドウが行ったり来たりしないようにする。
/// ・ユーザーが自分で動かし始めた(ドラッグ、スナップレイアウト、Win+矢印キー)ら補正をやめる。
/// ・常駐アプリが非表示→再表示された場合も、表示のたびに適用する。
/// </summary>
class Watcher : IDisposable
{
    const int WatchMs = 4000;          // 適用開始後、位置を監視して補正する期間
    const int TitleRetryMs = 1500;     // 表示直後のタイトル変化で別のルールに一致した場合に切り替える期間
    const int DuplicateMs = 400;       // 連続して届く表示イベントをまとめる
    const int SettleMs = 300;          // 2回目以降の補正は、位置がこの時間変化しなくなってから行う
    const int MaxCorrections = 5;      // 1回の表示あたりの補正回数の上限
    const int UserInputGraceMs = 300;  // 表示直後のこの間の入力は、ユーザーの操作とみなさない
    const int CursorMargin = 300;      // ウインドウの外側でもこの距離以内のクリックは、そのウインドウへの操作とみなす

    static readonly int[] MouseKeys = { 0x01, 0x02, 0x04 };   // 左・右・中ボタン
    static readonly int[] WinKeys = { 0x5B, 0x5C };           // 左・右 Windows キー

    sealed class State
    {
        public IntPtr Hwnd;
        public Rule? Rule;
        public long Start;        // 表示を検知した時刻
        public long ApplyAt;      // 適用を開始してよい時刻(遅延設定ぶん待つ)
        public long Until;        // 監視を終える時刻
        public int Corrections;   // 補正した回数
        public Native.RECT Last;  // 前回確認した位置
        public long StableSince;  // 位置が最後に変化した時刻
    }

    readonly Native.WinEventProc _proc;   // GC されないよう保持
    readonly List<IntPtr> _hooks = new();
    readonly Dictionary<IntPtr, State> _active = new();
    readonly HashSet<IntPtr> _seen = new();
    readonly DispatcherTimer _timer;
    readonly bool[] _prevMouse = new bool[MouseKeys.Length];
    readonly bool[] _prevWin = new bool[WinKeys.Length];

    public Watcher()
    {
        _proc = OnEvent;
        Hook(Native.EVENT_SYSTEM_MOVESIZESTART);
        Hook(Native.EVENT_OBJECT_SHOW);
        Hook(Native.EVENT_OBJECT_UNCLOAKED);
        Hook(Native.EVENT_OBJECT_NAMECHANGE);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
    }

    void Hook(uint evt) => _hooks.Add(Native.SetWinEventHook(
        evt, evt, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));

    // ---------------------------------------------------------------- イベント

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != Native.OBJID_WINDOW) return;
        long now = Environment.TickCount64;

        // ユーザーが自分で移動・サイズ変更を始めたら、そのウインドウの補正はやめる
        if (evt == Native.EVENT_SYSTEM_MOVESIZESTART)
        {
            _active.Remove(hwnd);
            return;
        }

        // 表示直後にタイトルが変わった場合(例: 空 → 「メモ帳 - タイトルなし」)
        if (evt == Native.EVENT_OBJECT_NAMECHANGE)
        {
            if (!_active.TryGetValue(hwnd, out var s) || now - s.Start > TitleRetryMs) return;
            var rule = RuleStore.Pick(RuleStore.EnabledFor(Native.GetProcessName(hwnd)), Native.GetTitle(hwnd));
            if (rule == null || ReferenceEquals(rule, s.Rule)) return;
            SetRule(s, rule, now);
            Check(s, now);
            return;
        }

        // 表示(SHOW)/ 非表示解除(UNCLOAKED)
        // UNCLOAKED は仮想デスクトップの切り替えでも発生するため、初回だけ扱う
        if (evt == Native.EVENT_OBJECT_UNCLOAKED && _seen.Contains(hwnd)) return;
        if (_active.TryGetValue(hwnd, out var cur) && now - cur.Start < DuplicateMs) return;
        if (!Native.IsTargetWindow(hwnd)) return;

        string proc = Native.GetProcessName(hwnd);
        if (proc == "") return;
        var cands = RuleStore.EnabledFor(proc);
        if (cands.Count == 0) return;

        if (_seen.Count > 5000) _seen.Clear();
        _seen.Add(hwnd);

        var st = new State { Hwnd = hwnd, Start = now };
        SetRule(st, RuleStore.Pick(cands, Native.GetTitle(hwnd)), now);   // タイトル未確定ならまだ null
        _active[hwnd] = st;

        if (!_timer.IsEnabled)
        {
            ResetInput();   // 表示のきっかけになったクリック等を「ユーザーの操作」と誤認しない
            _timer.Start();
        }
        Check(st, now);
    }

    static void SetRule(State st, Rule? rule, long now)
    {
        st.Rule = rule;
        st.ApplyAt = Math.Max(now, st.Start + Math.Max(0, rule?.Delay ?? 0));
        st.Until = st.ApplyAt + WatchMs;
        st.Corrections = 0;
        st.Last = default;
        st.StableSince = now;
    }

    // ---------------------------------------------------------------- 監視

    void OnTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        bool mousePressed = Poll(MouseKeys, _prevMouse);
        bool winPressed = Poll(WinKeys, _prevWin);
        IntPtr foreground = winPressed ? Native.GetForegroundWindow() : IntPtr.Zero;

        List<IntPtr>? done = null;
        foreach (var st in _active.Values)
        {
            bool finished = now > st.Until
                || !Native.IsWindow(st.Hwnd)
                || !Native.IsWindowVisible(st.Hwnd);

            // スナップレイアウトのクリックや Win+矢印キーなど、ユーザー自身の操作なら補正をやめる
            if (!finished && now - st.Start > UserInputGraceMs)
            {
                if (winPressed && foreground == st.Hwnd) finished = true;
                else if (mousePressed && CursorNearWindow(st.Hwnd)) finished = true;
            }

            if (finished) (done ??= new()).Add(st.Hwnd);
            else Check(st, now);
        }

        if (done != null) foreach (var h in done) _active.Remove(h);
        if (_active.Count == 0) _timer.Stop();
    }

    /// <summary>ルールの位置・サイズになっていなければ合わせる</summary>
    static void Check(State st, long now)
    {
        var r = st.Rule;
        var h = st.Hwnd;
        if (r == null || r.Width <= 0 || r.Height <= 0 || now < st.ApplyAt) return;
        if (Native.IsIconic(h)) return;

        var v = Native.GetVisibleRect(h);
        if (!SameRect(v, st.Last))
        {
            st.Last = v;
            st.StableSince = now;
        }

        // 登録値は「見た目の枠」基準
        if (v.Left == r.X && v.Top == r.Y &&
            v.Right - v.Left == r.Width && v.Bottom - v.Top == r.Height) return;

        // 1回目はすぐ合わせる。2回目以降(アプリに動かし返された場合)は、位置が落ち着くまで待つ
        if (st.Corrections > 0)
        {
            if (st.Corrections >= MaxCorrections) return;
            if (now - st.StableSince < SettleMs) return;
        }

        // 最大化で開こうとするアプリは、通常状態に戻してから合わせる
        if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);

        // 影の分を補正して指定(相手が応答しなくても固まらないよう非同期で)
        var (l, t, rr, b) = Native.GetShadowInsets(h);
        Native.SetWindowPos(h, IntPtr.Zero,
            r.X - l, r.Y - t, r.Width + l + rr, r.Height + t + b,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);

        st.Corrections++;
        st.StableSince = now;
    }

    static bool SameRect(Native.RECT a, Native.RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    // ---------------------------------------------------------------- ユーザー入力の検出

    void ResetInput()
    {
        Poll(MouseKeys, _prevMouse);
        Poll(WinKeys, _prevWin);
    }

    /// <summary>前回の確認以降にキー/ボタンが新たに押されたか</summary>
    static bool Poll(int[] keys, bool[] prev)
    {
        bool pressed = false;
        for (int i = 0; i < keys.Length; i++)
        {
            short s = Native.GetAsyncKeyState(keys[i]);
            bool down = (s & 0x8000) != 0;
            if ((down && !prev[i]) || (s & 0x0001) != 0) pressed = true;
            prev[i] = down;
        }
        return pressed;
    }

    static bool CursorNearWindow(IntPtr h)
    {
        if (!Native.GetCursorPos(out var p) || !Native.GetWindowRect(h, out var r)) return false;
        return p.X >= r.Left - CursorMargin && p.X <= r.Right + CursorMargin
            && p.Y >= r.Top - CursorMargin && p.Y <= r.Bottom + CursorMargin;
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var h in _hooks) Native.UnhookWinEvent(h);
    }
}
