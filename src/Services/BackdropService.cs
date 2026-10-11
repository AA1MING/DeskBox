using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DeskBox.Infrastructure;
using DeskBox.Interop;

namespace DeskBox.Services;

public static class BackdropService
{
	public static string AppliedBackdrop { get; private set; } = "Solid";

	public static void Apply(Window window, string backdrop, bool dark)
	{
		nint handle = new WindowInteropHelper(window).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}
		int value = (dark ? 1 : 0);
		Native.DwmSetWindowAttribute(handle, 20, ref value, 4);
		if (ThemeService.IsWindows11)
		{
			int value2 = 2;
			Native.DwmSetWindowAttribute(handle, 33, ref value2, 4);
			Native.MARGINS margins = new Native.MARGINS
			{
				LeftWidth = -1,
				RightWidth = -1,
				TopHeight = -1,
				BottomHeight = -1
			};
			Native.DwmExtendFrameIntoClientArea(handle, ref margins);
			int value3 = ((backdrop == "Mica") ? 2 : ((!(backdrop == "Acrylic")) ? 1 : 3));
			int num = Native.DwmSetWindowAttribute(handle, 38, ref value3, 4);
			AppPaths.Log($"Win11 背景材质：请求={backdrop} type={value3} hr={num}");
			AppliedBackdrop = ((num == 0) ? backdrop : "Solid");
			if (num != 0)
			{
				ApplyFallback(handle, backdrop, dark);
			}
		}
		else
		{
			ApplyFallback(handle, backdrop, dark);
		}
	}

	private static void ApplyFallback(nint hwnd, string backdrop, bool dark)
	{
		if (backdrop == "Solid" || ThemeService.HighContrast)
		{
			AppliedBackdrop = "Solid";
			SetAccent(hwnd, 0, 0);
			return;
		}
		int num = ((backdrop == "Acrylic") ? 180 : 0);
		byte b = (byte)(dark ? 31u : 243u);
		int gradient = (num << 24) | (b << 16) | (b << 8) | b;
		int state = ((backdrop == "Acrylic") ? 4 : 3);
		if (SetAccent(hwnd, state, gradient))
		{
			AppliedBackdrop = backdrop;
		}
		else
		{
			AppliedBackdrop = "Solid";
		}
	}

	private static bool SetAccent(nint hwnd, int state, int gradient)
	{
		try
		{
			Native.AccentPolicy structure = new Native.AccentPolicy
			{
				AccentState = state,
				AccentFlags = 2,
				GradientColor = gradient,
				AnimationId = 0
			};
			int num = Marshal.SizeOf<Native.AccentPolicy>();
			nint num2 = Marshal.AllocHGlobal(num);
			try
			{
				Marshal.StructureToPtr(structure, num2, fDeleteOld: false);
				Native.WINDOWCOMPOSITIONATTRIBDATA data = new Native.WINDOWCOMPOSITIONATTRIBDATA
				{
					Attribute = 19,
					Data = num2,
					SizeOfData = num
				};
				return Native.SetWindowCompositionAttribute(hwnd, ref data) != 0;
			}
			finally
			{
				Marshal.FreeHGlobal(num2);
			}
		}
		catch
		{
			return false;
		}
	}
}
