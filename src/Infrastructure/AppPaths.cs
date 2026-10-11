using System;
using System.IO;
using System.Text;
using DeskBox.Models;

namespace DeskBox.Infrastructure;

public static class AppPaths
{
	public static bool Portable { get; private set; }

	public static string BaseDir { get; private set; } = string.Empty;

	public static bool Degraded { get; private set; }

	public static string DegradedReason { get; private set; } = string.Empty;

	public static bool DataDirUsable { get; private set; }

	public static string ItemsDir => Path.Combine(BaseDir, "Items");

	public static string ConfigFile => Path.Combine(BaseDir, "config.json");

	public static string LogFile => Path.Combine(BaseDir, "deskbox.log");

	public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DeskBox.exe");

	public static string ExeDir => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

	public static string IconCacheDir => Path.Combine(BaseDir, "IconCache");

	public static string LastPrepareError { get; private set; } = string.Empty;

	public static void Init()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("DESKBOX_HOME");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			Portable = true;
			BaseDir = environmentVariable;
		}
		else if (File.Exists(Path.Combine(ExeDir, "portable.txt")))
		{
			Portable = true;
			BaseDir = ExeDir;
		}
		else
		{
			BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskBox");
		}
		string baseDir = BaseDir;
		if (TryPrepare(baseDir))
		{
			DataDirUsable = true;
			TryMigrateFromExeSide(baseDir);
			return;
		}
		Degraded = true;
		DegradedReason = "无法写入 " + baseDir;
		Portable = true;
		BaseDir = Path.Combine(ExeDir, "DeskBoxData");
		DataDirUsable = TryPrepare(BaseDir);
		if (!DataDirUsable)
		{
			BaseDir = Path.Combine(Path.GetTempPath(), "DeskBox");
			DataDirUsable = TryPrepare(BaseDir);
			if (!DataDirUsable)
			{
				Log("没有任何可写的数据目录，收纳的内容重启后会丢失");
			}
		}
	}

	private static void TryMigrateFromExeSide(string target)
	{
		try
		{
			string path = Path.Combine(ExeDir, "DeskBoxData");
			string text = Path.Combine(path, "config.json");
			if (!File.Exists(text) || File.Exists(Path.Combine(target, "config.json")))
			{
				return;
			}
			Directory.CreateDirectory(target);
			string text2 = Path.Combine(target, "Items");
			Directory.CreateDirectory(text2);
			File.Copy(text, Path.Combine(target, "config.json"), overwrite: true);
			string path2 = Path.Combine(path, "Items");
			if (Directory.Exists(path2))
			{
				foreach (string item in Directory.EnumerateFiles(path2))
				{
					string text3 = Path.Combine(text2, Path.GetFileName(item));
					if (!File.Exists(text3))
					{
						File.Copy(item, text3, overwrite: true);
					}
				}
			}
			Log("已把受限环境中的数据迁移到：" + target);
		}
		catch (Exception ex)
		{
			Log("数据迁移失败：" + ex.Message);
		}
	}

	private static bool TryPrepare(string dir)
	{
		try
		{
			Directory.CreateDirectory(dir);
			Directory.CreateDirectory(Path.Combine(dir, "Items"));
			return true;
		}
		catch (Exception ex)
		{
			LastPrepareError += $"{dir} -> {ex.GetType().Name}: {ex.Message}；";
			return false;
		}
	}

	public static string StoredFilePath(DeskItem item)
	{
		if (!string.IsNullOrEmpty(item.StoredName))
		{
			return Path.Combine(ItemsDir, item.StoredName);
		}
		return string.Empty;
	}

	public static string UniqueStoredName(string fileName)
	{
		string fileName2 = Path.GetFileName(fileName);
		string text = fileName2;
		int num = 2;
		while (File.Exists(Path.Combine(ItemsDir, text)) || Directory.Exists(Path.Combine(ItemsDir, text)))
		{
			text = $"{Path.GetFileNameWithoutExtension(fileName2)} ({num}){Path.GetExtension(fileName2)}";
			num++;
		}
		return text;
	}

	public static void Log(string message)
	{
		string contents = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
		string[] array = new string[3]
		{
			LogFile,
			Path.Combine(ExeDir, "deskbox.log"),
			Path.Combine(Path.GetTempPath(), "deskbox.log")
		};
		foreach (string path in array)
		{
			try
			{
				File.AppendAllText(path, contents, encoding);
				break;
			}
			catch
			{
			}
		}
	}
}
