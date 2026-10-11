using System;
using System.IO;
using DeskBox.Infrastructure;
using Microsoft.Win32;

namespace DeskBox.Services;

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

	private static string Command => "\"" + AppPaths.ExePath + "\" --tray";

	public static string Mechanism
	{
		get
		{
			if (RunValueExists())
			{
				return "注册表 Run 项";
			}
			if (File.Exists(StartupShortcut))
			{
				return "启动文件夹快捷方式";
			}
			return "未启用";
		}
	}

	public static bool IsEnabled()
	{
		if (!RunValueExists())
		{
			return File.Exists(StartupShortcut);
		}
		return true;
	}

	public static bool IsDisabledInWindows()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", writable: false);
			if (registryKey?.GetValue("DeskBox") is byte[] array && array.Length != 0)
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
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return registryKey?.GetValue("DeskBox") is string text && text.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static void ClearApprovedFlag()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", writable: true);
			registryKey?.DeleteValue("DeskBox", throwOnMissingValue: false);
		}
		catch
		{
		}
	}

	public static bool SetEnabled(bool enabled)
	{
		LastError = string.Empty;
		IsRestricted = false;
		if (!enabled)
		{
			return Disable();
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey != null)
			{
				registryKey.SetValue("DeskBox", Command, RegistryValueKind.String);
				if (RunValueExists())
				{
					ClearApprovedFlag();
					TryDeleteShortcut();
					AppPaths.Log("开机自启已开启（注册表 Run 项）");
					return true;
				}
				LastError = "写入注册表后读回为空。";
			}
			else
			{
				LastError = "无法打开注册表 Run 项。";
			}
		}
		catch (Exception ex)
		{
			LastError = "注册表写入被拒绝：" + ex.Message;
			AppPaths.Log("开机自启写注册表失败：" + ex.Message);
		}
		try
		{
			Directory.CreateDirectory(StartupFolder);
			TryDeleteShortcut();
			if (ShortcutService.Create(StartupShortcut, AppPaths.ExePath, "--tray", AppPaths.ExeDir) && File.Exists(StartupShortcut))
			{
				AppPaths.Log("开机自启已开启（启动文件夹快捷方式）");
				return true;
			}
			LastError += "；启动文件夹快捷方式也创建失败。";
		}
		catch (Exception ex2)
		{
			LastError = LastError + "；启动文件夹写入失败：" + ex2.Message;
		}
		IsRestricted = true;
		AppPaths.Log("开机自启开启失败：" + LastError);
		return false;
	}

	private static bool Disable()
	{
		bool flag = true;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			registryKey?.DeleteValue("DeskBox", throwOnMissingValue: false);
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
		}
		catch (Exception ex2)
		{
			flag = false;
			LastError = LastError + " 删除启动快捷方式失败：" + ex2.Message;
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
}
