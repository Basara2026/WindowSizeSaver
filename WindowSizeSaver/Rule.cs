using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WindowSizeSaver;

public class Rule : INotifyPropertyChanged
{
    public const string AnyTitle = "*";   // "*" = そのプロセスの全ウインドウ

    public event PropertyChangedEventHandler? PropertyChanged;

    bool _enabled;
    string _process = "";
    string _title = AnyTitle;
    int _x, _y, _width, _height, _delay = 100;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Process { get => _process; set => Set(ref _process, value); }
    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) Raise(nameof(DisplayTitle)); }
    }

    /// <summary>設定画面の表示用。「全ウインドウ」を表す "*" は空白で表示する(保存される値は "*" のまま)。</summary>
    [JsonIgnore]
    public string DisplayTitle => IsAny ? "" : Title;
    public int X { get => _x; set => Set(ref _x, value); }
    public int Y { get => _y; set => Set(ref _y, value); }
    public int Width { get => _width; set => Set(ref _width, value); }
    public int Height { get => _height; set => Set(ref _height, value); }

    /// <summary>ウインドウが表示されてから適用するまでの待ち時間(ミリ秒)。0 なら表示と同時に適用。</summary>
    public int Delay { get => _delay; set => Set(ref _delay, value); }

    [JsonIgnore]
    public bool IsAny => Title == AnyTitle;

    public Rule Clone() => new()
    {
        Enabled = Enabled, Process = Process, Title = Title,
        X = X, Y = Y, Width = Width, Height = Height, Delay = Delay
    };

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
