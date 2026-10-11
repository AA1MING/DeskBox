using System;
using System.IO;
using DeskBox.Infrastructure;
using DeskBox.Interop;

namespace DeskBox.Services;

public static class RecycleBin
{
	public static bool Delete(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}
		if (!File.Exists(path) && !Directory.Exists(path))
		{
			return true;
		}
		Native.SHFILEOPSTRUCT lpFileOp = new Native.SHFILEOPSTRUCT
		{
			hwnd = IntPtr.Zero,
			wFunc = 3u,
			pFrom = path + "\0\0",
			pTo = null,
			fFlags = 17492,
			fAnyOperationsAborted = false,
			hNameMappings = IntPtr.Zero,
			lpszProgressTitle = null
		};
		try
		{
			return Native.SHFileOperation(ref lpFileOp) == 0 && !lpFileOp.fAnyOperationsAborted;
		}
		catch (Exception ex)
		{
			AppPaths.Log("删除到回收站失败：" + ex.Message);
			return false;
		}
	}
}
