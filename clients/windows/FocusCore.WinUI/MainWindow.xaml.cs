using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusCore.WinUI;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<TaskViewModel> Tasks { get; } = new();
    public ObservableCollection<TimeboxSlotVM> Slots { get; } = new();

    private TaskViewModel? _selected;
    private int _timerTotalSec;

    public MainWindow()
    {
        this.InitializeComponent();

        // Timebox timeline: 06:00–23:00.
        for (int h = 6; h <= 23; h++)
        {
            Slots.Add(new TimeboxSlotVM(h, $"{h:D2}:00"));
        }
        for (int h = 6; h <= 23; h++) DetailHour.Items.Add($"{h:D2}:00");
        foreach (int m in new[] { 15, 30, 45, 60 }) DetailDuration.Items.Add($"{m} min");

        CoreService.Instance.TimerStateChanged += OnTimerStateChanged;
        CoreService.Instance.TimerCompleted += OnTimerCompleted;
    }

    // ------------------------------------------------------------------
    // Public surface used by Program (tray / hotkey)
    // ------------------------------------------------------------------
    public void AddTask(string id, string title)
    {
        Tasks.Add(new TaskViewModel(id, title));
    }

    public void SelectTask(string id)
    {
        var vm = Tasks.FirstOrDefault(t => t.Id == id);
        if (vm == null) return;
        TaskList.SelectedItem = vm;
        SelectCore(vm);
    }

    public void MarkTaskDone(string? id)
    {
        if (id == null) return;
        var vm = Tasks.FirstOrDefault(t => t.Id == id);
        if (vm == null) return;
        vm.Done = true;
    }

    // ------------------------------------------------------------------
    // Timer UI
    // ------------------------------------------------------------------
    private void OnTimerStartStop(object sender, RoutedEventArgs e)
    {
        if (CoreService.Instance.TimerIsRunning)
        {
            CoreService.Instance.TimerStop();
        }
        else if (CoreService.Instance.TimerIsPaused)
        {
            CoreService.Instance.TimerResume();
        }
        else
        {
            var vm = _selected;
            if (vm == null) return;
            CoreService.Instance.TimerStart(vm.Id, TimerMode.Pomodoro, 25 * 60);
            _timerTotalSec = 25 * 60;
        }
    }

    private void OnTimerPause(object sender, RoutedEventArgs e) => CoreService.Instance.TimerPause();
    private void OnTimerStop(object sender, RoutedEventArgs e) => CoreService.Instance.TimerStop();

    private void OnTimerStateChanged(TimerState st)
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
        {
            bool running = st.kind == (int)TimerStateKind.Running;
            bool paused = st.kind == (int)TimerStateKind.Paused;

            TimerText.Text = FormatTime(st.remaining_sec);
            TimerTask.Text = (running || paused) && !string.IsNullOrEmpty(_selected?.Title)
                ? $"Focus on: {_selected.Title}"
                : "No active timer";

            BtnStartStop.Content = running ? "⏹ Stop" : (paused ? "▶ Resume" : "▶ Start");
            BtnPause.IsEnabled = running;
            BtnStop.IsEnabled = running || paused;

            if (running || paused)
            {
                double total = Math.Max(_timerTotalSec, 1);
                double completed = total - st.remaining_sec;
                JumpList.SetProgress(this, Math.Clamp(completed, 0, total), total);
            }
            else
            {
                JumpList.ClearProgress(this);
            }

            RefreshTimebox();
        });
    }

    private void OnTimerCompleted()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
        {
            var vm = _selected;
            if (vm == null) return;

            string title = vm.Title;
            int duration = Math.Max(_timerTotalSec, 1);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            CoreService.Instance.InsertTimeEntry(
                Guid.NewGuid().ToString("N"), vm.Id, TimerMode.Pomodoro,
                now - (long)duration * 1000, now, duration, now, now);

            ToastHelper.ShowPomodoroFinished(title, duration);

            BtnStartStop.Content = "▶ Start";
            BtnPause.IsEnabled = false;
            BtnStop.IsEnabled = false;
            JumpList.ClearProgress(this);
            _timerTotalSec = 0;
        });
    }

    // ------------------------------------------------------------------
    // Task list / timebox
    // ------------------------------------------------------------------
    private void OnTaskSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TaskList.SelectedItem is TaskViewModel vm) SelectCore(vm);
    }

    private void OnTimeboxSlotClick(object sender, ItemClickEventArgs e)
    {
        if (_selected == null) return;
        if (e.ClickedItem is not TimeboxSlotVM slot) return;
        _selected.TimeboxStartHour = slot.Hour;
        RefreshTimebox();
    }

    private void OnApplyTimebox(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _selected.TimeboxStartHour = DetailHour.SelectedIndex >= 0
            ? 6 + DetailHour.SelectedIndex
            : (int?)null;
        _selected.TimeboxDurationMin = DetailDuration.SelectedIndex >= 0
            ? new[] { 15, 30, 45, 60 }[DetailDuration.SelectedIndex]
            : (int?)null;
        RefreshTimebox();
    }

    private void OnMarkDone(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _selected.Done = true;
    }

    private void SelectCore(TaskViewModel vm)
    {
        _selected = vm;
        bool show = vm != null;
        DetailPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        DetailTitle.Text = vm.Title;
        DetailDone.IsChecked = vm.Done;
        DetailHour.SelectedIndex = vm.TimeboxStartHour.HasValue ? vm.TimeboxStartHour.Value - 6 : -1;
        DetailDuration.SelectedIndex = vm.TimeboxDurationMin.HasValue
            ? Array.IndexOf(new[] { 15, 30, 45, 60 }, vm.TimeboxDurationMin.Value)
            : -1;

        CoreService.Instance.SelectedTaskId = vm.Id;
    }

    private void RefreshTimebox()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            string? title = Tasks.FirstOrDefault(t =>
                t.TimeboxStartHour.HasValue && t.TimeboxStartHour.Value == 6 + i)?.Title;
            slot.TaskTitle = title;
        }
    }

    private static string FormatTime(int totalSeconds)
    {
        int m = Math.Max(totalSeconds / 60, 0);
        int s = Math.Max(totalSeconds % 60, 0);
        return $"{m:D2}:{s:D2}";
    }
}

// ------------------------------------------------------------------
// View models (minimal, session-scoped)
// ------------------------------------------------------------------
public class TaskViewModel : INotifyPropertyChanged
{
    public string Id { get; }
    public string Title { get; set; }

    private bool _done;
    public bool Done
    {
        get => _done;
        set { if (_done != value) { _done = value; OnPropertyChanged(); } }
    }

    private int? _timeboxStartHour;
    public int? TimeboxStartHour
    {
        get => _timeboxStartHour;
        set { if (_timeboxStartHour != value) { _timeboxStartHour = value; OnPropertyChanged(); } }
    }

    private int? _timeboxDurationMin;
    public int? TimeboxDurationMin
    {
        get => _timeboxDurationMin;
        set { if (_timeboxDurationMin != value) { _timeboxDurationMin = value; OnPropertyChanged(); } }
    }

    public TaskViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TimeboxSlotVM : INotifyPropertyChanged
{
    public int Hour { get; }
    public string Label { get; }

    private string? _taskTitle;
    public string? TaskTitle
    {
        get => _taskTitle;
        set { if (_taskTitle != value) { _taskTitle = value; OnPropertyChanged(); } }
    }

    public TimeboxSlotVM(int hour, string label)
    {
        Hour = hour;
        Label = label;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
