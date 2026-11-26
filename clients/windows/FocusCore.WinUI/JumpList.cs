using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FocusCore.WinUI;

/// <summary>
/// Taskbar integration:
///   - `ITaskbarList3.SetProgressValue` / `SetProgressState` Pomodoro progress on the taskbar icon.
///   - Jump List tasks ("Quick Add Task", "Start Focus") via `ICustomDestinationList`.
///
/// Both are best-effort: the COM calls are guarded so a missing shell component
/// never crashes the app.
/// </summary>
public static class JumpList
{
    public const string AppId = "FocusCore";

    // GUIDs for the shell COM components we consume.
    private static readonly Guid CLSID_TaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid IID_ITaskbarList3 = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
    private static readonly Guid CLSID_DestinationList = new("77F10CF0-3DB5-4966-B520-B7C546AC3523");
    private static readonly Guid IID_ICustomDestinationList = new("85522440-A677-4622-A858-22CD95BD5824");
    private static readonly Guid IID_IObjectArray = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid IID_IShellLink = new("000214EE-0000-0000-C000-000000000046");
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    private static ITaskbarList3? _taskbar;

    /// <summary>Sets the explicit AppUserModelId for this process (toasts, taskbar grouping, Jump List).</summary>
    public static void SetAppUserModelId()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppId); }
        catch { /* best effort */ }
    }

    public static void Initialize(Window window)
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            SetAppUserModelId();
            SetJumpListTasks();
            _taskbar = GetTaskbarList();
            _taskbar?.HrInit();
        }
        catch { /* best effort */ }
    }

    public static void SetProgress(Window window, double current, double max)
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            var tb = GetTaskbarList();
            if (tb == null) return;
            if (max <= 0)
            {
                tb.SetProgressState(hwnd, TBPF.TBPF_NOPROGRESS);
            }
            else
            {
                tb.SetProgressState(hwnd, TBPF.TBPF_NORMAL);
                tb.SetProgressValue(hwnd, (ulong)Math.Round(current), (ulong)Math.Round(max));
            }
        }
        catch { /* best effort */ }
    }

    public static void ClearProgress(Window window)
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            GetTaskbarList()?.SetProgressState(hwnd, TBPF.TBPF_NOPROGRESS);
        }
        catch { /* best effort */ }
    }

    private static ITaskbarList3? GetTaskbarList()
    {
        if (_taskbar != null) return _taskbar;
        try
        {
            object obj = CoCreateInstance(CLSID_TaskbarList, null, CLSCTX_INPROC_SERVER, IID_ITaskbarList3);
            _taskbar = (ITaskbarList3)obj;
        }
        catch { _taskbar = null; }
        return _taskbar;
    }

    private static void SetJumpListTasks()
    {
        try
        {
            object dest = CoCreateInstance(CLSID_DestinationList, null, CLSCTX_INPROC_SERVER, IID_ICustomDestinationList);
            var cd = (ICustomDestinationList)dest;
            cd.AddUserTasks(BuildLinkArray());
            cd.CommitList();
        }
        catch { /* best effort — Jump List tasks require a pinned taskbar button */ }
    }

    private static IObjectArray BuildLinkArray()
    {
        string exe = Environment.ProcessPath ?? string.Empty;
        string dir = Path.GetDirectoryName(exe) ?? string.Empty;
        var links = new List<IShellLink>
        {
            MakeLink(exe, dir, "--quick-add", "Quick Add Task", "Open the quick-add task dialog"),
            MakeLink(exe, dir, "--start-focus", "Start Focus", "Start a Pomodoro focus session"),
        };
        return new ShellLinkArray(links);
    }

    private static IShellLink MakeLink(string path, string workingDir, string args, string title, string desc)
    {
        object shell = CoCreateInstance(CLSID_ShellLink, null, CLSCTX_INPROC_SERVER, IID_IShellLink);
        var link = (IShellLink)shell;
        link.SetPath(path);
        link.SetArguments(args);
        link.SetDescription(title);
        link.SetWorkingDirectory(workingDir);
        return link;
    }

    // ------------------------------------------------------------------
    // COM interop declarations.
    // ------------------------------------------------------------------

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.Interface)] object? pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shcore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] int SetProgressState(IntPtr hwnd, TBPF tbpFlags);
    }

    [ComImport, Guid("85522440-A677-4622-A858-22CD95BD5824"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        [PreserveSig] int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poaObjectArray);
        [PreserveSig] int AddDestination([MarshalAs(UnmanagedType.Interface)] object poiu, [MarshalAs(UnmanagedType.Interface)] object poiaRoot);
        [PreserveSig] int RemoveCategory([MarshalAs(UnmanagedType.Interface)] object psiArray, [MarshalAs(UnmanagedType.Interface)] object psiRoot);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poaObjectArray);
        [PreserveSig] int AppendKnownCategory(uint kdc);
        [PreserveSig] int CommitList();
        [PreserveSig] int GetRemovedDestinations(out IObjectArray ppoa, Guid riid, out object ppv);
        [PreserveSig] int DeleteAll([MarshalAs(UnmanagedType.Bool)] bool fRemoveAllDestinations);
    }

    // Implemented by a CCW-exposed C# class so the shell can enumerate our links.
    [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        uint GetCount(out uint count);
        int GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("000214EE-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] IntPtr pszFile, int cch);
        [PreserveSig] int GetIDList(IntPtr ppidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.LPWStr)] IntPtr pszName, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] IntPtr pszIconPath, int cch, out int piIcon);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        [PreserveSig] int SetHotKey(ushort wHotKey);
        [PreserveSig] int SetShowCmd(int iShowCmd);
        [PreserveSig] int GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] IntPtr pszDir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    }

    public enum TBPF
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8,
    }

    [ComVisible(true)]
    [Guid("4B7F1C2A-8D9E-4F3A-9C2B-7A1E6D5C4B3A")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ShellLinkArray : IObjectArray
    {
        private readonly List<IShellLink> _links;
        public ShellLinkArray(List<IShellLink> links) => _links = links;

        public uint GetCount(out uint count)
        {
            count = (uint)_links.Count;
            return 0;
        }

        public int GetAt(uint index, ref Guid riid, out object ppv)
        {
            if (index >= (uint)_links.Count) { ppv = null; return unchecked((int)0x80070057); } // E_INVALIDARG
            ppv = _links[(int)index];
            return 0;
        }
    }
}
