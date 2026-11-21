using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowSizeSaver;

/// <summary>
/// 設定画面。編集は作業用のコピーに対して行い、
/// 「OK」「適用」を押した時点で初めて有効(保存)になる。「キャンセル」で破棄する。
/// 閉じた時の位置・サイズ・列幅を記憶する。
/// </summary>
public partial class SettingsWindow : Window
{
    readonly ObservableCollection<Rule> _working = new();
    bool _loading;
    bool _startupRegistered;   // 実際にスタートアップへ登録されている状態(OK・適用で反映するまで変わらない)

    public SettingsWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionText;
        ApplySavedLayout();
        _startupRegistered = StartupRegistration.IsRegistered();
        StartupCheck.IsChecked = _startupRegistered;

        _loading = true;
        foreach (var r in RuleStore.Rules) AddWorking(r.Clone());
        _loading = false;

        _working.CollectionChanged += (_, _) => MarkDirty();
        RulesGrid.ItemsSource = _working;

        // ヘッダーの文字を中央揃えにする(表示された後に実行)
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(CenterColumnHeaders), DispatcherPriority.Loaded);

        // ホットキーで登録・更新されたルールを一覧へ反映
        RuleStore.Registered += OnRegistered;
        Closed += (_, _) => RuleStore.Registered -= OnRegistered;

        // OK・キャンセル・×のどれで閉じても、位置・サイズ・列幅を記憶する。
        // 強制終了などで失われないよう、列幅を変えた直後と、別のウインドウに切り替えた時にも保存する。
        Closing += (_, _) => SaveLayout();
        Deactivated += (_, _) => SaveLayout();
        RulesGrid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SaveLayout()));
    }

    // ---------------------------------------------------------------- 位置・サイズ・列幅の記憶

    void ApplySavedLayout()
    {
        var b = UiSettings.LoadSettingsWindow();
        if (b == null) return;
        ApplySavedBounds(b);
        ApplySavedColumns(b);
    }

    void ApplySavedBounds(WindowBounds b)
    {
        if (b.Width < MinWidth || b.Height < MinHeight) return;

        // モニター構成が変わって画面外になってしまう場合は、保存値を使わない
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(new Rect(b.Left, b.Top, b.Width, b.Height), screen);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 50) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = b.Left;
        Top = b.Top;
        Width = b.Width;
        Height = b.Height;
        if (b.Maximized) WindowState = WindowState.Maximized;
    }

    void ApplySavedColumns(WindowBounds b)
    {
        if (b.Columns == null || b.Columns.Count != RulesGrid.Columns.Count) return;

        for (int i = 0; i < b.Columns.Count; i++)
        {
            var w = b.Columns[i];
            // 以前のバージョンは窓幅に連動する列("Star")も保存していたが、現在は固定幅のみ
            if (w.Unit != "Pixel") continue;
            if (double.IsNaN(w.Value) || double.IsInfinity(w.Value) || w.Value < 20) continue;
            RulesGrid.Columns[i].Width = new DataGridLength(w.Value, DataGridLengthUnitType.Pixel);
        }
    }

    void SaveLayout()
    {
        // 最大化・最小化中は、元に戻した時の位置とサイズを保存する
        Rect r = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var columns = new List<ColumnWidth>();
        foreach (var col in RulesGrid.Columns)
        {
            double width = col.Width.UnitType == DataGridLengthUnitType.Pixel
                ? col.Width.Value
                : col.ActualWidth;
            columns.Add(width > 0
                ? new ColumnWidth { Value = width, Unit = "Pixel" }
                : new ColumnWidth { Value = 0, Unit = "Other" });
        }

        UiSettings.SaveSettingsWindow(new WindowBounds
        {
            Left = r.Left,
            Top = r.Top,
            Width = r.Width,
            Height = r.Height,
            Maximized = WindowState == WindowState.Maximized,
            Columns = columns
        });
    }

    // ---------------------------------------------------------------- 見た目の調整

    static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var d in FindVisualChildren<T>(child)) yield return d;
        }
    }

    /// <summary>
    /// ヘッダーの文字を中央揃えにする。
    /// Fluent のヘッダーは右側に余白(並べ替え記号などの分)があり、中央揃えにしても文字が左に寄るため、
    /// 実際の文字の位置を測って、列の中央との差を補正する。
    /// </summary>
    void CenterColumnHeaders()
    {
        foreach (var header in FindVisualChildren<DataGridColumnHeader>(RulesGrid))
        {
            header.HorizontalContentAlignment = HorizontalAlignment.Center;
            header.UpdateLayout();

            var text = FindVisualChildren<TextBlock>(header).FirstOrDefault();
            if (text == null || text.ActualWidth <= 0 || header.ActualWidth <= 0) continue;

            text.RenderTransform = Transform.Identity;   // 補正を一度外してから測る
            double center = text.TransformToAncestor(header).Transform(new Point(text.ActualWidth / 2, 0)).X;
            double offset = header.ActualWidth / 2 - center;
            if (Math.Abs(offset) >= 0.5)
                text.RenderTransform = new TranslateTransform(offset, 0);
        }
    }

    // ---------------------------------------------------------------- 編集内容

    void AddWorking(Rule rule)
    {
        rule.PropertyChanged += (_, _) => MarkDirty();
        _working.Add(rule);
    }

    void MarkDirty()
    {
        if (!_loading) ApplyButton.IsEnabled = true;
    }

    // ホットキー登録は既に保存済みなので、変更扱い(適用ボタンの有効化)にはしない
    void OnRegistered(IReadOnlyList<Rule> rules)
    {
        _loading = true;
        try
        {
            foreach (var r in rules)
            {
                var w = _working.FirstOrDefault(a =>
                    a.Process.Equals(r.Process, StringComparison.OrdinalIgnoreCase) && a.Title == r.Title);
                if (w == null)
                {
                    AddWorking(r.Clone());
                }
                else
                {
                    w.X = r.X; w.Y = r.Y; w.Width = r.Width; w.Height = r.Height;
                }
            }
        }
        finally { _loading = false; }
    }

    bool CommitGridEdit()
    {
        return RulesGrid.CommitEdit(DataGridEditingUnit.Cell, true)
            && RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    bool ApplyChanges()
    {
        if (!CommitGridEdit())
        {
            MessageBox.Show(this, "入力内容に誤りがあります。X・Y・幅・高さ・遅延には整数を入力してください。",
                "WindowSizeSaver", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!RuleStore.Replace(_working))
        {
            MessageBox.Show(this,
                $"設定ファイルを保存できませんでした。\n{RuleStore.FilePath}\n\n{RuleStore.LastError}",
                "WindowSizeSaver", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // スタートアップ登録の変更を反映
        bool wantStartup = StartupCheck.IsChecked == true;
        if (wantStartup != _startupRegistered)
        {
            if (!StartupRegistration.Set(wantStartup))
            {
                MessageBox.Show(this, "スタートアップの登録を変更できませんでした。",
                    "WindowSizeSaver", MessageBoxButton.OK, MessageBoxImage.Warning);
                _startupRegistered = StartupRegistration.IsRegistered();
                StartupCheck.IsChecked = _startupRegistered;
                return false;
            }
            _startupRegistered = wantStartup;
        }

        ApplyButton.IsEnabled = false;
        return true;
    }

    /// <summary>スタートアップ登録のチェック: 変更扱いにするだけ。OK・適用を押した時に反映する。</summary>
    void OnStartupClick(object sender, RoutedEventArgs e) => MarkDirty();

    void OnOk(object sender, RoutedEventArgs e)
    {
        if (ApplyChanges()) Close();
    }

    void OnApply(object sender, RoutedEventArgs e) => ApplyChanges();

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Rule rule)
            _working.Remove(rule);
    }
}
