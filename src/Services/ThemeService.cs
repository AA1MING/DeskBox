using System;
using System.Windows;
using System.Windows.Media;
using DeskBox.Interop;
using Microsoft.Win32;

namespace DeskBox.Services;

public static class ThemeService
{
	public static AppTheme SystemTheme { get; private set; } = AppTheme.Light;

	public static Color AccentColor { get; private set; } = Color.FromRgb(0, 120, 212);

	public static bool HighContrast { get; private set; }

	public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

	public static int BuildNumber => Environment.OSVersion.Version.Build;

	public static event Action? Changed;

	public static void Refresh()
	{
		bool flag = ReadSystemUsesDark();
		Color color = ReadAccentColor();
		bool highContrast = SystemParameters.HighContrast;
		bool flag2 = flag != (SystemTheme == AppTheme.Dark) || color != AccentColor || highContrast != HighContrast;
		SystemTheme = (flag ? AppTheme.Dark : AppTheme.Light);
		AccentColor = color;
		HighContrast = highContrast;
		if (flag2)
		{
			Changed?.Invoke();
		}
	}

	private static bool ReadSystemUsesDark()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", writable: false);
			if (registryKey?.GetValue("AppsUseLightTheme") is int num)
			{
				return num == 0;
			}
		}
		catch
		{
		}
		return false;
	}

	private static Color ReadAccentColor()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\DWM", writable: false);
			if (registryKey?.GetValue("AccentColor") is int num)
			{
				byte b = (byte)(num & 0xFF);
				byte b2 = (byte)((num >>> 8) & 0xFF);
				byte b3 = (byte)((num >>> 16) & 0xFF);
				if (b + b2 + b3 > 0)
				{
					return Color.FromRgb(b, b2, b3);
				}
			}
		}
		catch
		{
		}
		try
		{
			if (Native.DwmGetColorizationColor(out var colorization, out var _) == 0)
			{
				byte r = (byte)((colorization >> 16) & 0xFF);
				byte g = (byte)((colorization >> 8) & 0xFF);
				byte b4 = (byte)(colorization & 0xFF);
				return Color.FromRgb(r, g, b4);
			}
		}
		catch
		{
		}
		return Color.FromRgb(0, 120, 212);
	}

	public static Color Mix(Color a, Color b, double t)
	{
		t = Math.Clamp(t, 0.0, 1.0);
		return Color.FromRgb((byte)((double)(int)a.R + (double)(b.R - a.R) * t), (byte)((double)(int)a.G + (double)(b.G - a.G) * t), (byte)((double)(int)a.B + (double)(b.B - a.B) * t));
	}

	public static Color WithAlpha(Color c, byte alpha)
	{
		return Color.FromArgb(alpha, c.R, c.G, c.B);
	}
}
