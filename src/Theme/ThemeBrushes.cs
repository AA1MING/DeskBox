using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeskBox.Services;

namespace DeskBox.Theme;

public static class ThemeBrushes
{
	public static readonly Color FallbackAccent = Color.FromRgb(0, 120, 212);

	public static Color CurrentAccent
	{
		get
		{
			if (!ThemeService.HighContrast)
			{
				if (!ConfigService.Current.UseAccentColor)
				{
					return FallbackAccent;
				}
				return ThemeService.AccentColor;
			}
			return SystemColors.HighlightColor;
		}
	}

	public static bool IsDarkNow
	{
		get
		{
			string themeMode = ConfigService.Current.ThemeMode;
			if (!(themeMode == "Light"))
			{
				if (themeMode == "Dark")
				{
					return true;
				}
				return ThemeService.SystemTheme == AppTheme.Dark;
			}
			return false;
		}
	}

	public static bool IsOpaqueNow
	{
		get
		{
			if (!ThemeService.HighContrast)
			{
				return ConfigService.Current.Backdrop == "Solid" || !BackdropService.TransparencyEnabled;
			}
			return true;
		}
	}

	public static void Apply()
	{
		if (Application.Current != null)
		{
			bool isDarkNow = IsDarkNow;
			bool isOpaqueNow = IsOpaqueNow;
			Color currentAccent = CurrentAccent;
			byte a = (byte)(isOpaqueNow ? 255u : 18u);
			byte a2 = (byte)(isOpaqueNow ? 255u : 24u);
			Set("PanelFill", isDarkNow ? Color.FromArgb(a, 32, 32, 32) : Color.FromArgb(a, 246, 246, 246));
			Set("MenuFill", isDarkNow ? Color.FromArgb(a2, 43, 43, 43) : Color.FromArgb(a2, 251, 251, 251));
			Set("PanelStroke", isDarkNow ? Color.FromArgb(31, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(24, 0, 0, 0));
			Set("TextFill", isDarkNow ? Color.FromArgb(232, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(230, 0, 0, 0));
			Set("TextSecondary", isDarkNow ? Color.FromArgb(158, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(158, 0, 0, 0));
			Set("HoverFill", isDarkNow ? Color.FromArgb(34, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(20, 0, 0, 0));
			Set("PressedFill", isDarkNow ? Color.FromArgb(54, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(34, 0, 0, 0));
			Set("ControlFill", isDarkNow ? Color.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(180, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			Set("ControlStroke", isDarkNow ? Color.FromArgb(38, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(26, 0, 0, 0));
			Set("AccentFill", currentAccent);
			Set("AccentSoftFill", Color.FromArgb(61, currentAccent.R, currentAccent.G, currentAccent.B));
			Set("AccentText", Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue));
			Set("DividerFill", isDarkNow ? Color.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(22, 0, 0, 0));
			Set("WindowFill", isDarkNow ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243));
		}
	}

	private static void Set(string key, Color color)
	{
		if (ThemeService.HighContrast)
		{
			if (key is "PanelFill" or "MenuFill" or "WindowFill") color = SystemColors.WindowColor;
			else if (key is "TextFill" or "TextSecondary") color = SystemColors.WindowTextColor;
		}
		if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
		{
			Color previous = brush.Color;
			if (previous == color) return;
			brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
			brush.Color = color;
			if (!ThemeService.HighContrast)
				brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous, color, Motion.Duration)
				{
					EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
				});
		}
		else Application.Current.Resources[key] = new SolidColorBrush(color);
	}
}
