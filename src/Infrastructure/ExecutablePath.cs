using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskBox.Infrastructure;

internal static class ExecutablePath
{
    // Environment.ProcessPath keeps the startup name even when the running file is renamed.
    // Query the mapped image instead; never guess a replacement by scanning the directory.
    internal static string Current()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var path = new StringBuilder(32768);
            uint length = GetMappedFileName(process.Handle, process.MainModule!.BaseAddress, path, path.Capacity);
            if (length == 0 || length >= path.Capacity) throw new Win32Exception();
            string mapped = path.ToString();
            if (mapped.StartsWith(@"\Device\Mup\", StringComparison.OrdinalIgnoreCase))
                return RequireFile(@"\\" + mapped[12..]);
            foreach (string drive in Environment.GetLogicalDrives())
            {
                var device = new StringBuilder(32768);
                if (QueryDosDevice(drive[..2], device, device.Capacity) == 0) continue;
                string prefix = device + @"\";
                if (mapped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return RequireFile(drive + mapped[prefix.Length..]);
            }
            throw new IOException("无法解析程序所在磁盘");
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            throw new IOException("无法定位正在运行的程序。请退出 DeskBox，从当前文件位置重新打开后再更新。", ex);
        }
    }

    private static string RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("程序文件已移动或删除", path);
        return path;
    }

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetMappedFileName(IntPtr process, IntPtr address, StringBuilder path, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string device, StringBuilder target, int size);
}
