using System;
using System.IO;
using System.Reflection;
using DeskBox.Infrastructure;
using DeskBox.Services;

internal static class AutoStartChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // 只做只读断言：不接触注册表写入，也不会改动本机真实的自启项。
        string shortcut = AutoStartService.StartupShortcut;
        check(shortcut.EndsWith("DeskBox.lnk", StringComparison.OrdinalIgnoreCase), "autostart shortcut uses a stable DeskBox.lnk name");
        check(string.Equals(Path.GetDirectoryName(shortcut), Environment.GetFolderPath(Environment.SpecialFolder.Startup), StringComparison.OrdinalIgnoreCase),
            "autostart shortcut lives in the per-user Startup folder (machine independent)");

        bool enabled = AutoStartService.IsEnabled();
        string mechanism = AutoStartService.Mechanism;
        check(mechanism == "未启用" || mechanism.Contains("注册表") || mechanism.Contains("启动文件夹"),
            "autostart mechanism reports a known state");
        check(enabled == (mechanism != "未启用"), "enabled state matches the reported mechanism");
        check(enabled || !AutoStartService.PointsAtCurrentExecutable(),
            "disabled autostart never reports a current target");

        var command = (string)typeof(AutoStartService)
            .GetProperty("Command", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        check(command.Contains(AppPaths.ExePath), "autostart command points at the running executable (no hard-coded drive)");
        check(command.Contains("--tray"), "autostart launches silently into the tray");

        // 关闭且未启用时的校正必须是静默无操作，绝不会误删用户其它自启项。
        if (!enabled)
        {
            var tune = (AutoStartService.AutoStartTune)typeof(AutoStartService)
                .GetMethod("Tune", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                .Invoke(null, new object[] { false })!;
            check(!tune.Repaired && !tune.Failed, "disabled autostart reconciles as a quiet no-op");
        }
    }
}
