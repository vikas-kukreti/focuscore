using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusCore.WinUI;

internal static class FocusCoreNative
{
    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_version();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int focuscore_open(byte* dbPath);

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void focuscore_close();

    [DllImport("focuscore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr focuscore_quick_add(byte* title, byte* scheduledDate);
}

internal sealed class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var native = FocusCoreNative.focuscore_version();
        var version = Marshal.PtrToStringAnsi(native) ?? "focuscore";

        var app = Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var window = new Window { Title = $"FocusCore — {version}" };
            var root = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 12,
            };
            root.Children.Add(new TextBlock { Text = "FocusCore", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeight.Bold });
            root.Children.Add(new TextBlock { Text = "Native task, timeboxing & Pomodoro suite.", FontSize = 14 });
            root.Children.Add(new Button { Content = "Quick Add", MinWidth = 120 });
            window.Content = root;
            window.Activate();
        });
    }
}
