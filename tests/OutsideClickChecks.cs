using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using DeskBox;
using DeskBox.Interop;

internal static class OutsideClickChecks
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static nint Field(MainWindow main, string name) =>
        (nint)typeof(MainWindow).GetField(name, Flags)!.GetValue(main)!;

    private static void SetField(MainWindow main, string name, object value) =>
        typeof(MainWindow).GetField(name, Flags)!.SetValue(main, value);

    private static bool WouldClose(MainWindow main, nint hwnd) =>
        (bool)typeof(MainWindow).GetMethod("OutsideClickWouldClose", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, new object[] { hwnd })!;

    internal static async System.Threading.Tasks.Task Run(MainWindow main, string output, Action<bool, string> check)
    {
        main.CaptureMode = false;
        main.ShowPanel();
        await System.Threading.Tasks.Task.Delay(400);
        check(main.IsVisible, "panel is visible before outside-click checks");

        // The click-away decision is driven by a global mouse hook, installed while open.
        check(Field(main, "_mouseHook") != IntPtr.Zero, "global mouse hook is installed while the panel is open");

        nint self = new WindowInteropHelper(main).Handle;
        nint foreign = new IntPtr(0x1234);

        check(WouldClose(main, foreign), "a window that is not the panel counts as an outside click");
        check(!WouldClose(main, self), "the panel's own window is not an outside click");
        check(!WouldClose(main, IntPtr.Zero), "an unresolved hit target never triggers closing");

        // The pin keeps the panel open even for a genuine outside click.
        SetField(main, "_pinned", true);
        check(!WouldClose(main, foreign), "pinned panel ignores outside clicks");
        SetField(main, "_pinned", false);

        // An open menu / dialog must also suppress click-away.
        SetField(main, "_menuOpen", true);
        check(!WouldClose(main, foreign), "open menu suppresses outside-click closing");
        SetField(main, "_menuOpen", false);

        // While Settings (an external dialog) is open, click-away must be frozen.
        typeof(MainWindow).GetMethod("SetExternalDialogOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
            .Invoke(main, new object[] { true });
        check(!WouldClose(main, foreign), "open settings freezes outside-click closing");
        typeof(MainWindow).GetMethod("BeginCloseFromOutsideClick", Flags)!.Invoke(main, null);
        await System.Threading.Tasks.Task.Delay(300);
        check(main.IsVisible, "panel stays open while settings is open");
        typeof(MainWindow).GetMethod("SetExternalDialogOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
            .Invoke(main, new object[] { false });

        // Simulate the hook dispatch path and expect the panel to animate shut.
        typeof(MainWindow).GetMethod("BeginCloseFromOutsideClick", Flags)!.Invoke(main, null);
        await System.Threading.Tasks.Task.Delay(600);
        check(!main.IsVisible, "hook-driven outside click hides the panel");

        check(Field(main, "_mouseHook") == IntPtr.Zero, "global mouse hook is released after the panel hides");
    }
}
