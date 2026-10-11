using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Services;
using DeskBox.Theme;

namespace DeskBox;

public partial class App : Application
{

	private Mutex? _instanceMutex;

	private EventWaitHandle? _showSignal;

	private HotkeyService? _hotkeys;

	private TrayService? _tray;

	private MainWindow? _panel;

	private bool _captureMode;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += (_, args) =>
		{
			AppPaths.Log("界面线程未处理异常：" + args.Exception);
			args.Handled = true;
		};
		AppDomain.CurrentDomain.UnhandledException += (object _, UnhandledExceptionEventArgs args) =>
		{
			AppPaths.Log("未处理异常：" + args.ExceptionObject);
		};
		string[] array = e.Args ?? Array.Empty<string>();
		int num = Array.IndexOf(array, "--capture");
		bool flag = num >= 0;
		_captureMode = flag || Array.IndexOf(array, "--icons") >= 0 || Array.IndexOf(array, "--selftest") >= 0 || Array.IndexOf(array, "--autostart") >= 0 || Array.IndexOf(array, "--diag") >= 0 || Array.IndexOf(array, "--capture-menu") >= 0;
		bool flag2 = array.Contains("--tray", StringComparer.OrdinalIgnoreCase);
		if (!_captureMode)
		{
			_instanceMutex = new Mutex(initiallyOwned: true, "Local\\DeskBox_SingleInstance", out var createdNew);
			if (!createdNew)
			{
				try
				{
					EventWaitHandle.OpenExisting("Local\\DeskBox_ShowPanel").Set();
				}
				catch
				{
				}
				Shutdown();
				return;
			}
		}
		AppPaths.Init();
		ConfigService.Load();
		ThemeService.Refresh();
		WriteStartupReport();
		ThemeService.Changed += OnThemeServiceChanged;
		ThemeBrushes.Apply();
		int num2 = Array.IndexOf(array, "--autostart");
		if (num2 >= 0)
		{
			string text = ((num2 + 1 < array.Length) ? array[num2 + 1].ToLowerInvariant() : "status");
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("DeskBox 开机自启诊断 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			stringBuilder.AppendLine("程序路径 = " + AppPaths.ExePath);
			stringBuilder.AppendLine("数据目录 = " + AppPaths.BaseDir + (AppPaths.Degraded ? ("（受限：" + AppPaths.DegradedReason + "）") : string.Empty));
			stringBuilder.AppendLine("启动文件夹 = " + AutoStartService.StartupFolder);
			stringBuilder.AppendLine("当前状态 = " + (AutoStartService.IsEnabled() ? ("已启用 / " + AutoStartService.Mechanism) : "未启用"));
			if (text == "on" || text == "off")
			{
				bool flag3 = text == "on";
				bool flag4 = AutoStartService.SetEnabled(flag3);
				stringBuilder.AppendLine("执行" + (flag3 ? "开启" : "关闭") + " = " + (flag4 ? "成功" : "失败"));
				if (!string.IsNullOrEmpty(AutoStartService.LastError))
				{
					stringBuilder.AppendLine("失败原因 = " + AutoStartService.LastError);
				}
				stringBuilder.AppendLine("执行后状态 = " + (AutoStartService.IsEnabled() ? ("已启用 / " + AutoStartService.Mechanism) : "未启用"));
			}
			stringBuilder.AppendLine("被 Windows 启动应用列表关闭 = " + AutoStartService.IsDisabledInWindows());
			File.WriteAllText(Path.Combine(AppPaths.BaseDir, "autostart.txt"), stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Shutdown();
			return;
		}
		int num3 = Array.IndexOf(array, "--diag");
		if (num3 >= 0)
		{
			string path = ((num3 + 1 < array.Length) ? array[num3 + 1] : Path.Combine(Path.GetTempPath(), "deskbox-diag.txt"));
			StringBuilder stringBuilder2 = new StringBuilder();
			stringBuilder2.AppendLine("DeskBox 诊断 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			stringBuilder2.AppendLine("DESKBOX_HOME        = [" + (Environment.GetEnvironmentVariable("DESKBOX_HOME") ?? "null") + "]");
			stringBuilder2.AppendLine("ProcessPath         = [" + (Environment.ProcessPath ?? "null") + "]");
			stringBuilder2.AppendLine("ApplicationData     = [" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "]");
			stringBuilder2.AppendLine("LocalApplicationData= [" + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "]");
			stringBuilder2.AppendLine("CurrentDirectory    = [" + Environment.CurrentDirectory + "]");
			stringBuilder2.AppendLine("TempPath            = [" + Path.GetTempPath() + "]");
			stringBuilder2.AppendLine("ExeDir              = [" + AppPaths.ExeDir + "]");
			stringBuilder2.AppendLine("BaseDir             = [" + AppPaths.BaseDir + "]");
			stringBuilder2.AppendLine("DataDirUsable       = " + AppPaths.DataDirUsable + "   Degraded = " + AppPaths.Degraded);
			stringBuilder2.AppendLine("PrepareError        = " + (string.IsNullOrEmpty(AppPaths.LastPrepareError) ? "（无）" : AppPaths.LastPrepareError));
			stringBuilder2.AppendLine("父进程              = " + Native.GetParentProcessName());
			try
			{
				WindowsIdentity current = WindowsIdentity.GetCurrent();
				List<string> source = (from IdentityReference g in current.Groups
					select g.Value).ToList();
				stringBuilder2.AppendLine("身份                = " + current.Name + "   属主 = " + current.Owner);
				stringBuilder2.AppendLine("完整性级别          = " + string.Join(",", source.Where((string g) => g.StartsWith("S-1-16-", StringComparison.Ordinal))) + "   （S-1-16-4096=低 8192=中 12288=高）");
				stringBuilder2.AppendLine("AppContainer 包 SID = " + (source.Any((string g) => g.StartsWith("S-1-15-2-", StringComparison.Ordinal)) ? string.Join(",", source.Where((string g) => g.StartsWith("S-1-15-2-", StringComparison.Ordinal))) : "无"));
				stringBuilder2.AppendLine("受限令牌 SID        = " + (source.Any((string g) => g.StartsWith("S-1-5-12-", StringComparison.Ordinal)) ? "有（RESTRICTED）" : "无"));
				stringBuilder2.AppendLine("是否受限令牌        = " + current.IsSystem + " / 令牌类型见上");
			}
			catch (Exception ex)
			{
				stringBuilder2.AppendLine("令牌信息读取失败    = " + ex.Message);
			}
			stringBuilder2.AppendLine();
			stringBuilder2.AppendLine("--- 逐个候选目录直接试写 ---");
			string[] array2 = new string[3]
			{
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskBox"),
				Path.Combine(AppPaths.ExeDir, "DeskBoxData"),
				Path.Combine(Path.GetTempPath(), "DeskBox")
			};
			foreach (string text2 in array2)
			{
				try
				{
					Directory.CreateDirectory(text2);
					string path2 = Path.Combine(text2, "writetest.tmp");
					File.WriteAllText(path2, "x");
					File.Delete(path2);
					stringBuilder2.AppendLine("OK    " + text2);
				}
				catch (Exception ex2)
				{
					stringBuilder2.AppendLine("FAIL  " + text2 + "  =>  " + ex2.GetType().Name + ": " + ex2.Message);
				}
			}
			try
			{
				File.WriteAllText(path, stringBuilder2.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
			catch
			{
			}
			Shutdown();
			return;
		}
		int num5 = Array.IndexOf(array, "--capture-menu");
		if (num5 >= 0)
		{
			string outPath = ((num5 + 1 < array.Length) ? array[num5 + 1] : Path.Combine(Path.GetTempPath(), "deskbox-menu.png"));
			RunMenuCapture(outPath);
			return;
		}
		int num6 = Array.IndexOf(array, "--selftest");
		if (num6 >= 0)
		{
			Diagnostics.SelfTest((num6 + 1 < array.Length) ? array[num6 + 1] : Path.Combine(AppPaths.BaseDir, "selftest.txt"));
			Shutdown();
			return;
		}
		int num7 = Array.IndexOf(array, "--icons");
		if (num7 >= 0)
		{
			Diagnostics.IconSheet((num7 + 1 < array.Length) ? array[num7 + 1] : Path.Combine(AppPaths.BaseDir, "icons.png"));
			Shutdown();
			return;
		}
		if ((AppPaths.Degraded || !AppPaths.DataDirUsable) && !flag2 && !array.Contains("--relaunched", StringComparer.OrdinalIgnoreCase) && MessageBox.Show(AppPaths.DataDirUsable ? "DeskBox 现在运行在受限环境里（被其他程序以沙箱方式启动）。\n\n这种状态下：从桌面拖进来的文件搬不动，开机自启也设置不了。\n\n要现在用正常方式重新启动一次吗？（推荐）" : "DeskBox 找不到任何可写的数据目录，现在收纳的东西重启后就会丢失。\n\n原因是程序被别的工具以沙箱方式启动，系统把用户目录和程序目录的写入都拒绝了。\n\n请用正常方式重新启动一次（推荐），或自己关掉后用文件资源管理器双击 DeskBox.exe。", "DeskBox", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK && TryRelaunchUnrestricted())
		{
			Shutdown();
			return;
		}
		_panel = new MainWindow();
		if (flag)
		{
			RunCapture(array, num);
			return;
		}
		StartShowSignalThread();
		new WindowInteropHelper(_panel).EnsureHandle();
		_hotkeys = new HotkeyService();
		_hotkeys.Pressed += () =>
		{
			_panel?.TogglePanel();
		};
		_hotkeys.SystemSettingsChanged += () =>
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
			{
				ThemeService.Refresh();
				ApplyThemeEverywhere();
			}), Array.Empty<object>());
		};
		_hotkeys.Attach();
		if (!string.IsNullOrWhiteSpace(ConfigService.Current.Hotkey))
		{
			_hotkeys.Register(ConfigService.Current.Hotkey);
		}
		_tray = new TrayService();
		_tray.ToggleRequested += () =>
		{
			_panel?.TogglePanel();
		};
		_tray.SettingsRequested += OpenSettings;
		_tray.OpenDataFolderRequested += OpenDataFolder;
		_tray.HelpRequested += ShowHelp;
		_tray.ExitRequested += ExitApp;
		_tray.AutoStartStateProvider = AutoStartService.IsEnabled;
		_tray.AutoStartSetter = ToggleAutoStart;
		ApplyThemeEverywhere();
		if (AppPaths.Degraded)
		{
			_tray.ShowBalloon("DeskBox 正运行在受限环境", "系统不允许写入正常的数据目录（" + AppPaths.DegradedReason + "），数据暂时放在程序目录旁边，开机自启等系统设置也可能无法修改。建议关掉程序，直接在文件资源管理器里双击 DeskBox.exe 运行。");
			if (!ConfigService.Current.RestrictedNoticeShown)
			{
				ConfigService.Current.RestrictedNoticeShown = true;
				ConfigService.Save();
				MessageBox.Show("DeskBox 现在运行在受限环境里。\n\n系统不允许它写入正常的数据目录：\n" + AppPaths.DegradedReason + "\n\n于是数据被临时放在了程序目录旁边的 DeskBoxData 文件夹，并且「开机自启动」这类系统设置会失败。\n\n原因通常是它被别的工具以沙箱方式启动（例如从某个管理界面里点开）。\n解决办法：关掉当前程序，打开文件资源管理器，直接双击 DeskBox.exe 运行。恢复正常运行后，之前的数据会自动迁移回正式目录。", "DeskBox", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		else if (string.IsNullOrWhiteSpace(ConfigService.Current.Hotkey))
		{
			_tray.ShowBalloon("DeskBox 已经驻留在托盘", "还没有设置呼出热键。左键单击托盘图标即可打开面板，或到“设置”里记录一个热键。");
		}
		else if (!flag2)
		{
			_tray.ShowBalloon("DeskBox 已启动", "按 " + ConfigService.Current.Hotkey + " 随时呼出面板。");
		}
		if (!flag2 && !ConfigService.Current.Items.Any())
		{
			_panel.ShowPanel();
		}
	}

	private void StartShowSignalThread()
	{
		try
		{
			_showSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\DeskBox_ShowPanel");
		}
		catch (Exception ex)
		{
			AppPaths.Log("创建单实例信号失败：" + ex.Message);
			return;
		}
		Thread thread = new Thread(() =>
		{
			while (true)
			{
				try
				{
					_showSignal.WaitOne();
					((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
					{
						_panel?.TogglePanel();
					}), Array.Empty<object>());
				}
				catch
				{
					break;
				}
			}
		});
		thread.IsBackground = true;
		thread.Name = "DeskBox.ShowSignal";
		thread.Start();
	}

	private static void WriteStartupReport()
	{
		try
		{
			string[] array = (from string k in Environment.GetEnvironmentVariables().Keys
				where k.StartsWith("DSH", StringComparison.OrdinalIgnoreCase)
				select k).ToArray();
			string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] exe={AppPaths.ExePath}｜数据目录={AppPaths.BaseDir}｜可写={AppPaths.DataDirUsable}｜受限={AppPaths.Degraded}｜父进程={Native.GetParentProcessName()}｜沙箱相关环境变量={((array.Length == 0) ? "无" : string.Join(",", array))}";
			AppPaths.Log(text);
			try
			{
				File.AppendAllText(Path.Combine(AppPaths.ExeDir, "DeskBox-startup.log"), text + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			AppPaths.Log("启动自述失败：" + ex.Message);
		}
	}

	private static bool TryRelaunchUnrestricted()
	{
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "DeskBox-relaunch.lnk");
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			if (!ShortcutService.Create(text, AppPaths.ExePath, "--relaunched", AppPaths.ExeDir))
			{
				return false;
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "\"" + text + "\"",
				UseShellExecute = true
			});
			return true;
		}
		catch (Exception ex)
		{
			AppPaths.Log("重新启动失败：" + ex.Message);
			return false;
		}
	}

	private void ToggleAutoStart(bool wanted)
	{
		if (AutoStartService.SetEnabled(wanted))
		{
			ConfigService.Current.AutoStart = wanted;
			ConfigService.Save();
			_tray?.ShowBalloon("DeskBox", wanted ? ("已开启开机自启动（" + AutoStartService.Mechanism + "）") : "已关闭开机自启动");
		}
		else
		{
			_tray?.ShowBalloon("开机自启设置失败", AutoStartService.LastError);
			MessageBox.Show("设置开机自启动失败。\n\n原因：" + AutoStartService.LastError + "\n\n如果程序是被别的工具以沙箱方式启动的，系统会拒绝写注册表和启动文件夹；请直接在文件资源管理器里双击 DeskBox.exe 运行后再试。", "DeskBox", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void OnThemeServiceChanged()
	{
		ApplyThemeEverywhere();
	}

	public void ApplyThemeEverywhere()
	{
		ThemeBrushes.Apply();
		_panel?.ApplyTheme();
		_tray?.ApplyTheme(ThemeBrushes.IsDarkNow, ThemeBrushes.CurrentAccent);
		SettingsWindow.Current?.ApplyTheme();
	}

	private void RunCapture(string[] args, int captureIndex)
	{
		string path = ((captureIndex + 1 < args.Length && !args[captureIndex + 1].StartsWith("--")) ? args[captureIndex + 1] : Path.Combine(AppPaths.BaseDir, "capture.png"));
		if (_panel == null)
		{
			return;
		}
		Window target;
		if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
		{
			SettingsWindow settingsWindow = new SettingsWindow
			{
				Topmost = true
			};
			settingsWindow.Show();
			settingsWindow.Activate();
			target = settingsWindow;
		}
		else
		{
			if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
			{
				_panel.LoadDemoData();
			}
			_panel.CaptureMode = true;
			_panel.ShowPanel();
			target = _panel;
		}
		DispatcherTimer timer = null;
		timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1600.0), (DispatcherPriority)4, (EventHandler)delegate
		{
			timer.Stop();
			try
			{
				Diagnostics.CaptureWindow(target, path);
				AppPaths.Log("截图完成：" + path);
			}
			catch (Exception ex)
			{
				AppPaths.Log("截图失败：" + ex);
			}
			ExitApp();
		}, ((DispatcherObject)this).Dispatcher);
		timer.Start();
	}

	private void RunMenuCapture(string outPath)
	{
		ContextMenu menu = new ContextMenu
		{
			Style = (TryFindResource("DeskMenuStyle") as Style),
			Placement = PlacementMode.Absolute,
			HorizontalOffset = 420.0,
			VerticalOffset = 320.0
		};
		Add("打开面板");
		Add("设置…");
		menu.Items.Add(new Separator());
		Add("开机自启动", checkable: true);
		Add("打开数据文件夹");
		Add("使用说明");
		menu.Items.Add(new Separator());
		Add("退出");
		menu.IsOpen = true;
		DispatcherTimer timer = null;
		timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1300.0), (DispatcherPriority)4, (EventHandler)delegate
		{
			timer.Stop();
			try
			{
				Diagnostics.CaptureRegion(outPath, 380, 280, 460, 400);
			}
			catch (Exception ex)
			{
				AppPaths.Log("菜单截图失败：" + ex.Message);
			}
			menu.IsOpen = false;
			Shutdown();
		}, ((DispatcherObject)this).Dispatcher);
		timer.Start();
		void Add(string header, bool checkable = false)
		{
			menu.Items.Add(new MenuItem
			{
				Header = header,
				Style = (TryFindResource("DeskMenuItemStyle") as Style),
				IsCheckable = checkable,
				IsChecked = checkable
			});
		}
	}

	public void OpenSettings()
	{
		SettingsWindow current = SettingsWindow.Current;
		if (current == null)
		{
			current = new SettingsWindow();
			current.Closed += delegate
			{
				_panel?.Refresh();
			};
			current.Show();
		}
		else
		{
			current.Activate();
		}
	}

	private void OpenDataFolder()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "\"" + AppPaths.BaseDir + "\"",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppPaths.Log("打开数据目录失败：" + ex.Message);
		}
	}

	private void ShowHelp()
	{
		MessageBox.Show("DeskBox 桌面收纳盒\n\n· 把桌面上的快捷方式或文件直接拖进面板，原文件会被移动到收纳目录，桌面立刻变干净。\n· 单击图标打开；按住图标拖动可以调整位置，拖到文件夹上可以归档。\n· 右键图标：打开 / 以管理员身份运行 / 打开所在位置 / 重命名 / 删除 / 导出到桌面 / 属性。\n· 顶部“新建文件夹”可以分类；“一键排序”可以在手动顺序、名称、类型、使用频率之间切换。\n· 按 Esc 或点击面板外部关闭；托盘图标左键可以再次呼出。\n\n数据目录：" + AppPaths.BaseDir, "DeskBox 使用说明", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	public void ExitApp()
	{
		if (!_captureMode)
		{
			ConfigService.Save();
		}
		_tray?.Dispose();
		_hotkeys?.Dispose();
		Shutdown();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (!_captureMode)
		{
			ConfigService.Save();
		}
		_tray?.Dispose();
		_hotkeys?.Dispose();
		_instanceMutex?.Dispose();
		base.OnExit(e);
	}

	public void RestartHotkey()
	{
		if (_hotkeys == null)
		{
			_hotkeys = new HotkeyService();
		}
		_hotkeys.Attach();
		_hotkeys.Register(ConfigService.Current.Hotkey);
	}

	public void SuspendHotkey()
	{
		_hotkeys?.Unregister();
	}

	public void ResumeHotkey()
	{
		if (!string.IsNullOrWhiteSpace(ConfigService.Current.Hotkey))
		{
			if (_hotkeys == null)
			{
				_hotkeys = new HotkeyService();
			}
			_hotkeys.Attach();
			_hotkeys.Register(ConfigService.Current.Hotkey);
		}
	}
}
