using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FocusCore.WinUI;

/// <summary>
/// Mirror of the Rust `TimerState` returned by `focuscore_timer_state()`.
/// Layout must match `TimerStateAbi` in `core/src/abi.rs` (repr(C), 5 fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TimerState
{
    public int kind;        // 0 Idle, 1 Running, 2 Paused
    public IntPtr task_id;  // null-terminated ANSI string; IntPtr.Zero when none
    public int mode;        // -1 none, else TimerMode discriminant
    public int remaining_sec;
    public int elapsed_sec;
}

public enum TimerMode { Pomodoro = 0, Stopwatch = 1 }
public enum TimerStateKind { Idle = 0, Running = 1, Paused = 2 }

/// <summary>
/// Singleton over the `focuscore.dll` C ABI.
///
/// Owns the core database at %LOCALAPPDATA%\FocusCore\focuscore.db and polls the
/// timer engine once a second on a DispatcherTimer, surfacing state changes so
/// the UI, tray tooltip and taskbar progress can stay in sync.
/// </summary>
public sealed class CoreService : IDisposable
{
    public static CoreService Instance { get; } = new CoreService();

    private readonly DispatcherTimer _poll;
    private TimerState _last;          // last polled state (task_id already copied/freed)
    private string? _selectedTaskId;

    public event Action<TimerState>? TimerStateChanged;
    public event Action? TimerCompleted; // Raised when the timer transitions Running -> Idle.

    private CoreService()
    {
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => Poll();
    }

    /// <summary>Opens the core database. Creates the parent folder if missing.</summary>
    public bool Initialize(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch { /* best effort; open will report failure if the path is unusable */ }

        IntPtr pathPtr = StringToAnsiPtr(dbPath); // open reads the path synchronously
        int rc = Native.focuscore_open(pathPtr);
        FreeString(pathPtr);
        if (rc != 0) return false;

        _poll.Start();
        return true;
    }

    public void Shutdown()
    {
        _poll.Stop();
        Native.focuscore_close();
        if (_last.task_id != IntPtr.Zero)
        {
            Native.focuscore_string_free(_last.task_id);
            _last.task_id = IntPtr.Zero;
        }
    }

    public void Dispose() => Shutdown();

    /// <summary>Task selected in the UI (used by tray "Complete Task" and toasts).</summary>
    public string? SelectedTaskId
    {
        get => _selectedTaskId;
        set => _selectedTaskId = value;
    }

    /// <summary>True when the timer is currently running.</summary>
    public bool TimerIsRunning => _last.kind == (int)TimerStateKind.Running;

    /// <summary>True when the timer is paused.</summary>
    public bool TimerIsPaused => _last.kind == (int)TimerStateKind.Paused;

    /// <summary>Remaining seconds from the last polled state.</summary>
    public int RemainingSec => _last.remaining_sec;

    /// <summary>Quick-add a top-level task. Returns the new task id, or null on failure.</summary>
    public string? QuickAdd(string title, string? scheduledDate = null)
    {
        IntPtr titlePtr = StringToAnsiPtr(title ?? "");
        IntPtr datePtr = scheduledDate == null ? IntPtr.Zero : StringToAnsiPtr(scheduledDate);
        try
        {
            IntPtr idPtr = Native.focuscore_quick_add(titlePtr, datePtr);
            string? id = PtrToString(idPtr);
            if (idPtr != IntPtr.Zero) Native.focuscore_string_free(idPtr); // returned CString is owned by the caller
            return string.IsNullOrEmpty(id) ? null : id;
        }
        finally
        {
            FreeString(titlePtr);
            FreeString(datePtr);
        }
    }

    public void TimerStart(string taskId, TimerMode mode, int totalSec)
    {
        IntPtr ptr = StringToAnsiPtr(taskId);
        try { Native.focuscore_timer_start(ptr, (int)mode, totalSec); }
        finally { FreeString(ptr); }
    }

    public void TimerStop() => Native.focuscore_timer_stop();
    public void TimerPause() => Native.focuscore_timer_pause();
    public void TimerResume() => Native.focuscore_timer_resume();
    public void TimerReset() => Native.focuscore_timer_reset();

    /// <summary>Log a finished interval. Returns the entry id.</summary>
    public string? InsertTimeEntry(
        string id,
        string taskId,
        TimerMode mode,
        long startedAt,
        long endedAt,
        int durationSec,
        long createdAt,
        long updatedAt)
    {
        IntPtr idPtr = StringToAnsiPtr(id);
        IntPtr tidPtr = StringToAnsiPtr(taskId);
        try
        {
            IntPtr outPtr = Native.focuscore_insert_time_entry(
                idPtr, tidPtr, (int)mode, startedAt, endedAt, durationSec, createdAt, updatedAt);
            string? idOut = PtrToString(outPtr);
            return string.IsNullOrEmpty(idOut) ? null : idOut;
        }
        finally
        {
            FreeString(idPtr);
            FreeString(tidPtr);
        }
    }

    /// <summary>JSON list of time entries for a task (raw string from the core).</summary>
    public string? ListTimeEntriesJson(string taskId)
    {
        IntPtr tidPtr = StringToAnsiPtr(taskId);
        try { return PtrToString(Native.focuscore_list_time_entries(tidPtr)); }
        finally { FreeString(tidPtr); }
    }

    /// <summary>One delta-sync round-trip. 0 = ok, non-zero = failure.</summary>
    public int Sync(string baseUrl, string token)
    {
        IntPtr urlPtr = StringToAnsiPtr(baseUrl);
        IntPtr tokPtr = StringToAnsiPtr(token);
        try { return Native.focuscore_sync(urlPtr, tokPtr); }
        finally { FreeString(urlPtr); FreeString(tokPtr); }
    }

    // ------------------------------------------------------------------
    // Internal: poll the timer engine once per second.
    // ------------------------------------------------------------------
    private void Poll()
    {
        TimerState st = Native.focuscore_timer_state();

        // Copy the task id out of the struct, then release the core-owned string.
        string? taskId = PtrToString(st.task_id);
        if (st.task_id != IntPtr.Zero)
        {
            Native.focuscore_string_free(st.task_id);
            st.task_id = IntPtr.Zero;
        }

        bool completed = _last.kind == (int)TimerStateKind.Running
            && st.kind == (int)TimerStateKind.Idle
            && _last.remaining_sec > 0
            && st.remaining_sec == 0;

        // Release the previous state's owned string before we overwrite it.
        if (_last.task_id != IntPtr.Zero)
        {
            Native.focuscore_string_free(_last.task_id);
        }
        _last = st;

        TimerStateChanged?.Invoke(st);
        if (completed) TimerCompleted?.Invoke();
    }

    // ------------------------------------------------------------------
    // String marshaling helpers (C ABI = null-terminated ANSI/UTF-8 strings).
    // ------------------------------------------------------------------
    private static IntPtr StringToAnsiPtr(string s) => Marshal.StringToHGlobalAnsi(s);
    private static void FreeString(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private static string PtrToString(IntPtr ptr) => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr)!;
}

internal static class Native
{
    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_version();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int focuscore_open(IntPtr dbPath);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_close();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_quick_add(IntPtr title, IntPtr scheduledDate);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern TimerState focuscore_timer_state();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_timer_start(IntPtr taskId, int mode, int totalSec);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_timer_pause();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_timer_resume();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int focuscore_timer_stop();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_timer_reset();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_insert_time_entry(
        IntPtr id, IntPtr taskId, int mode,
        long startedAt, long endedAt, int durationSec, long createdAt, long updatedAt);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_list_time_entries(IntPtr taskId);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int focuscore_sync(IntPtr baseUrl, IntPtr token);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_string_free(IntPtr ptr);
}
