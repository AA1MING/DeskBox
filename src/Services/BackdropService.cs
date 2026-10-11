using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using Microsoft.Win32;

namespace DeskBox.Services;

public static class BackdropService
{
    private sealed class State { public string Key = ""; public string Applied = "Solid"; }
    private static readonly ConditionalWeakTable<Window, State> States = new();
    public static string AppliedBackdrop { get; private set; } = "Solid";
    public static bool TransparencyEnabled
    {
        get
        {
            if (ThemeService.HighContrast) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch { return true; }
        }
    }

    public static string Apply(Window window, string backdrop, bool dark)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source?.CompositionTarget == null) return "Solid";
        bool transparent = backdrop != "Solid" && TransparencyEnabled;
        string mode = transparent ? backdrop : "Solid";
        var state = States.GetOrCreateValue(window);
        string key = mode + ":" + dark;
        if (state.Key == key) return state.Applied;
        nint hwnd = source.Handle;
        int darkValue = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref darkValue, 4);
        int corners = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref corners, 4);
        SetAccent(hwnd, 0, 0);
        int none = 1;
        Native.DwmSetWindowAttribute(hwnd, 38, ref none, 4);
        var margins = new Native.MARGINS();
        Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        string applied = "Solid";
        if (transparent)
        {
            // Transparent WPF render target is required to expose the DWM backdrop.
            source.CompositionTarget.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
            window.Background = Brushes.Transparent;
            margins.LeftWidth = margins.RightWidth = margins.TopHeight = margins.BottomHeight = -1;
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            int type = mode == "Acrylic" ? 3 : 2;
            int hr = ThemeService.BuildNumber >= 22621 ? Native.DwmSetWindowAttribute(hwnd, 38, ref type, 4) : -1;
            if (hr == 0) applied = mode;
            else
            {
                byte tint = dark ? (byte)32 : (byte)245;
                int gradient = (190 << 24) | (tint << 16) | (tint << 8) | tint;
                if (SetAccent(hwnd, mode == "Acrylic" ? 4 : 3, gradient)) applied = mode;
            }
        }
        if (applied == "Solid")
        {
            SetAccent(hwnd, 0, 0);
            Native.DwmSetWindowAttribute(hwnd, 38, ref none, 4);
            margins = new Native.MARGINS();
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var color = ThemeService.HighContrast ? SystemColors.WindowColor : dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(246, 246, 246);
            source.CompositionTarget.BackgroundColor = color;
            window.Background = new SolidColorBrush(color);
        }
        state.Key = key;
        state.Applied = applied;
        AppliedBackdrop = applied;
        AppPaths.Log($"背景材质：请求={backdrop} 实际={applied} HWND={hwnd}");
        return applied;
    }

    public static void Cloak(Window window, bool hidden)
    {
        int value = hidden ? 1 : 0;
        Native.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 13, ref value, 4);
    }

    private static bool SetAccent(nint hwnd, int state, int gradient)
    {
        var policy = new Native.AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = gradient };
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<Native.AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, memory, false);
            var data = new Native.WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, Data = memory, SizeOfData = Marshal.SizeOf<Native.AccentPolicy>() };
            return Native.SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
