namespace TaskbarTextOverlayWpf
{
    public partial class App : System.Windows.Application
    {
        private TrayIcon? _tray;
        private SettingsService? _settingsService;
        private Settings? _settings;
        private MainWindow? _overlay;

        private static System.Threading.Mutex? _mutex;

        private PythonEngine? _engine;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            // ��ʵ��
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "TaskbarTextOverlayWpf.SingleInstance", out createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("�����������С�", "��ʾ");
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // ��������
            _settingsService = new SettingsService();
            _settings = _settingsService.Load();

            // ���Ӵ�
            _overlay = new MainWindow(_settings!);
            _overlay.Show();

            // ����
            _tray = new TrayIcon(
                onOpenSettings: () =>
                {
                    var win = new SettingsWindow(_settings!, _settingsService!);
                    win.Owner = _overlay;
                    win.Show();
                    win.Activate();
                },
                onToggleOverlay: () =>
                {
                    _overlay!.Visibility = _overlay.Visibility == System.Windows.Visibility.Visible
                        ? System.Windows.Visibility.Hidden
                        : System.Windows.Visibility.Visible;
                },
                onExit: () =>
                {
                    _tray!.Dispose();
                    _overlay!.Close();
                    Shutdown();
                });

            // ������Ӱ��ű������������仯 �� ��������
            _settings!.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName is nameof(Settings.UsePython)
                    or nameof(Settings.PythonCode)
                    or nameof(Settings.PythonPath)
                    or nameof(Settings.MinIntervalMs)
                    or nameof(Settings.StartTimeoutMs)
                    or nameof(Settings.InactivityTimeoutMs)
                    or nameof(Settings.MaxBytesPerFrame)
                    or nameof(Settings.Dedupe))
                {
                    RestartEngine();
                }
            };

            RestartEngine();
        }

        private async void RestartEngine()
        {
            if (_settings is null) return;

            await StopEngineAsync();

            if (!_settings.UsePython)
            {
                // δ���ýű���ֻ��ʾ��̬ Text
                return;
            }

            _engine = new PythonEngine(_settings);
            // ÿ��֡�����ű� stdout ��һ�У�����һ���ı�
            _engine.OnFrame += text =>
            {
                if (_settings!.Dedupe && _settings.Text == text) return; // ��ѡȥ��
                _settings.Text = text ?? string.Empty;
            };
            // �������¼���������ʾ
            _engine.OnError += err =>
            {
                System.Diagnostics.Debug.WriteLine("[PythonEngine] " + err);
            };

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(_settings!.StartTimeoutMs + 10000);
                await _engine.StartAsync(cts.Token);
            }
            catch
            {
                // �����쳣ʱ����ԭ�ı���������
            }
        }

        private async System.Threading.Tasks.Task StopEngineAsync()
        {
            if (_engine != null)
            {
                try { await _engine.StopAsync(); } catch { }
                _engine = null;
            }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _tray?.Dispose();
            _ = StopEngineAsync();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
