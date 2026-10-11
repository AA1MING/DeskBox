using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using DeskBox.Infrastructure;
using DeskBox.Interop;

namespace DeskBox.Services;

public sealed class HotkeyService : IDisposable
{
	private const int HotkeyId = 17491;

	private HwndSource? _source;

	private bool _registered;

	public string CurrentSpec { get; private set; } = string.Empty;

	public event Action? Pressed;

	public event Action? SystemSettingsChanged;

	public void Attach()
	{
		if (_source == null)
		{
			HwndSourceParameters hwndSourceParameters = new HwndSourceParameters("DeskBoxMessageWindow");
			hwndSourceParameters.ParentWindow = Native.HWND_MESSAGE;
			hwndSourceParameters.WindowStyle = 0;
			hwndSourceParameters.Width = 0;
			hwndSourceParameters.Height = 0;
			HwndSourceParameters parameters = hwndSourceParameters;
			_source = new HwndSource(parameters);
			_source.AddHook(WndProc);
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
		case 786:
		{
			nint num = wParam;
			if (((IntPtr)num).ToInt32() == 17491)
			{
				Pressed?.Invoke();
			}
			break;
		}
		case 26:
		case 794:
		case 800:
			SystemSettingsChanged?.Invoke();
			break;
		}
		return IntPtr.Zero;
	}

	public bool Register(string spec)
	{
		Attach();
		Unregister();
		CurrentSpec = spec;
		if (string.IsNullOrWhiteSpace(spec) || _source == null)
		{
			return true;
		}
		if (!TryParse(spec, out var modifiers, out var virtualKey))
		{
			AppPaths.Log("热键格式无法识别：" + spec);
			return false;
		}
		_registered = Native.RegisterHotKey(_source.Handle, 17491, modifiers | 0x4000, virtualKey);
		if (!_registered)
		{
			AppPaths.Log("热键注册失败：" + spec);
		}
		return _registered;
	}

	public void Unregister()
	{
		if (_source != null && _registered)
		{
			Native.UnregisterHotKey(_source.Handle, 17491);
		}
		_registered = false;
	}

	public static bool TryParse(string spec, out uint modifiers, out uint virtualKey)
	{
		modifiers = 0u;
		virtualKey = 0u;
		if (string.IsNullOrWhiteSpace(spec))
		{
			return false;
		}
		string[] array = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0)
		{
			return false;
		}
		for (int i = 0; i < array.Length - 1; i++)
		{
			switch (array[i].ToLowerInvariant())
			{
			case "ctrl":
			case "control":
				modifiers |= ModControl;
				break;
			case "alt":
				modifiers |= ModAlt;
				break;
			case "shift":
				modifiers |= ModShift;
				break;
			case "win":
				modifiers |= ModWin;
				break;
			default:
				return false;
			}
		}
		if (!Enum.TryParse<Key>(array[^1], true, out Key result))
		{
			return false;
		}
		if ((int)result == 0)
		{
			return false;
		}
		virtualKey = (uint)KeyInterop.VirtualKeyFromKey(result);
		return virtualKey != 0;
	}

	/// <summary>Win32 修饰键位标志（RegisterHotKey 的 fsModifiers 参数）。</summary>
	public const uint ModAlt = 0x1;
	public const uint ModControl = 0x2;
	public const uint ModShift = 0x4;
	public const uint ModWin = 0x8;

	/// <summary>把 WPF 的 ModifierKeys 转成 RegisterHotKey 用的位标志。</summary>
	public static uint ToNativeModifiers(ModifierKeys modifiers)
	{
		uint result = 0;
		if ((modifiers & ModifierKeys.Control) != 0) result |= ModControl;
		if ((modifiers & ModifierKeys.Alt) != 0) result |= ModAlt;
		if ((modifiers & ModifierKeys.Shift) != 0) result |= ModShift;
		if ((modifiers & ModifierKeys.Windows) != 0) result |= ModWin;
		return result;
	}

	public static string BuildSpec(uint modifiers, Key key)
	{
		List<string> list = new List<string>();
		if ((modifiers & ModControl) != 0)
		{
			list.Add("Ctrl");
		}
		if ((modifiers & ModAlt) != 0)
		{
			list.Add("Alt");
		}
		if ((modifiers & ModShift) != 0)
		{
			list.Add("Shift");
		}
		if ((modifiers & ModWin) != 0)
		{
			list.Add("Win");
		}
		list.Add(key.ToString());
		return string.Join("+", list);
	}

	public static string DisplayName(string? spec)
	{
		if (string.IsNullOrWhiteSpace(spec))
		{
			return "未设置";
		}
		string[] array = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0)
		{
			return "未设置";
		}
		if (Enum.TryParse<Key>(array[^1], true, out Key result))
		{
			array[^1] = KeyDisplayName(result);
		}
		return string.Join("+", array);
	}

	/// <summary>把 Key 枚举转成界面上显示的按键名。</summary>
	public static string KeyDisplayName(Key key)
	{
		switch (key)
		{
			case Key.Back: return "退格";
			case Key.Tab: return "Tab";
			case Key.Return: return "回车";
			case Key.Escape: return "Esc";
			case Key.Space: return "空格";
			case Key.Prior: return "PageUp";
			case Key.Next: return "PageDown";
			case Key.End: return "End";
			case Key.Home: return "Home";
			case Key.Left: return "←";
			case Key.Up: return "↑";
			case Key.Right: return "→";
			case Key.Down: return "↓";
			case Key.Insert: return "Ins";
			case Key.Delete: return "Del";
			case Key.Oem1: return ";";
			case Key.OemPlus: return "=";
			case Key.OemComma: return ",";
			case Key.OemMinus: return "-";
			case Key.OemPeriod: return ".";
			case Key.OemQuestion: return "/";
			case Key.Oem3: return "~";
			case Key.OemOpenBrackets: return "[";
			case Key.Oem6: return "]";
			case Key.OemQuotes: return "'";
			case Key.OemBackslash: return "\\";
		}

		// 数字键 D0..D9 -> "0".."9"
		if (key >= Key.D0 && key <= Key.D9)
		{
			return ((char)('0' + (int)(key - Key.D0))).ToString();
		}

		// 小键盘 NumPad0..NumPad9
		if (key >= Key.NumPad0 && key <= Key.NumPad9)
		{
			return "小键盘" + (key - Key.NumPad0);
		}

		return key.ToString();
	}


	public void Dispose()
	{
		Unregister();
		_source?.Dispose();
		_source = null;
	}
}



