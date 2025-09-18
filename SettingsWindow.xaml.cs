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

            // �����б������ö��
            FontCombo.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies
                                        .Select(f => f.Source).OrderBy(s => s);
            AlignCombo.ItemsSource = Enum.GetValues(typeof(System.Windows.TextAlignment));
        }

        // ����ɫ�Ի���ע�⣺WinForms ColorDialog ���� Alpha������Ĭ�� FF ��͸����
        private void PickColor_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.ColorDialog { AllowFullOpen = true, FullOpen = true };

            // �Ե�ǰ��ɫ��Ϊ��ʼֵ
            if (TryNormalizeHex(ColorTextBox.Text, out var cur))
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(cur);
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = $"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                _settings.ForegroundHex = hex;                 // д�� -> ���� Brush ˢ��
                _lastValidHex = _settings.ForegroundHex;       // �Թ淶�����Ϊ׼
                ColorTextBox.Text = _lastValidHex;
                ColorTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?
                                 .UpdateSource();
            }
        }

        // �ı���ʧ����У�飻�Ϸ���Ӧ�ò�ͬ�����󶨣��Ƿ������
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
                    "��ɫ��ʽ��Ч����ʹ�� #RRGGBB �� #AARRGGBB��",
                    "��Ч��ɫ",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                ColorTextBox.Text = _lastValidHex; // ���ص��ϴ���Чֵ
            }
        }

        // ��һ���� #AARRGGBB��֧�� #RGB/#RRGGBB/#AARRGGBB �ȣ�
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
