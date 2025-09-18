using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace TaskbarTextOverlayWpf
{
    public partial class MainWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_DPICHANGED = 0x02E0;
        private const int WM_SETTINGCHANGE = 0x001A;

        private static uint WM_TASKBARCREATED = 0;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private const uint ABM_GETTASKBARPOS = 0x00000005;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        private HwndSource? _source;
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly Settings _settings;

        public MainWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings;
            DataContext = _settings;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source!.AddHook(WndProcHook);

            WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

            RepositionToTaskbar();

            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RepositionToTaskbar();

        private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)
             || e.PropertyName == nameof(SystemParameters.PrimaryScreenHeight)
             || e.PropertyName == nameof(SystemParameters.PrimaryScreenWidth))
            {
                RepositionToTaskbar();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.OnClosed(e);
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TASKBARCREATED)
            {
                RepositionToTaskbar();
                handled = true;
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    handled = true; return new IntPtr(MA_NOACTIVATE);
                case WM_NCHITTEST:
                    handled = true; return new IntPtr(HTTRANSPARENT);
                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                case WM_SETTINGCHANGE:
                    RepositionToTaskbar();
                    break;
            }
            return IntPtr.Zero;
        }

        private void RepositionToTaskbar()
        {
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            bool got = SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != IntPtr.Zero;

            RECT px;
            if (got)
            {
                px = abd.rc;
            }
            else
            {
                var tray = FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero || !GetWindowRect(tray, out px))
                {
                    Visibility = Visibility.Hidden;
                    return;
                }
            }

            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is null) return;

            var m = src.CompositionTarget.TransformFromDevice;
            var tl = m.Transform(new System.Windows.Point(px.Left, px.Top));
            var br = m.Transform(new System.Windows.Point(px.Right, px.Bottom));

            Left = tl.X;
            Top = tl.Y;
            Width = Math.Max(0, br.X - tl.X);
            Height = Math.Max(0, br.Y - tl.Y);

            Visibility = (Width > 0 && Height > 0) ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
