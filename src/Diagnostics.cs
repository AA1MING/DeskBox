using System;
using System.Collections.Generic;
using System.Drawing;
using Size = System.Drawing.Size;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox;

public static class Diagnostics
{
	public static void IconSheet(string outPath)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("DeskBox 诊断 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("OS build = " + Environment.OSVersion.Version.Build + "  Win11 = " + ThemeService.IsWindows11);
		stringBuilder.AppendLine("系统主题 = " + ThemeService.SystemTheme.ToString() + "  强调色 = " + ThemeService.AccentColor.ToString());
		stringBuilder.AppendLine("数据目录 = " + AppPaths.BaseDir);
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		string path = Path.Combine(folderPath, "System32");
		string text = Path.Combine(AppPaths.BaseDir, "自检快捷方式.lnk");
		stringBuilder.AppendLine("创建测试快捷方式 = " + ShortcutService.Create(text, Path.Combine(path, "notepad.exe")) + " -> " + text);
		(string, string)[] array = new (string, string)[11]
		{
			(Path.Combine(path, "notepad.exe"), "notepad.exe"),
			(Path.Combine(path, "calc.exe"), "calc.exe"),
			(Path.Combine(path, "mspaint.exe"), "mspaint.exe"),
			(Path.Combine(path, "cmd.exe"), "cmd.exe"),
			(Path.Combine(path, "dxdiag.exe"), "dxdiag.exe"),
			(Path.Combine(path, "shell32.dll"), "shell32.dll"),
			(Path.Combine(path, "SnippingTool.exe"), "SnippingTool.exe"),
			(folderPath, "C:\\Windows (文件夹)"),
			(text, "自检快捷方式.lnk"),
			(Path.Combine(path, "notepad.exe") + "|missing", "不存在的文件"),
			("::stock-folder::", "分组图标（不依赖路径）")
		};
		int height = 60 + array.Length * 76 + 20;
		using Bitmap bitmap = new Bitmap(434, height);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.FromArgb(255, 32, 32, 32));
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			using Font font = new Font("Microsoft YaHei UI", 12f, System.Drawing.FontStyle.Bold);
			using Font font2 = new Font("Microsoft YaHei UI", 9f);
			using SolidBrush brush = new SolidBrush(Color.White);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(255, 170, 170, 170));
			graphics.DrawString("图标对照表：列1=浅色底 列2=深色底 列3=SHGetFileInfo 回退", font, brush, 12f, 12f);
			graphics.DrawString("图标来源", font2, brush2, 12f, 40f);
			graphics.DrawString("IShellItemImageFactory", font2, brush2, 216f, 40f);
			graphics.DrawString("回退方案", font2, brush2, 352f, 40f);
			for (int i = 0; i < array.Length; i++)
			{
				(string, string) tuple = array[i];
				string item = tuple.Item1;
				string item2 = tuple.Item2;
				int num = 60 + i * 76;
				string text2;
				if (!item.EndsWith("|missing", StringComparison.Ordinal))
				{
					text2 = item;
				}
				else
				{
					string text3 = item;
					text2 = text3.Substring(0, text3.Length - 8) + "_not_exist_.exe";
				}
				string text4 = text2;
				graphics.DrawString(item2, font2, brush, 12f, num + 22);
				string s;
				if (text4.Length <= 34)
				{
					s = text4;
				}
				else
				{
					int length = text4.Length;
					int num2 = length - 33;
					s = "…" + text4.Substring(num2, length - num2);
				}
				graphics.DrawString(s, font2, brush2, 12f, num + 40);
				bool flag = text4 == "::stock-folder::";
				BitmapSource bitmapSource = (flag ? IconService.GetFolderIcon(48) : IconService.GetIcon(text4, 48)) as BitmapSource;
				BitmapSource bitmapSource2 = (flag ? IconService.GetFolderIcon(48) : IconService.GetIconFallback(text4, 48)) as BitmapSource;
				DrawSwatch(graphics, bitmapSource, 210, num, 48, Color.FromArgb(255, 245, 245, 245));
				DrawSwatch(graphics, bitmapSource, 266, num, 48, Color.FromArgb(255, 24, 24, 24));
				DrawSwatch(graphics, bitmapSource2, 322, num, 48, Color.FromArgb(255, 24, 24, 24));
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler appendInterpolatedStringHandler = new StringBuilder.AppendInterpolatedStringHandler(27, 4, stringBuilder2);
				appendInterpolatedStringHandler.AppendFormatted(item2);
				appendInterpolatedStringHandler.AppendLiteral("\texists=");
				appendInterpolatedStringHandler.AppendFormatted(File.Exists(text4) || Directory.Exists(text4));
				appendInterpolatedStringHandler.AppendLiteral("\tprimary=");
				appendInterpolatedStringHandler.AppendFormatted((bitmapSource == null) ? "null" : (bitmapSource.PixelWidth + "px"));
				appendInterpolatedStringHandler.AppendLiteral("\tfallback=");
				appendInterpolatedStringHandler.AppendFormatted((bitmapSource2 == null) ? "null" : (bitmapSource2.PixelWidth + "px"));
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(27, 4, stringBuilder3);
				handler.AppendFormatted(item2);
				handler.AppendLiteral("\texists=");
				handler.AppendFormatted(File.Exists(text4) || Directory.Exists(text4));
				handler.AppendLiteral("\tprimary=");
				handler.AppendFormatted((bitmapSource == null) ? "null" : (bitmapSource.PixelWidth + "px"));
				handler.AppendLiteral("\tfallback=");
				handler.AppendFormatted((bitmapSource2 == null) ? "null" : (bitmapSource2.PixelWidth + "px"));
				stringBuilder3.AppendLine(ref handler);
			}
		}
		string directoryName = Path.GetDirectoryName(outPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		bitmap.Save(outPath, ImageFormat.Png);
		File.WriteAllText(Path.ChangeExtension(outPath, ".txt"), stringBuilder.ToString(), Encoding.UTF8);
		AppPaths.Log("自检完成：" + outPath);
	}

	public static void SelfTest(string reportPath)
	{
		StringBuilder log = new StringBuilder();
		int pass = 0;
		int fail = 0;
		log.AppendLine("DeskBox 自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		log.AppendLine("数据目录 = " + AppPaths.BaseDir);
		log.AppendLine();
		string text = Path.Combine(AppPaths.BaseDir, "selftest-src");
		try
		{
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, "笔记.txt");
			string text3 = Path.Combine(text, "报告.docx");
			File.WriteAllText(text2, "hello");
			File.WriteAllText(text3, "world");
			string text4 = Path.Combine(text, "子文件夹");
			Directory.CreateDirectory(text4);
			File.WriteAllText(Path.Combine(text4, "内层.txt"), "inner");
			string text5 = Path.Combine(text, "记事本.lnk");
			string text6 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
			Check("创建快捷方式", ShortcutService.Create(text5, text6), text5);
			Check("快捷方式能解析出目标", string.Equals(ShortcutService.Resolve(text5)?.Target, text6, StringComparison.OrdinalIgnoreCase), ShortcutService.Resolve(text5)?.Target ?? "null");
			List<DeskItem> list = ItemStore.ImportAll(new string[4] { text2, text3, text4, text5 });
			Check("导入返回 4 个条目", list.Count == 4, "实际 " + list.Count);
			Check("原位置的 txt 已被移走", !File.Exists(text2));
			Check("原位置的文件夹已被移走", !Directory.Exists(text4));
			DeskItem deskItem = list.FirstOrDefault((DeskItem i) => i.Kind == ItemKind.File && (i.StoredName?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ?? false));
			Check("txt 识别为普通文件", deskItem != null && deskItem.Kind == ItemKind.File, deskItem?.Kind.ToString() ?? "null");
			Check("txt 已进入收纳仓库", deskItem != null && deskItem.StoredName != null && File.Exists(Path.Combine(AppPaths.ItemsDir, deskItem.StoredName)), deskItem?.StoredName ?? "null");
			DeskItem deskItem2 = list.FirstOrDefault((DeskItem i) => i.Kind == ItemKind.Shortcut);
			Check("lnk 识别为快捷方式", deskItem2 != null && deskItem2.Kind == ItemKind.Shortcut, deskItem2?.Kind.ToString() ?? "null");
			Check("lnk 记下了解析后的目标", string.Equals(deskItem2?.Target, text6, StringComparison.OrdinalIgnoreCase), deskItem2?.Target ?? "null");
			DeskItem deskItem3 = list.FirstOrDefault((DeskItem i) => i.IsGroup);
			Check("文件夹变成了分组", deskItem3 != null, deskItem3?.Name ?? "null");
			Check("分组的子项被收纳进来", deskItem3 != null && deskItem3.Children.Count == 1, "子项 " + (deskItem3?.Children.Count ?? (-1)));
			ConfigService.Current.Items.Clear();
			ConfigService.Current.Items.AddRange(list);
			List<DeskItem> list2 = NameService.Sort(ConfigService.Current.Items, "Name");
			Check("按名称排序不丢项", list2.Count == list.Count);
			if (deskItem != null)
			{
				deskItem.Name = "会议笔记";
			}
			ConfigService.Save();
			ConfigService.Load();
			DeskItem deskItem4 = ConfigService.Current.Items.FirstOrDefault((DeskItem i) => i.Name == "会议笔记");
			Check("重命名后能持久化", deskItem4 != null);
			if (deskItem4 != null)
			{
				deskItem4.ClickCount = 7;
			}
			ConfigService.Save();
			ConfigService.Load();
			DeskItem deskItem5 = ConfigService.Current.Items.FirstOrDefault((DeskItem i) => i.Name == "会议笔记");
			Check("点击次数能持久化", deskItem5 != null && deskItem5.ClickCount == 7);
			string text7 = AppPaths.StoredFilePath(ConfigService.Current.Items[0]);
			bool flag = RecycleBin.Delete(text7);
			Check("删除进回收站", flag && !File.Exists(text7), text7);
			log.AppendLine();
			log.AppendLine("开机自启当前状态 = " + AutoStartService.IsEnabled());
			log.AppendLine("系统主题 = " + ThemeService.SystemTheme.ToString() + "，强调色 = " + ThemeService.AccentColor.ToString() + "，Win11 = " + ThemeService.IsWindows11);
			log.AppendLine("热键解析 Ctrl+Alt+Space => " + (HotkeyService.TryParse("Ctrl+Alt+Space", out var modifiers, out var virtualKey) ? $"mods={modifiers} vk={virtualKey}" : "失败"));
			log.AppendLine("热键显示名 Ctrl+Alt+Space => " + HotkeyService.DisplayName("Ctrl+Alt+Space"));
		}
		catch (Exception ex)
		{
			fail++;
			log.AppendLine("[FAIL] 自检过程异常：" + ex);
		}
		log.AppendLine();
		StringBuilder stringBuilder = log;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
		handler.AppendLiteral("结果：");
		handler.AppendFormatted(pass);
		handler.AppendLiteral(" 项通过，");
		handler.AppendFormatted(fail);
		handler.AppendLiteral(" 项失败");
		stringBuilder.AppendLine(ref handler);
		string directoryName = Path.GetDirectoryName(reportPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(reportPath, log.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		void Check(string name, bool ok, string detail = "")
		{
			if (ok)
			{
				pass++;
			}
			else
			{
				fail++;
			}
			StringBuilder stringBuilder2 = log;
			StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(3, 3, stringBuilder2);
			handler2.AppendLiteral("[");
			handler2.AppendFormatted(ok ? "PASS" : "FAIL");
			handler2.AppendLiteral("] ");
			handler2.AppendFormatted(name);
			handler2.AppendFormatted((detail.Length > 0) ? ("  -> " + detail) : "");
			stringBuilder2.AppendLine(ref handler2);
		}
	}

	public static void CaptureRegion(string outPath, int x, int y, int width, int height)
	{
		string directoryName = Path.GetDirectoryName(outPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		using Bitmap bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height));
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
		}
		bitmap.Save(outPath, ImageFormat.Png);
	}

	public static void CaptureWindow(Window window, string outPath)
	{
		Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var lpRect);
		int width = Math.Max(1, lpRect.Width);
		int height = Math.Max(1, lpRect.Height);
		string directoryName = Path.GetDirectoryName(outPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		using Bitmap bitmap = new Bitmap(width, height);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.CopyFromScreen(lpRect.Left, lpRect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
		}
		bitmap.Save(outPath, ImageFormat.Png);
	}

	private static void DrawSwatch(Graphics g, BitmapSource? source, int x, int y, int size, Color background)
	{
		using SolidBrush brush = new SolidBrush(background);
		g.FillRectangle(brush, x, y, size + 12, size + 12);
		if (source == null)
		{
			return;
		}
		using MemoryStream memoryStream = new MemoryStream();
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(source));
		pngBitmapEncoder.Save(memoryStream);
		memoryStream.Position = 0L;
		using Image image = Image.FromStream(memoryStream);
		g.DrawImage(image, x + 6, y + 6, size, size);
	}
}
