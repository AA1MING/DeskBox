using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Theme;

namespace DeskBox;

public partial class SettingsWindow : Window, IComponentConnector
{
	private bool _loading = true;

	public static SettingsWindow? Current { get; private set; }

	public SettingsWindow()
	{
		InitializeComponent();
		Current = this;
		LoadFromConfig();
		_loading = false;
		SourceInitialized += delegate
		{
			ApplyTheme();
		};
		Activated += delegate
		{
			(Application.Current as App)?.SuspendHotkey();
		};
		Deactivated += delegate
		{
			(Application.Current as App)?.ResumeHotkey();
		};
		Closed += delegate
		{
			Current = null;
			(Application.Current as App)?.ResumeHotkey();
		};
	}

	private void LoadFromConfig()
	{
		AppConfig current = ConfigService.Current;
		ThemeSystem.IsChecked = current.ThemeMode == "System";
		ThemeLight.IsChecked = current.ThemeMode == "Light";
		ThemeDark.IsChecked = current.ThemeMode == "Dark";
		BackdropMica.IsChecked = current.Backdrop == "Mica";
		BackdropAcrylic.IsChecked = current.Backdrop == "Acrylic";
		BackdropSolid.IsChecked = current.Backdrop == "Solid";
		AccentCheck.IsChecked = current.UseAccentColor;
		LabelCheck.IsChecked = current.ShowLabels;
		IconStyleMono.IsChecked = current.MonochromeIcons;
		IconStyleColor.IsChecked = !current.MonochromeIcons;
		AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
		UpdateAutoStartStatus();
		IconSizeSlider.Value = Math.Clamp(current.IconSize, 32, 96);
		IconSizeText.Text = $"{current.IconSize} 像素";
		bool flag = current.WidthRatio < 0.5;
		bool flag2 = current.WidthRatio > 0.68;
		SizeSmall.IsChecked = flag;
		SizeLarge.IsChecked = flag2;
		SizeMedium.IsChecked = !flag && !flag2;
		UpdateHotkeyDisplay();
		DataPathBox.Text = AppPaths.BaseDir;
		VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
	}

	private void UpdateHotkeyDisplay()
	{
		string hotkey = ConfigService.Current.Hotkey;
		HotkeyBox.Text = HotkeyService.DisplayName(hotkey);
		HotkeyHint.Text = (string.IsNullOrWhiteSpace(hotkey) ? "当前没有设置热键：可以左键单击托盘图标打开面板。" : ("按 " + HotkeyService.DisplayName(hotkey) + " 可以随时呼出面板；要修改就点一下输入框重新按一次，按 Backspace 可以清空。"));
	}

	public void ApplyTheme()
	{
		bool isDarkNow = ThemeBrushes.IsDarkNow;
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			int value = (isDarkNow ? 1 : 0);
			Native.DwmSetWindowAttribute(handle, 20, ref value, 4);
			if (ThemeService.IsWindows11)
			{
				int value2 = 2;
				Native.DwmSetWindowAttribute(handle, 33, ref value2, 4);
			}
		}
		Background = (Brush)Application.Current.Resources["WindowFill"];
	}

	private void Save()
	{
		ConfigService.Save();
		(Application.Current as App)?.ApplyThemeEverywhere();
	}

	private void Theme_Checked(object sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton radioButton)
		{
			ConfigService.Current.ThemeMode = (string)radioButton.Tag;
			Save();
		}
	}

	private void Backdrop_Checked(object sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton radioButton)
		{
			ConfigService.Current.Backdrop = (string)radioButton.Tag;
			Save();
		}
	}

	private void Size_Checked(object sender, RoutedEventArgs e)
	{
		if (_loading || !(sender is RadioButton radioButton))
		{
			return;
		}
		string text = (string)radioButton.Tag;
		if (!(text == "Small"))
		{
			if (text == "Large")
			{
				ConfigService.Current.WidthRatio = 0.78;
				ConfigService.Current.HeightRatio = 0.86;
			}
			else
			{
				ConfigService.Current.WidthRatio = 0.6;
				ConfigService.Current.HeightRatio = 0.7;
			}
		}
		else
		{
			ConfigService.Current.WidthRatio = 0.45;
			ConfigService.Current.HeightRatio = 0.55;
		}
		Save();
	}

	private void Accent_Changed(object sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			ConfigService.Current.UseAccentColor = AccentCheck.IsChecked == true;
			Save();
		}
	}

	private void Label_Changed(object sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			ConfigService.Current.ShowLabels = LabelCheck.IsChecked == true;
			Save();
		}
	}

	private void IconStyle_Checked(object sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton radioButton)
		{
			ConfigService.Current.MonochromeIcons = (string)radioButton.Tag == "Mono";
			Save();
		}
	}

	private void IconSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!_loading)
		{
			int num = (int)Math.Round(e.NewValue);
			ConfigService.Current.IconSize = num;
			if (IconSizeText != null)
			{
				IconSizeText.Text = $"{num} 像素";
			}
			Save();
		}
	}

	private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		HotkeyHint.Text = "现在直接按下组合键，例如 Ctrl+Alt+空格；按 Backspace 清空。";
	}

	private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		e.Handled = true;

		// 按住 Alt 等系统键时，真正的按键在 SystemKey 里
		Key key = e.Key == Key.System ? e.SystemKey : e.Key;

		// 还停留在"只按了修饰键"的阶段：提示继续按下一个键
		// 注意：Key 底层是 int，这里必须先转 uint 再比较；
		// 否则普通键(如 A=44、空格=18、F1=90)减去 70/116 得到负数，
		// 有符号比较会误判成修饰键，导致热键永远录不进去。
		if ((uint)(key - Key.LWin) <= 1 || (uint)(key - Key.LeftShift) <= 5)
		{
			HotkeyHint.Text = "继续按下一个字母 / 数字 / 功能键…";
		}
		else if (key == Key.Back || key == Key.Delete)
		{
			SetHotkey(string.Empty);
		}
		else if (key != Key.Tab)
		{
			uint modifiers = HotkeyService.ToNativeModifiers(Keyboard.Modifiers);

			if (modifiers == 0)
			{
				HotkeyHint.Text = "热键至少要带一个 Ctrl / Alt / Shift / Win 修饰键，否则会干扰正常打字。";
			}
			else
			{
				SetHotkey(HotkeyService.BuildSpec(modifiers, key));
			}
		}
	}

	private void SetHotkey(string spec)
	{
		ConfigService.Current.Hotkey = spec;
		ConfigService.Save();
		(Application.Current as App)?.RestartHotkey();
		UpdateHotkeyDisplay();
	}

	private void ClearHotkey_Click(object sender, RoutedEventArgs e)
	{
		SetHotkey(string.Empty);
	}

	private void UpdateAutoStartStatus()
	{
		if (AutoStartService.IsEnabled())
		{
			string mechanism = AutoStartService.Mechanism;
			AutoStartStatus.Text = (AutoStartService.IsDisabledInWindows() ? ("已经写入（" + mechanism + "），但在 Windows 的“启动应用”列表里被关掉了：打开 任务管理器 → 启动应用，把 DeskBox 改成“已启用”。") : ("已启用（" + mechanism + "）。开机后会静默驻留托盘，不弹窗、不抢焦点。"));
		}
		else
		{
			AutoStartStatus.Text = (string.IsNullOrEmpty(AutoStartService.LastError) ? "未启用。勾选后开机会自动在托盘待命。" : ("上次开启失败：" + AutoStartService.LastError));
		}
	}

	private void AutoStart_Changed(object sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			bool valueOrDefault = AutoStartCheck.IsChecked == true;
			if (!AutoStartService.SetEnabled(valueOrDefault))
			{
				_loading = true;
				AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
				_loading = false;
				UpdateAutoStartStatus();
				MessageBox.Show(this, "设置开机自启动失败。\n\n原因：" + AutoStartService.LastError + "\n\n最常见的情况是：程序运行在受限环境里（例如被别的工具以沙箱方式启动），系统既不允许写注册表，也不允许写启动文件夹。\n\n请关掉这个窗口，直接在文件资源管理器里双击 DeskBox.exe 运行，然后再试一次。", "DeskBox", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			else
			{
				ConfigService.Current.AutoStart = valueOrDefault;
				ConfigService.Save();
				UpdateAutoStartStatus();
			}
		}
	}

	private void OpenData_Click(object sender, RoutedEventArgs e)
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

	private void Reset_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show(this, "把所有设置恢复成默认值？（收纳的项目不会被删除）", "DeskBox", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
		{
			List<DeskItem> items = ConfigService.Current.Items;
			AppConfig appConfig = new AppConfig
			{
				Items = items
			};
			ConfigService.Current.ThemeMode = appConfig.ThemeMode;
			ConfigService.Current.Backdrop = appConfig.Backdrop;
			ConfigService.Current.UseAccentColor = appConfig.UseAccentColor;
			ConfigService.Current.ShowLabels = appConfig.ShowLabels;
			ConfigService.Current.IconSize = appConfig.IconSize;
			ConfigService.Current.WidthRatio = appConfig.WidthRatio;
			ConfigService.Current.HeightRatio = appConfig.HeightRatio;
			ConfigService.Current.SortMode = "Manual";
			_loading = true;
			LoadFromConfig();
			_loading = false;
			Save();
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
