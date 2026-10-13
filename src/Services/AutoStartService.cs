using System;
using System.IO;
using System.Security;
using DeskBox.Infrastructure;
using Microsoft.Win32;

namespace DeskBox.Services;

/// <summary>
/// 开机自启。使用 <c>HKCU\...\Run</c> 注册表项作为主要机制，并以“启动”文件夹快捷方式兜底，
/// 二者都只影响当前用户，因而不依赖程序所在目录、盘符或是否便携运行，换一台电脑也能生效。
/// </summary>
public static class AutoStartService
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ApprovedKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

	private const string ValueName = "DeskBox";

	private const string ShortcutName = "DeskBox.lnk";

	public static string LastError { get; private set; } = string.Empty;

	public static bool IsRestricted { get; private set; }

	public static string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

	public static string StartupShortcut => Path.Combine(StartupFolder, "DeskBox.lnk");

	/// <summary>启动命令：指向当前正在运行的程序，附加 --tray 静默驻留托盘。</summary>
	private static string Command => "\"" + AppPaths.ExePath + "\" --tray";

	/// <summary>已生效的机制；如果有多个机制同时存在，用“+”连接。</summary>
	public static string Mechanism
	{
		get
		{
			bool registry = RunValueExists();
			bool shortcut = File.Exists(StartupShortcut);
			if (registry && shortcut) return "注册表 Run 项 + 启动文件夹快捷方式";
			if (registry) return "注册表 Run 项";
			if (shortcut) return "启动文件夹快捷方式";
			return "未启用";
		}
	}

	public static bool IsEnabled()
	{
		return RunValueExists() || File.Exists(StartupShortcut);
	}

	/// <summary>当前注册的启动命令是否已指向正在运行的程序；用于识别程序移动/更新后的过期项。</summary>
	public static bool PointsAtCurrentExecutable()
	{
		if (RunValueExists() && !RegistryMatchesCurrent()) return false;
		if (File.Exists(StartupShortcut) && !ShortcutMatchesCurrent()) return false;
		return IsEnabled();
	}

	public static bool IsDisabledInWindows() => IsDisabledInWindows(ValueName) || IsDisabledInWindows(ShortcutName);

	private static bool IsDisabledInWindows(string name)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: false);
			if (registryKey?.GetValue(name) is byte[] array && array.Length != 0)
			{
				return (array[0] & 1) != 0;
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool RunValueExists()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
			return registryKey?.GetValue(ValueName) is string text && text.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool RegistryMatchesCurrent()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
			return registryKey?.GetValue(ValueName) is string text &&
				text.IndexOf(AppPaths.ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool ShortcutMatchesCurrent()
	{
		var info = ShortcutService.Resolve(StartupShortcut);
		return info != null && string.Equals(info.Target, AppPaths.ExePath, StringComparison.OrdinalIgnoreCase);
	}

	private static void ClearApprovedFlag(string name)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
			registryKey?.DeleteValue(name, throwOnMissingValue: false);
		}
		catch
		{
		}
	}

	private static bool TrySetRegistry()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
			if (registryKey == null)
			{
				LastError = "无法打开注册表 Run 项。";
				return false;
			}
			registryKey.SetValue(ValueName, Command, RegistryValueKind.String);
			if (RegistryMatchesCurrent())
			{
				ClearApprovedFlag(ValueName);
				AppPaths.Log("开机自启已开启（注册表 Run 项）");
				return true;
			}
			LastError = "写入注册表后读回为空。";
		}
		catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or SystemException)
		{
			LastError = "注册表写入被拒绝：" + ex.Message;
			AppPaths.Log("开机自启写注册表失败：" + ex.Message);
		}
		return false;
	}

	private static bool TryCreateShortcut()
	{
		try
		{
			Directory.CreateDirectory(StartupFolder);
			TryDeleteShortcut();
			if (ShortcutService.Create(StartupShortcut, AppPaths.ExePath, "--tray", AppPaths.ExeDir) && File.Exists(StartupShortcut))
			{
				ClearApprovedFlag(ShortcutName);
				AppPaths.Log("开机自启已开启（启动文件夹快捷方式）");
				return true;
			}
			LastError = Append(LastError, "启动文件夹快捷方式创建失败。");
		}
		catch (Exception ex)
		{
			LastError = Append(LastError, "启动文件夹写入失败：" + ex.Message);
		}
		return false;
	}

	public static bool SetEnabled(bool enabled)
	{
		LastError = string.Empty;
		IsRestricted = false;
		if (!enabled)
		{
			return Disable();
		}
		// 两种机制都尝试，只要有一种成功即可；都成功时更耐系统策略差异。
		bool registry = TrySetRegistry();
		bool shortcut = TryCreateShortcut();
		if (registry || shortcut)
		{
			return true;
		}
		IsRestricted = true;
		AppPaths.Log("开机自启开启失败：" + LastError);
		return false;
	}

	private static string Append(string current, string addition) =>
		string.IsNullOrEmpty(current) ? addition : current + "；" + addition;

	private static bool Disable()
	{
		bool flag = true;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
			registryKey?.DeleteValue(ValueName, throwOnMissingValue: false);
			ClearApprovedFlag(ValueName);
		}
		catch (Exception ex)
		{
			flag = false;
			LastError = "删除注册表项失败：" + ex.Message;
			AppPaths.Log("关闭开机自启失败：" + ex.Message);
		}
		try
		{
			TryDeleteShortcut();
			ClearApprovedFlag(ShortcutName);
		}
		catch (Exception ex2)
		{
			flag = false;
			LastError = Append(LastError, "删除启动快捷方式失败：" + ex2.Message);
		}
		if (flag)
		{
			AppPaths.Log("开机自启已关闭");
		}
		return flag;
	}

	private static void TryDeleteShortcut()
	{
		string startupShortcut = StartupShortcut;
		if (File.Exists(startupShortcut))
		{
			File.Delete(startupShortcut);
		}
	}

	/// <summary>启动时的自启状态校正结果。</summary>
	internal sealed record AutoStartTune(bool Repaired, bool Failed, string Message);

	/// <summary>
	/// 启动时把自启状态拉回用户保存的意图，并在程序路径变化（移动、更新换盘）后自动修复过期项。
	/// 返回结果不会直接弹窗，交给调用方（App）选择提示方式。
	/// </summary>
	internal static AutoStartTune Tune(bool wanted)
	{
		if (!wanted)
		{
			if (!IsEnabled()) return new AutoStartTune(false, false, string.Empty);
			bool removed = SetEnabled(false);
			return removed
				? new AutoStartTune(true, false, "已按上次的设置关闭开机自启动。")
				: new AutoStartTune(false, true, LastError);
		}

		bool enabled = IsEnabled();
		bool current = enabled && PointsAtCurrentExecutable();
		if (current) return new AutoStartTune(false, false, string.Empty);

		string reason = enabled
			? "检测到程序位置已变化，已自动修复开机自启动。"
			: "已恢复开机自启动。";
		return SetEnabled(true)
			? new AutoStartTune(true, false, reason)
			: new AutoStartTune(false, true, LastError);
	}
}
