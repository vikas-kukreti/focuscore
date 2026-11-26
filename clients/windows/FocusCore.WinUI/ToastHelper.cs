using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.AppNotifications;

using ToastContent = Microsoft.Toolkit.Uwp.Notifications.ToastContent;
using ToastContentBuilder = Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder;
using ToastButton = Microsoft.Toolkit.Uwp.Notifications.ToastButton;

namespace FocusCore.WinUI;

/// <summary>
/// Interactive Windows toasts via the Windows App SDK `ToastNotificationManager`,
/// with toast content built by `Microsoft.Toolkit.Uwp.Notifications`.
///
/// Buttons carry action arguments (`start_break`, `extend_5m`, `mark_done`) and a
/// task payload. Subscribe to `ActionInvoked` to handle them in the UI layer.
/// </summary>
public static class ToastHelper
{
    public const string AppId = "FocusCore";

    /// <summary>Raised when a toast action button is invoked.</summary>
    public static event Action<(string Action, string Payload)>? ActionInvoked;

    /// <summary>Shown when a Pomodoro finishes.</summary>
    public static void ShowPomodoroFinished(string taskTitle, int durationSec)
    {
        string minutes = (durationSec / 60).ToString();
        var content = new ToastContentBuilder()
            .AddArgument("type", "pomodoro_finished")
            .AddArgument("task", taskTitle ?? string.Empty)
            .AddText("Pomodoro complete")
            .AddText($"Finished work on \"{EscapeText(taskTitle)}\" ({minutes} min).")
            .AddButton(new ToastButton().SetContent("Start Break").AddArgument("action", "start_break"))
            .AddButton(new ToastButton().SetContent("Extend 5m").AddArgument("action", "extend_5m"))
            .AddButton(new ToastButton().SetContent("Mark Done").AddArgument("action", "mark_done"))
            .GetToastContent();

        Show(content.GetXml(), "pomodoro_finished");
    }

    /// <summary>Shown when a scheduled timebox block starts.</summary>
    public static void ShowTimeboxStarted(string taskTitle, string time)
    {
        var content = new ToastContentBuilder()
            .AddArgument("type", "timebox_started")
            .AddArgument("task", taskTitle ?? string.Empty)
            .AddText("Timebox starting")
            .AddText($"\"{EscapeText(taskTitle)}\" begins at {time}.")
            .AddButton(new ToastButton().SetContent("Snooze 5m").AddArgument("action", "snooze_5m"))
            .AddButton(new ToastButton().SetContent("Dismiss").AddArgument("action", "dismiss"))
            .GetToastContent();

        Show(content.GetXml(), $"timebox_{time}");
    }

    private static void Show(string xml, string tag)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            var notification = new ToastNotification(doc);
            notification.Tag = tag;

            notification.Activated += (_, args) =>
            {
                string action = GetArg(args.Arguments, "action");
                string payload = GetArg(args.Arguments, "task");
                if (action.Length > 0)
                {
                    ActionInvoked?.Invoke((action, payload));
                }
            };

            notifier.Show(notification);
        }
        catch
        {
            // Toasts are best-effort on Windows (e.g. disabled by group policy).
        }
    }

    private static string GetArg(string args, string name)
    {
        if (string.IsNullOrEmpty(args)) return string.Empty;
        foreach (var pair in args.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name) return kv[1];
        }
        return string.Empty;
    }

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
