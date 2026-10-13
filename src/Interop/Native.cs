using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskBox.Interop;

internal static class Native
{
	[Flags]
	public enum HotkeyModifiers : uint
	{
		None = 0u,
		Alt = 1u,
		Control = 2u,
		Shift = 4u,
		Win = 8u,
		NoRepeat = 0x4000u
	}

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;

		public int Width => Right - Left;

		public int Height => Bottom - Top;
	}

	public struct POINT
	{
		public int X;

		public int Y;
	}

	public struct MARGINS
	{
		public int LeftWidth;

		public int RightWidth;

		public int TopHeight;

		public int BottomHeight;
	}

	public struct WINDOWCOMPOSITIONATTRIBDATA
	{
		public int Attribute;

		public nint Data;

		public int SizeOfData;
	}

	public struct AccentPolicy
	{
		public int AccentState;

		public int AccentFlags;

		public int GradientColor;

		public int AnimationId;
	}

	[ComImport]
	[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IShellItemImageFactory
	{
		void GetImage(SIZE size, int flags, out nint phbm);
	}

	public struct SIZE(int x, int y)
	{
		public int cx = x;

		public int cy = y;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct SHFILEOPSTRUCT
	{
		public nint hwnd;

		public uint wFunc;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pFrom;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? pTo;

		public ushort fFlags;

		[MarshalAs(UnmanagedType.Bool)]
		public bool fAnyOperationsAborted;

		public nint hNameMappings;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpszProgressTitle;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct SHELLEXECUTEINFO
	{
		public int cbSize;

		public uint fMask;

		public nint hwnd;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpVerb;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpFile;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpParameters;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpDirectory;

		public int nShow;

		public nint hInstApp;

		public nint lpIDList;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpClass;

		public nint hkeyClass;

		public uint dwHotKey;

		public nint hIcon;

		public nint hProcess;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct SHFILEINFO
	{
		public nint hIcon;

		public int iIcon;

		public uint dwAttributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szDisplayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string szTypeName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct PROCESSENTRY32
	{
		public uint dwSize;

		public uint cntUsage;

		public uint th32ProcessID;

		public nint th32DefaultHeapID;

		public uint th32ModuleID;

		public uint cntThreads;

		public uint th32ParentProcessID;

		public int pcPriClassBase;

		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szExeFile;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct SHSTOCKICONINFO
	{
		public uint cbSize;

		public nint hIcon;

		public int iSysImageIndex;

		public int iIcon;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szPath;
	}

	public const int WM_HOTKEY = 786;

	public const int WM_SETTINGCHANGE = 26;

	public const int WM_DWMCOLORIZATIONCOLORCHANGED = 800;

	public const int WM_THEMECHANGED = 794;

	public const int WM_DPICHANGED = 736;

	public static readonly nint HWND_MESSAGE = new IntPtr(-3);

	public static readonly nint HWND_TOPMOST = new IntPtr(-1);

	public const uint SWP_NOSIZE = 1u;

	public const uint SWP_NOMOVE = 2u;

	public const uint SWP_NOZORDER = 4u;

	public const uint SWP_NOACTIVATE = 16u;

	public const uint SWP_SHOWWINDOW = 64u;

	public const int VK_LBUTTON = 1;

	public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

	public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

	public const int DWMWA_CAPTION_COLOR = 35;

	public const int DWMWA_TEXT_COLOR = 36;

	public const int DWMWA_BORDER_COLOR = 34;

	public const int WCA_ACCENT_POLICY = 19;

	public const int ACCENT_DISABLED = 0;

	public const int ACCENT_ENABLE_GRADIENT = 1;

	public const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;

	public const int ACCENT_ENABLE_BLURBEHIND = 3;

	public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

	public const int SIIGBF_RESIZETOFIT = 0;

	public const int SIIGBF_BIGGERSIZEOK = 1;

	public const int SIIGBF_MEMORYONLY = 2;

	public const int SIIGBF_ICONONLY = 4;

	public const int SIIGBF_THUMBNAILONLY = 8;

	public const int SIIGBF_INCACHEONLY = 16;

	public const uint FO_DELETE = 3u;

	public const ushort FOF_SILENT = 4;

	public const ushort FOF_NOCONFIRMATION = 16;

	public const ushort FOF_ALLOWUNDO = 64;

	public const ushort FOF_NOERRORUI = 1024;

	public const ushort FOF_WANTNUKEWARNING = 16384;

	public const uint SEE_MASK_INVOKEIDLIST = 12u;

	public const uint SEE_MASK_NOCLOSEPROCESS = 64u;

	public const uint SEE_MASK_FLAG_NO_UI = 1024u;

	public const int SW_SHOWNORMAL = 1;

	public const int SW_SHOW = 5;

	public const int MDT_EFFECTIVE_DPI = 0;

	public const uint MONITOR_DEFAULTTONEAREST = 2u;

	public const uint SHGFI_ICON = 256u;

	public const uint SHGFI_LARGEICON = 0u;

	public const uint SHGFI_SMALLICON = 1u;

	public const uint SHGFI_USEFILEATTRIBUTES = 16u;

	public const uint TH32CS_SNAPPROCESS = 2u;

	public const int SIID_FOLDER = 3;

	public const uint SHGSI_ICON = 256u;

	public const uint SHGSI_LARGEICON = 0u;

	public const uint SHGSI_SMALLICON = 1u;

	public const int SW_HIDE = 0;

	public const uint WM_COMMAND = 273u;

	public const uint SMTO_ABORTIFHUNG = 2u;

	public static readonly nint TOGGLE_DESKTOP_ICONS = new IntPtr(29698);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	public static extern short GetAsyncKeyState(int vKey);

	public static bool IsLeftButtonDown()
	{
		return (GetAsyncKeyState(1) & 0x8000) != 0;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("dwmapi.dll")]
	public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

	[DllImport("dwmapi.dll")]
	public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

	[DllImport("dwmapi.dll")]
	public static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

	[DllImport("dwmapi.dll")]
	public static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);

	[DllImport("dwmapi.dll")]
	public static extern int DwmIsCompositionEnabled(out bool enabled);

	[DllImport("dwmapi.dll")]
	public static extern int DwmFlush();

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteObject(nint hObject);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
	public static extern IShellItemImageFactory SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, nint pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	[DllImport("shcore.dll")]
	public static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

	[DllImport("user32.dll")]
	public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern uint GetDpiForWindow(nint hwnd);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DestroyIcon(nint hIcon);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(nint hObject);

	public static string GetParentProcessName()
	{
		nint num = IntPtr.Zero;
		try
		{
			num = CreateToolhelp32Snapshot(2u, 0u);
			if (num == IntPtr.Zero || num == new IntPtr(-1))
			{
				return "未知";
			}
			int processId = Environment.ProcessId;
			uint num2 = 0u;
			PROCESSENTRY32 lppe = new PROCESSENTRY32
			{
				dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>()
			};
			if (Process32First(num, ref lppe))
			{
				do
				{
					if (lppe.th32ProcessID == processId)
					{
						num2 = lppe.th32ParentProcessID;
						break;
					}
				}
				while (Process32Next(num, ref lppe));
			}
			if (num2 == 0)
			{
				return "未知";
			}
			lppe = new PROCESSENTRY32
			{
				dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>()
			};
			if (Process32First(num, ref lppe))
			{
				do
				{
					if (lppe.th32ProcessID == num2)
					{
						return lppe.szExeFile + " (pid " + num2 + ")";
					}
				}
				while (Process32Next(num, ref lppe));
			}
			return "pid " + num2;
		}
		catch (Exception ex)
		{
			return "查询失败：" + ex.Message;
		}
		finally
		{
			if (num != IntPtr.Zero && num != new IntPtr(-1))
			{
				CloseHandle(num);
			}
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern int SHGetStockIconInfo(int siid, uint uFlags, ref SHSTOCKICONINFO psii);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	public static extern nint SendMessageTimeout(nint hWnd, uint Msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

	// ---- 全局鼠标钩子（点击面板外立即收起） ----

	public const int WH_MOUSE_LL = 14;

	public const int WM_LBUTTONDOWN = 513;

	public const int WM_LBUTTONUP = 514;

	public const int WM_RBUTTONUP = 517;

	public const int WM_MBUTTONUP = 520;

	public const int WM_XBUTTONUP = 524;

	public const uint GA_ROOT = 2u;

	[StructLayout(LayoutKind.Sequential)]
	public struct MSLLHOOKSTRUCT
	{
		public POINT pt;
		public uint mouseData;
		public uint flags;
		public uint time;
		public nint dwExtraInfo;
	}

	public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern nint GetModuleHandle(string? lpModuleName);

	[DllImport("user32.dll")]
	public static extern nint WindowFromPoint(POINT point);

	[DllImport("user32.dll")]
	public static extern nint GetAncestor(nint hwnd, uint gaFlags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	/// <summary>判断屏幕坐标是否落在任务栏（含通知区域）上，用于避免把点击托盘图标当成“点击面板外”。</summary>
	public static bool IsPointOnTaskbar(int x, int y)
	{
		try
		{
			nint child = WindowFromPoint(new POINT { X = x, Y = y });
			if (child == IntPtr.Zero) return false;
			nint root = GetAncestor(child, GA_ROOT);
			if (root == IntPtr.Zero) root = child;
			var buffer = new StringBuilder(64);
			if (GetClassName(root, buffer, buffer.Capacity) == 0) return false;
			string name = buffer.ToString();
			return name.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}
};
