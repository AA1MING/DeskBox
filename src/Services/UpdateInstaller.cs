using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DeskBox.Infrastructure;

namespace DeskBox.Services;

internal sealed record UpdatePlan(int ParentId, long ParentStarted, string Target, string Download, string Sha256, string Version, string DataDirectory);

public static class UpdateInstaller
{
    internal static void ProbeDirectory(string directory)
    {
        string probe = Path.Combine(directory, ".deskbox-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(probe)) { } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException("程序目录不可写，请移到可写目录后重试", ex); }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }
    internal static async Task VerifyAsync(string path, string hash, Version version)
    {
        if (!UpdateService.IsHash(hash)) throw new InvalidDataException("更新校验值无效");
        using (var input = File.OpenRead(path))
        {
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(input));
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新文件校验失败");
        }
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.ProductName != "DeskBox" || info.ProductVersion == null || UpdateService.ParseVersion(info.ProductVersion) != version)
            throw new InvalidDataException("下载程序版本不符");
    }
    public static void Start(PreparedUpdate update)
    {
        string executable = ExecutablePath.Current();
        var info = FileVersionInfo.GetVersionInfo(executable);
        if (info.ProductName != "DeskBox" || info.ProductVersion == null || UpdateService.ParseVersion(info.ProductVersion) != UpdateService.CurrentVersion)
            throw new IOException("程序文件已改变，请退出 DeskBox 并重新打开后再更新。");
        ProbeDirectory(Path.GetDirectoryName(executable)!);
        string helper = Path.Combine(update.Directory, "helper.exe");
        File.Copy(executable, helper, false);
        using var parent = Process.GetCurrentProcess();
        var plan = new UpdatePlan(parent.Id, parent.StartTime.ToUniversalTime().Ticks, executable, update.Download, update.Sha256, update.Version.ToString(), AppPaths.BaseDir);
        string manifest = Path.Combine(update.Directory, "plan.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = update.Directory };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(manifest);
        using var process = Process.Start(start) ?? throw new IOException("无法启动更新，请重试");
    }
    internal static UpdatePlan ReadPlan(string manifest)
    {
        if (new FileInfo(manifest).Length > 65536) throw new InvalidDataException("更新计划过大");
        string root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("更新计划无效");
        if (!Path.IsPathFullyQualified(plan.Target) || !Path.IsPathFullyQualified(plan.DataDirectory) ||
            !Path.IsPathFullyQualified(plan.Download) ||
            !string.Equals(Path.GetFullPath(plan.Download), Path.Combine(root, "new.exe"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(plan.Target), Path.GetFullPath(plan.Download), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(plan.Target), ".exe", StringComparison.OrdinalIgnoreCase) || !UpdateService.IsHash(plan.Sha256))
            throw new InvalidDataException("更新路径无效");
        return plan;
    }
    internal static void ReportReady(string path)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (Path.GetFileName(path) != "ready") throw new InvalidDataException("启动确认路径无效");
        var plan = ReadPlan(Path.Combine(root, "plan.json"));
        if (!string.Equals(Path.GetFullPath(plan.Target), Path.GetFullPath(AppPaths.ExePath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(plan.DataDirectory), Path.GetFullPath(AppPaths.BaseDir), StringComparison.OrdinalIgnoreCase) ||
            UpdateService.ParseVersion(plan.Version) != UpdateService.CurrentVersion)
            throw new InvalidDataException("启动确认不匹配");
        File.WriteAllText(path, "ready");
    }
    public static async Task ApplyAsync(string manifest)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        string result = Path.Combine(root, "result.txt");
        string? target = null, candidate = null, backup = null;
        bool replaced = false;
        Process? newProcess = null;
        UpdatePlan? loadedPlan = null;
        try
        {
            var plan = ReadPlan(manifest);
            loadedPlan = plan;
            target = plan.Target;
            await VerifyAsync(plan.Download, plan.Sha256, UpdateService.ParseVersion(plan.Version));
            if (FileVersionInfo.GetVersionInfo(target).ProductName != "DeskBox") throw new InvalidDataException("原程序不匹配");
            try
            {
                using var parent = Process.GetProcessById(plan.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStarted)
                    await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            }
            catch (ArgumentException) { }
            string? installedVersion = FileVersionInfo.GetVersionInfo(target).ProductVersion;
            if (installedVersion == null || UpdateService.ParseVersion(installedVersion) >= UpdateService.ParseVersion(plan.Version))
                throw new InvalidDataException("当前程序已是相同或更新版本");
            string suffix = Guid.NewGuid().ToString("N");
            candidate = target + ".update-" + suffix;
            backup = target + ".backup-" + suffix;
            File.Copy(plan.Download, candidate, false);
            await VerifyAsync(candidate, plan.Sha256, UpdateService.ParseVersion(plan.Version));
            // Replace on the same volume keeps the previous executable intact until the swap succeeds.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Replace(candidate, target, backup); replaced = true; break; }
                catch (IOException) when (attempt < 19) { await Task.Delay(250); }
            }
            var start = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(target)! };
            File.Delete(Path.Combine(root, "ready"));
            start.Environment["DESKBOX_HOME"] = plan.DataDirectory;
            start.ArgumentList.Add("--tray"); start.ArgumentList.Add("--update-ready"); start.ArgumentList.Add(Path.Combine(root, "ready"));
            newProcess = Process.Start(start) ?? throw new IOException("无法启动新版");
            for (int attempt = 0; !File.Exists(Path.Combine(root, "ready")); attempt++)
            {
                if (newProcess.HasExited || attempt >= 120) throw new IOException("新版启动失败");
                await Task.Delay(250);
            }
            replaced = false; // The restarted application has confirmed it loaded the original data directory.
            File.WriteAllText(result, "更新成功 " + plan.Version);
            try { File.Delete(backup); File.Delete(plan.Download); } catch { }
        }
        catch (Exception ex)
        {
            string message = "更新失败：" + ex.Message;
            if (replaced && target != null && backup != null)
            {
                try { if (newProcess != null && !newProcess.HasExited) { newProcess.Kill(); await newProcess.WaitForExitAsync(); } } catch { }
                try { File.Replace(backup, target, null); message += "，已恢复旧版"; }
                catch (Exception restore) { message += "；恢复失败：" + restore.Message + "；备份：" + backup; }
            }
            File.WriteAllText(result, message);
            if (target != null && loadedPlan != null && File.Exists(target) && FileVersionInfo.GetVersionInfo(target).ProductName == "DeskBox")
            {
                try
                {
                    var start = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(target)! };
                    start.Environment["DESKBOX_HOME"] = loadedPlan.DataDirectory;
                    start.ArgumentList.Add("--tray"); start.ArgumentList.Add("--update-error"); start.ArgumentList.Add(result);
                    Process.Start(start)?.Dispose();
                }
                catch { }
            }
        }
        finally
        {
            newProcess?.Dispose();
            if (candidate != null && File.Exists(candidate)) { try { File.Delete(candidate); } catch { } }
        }
    }
}
