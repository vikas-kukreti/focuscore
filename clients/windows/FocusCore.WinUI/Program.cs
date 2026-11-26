using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FocusCore.WinUI;

internal static class Program
{
    // ------------------------------------------------------------------
    // Win32 constants
    // ------------------------------------------------------------------
    private const int WM_APP = 0x8000;
    private const int WM_TRAY = WM_APP + 1;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_COMMAND = 0x0111;
    private const int WM_DESTROY = 0x0002;
    private const int WM_LBUTTONDOWN = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_LEFTALIGN = 0x00000000;
    private const uint TPM_TOPALIGN = 0x00000000;
    private const uint TPM_RIGHTBUTTON = 0x00000002;

    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0001;
    private const int VK_SPACE = 0x20;
    private const int HOTKEY_ID = 1;

    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
    private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    private const int CMD_PAUSE_RESUME = 1001;
    private const int CMD_COMPLETE_TASK = 1002;
    private const int CMD_QUICK_ADD = 1003;
    private const int CMD_QUIT = 1004;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private static MainWindow? _window;
    private static IntPtr _trayWnd;
    private static IntPtr _trayIcon;
    private static WndProcDelegate _wndProc = null!;
    private static GCHandle _wndProcHandle;

    [STAThread]
    private static void Main(string[] args)
    {
        // Explicit AppUserModelId so toasts, taskbar grouping and Jump Lists bind
        // to this unpackaged WinAppSDK desktop process.
        JumpList.SetAppUserModelId();

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var window = new MainWindow { Title = "FocusCore" };
            App.Current.MainWindow = window;
            App.Current.DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            string dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusCore",
                "focuscore.db");
            if (!CoreService.Instance.Initialize(dbPath))
            {
                CoreService.Instance.Initialize("focuscore.db"); // fallback to working directory
            }

            _window = window;
            CreateTrayWindow(window);

            // Keep the tray tooltip in sync with the timer.
            CoreService.Instance.TimerStateChanged += st => UpdateTrayTip(st);

            window.Activate();
            JumpList.Initialize(window);
        });
    }

    // ------------------------------------------------------------------
    // Hidden message window + system tray (Shell_NotifyIcon)
    // ------------------------------------------------------------------
    private static void CreateTrayWindow(MainWindow window)
    {
        _wndProc = WndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc);

        IntPtr hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = "FocusCore.TrayMessageWindow",
        };
        // 1410 = ERROR_CLASS_ALREADY_EXISTS; safe to ignore for a single-instance app.
        if (RegisterClassW(ref wc) == 0 && GetLastError() != 1410) { }

        _trayWnd = CreateWindowExW(
            0, "FocusCore.TrayMessageWindow", "", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

        _trayIcon = LoadIconW(IntPtr.Zero, IDI_APPLICATION);
        AddOrModifyTrayIcon(NIM_ADD);
        RegisterHotKey(_trayWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
    }

    private static void AddOrModifyTrayIcon(uint command)
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _trayWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _trayIcon,
            szTip = "FocusCore — Idle",
        };
        Shell_NotifyIconW(command, ref nid);
    }

    private static void RemoveTrayIcon()
    {
        if (_trayWnd == IntPtr.Zero) return;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _trayWnd,
            uID = 1,
        };
        Shell_NotifyIconW(NIM_DELETE, ref nid);
    }

    private static void UpdateTrayTip(TimerState st)
    {
        string tip = st.kind switch
        {
            (int)TimerStateKind.Running => $"FocusCore — {FormatTime(st.remaining_sec)} remaining",
            (int)TimerStateKind.Paused => "FocusCore — Paused",
            _ => "FocusCore — Idle",
        };
        SetTrayTip(tip);
    }

    private static void SetTrayTip(string tip)
    {
        if (_trayWnd == IntPtr.Zero) return;
        if (tip.Length > 127) tip = tip[..127];
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _trayWnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = tip,
        };
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAY:
            {
                uint ev = (uint)(lParam.ToInt64() & 0xFFFF);
                if (ev == WM_LBUTTONDOWN)
                {
                    ShowQuickControlDialog();
                    return 0;
                }
                if (ev == WM_RBUTTONDOWN)
                {
                    ShowTrayMenu();
                    return 0;
                }
                break;
            }
            case WM_HOTKEY:
            {
                if (wParam.ToInt32() == HOTKEY_ID)
                {
                    ShowQuickAddDialog();
                    return 0;
                }
                break;
            }
            case WM_COMMAND:
            {
                int cmd = LOWORD(wParam);
                switch (cmd)
                {
                    case CMD_PAUSE_RESUME: TogglePauseResume(); break;
                    case CMD_COMPLETE_TASK: _window?.MarkTaskDone(CoreService.Instance.SelectedTaskId); break;
                    case CMD_QUICK_ADD: ShowQuickAddDialog(); break;
                    case CMD_QUIT: QuitApp(); break;
                }
                return 0;
            }
            case WM_DESTROY:
            {
                RemoveTrayIcon();
                UnregisterHotKey(_trayWnd, HOTKEY_ID);
                break;
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void TogglePauseResume()
    {
        if (CoreService.Instance.TimerIsPaused) CoreService.Instance.TimerResume();
        else if (CoreService.Instance.TimerIsRunning) CoreService.Instance.TimerPause();
    }

    // ------------------------------------------------------------------
    // Tray context menu
    // ------------------------------------------------------------------
    private static void ShowTrayMenu()
    {
        if (_trayWnd == IntPtr.Zero) return;
        IntPtr hMenu = CreatePopupMenuW();
        string pauseResume = CoreService.Instance.TimerIsPaused ? "Resume" : "Pause";
        AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_PAUSE_RESUME, pauseResume);
        AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_COMPLETE_TASK, "Complete Task");
        AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_QUICK_ADD, "Quick Add");
        AppendMenuW(hMenu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_QUIT, "Quit");

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_trayWnd);
        TrackPopupMenuEx(hMenu, TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RIGHTBUTTON, pt.X, pt.Y, _trayWnd, IntPtr.Zero);
        DestroyMenu(hMenu);
    }

    // ------------------------------------------------------------------
    // Quick-add dialog (global hotkey Ctrl+Shift+Space and tray)
    // ------------------------------------------------------------------
    private static void ShowQuickAddDialog()
    {
        if (_window == null || _window.Content == null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _window.Content.XamlRoot,
            Title = "Quick Add",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
        };
        var box = new TextBox { PlaceholderText = "Task title", Width = 340 };
        dialog.Content = box;
        dialog.PrimaryButtonClick += (_, _) =>
        {
            string title = box.Text?.Trim() ?? string.Empty;
            if (title.Length == 0) return;
            string? id = CoreService.Instance.QuickAdd(title);
            if (id != null)
            {
                _window.AddTask(id, title);
                CoreService.Instance.SelectedTaskId = id;
                _window.SelectTask(id);
            }
        };
        _ = dialog.ShowAsync();
    }

    // ------------------------------------------------------------------
    // Quick-control flyout (tray left-click)
    // ------------------------------------------------------------------
    private static void ShowQuickControlDialog()
    {
        if (_window == null || _window.Content == null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _window.Content.XamlRoot,
            Title = "Quick Control",
            PrimaryButtonText = "Close",
        };
        var status = new TextBlock { Text = TimerStatusText(), FontSize = 16 };
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(status);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var pauseResume = new Button { Content = CoreService.Instance.TimerIsPaused ? "Resume" : "Pause" };
        var stop = new Button { Content = "Stop" };
        var quickAdd = new Button { Content = "Quick Add" };
        pauseResume.Click += (_, _) => { TogglePauseResume(); status.Text = TimerStatusText(); };
        stop.Click += (_, _) => { CoreService.Instance.TimerStop(); status.Text = TimerStatusText(); };
        quickAdd.Click += (_, _) => ShowQuickAddDialog();
        btns.Children.Add(pauseResume);
        btns.Children.Add(stop);
        btns.Children.Add(quickAdd);
        sp.Children.Add(btns);
        dialog.Content = sp;
        _ = dialog.ShowAsync();
    }

    private static string TimerStatusText() =>
        CoreService.Instance.TimerIsRunning ? $"Running — {FormatTime(CoreService.Instance.RemainingSec)}"
        : CoreService.Instance.TimerIsPaused ? "Paused"
        : "Idle";

    private static string FormatTime(int totalSeconds)
    {
        int m = totalSeconds / 60;
        int s = totalSeconds % 60;
        if (m < 0) m = 0;
        if (s < 0) s = 0;
        return $"{m:D2}:{s:D2}";
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private static void QuitApp()
    {
        RemoveTrayIcon();
        UnregisterHotKey(_trayWnd, HOTKEY_ID);
        CoreService.Instance.Shutdown();
        Environment.Exit(0);
    }

    // ------------------------------------------------------------------
    // Win32 P/Invoke
    // ------------------------------------------------------------------
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpwc);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static extern IntPtr CreatePopupMenuW();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    private static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern int GetLastError();

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpnid);

    private static int LOWORD(IntPtr ptr) => (int)((uint)(ptr.ToInt64() & 0xFFFFFFFF) & 0xFFFF);

    // ------------------------------------------------------------------
    // Win32 structures
    // ------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    }
}
