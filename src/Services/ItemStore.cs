using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Models;

namespace DeskBox.Services;

public static class ItemStore
{
	private static readonly string[] ShortcutExtensions = new string[3] { ".lnk", ".url", ".appref-ms" };

	public static int CopiedInsteadOfMoved { get; private set; }

	public static bool IsShortcut(string path)
	{
		return ShortcutExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	public static List<DeskItem> ImportAll(IEnumerable<string> paths)
	{
		List<DeskItem> list = new List<DeskItem>();
		foreach (string path in paths)
		{
			try
			{
				if (Directory.Exists(path))
				{
					list.Add(ImportDirectory(path));
				}
				else if (File.Exists(path))
				{
					list.Add(ImportFile(path));
				}
			}
			catch (Exception ex)
			{
				AppPaths.Log("收纳失败 " + path + "：" + ex.Message);
			}
		}
		return list;
	}

	public static void ResetImportStats()
	{
		CopiedInsteadOfMoved = 0;
	}

	private static DeskItem ImportFile(string sourcePath)
	{
		string fullPath = Path.GetFullPath(sourcePath);
		string text;
		if (fullPath.StartsWith(Path.GetFullPath(AppPaths.ItemsDir), StringComparison.OrdinalIgnoreCase))
		{
			text = Path.GetFileName(fullPath);
		}
		else
		{
			text = AppPaths.UniqueStoredName(Path.GetFileName(sourcePath));
			string destFileName = Path.Combine(AppPaths.ItemsDir, text);
			try
			{
				File.Move(sourcePath, destFileName);
			}
			catch (Exception ex)
			{
				File.Copy(sourcePath, destFileName, overwrite: false);
				CopiedInsteadOfMoved++;
				AppPaths.Log("无法移动，已改为复制（原文件保留）：" + sourcePath + " -> " + ex.Message);
			}
		}
		string text2 = Path.Combine(AppPaths.ItemsDir, text);
		DeskItem deskItem = new DeskItem
		{
			Name = Path.GetFileNameWithoutExtension(text),
			StoredName = text,
			Kind = ((!IsShortcut(text)) ? ItemKind.File : ItemKind.Shortcut)
		};
		if (deskItem.Kind == ItemKind.Shortcut)
		{
			ShortcutInfo shortcutInfo = ShortcutService.Resolve(text2);
			if ((object)shortcutInfo != null)
			{
				deskItem.Target = shortcutInfo.Target;
				deskItem.Arguments = shortcutInfo.Arguments;
			}
		}
		else if (string.Equals(Path.GetExtension(text), ".exe", StringComparison.OrdinalIgnoreCase))
		{
			deskItem.Target = text2;
		}
		return deskItem;
	}

	private static DeskItem ImportDirectory(string sourcePath)
	{
		DeskItem deskItem = new DeskItem
		{
			Name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)),
			Kind = ItemKind.Group
		};
		foreach (string item in Directory.EnumerateFileSystemEntries(sourcePath))
		{
			try
			{
				if (File.Exists(item))
				{
					deskItem.Children.Add(ImportFile(item));
				}
				else if (Directory.Exists(item))
				{
					deskItem.Children.Add(ImportDirectory(item));
				}
			}
			catch (Exception ex)
			{
				AppPaths.Log("收纳子项失败 " + item + "：" + ex.Message);
			}
		}
		TryRemoveEmptyDirectory(sourcePath);
		return deskItem;
	}

	private static void TryRemoveEmptyDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
			{
				Directory.Delete(path, recursive: false);
			}
		}
		catch (Exception ex)
		{
			AppPaths.Log("清理空文件夹失败 " + path + "：" + ex.Message);
		}
	}

	public static string? ExportToDesktop(DeskItem item)
	{
		string text = AppPaths.StoredFilePath(item);
		if (string.IsNullOrEmpty(text) || !File.Exists(text))
		{
			return null;
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		string text2 = Path.Combine(folderPath, item.StoredName ?? Path.GetFileName(text));
		int num = 2;
		while (File.Exists(text2))
		{
			text2 = Path.Combine(folderPath, $"{Path.GetFileNameWithoutExtension(text)} ({num}){Path.GetExtension(text)}");
			num++;
		}
		File.Copy(text, text2);
		return text2;
	}

	public static string LaunchPath(DeskItem item)
	{
		string text = AppPaths.StoredFilePath(item);
		if (!string.IsNullOrEmpty(text) && (File.Exists(text) || Directory.Exists(text)))
		{
			return text;
		}
		return item.Target ?? string.Empty;
	}

	public static void Open(DeskItem item)
	{
		string path = LaunchPath(item);
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		try
		{
			// 快捷方式优先自己解析目标再启动，不依赖 Windows 的 .lnk 文件关联。
			// 否则一旦 lnkfile\IsShortcut 被“去小箭头”之类工具删掉，
			// 所有快捷方式都会报“没有应用程序与此操作的指定文件有关联”。
			if (TryLaunchShortcut(path))
			{
				return;
			}

			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true,
				WorkingDirectory = (Path.GetDirectoryName(path) ?? AppPaths.ItemsDir)
			});
		}
		catch (Exception ex)
		{
			AppPaths.Log("打开失败 " + path + "：" + ex.Message);
			MessageBox.Show("无法打开：" + path + "\n" + ex.Message, "DeskBox", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	/// <summary>
	/// 解析 .lnk 并直接启动其目标。成功返回 true；不是快捷方式或解析失败返回 false。
	/// </summary>
	private static bool TryLaunchShortcut(string path)
	{
		if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		ShortcutInfo? info = ShortcutService.Resolve(path);
		if (info == null || string.IsNullOrWhiteSpace(info.Target))
		{
			return false;
		}

		string target = info.Target;
		string workingDirectory = string.IsNullOrWhiteSpace(info.WorkingDirectory)
			? (Path.GetDirectoryName(target) ?? string.Empty)
			: info.WorkingDirectory;

		Process.Start(new ProcessStartInfo
		{
			FileName = target,
			Arguments = info.Arguments ?? string.Empty,
			WorkingDirectory = workingDirectory,
			UseShellExecute = true
		});

		return true;
	}



	public static void OpenAsAdministrator(DeskItem item)
	{
		string text = LaunchPath(item);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			if (item.Kind == ItemKind.Shortcut && !string.IsNullOrEmpty(item.Target))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = item.Target,
					Arguments = (item.Arguments ?? string.Empty),
					UseShellExecute = true,
					Verb = "runas"
				});
			}
			else
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true,
					Verb = "runas"
				});
			}
		}
		catch (Exception ex)
		{
			AppPaths.Log("以管理员身份运行失败：" + ex.Message);
		}
	}

	public static void OpenContainingFolder(DeskItem item)
	{
		string text = LaunchPath(item);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			if (Directory.Exists(text))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "\"" + text + "\"",
					UseShellExecute = true
				});
			}
			else
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "/select,\"" + text + "\"",
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppPaths.Log("打开所在位置失败：" + ex.Message);
		}
	}

	public static void ShowProperties(DeskItem item)
	{
		string text = LaunchPath(item);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		Native.SHELLEXECUTEINFO lpExecInfo = new Native.SHELLEXECUTEINFO
		{
			cbSize = Marshal.SizeOf<Native.SHELLEXECUTEINFO>(),
			fMask = 12u,
			hwnd = IntPtr.Zero,
			lpVerb = "properties",
			lpFile = text,
			lpParameters = null,
			lpDirectory = null,
			nShow = 5,
			hInstApp = IntPtr.Zero
		};
		try
		{
			Native.ShellExecuteEx(ref lpExecInfo);
		}
		catch (Exception ex)
		{
			AppPaths.Log("显示属性失败：" + ex.Message);
		}
	}
}
