using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TaskbarTextOverlayWpf
{
    public class Settings : INotifyPropertyChanged
    {
        // ===== 文本样式 =====
        private string _text = "Hello Taskbar — 任务栏文本";
        private string _fontFamily = "Segoe UI Semibold";
        private double _fontSize = 16;
        private bool _isBold = false;

        // 颜色十六进制 + 缓存 Brush
        private string _foregroundHex = "#FFFFFFFF";
        private System.Windows.Media.SolidColorBrush _foregroundBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);

        private System.Windows.TextAlignment _alignment = System.Windows.TextAlignment.Center;
        private double _shadowOpacity = 0.85;
        private double _shadowBlur = 8;

        // ===== Python 事件流（Spec v2）配置 =====
        private bool _usePython = false;
        private string _pythonCode =
            "# 每行输出一帧\nimport time\nprint('Ready')\nwhile True:\n    print(time.strftime('%H:%M:%S'))\n    time.sleep(1)\n";
        private string? _pythonPath = null;

        // 引擎参数
        private int _minIntervalMs = 250;
        private int _startTimeoutMs = 3000;
        private int _inactivityTimeoutMs = 30000;
        private int _maxBytesPerFrame = 16384;
        private bool _dedupe = false;
        private string _fallbackText = "";

        // ===== 多色行：逐片段渲染 =====
        private bool _useRich = false;
        public ObservableCollection<RichSegment> RichSegments { get; } = new();

        // ===== 可序列化属性 =====
        public string Text { get => _text; set { _text = value; OnChanged(); } }
        public string FontFamily { get => _fontFamily; set { _fontFamily = value; OnChanged(); } }
        public double FontSize { get => _fontSize; set { _fontSize = value; OnChanged(); } }
        public bool IsBold { get => _isBold; set { _isBold = value; OnChanged(); OnChanged(nameof(FontWeightValue)); } }

        public string ForegroundHex
        {
            get => _foregroundHex;
            set
            {
                if (value == _foregroundHex) return;
                if (TryParseColor(value, out var color, out var normalized))
                {
                    _foregroundHex = normalized;
                    _foregroundBrush = new System.Windows.Media.SolidColorBrush(color);
                    _foregroundBrush.Freeze();
                    OnChanged();
                    OnChanged(nameof(ForegroundBrush));
                }
            }
        }

        public System.Windows.TextAlignment Alignment
        {
            get => _alignment;
            set { _alignment = value; OnChanged(); }
        }

        public double ShadowOpacity { get => _shadowOpacity; set { _shadowOpacity = value; OnChanged(); } }
        public double ShadowBlur { get => _shadowBlur; set { _shadowBlur = value; OnChanged(); } }

        // Python 配置
        public bool UsePython { get => _usePython; set { _usePython = value; OnChanged(); } }
        public string PythonCode { get => _pythonCode; set { _pythonCode = value; OnChanged(); } }
        public string? PythonPath { get => _pythonPath; set { _pythonPath = value; OnChanged(); } }

        public int MinIntervalMs { get => _minIntervalMs; set { _minIntervalMs = value; OnChanged(); } }
        public int StartTimeoutMs { get => _startTimeoutMs; set { _startTimeoutMs = value; OnChanged(); } }
        public int InactivityTimeoutMs { get => _inactivityTimeoutMs; set { _inactivityTimeoutMs = value; OnChanged(); } }
        public int MaxBytesPerFrame { get => _maxBytesPerFrame; set { _maxBytesPerFrame = value; OnChanged(); } }
        public bool Dedupe { get => _dedupe; set { _dedupe = value; OnChanged(); } }
        public string FallbackText { get => _fallbackText; set { _fallbackText = value; OnChanged(); } }

        // 多色模式开关
        public bool UseRich { get => _useRich; set { _useRich = value; OnChanged(); } }

        // ===== 运行时派生属性 =====
        [JsonIgnore] public System.Windows.Media.Brush ForegroundBrush => _foregroundBrush;
        [JsonIgnore]
        public System.Windows.FontWeight FontWeightValue =>
            IsBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static bool TryParseColor(string input,
            out System.Windows.Media.Color color, out string normalized)
        {
            color = default; normalized = default!;
            try
            {
                var brush = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString(input)!;
                if (brush is not System.Windows.Media.SolidColorBrush solid) return false;
                color = solid.Color;
                normalized = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                return true;
            }
            catch { return false; }
        }
    }

    // ===== 逐片段（逐字符）模型 =====
    public class RichSegment : INotifyPropertyChanged
    {
        private string _text = "";
        private string _foregroundHex = "#FFFFFFFF";
        private System.Windows.Media.SolidColorBrush _foregroundBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);

        public string Text { get => _text; set { _text = value ?? ""; OnChanged(); } }

        public string ForegroundHex
        {
            get => _foregroundHex;
            set
            {
                if (value == _foregroundHex) return;
                _foregroundHex = value ?? "#FFFFFFFF";
                if (TryParseColor(_foregroundHex, out var color))
                {
                    _foregroundBrush = new System.Windows.Media.SolidColorBrush(color);
                    _foregroundBrush.Freeze();
                    OnChanged(nameof(ForegroundBrush));
                }
            }
        }

        [JsonIgnore] public System.Windows.Media.Brush ForegroundBrush => _foregroundBrush;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static bool TryParseColor(string input, out System.Windows.Media.Color color)
        {
            color = default;
            try
            {
                var brush = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString(input)!;
                if (brush is not System.Windows.Media.SolidColorBrush solid) return false;
                color = solid.Color;
                return true;
            }
            catch { return false; }
        }
    }
}
