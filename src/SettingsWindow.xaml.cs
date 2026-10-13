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
    private readonly UpdateService? _updates;

	public static SettingsWindow? Current { get; private set; }

	public SettingsWindow()
	{
		InitializeComponent();
        Motion.AttachWindow(this, SettingsSurface);
        SourceInitialized += (_, _) =>
        {
            var point = System.Windows.Forms.Cursor.Position;
            var screen = System.Windows.Forms.Screen.FromPoint(point);
            uint nativeDpi = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle);
            double dpi = nativeDpi > 0 ? nativeDpi / 96d : VisualTreeHelper.GetDpi(this).DpiScaleY;
            SettingsContent.MaxHeight = Math.Max(200, Math.Min(780, screen.WorkingArea.Height / dpi - 80));
            MaxHeight = screen.WorkingArea.Height / dpi - 24;
        };
		Current = this;
		LoadFromConfig();
		_loading = false;
        _updates = (Application.Current as App)?.Updates;
        if (_updates != null) _updates.Changed += RefreshUpdate;
        RefreshUpdate();
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
            if (_updates != null) _updates.Changed -= RefreshUpdate;
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
		BackdropGlass.IsChecked = current.Backdrop == "Glass" || current.Backdrop == "Mica";
		BackdropAcrylic.IsChecked = current.Backdrop == "Acrylic";
		BackdropSolid.IsChecked = current.Backdrop == "Solid";
		AccentCheck.IsChecked = current.UseAccentColor;
		LabelCheck.IsChecked = current.ShowLabels;
		IconStyleMono.IsChecked = current.MonochromeIcons;
		IconStyleColor.IsChecked = !current.MonochromeIcons;
		AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
        UpdateCheck.IsChecked = current.CheckUpdatesOnStartup;
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
        HotkeyHint.Text = string.IsNullOrWhiteSpace(hotkey) ? "点击输入框录入快捷键" : "点击输入框更改，Backspace 清空";
    }

    public void ApplyTheme()
    {
        string actual = BackdropService.Apply(this, ConfigService.Current.Backdrop, ThemeBrushes.IsDarkNow);
        bool fallback = ConfigService.Current.Backdrop != "Solid" && actual == "Solid";
        BackdropStatus.Text = !BackdropService.TransparencyEnabled ? "系统透明效果未开启" : "当前使用纯色背景";
        BackdropStatus.Visibility = fallback ? Visibility.Visible : Visibility.Collapsed;
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
		HotkeyHint.Text = "按下组合键，Backspace 清空";
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
			HotkeyHint.Text = "继续按下主键…";
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
				HotkeyHint.Text = "请加入 Ctrl、Alt、Shift 或 Win";
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
        AutoStartStatus.Text = AutoStartService.IsEnabled()
            ? (AutoStartService.IsDisabledInWindows() ? "请在任务管理器中启用 DeskBox 启动项" : "开机后驻留托盘")
            : (string.IsNullOrEmpty(AutoStartService.LastError) ? "" : "开启失败：" + AutoStartService.LastError);
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
				MessageBox.Show(this, "开机启动设置失败。\n" + AutoStartService.LastError + "\n请通过文件资源管理器重新启动后重试。", "DeskBox", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
            ConfigService.Current.CheckUpdatesOnStartup = appConfig.CheckUpdatesOnStartup;
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

    private void UpdateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ConfigService.Current.CheckUpdatesOnStartup = UpdateCheck.IsChecked == true;
        ConfigService.Save();
    }

    private void RefreshUpdate()
    {
        CheckUpdateButton.IsEnabled = _updates != null && !_updates.IsBusy;
        InstallUpdateButton.Visibility = _updates?.Available != null ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = _updates != null && !_updates.IsBusy;
        UpdateStatus.Text = _updates?.Status ?? "";
        UpdateProgress.Visibility = _updates?.IsDownloading == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = _updates?.Progress ?? 0;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updates != null) await _updates.CheckAsync();
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updates == null || Application.Current is not App app) return;
        try { await app.InstallUpdateAsync(await _updates.DownloadAsync()); }
        catch (Exception ex)
        {
            AppPaths.Log("安装更新失败：" + ex.Message);
            RefreshUpdate();
            UpdateStatus.Text = "更新失败：" + ex.Message;
        }
    }
}
