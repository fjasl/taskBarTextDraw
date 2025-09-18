using System;
using System.Linq;

namespace TaskbarTextOverlayWpf
{
    public partial class SettingsWindow : System.Windows.Window
    {
        private readonly Settings _settings;
        private readonly SettingsService _svc;
        private string _lastValidHex = "#FFFFFFFF";

        public SettingsWindow(Settings settings, SettingsService svc)
        {
            InitializeComponent();
            _settings = settings;
            _svc = svc;
            DataContext = _settings;

            _lastValidHex = _settings.ForegroundHex;

            // 字体列表与对齐枚举
            FontCombo.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies
                                        .Select(f => f.Source).OrderBy(s => s);
            AlignCombo.ItemsSource = Enum.GetValues(typeof(System.Windows.TextAlignment));
        }

        // 打开颜色对话框（注意：WinForms ColorDialog 不含 Alpha，返回默认 FF 不透明）
        private void PickColor_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.ColorDialog { AllowFullOpen = true, FullOpen = true };

            // 以当前颜色作为初始值
            if (TryNormalizeHex(ColorTextBox.Text, out var cur))
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(cur);
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = $"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                _settings.ForegroundHex = hex;                 // 写回 -> 触发 Brush 刷新
                _lastValidHex = _settings.ForegroundHex;       // 以规范化后的为准
                ColorTextBox.Text = _lastValidHex;
                ColorTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?
                                 .UpdateSource();
            }
        }

        // 文本框失焦：校验；合法则应用并同步到绑定；非法则回退
        private void ColorTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            var input = ColorTextBox.Text?.Trim() ?? string.Empty;
            if (TryNormalizeHex(input, out var normalized))
            {
                _settings.ForegroundHex = normalized;
                _lastValidHex = normalized;
                ColorTextBox.Text = normalized;
                ColorTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?
                                   .UpdateSource();
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
                System.Windows.MessageBox.Show(
                    "颜色格式无效：请使用 #RRGGBB 或 #AARRGGBB。",
                    "无效颜色",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                ColorTextBox.Text = _lastValidHex; // 驳回到上次有效值
            }
        }

        // 归一化到 #AARRGGBB（支持 #RGB/#RRGGBB/#AARRGGBB 等）
        private static bool TryNormalizeHex(string input, out string normalized)
        {
            normalized = default!;
            try
            {
                var brush = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString(input)!;

                if (brush is not System.Windows.Media.SolidColorBrush solid) return false;

                var c = solid.Color;
                normalized = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _svc.Save(_settings);
            Close();
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
