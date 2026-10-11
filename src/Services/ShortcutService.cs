using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DeskBox.Infrastructure;

namespace DeskBox.Services;

public static class ShortcutService
{
	[ComImport]
	[Guid("00021401-0000-0000-C000-000000000046")]
	private class ShellLinkObject
	{
	}

	[ComImport]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellLinkW
	{
		void GetPath([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);

		void GetIDList(out nint ppidl);

		void SetIDList(nint pidl);

		void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

		void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

		void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

		void GetHotkey(out short pwHotkey);

		void SetHotkey(short wHotkey);

		void GetShowCmd(out int piShowCmd);

		void SetShowCmd(int iShowCmd);

		void GetIconLocation([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

		void Resolve(nint hwnd, uint fFlags);

		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
	}

	[ComImport]
	[Guid("0000010b-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPersistFile
	{
		void GetClassID(out Guid pClassID);

		[PreserveSig]
		int IsDirty();

		void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

		void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);

		void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

		void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
	}

	public static bool Create(string lnkPath, string targetPath, string? arguments = null, string? workingDirectory = null)
	{
		object obj = null;
		try
		{
			obj = new ShellLinkObject();
			IShellLinkW shellLinkW = (IShellLinkW)obj;
			shellLinkW.SetPath(targetPath);
			if (!string.IsNullOrEmpty(arguments))
			{
				shellLinkW.SetArguments(arguments);
			}
			shellLinkW.SetWorkingDirectory(string.IsNullOrEmpty(workingDirectory) ? (Path.GetDirectoryName(targetPath) ?? string.Empty) : workingDirectory);
			((IPersistFile)shellLinkW).Save(lnkPath, fRemember: true);
			return true;
		}
		catch (Exception ex)
		{
			AppPaths.Log("创建快捷方式失败：" + ex.Message);
			return false;
		}
		finally
		{
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}

	public static ShortcutInfo? Resolve(string lnkPath)
	{
		object obj = null;
		try
		{
			obj = new ShellLinkObject();
			IShellLinkW shellLinkW = (IShellLinkW)obj;
			((IPersistFile)shellLinkW).Load(lnkPath, 0u);
			StringBuilder stringBuilder = new StringBuilder(1024);
			shellLinkW.GetPath(stringBuilder, stringBuilder.Capacity, IntPtr.Zero, 0u);
			StringBuilder stringBuilder2 = new StringBuilder(1024);
			shellLinkW.GetArguments(stringBuilder2, stringBuilder2.Capacity);
			StringBuilder stringBuilder3 = new StringBuilder(1024);
			shellLinkW.GetWorkingDirectory(stringBuilder3, stringBuilder3.Capacity);
			StringBuilder stringBuilder4 = new StringBuilder(1024);
			shellLinkW.GetIconLocation(stringBuilder4, stringBuilder4.Capacity, out var piIcon);
			return new ShortcutInfo(stringBuilder.ToString(), stringBuilder2.ToString(), stringBuilder3.ToString(), stringBuilder4.ToString(), piIcon);
		}
		catch (Exception ex)
		{
			AppPaths.Log("解析快捷方式失败 " + lnkPath + "：" + ex.Message);
			return null;
		}
		finally
		{
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}
}
