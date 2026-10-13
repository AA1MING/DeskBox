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
    private sealed class State { public string Key = ""; public string Applied = "Solid"; public bool NeedsReapply; }
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
        // 每次应用都对齐标题栏配色（幂等、开销小），避免带系统标题栏的设置页与无边框面板不一致。
        ApplyCaptionTheme(source.Handle, dark);
        // DWM 可能在 RDP、切换桌面/GPU、开关透明效果等情况下静默丢弃材质，
        // 而缓存键看起来仍然一致；如果直接返回就不会重新应用，表现为“云母失效”。
        if (state.Key == key && !state.NeedsReapply && VerifyBackdrop(source.Handle, mode, dark)) return state.Applied;
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
            // 透明 WPF 渲染目标：让桌面能透过窗口被合成器看到。
            source.CompositionTarget.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
            window.Background = Brushes.Transparent;
            margins.LeftWidth = margins.RightWidth = margins.TopHeight = margins.BottomHeight = -1;
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (mode == "Glass")
            {
                // 玻璃：用系统“透明窗口”材质（DWMSBT_TRANSIENTWINDOW = 3），真正透出并模糊桌面，
                // 观感通透、着色轻，接近 macOS 的玻璃；不支持的旧系统退回合成器模糊。
                int transient = 3;
                int hr = ThemeService.BuildNumber >= 22621 ? Native.DwmSetWindowAttribute(hwnd, 38, ref transient, 4) : -1;
                if (hr == 0) applied = "Glass";
                else
                {
                    int gradient = (0x46 << 24) | ((dark ? 0x18 : 0xF6) << 16) | ((dark ? 0x1A : 0xF6) << 8) | (dark ? 0x22 : 0xF8);
                    if (SetAccent(hwnd, Native.ACCENT_ENABLE_BLURBEHIND, gradient)) applied = "Glass";
                    else if (SetAccent(hwnd, Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, gradient)) applied = "Glass";
                }
            }
            else
            {
                // 亚克力：合成器模糊 + 较重的着色，质感更“磨砂”。
                byte tint = dark ? (byte)32 : (byte)245;
                int gradient = (0xB4 << 24) | (tint << 16) | (tint << 8) | tint;
                if (SetAccent(hwnd, Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, gradient)) applied = mode;
                else if (ThemeService.BuildNumber >= 22621)
                {
                    int transient = 3;
                    if (Native.DwmSetWindowAttribute(hwnd, 38, ref transient, 4) == 0) applied = mode;
                }
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
        state.NeedsReapply = false;
        state.Applied = applied;
        AppliedBackdrop = applied;
        int attrReadback = -1;
        Native.DwmGetWindowAttribute(hwnd, 38, out attrReadback, 4);
        int accentReadback = ReadAccentState(hwnd);
        AppPaths.Log($"背景材质：请求={backdrop} 实际={applied} 系统背景={attrReadback} 合成器={accentReadback} 透明={transparent} 深色={dark} HWND={hwnd}");
        return applied;
    }

    public static void Cloak(Window window, bool hidden)
    {
        int value = hidden ? 1 : 0;
        Native.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 13, ref value, 4);
    }

    /// <summary>标记某窗口需要重新应用材质（DWM 事件后调用）。</summary>
    public static void RequestReapply(Window window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        state.NeedsReapply = true;
    }

    /// <summary>
    /// 读取窗口实际的后台类型，确认缓存宣称的材质仍然生效。
    /// 读取失败（旧系统不支持）时保守返回 true，保持原有行为。
    /// </summary>
    private static bool VerifyBackdrop(nint hwnd, string mode, bool dark)
    {
        try
        {
            if (ThemeService.BuildNumber < 22621) return true;
            if (Native.DwmGetWindowAttribute(hwnd, 38, out int actual, 4) != 0) return true;
            if (mode == "Solid") return actual == 1 || actual == 0;
            return actual == 3; // 玻璃与亚克力在 Win11 都走 DWMSBT_TRANSIENTWINDOW(3)
        }
        catch
        {
            return true;
        }
    }

    /// <summary>读取窗口当前的合成器（Accent）状态，用于诊断玻璃/亚克力是否真的生效。</summary>
    private static int ReadAccentState(nint hwnd)
    {
        try
        {
            var policy = new Native.AccentPolicy();
            nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<Native.AccentPolicy>());
            try
            {
                Marshal.StructureToPtr(policy, memory, false);
                var data = new Native.WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, Data = memory, SizeOfData = Marshal.SizeOf<Native.AccentPolicy>() };
                if (Native.GetWindowCompositionAttribute(hwnd, ref data) == 0) return -1;
                return Marshal.PtrToStructure<Native.AccentPolicy>(memory).AccentState;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        catch { return -1; }
    }

    /// <summary>
    /// 让带系统标题栏的窗口（如设置页）标题栏颜色跟随应用主题，
    /// 避免系统亮色 + 应用深色时“主页与设置页主题不统一”。
    /// </summary>
    private static void ApplyCaptionTheme(nint hwnd, bool dark)
    {
        try
        {
            // DWMWA_CAPTION_COLOR / TEXT_COLOR / BORDER_COLOR 需要 COLORREF(0x00BBGGRR)。
            int caption = dark ? 0x00202020 : 0x00F6F6F6;
            int text = dark ? 0x00FFFFFF : 0x00000000;
            int border = dark ? 0x002B2B2B : 0x00E2E2E2;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref caption, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TEXT_COLOR, ref text, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
        }
        catch (Exception ex) { AppPaths.Log("设置标题栏颜色失败：" + ex.Message); }
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
