using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DeskBox.Infrastructure;

namespace DeskBox.Services;

public sealed record ReleaseUpdate(Version Version, string Tag, Uri Download, long Size, string? Sha256, Uri? Checksums);
public sealed record PreparedUpdate(string Directory, string Download, string Sha256, Version Version);
internal sealed record UpdateEnvironment(string Executable, string DataDirectory, bool IsSingleFile);

public sealed class UpdateService : IDisposable
{
    private const string Repository = "https://github.com/AA1MING/DeskBox";
    private const long MaxDownload = 128 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Version _installedVersion;
    private readonly Func<UpdateEnvironment> _environment;
    private readonly CancellationTokenSource _lifetime = new();
    public static Version CurrentVersion => Normalize(typeof(App).Assembly.GetName().Version ?? new Version(1, 1, 1));
    public ReleaseUpdate? Available { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsDownloading { get; private set; }
    public int Progress { get; private set; }
    public string Status { get; private set; } = "";
    public event Action? Changed;

    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }) { }
    internal UpdateService(HttpClient http, Version? installedVersion = null, Func<UpdateEnvironment>? environment = null)
    {
        _http = http;
        _installedVersion = installedVersion ?? CurrentVersion;
        _environment = environment ?? (() => new UpdateEnvironment(AppPaths.ExePath, AppPaths.BaseDir, typeof(App).Assembly.Location.Length == 0));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DeskBox/" + CurrentVersion);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    internal static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    internal static Version ParseVersion(string tag)
    {
        if (!Regex.IsMatch(tag, @"^[vV]?\d+\.\d+\.\d+(\.\d+)?$")) throw new InvalidDataException("版本格式无效");
        return Normalize(Version.Parse(tag.TrimStart('v', 'V')));
    }
    internal static bool IsHash(string? hash) => hash != null && Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$");
    private static Uri AssetUri(JsonElement asset, string tag, string name)
    {
        string expected = Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name;
        string actual = asset.GetProperty("browser_download_url").GetString() ?? "";
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new InvalidDataException("更新地址无效");
        return new Uri(actual);
    }
    internal static ReleaseUpdate? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        Version version = ParseVersion(tag);
        if (version <= Normalize(current)) return null;
        JsonElement exe = default, sums = default;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name == "DeskBox.exe") exe = asset;
            if (name == "SHA256SUMS.txt") sums = asset;
        }
        if (exe.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("新版缺少程序附件");
        long size = exe.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxDownload) throw new InvalidDataException("更新文件大小无效");
        string? hash = null;
        if (exe.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String)
        {
            string value = digest.GetString()!;
            if (!value.StartsWith("sha256:", StringComparison.Ordinal) || !IsHash(value[7..])) throw new InvalidDataException("更新校验值无效");
            hash = value[7..].ToLowerInvariant();
        }
        Uri? checksums = sums.ValueKind == JsonValueKind.Undefined ? null : AssetUri(sums, tag, "SHA256SUMS.txt");
        if (hash == null && checksums == null) throw new InvalidDataException("新版缺少校验文件");
        return new ReleaseUpdate(version, tag, AssetUri(exe, tag, "DeskBox.exe"), size, hash, checksums);
    }
    internal static string ParseChecksums(string text)
    {
        string? found = null;
        foreach (string line in text.Split('\n'))
        {
            var match = Regex.Match(line.Trim().TrimStart('\uFEFF'), @"^([a-fA-F0-9]{64})\s+\*?DeskBox\.exe$");
            if (!match.Success) continue;
            if (found != null) throw new InvalidDataException("校验文件包含重复条目");
            found = match.Groups[1].Value.ToLowerInvariant();
        }
        return found ?? throw new InvalidDataException("缺少程序校验值");
    }
    private void Notify(string? status = null)
    {
        if (status != null) Status = status;
        Changed?.Invoke();
    }
    public async Task CheckAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Notify("正在检查…");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await _http.GetAsync("https://api.github.com/repos/AA1MING/DeskBox/releases/latest", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) { Available = null; Notify("暂无新版本"); return; }
            response.EnsureSuccessStatusCode();
            string json = await ReadTextAsync(response, 1024 * 1024, timeout.Token);
            Available = ParseRelease(json, _installedVersion);
            Notify(Available == null ? "已是最新版" : "发现新版 " + Available.Tag);
        }
        catch (Exception ex)
        {
            AppPaths.Log("检查更新失败：" + ex.Message);
            Notify("检查失败，请稍后重试");
        }
        finally { IsBusy = false; Notify(); }
    }
    private static async Task<string> ReadTextAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + count > limit) throw new InvalidDataException("更新响应过大");
            await buffer.WriteAsync(bytes.AsMemory(0, count), token);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task<PreparedUpdate> DownloadAsync()
    {
        if (IsBusy || Available == null) throw new InvalidOperationException("请先检查更新");
        var release = Available;
        IsBusy = IsDownloading = true;
        Progress = 0;
        Notify("正在下载…");
        string? job = null;
        try
        {
            // A copied single-file executable runs the updater after the main process exits.
            var environment = _environment();
            if (!environment.IsSingleFile) throw new InvalidOperationException("请使用 Releases 单文件程序更新");
            UpdateInstaller.ProbeDirectory(Path.GetDirectoryName(environment.Executable)!);
            job = Path.Combine(environment.DataDirectory, "Updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job);
            string hash = release.Sha256 ?? "";
            if (release.Checksums != null)
            {
                using var sums = await _http.GetAsync(release.Checksums, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token);
                sums.EnsureSuccessStatusCode();
                string expected = ParseChecksums(await ReadTextAsync(sums, 65536, _lifetime.Token));
                if (hash.Length > 0 && hash != expected) throw new InvalidDataException("校验文件与附件不一致");
                hash = expected;
            }
            string path = Path.Combine(job, "new.exe");
            using (var response = await _http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token))
            {
                response.EnsureSuccessStatusCode();
                using var input = await response.Content.ReadAsStreamAsync(_lifetime.Token);
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                byte[] bytes = new byte[65536];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(bytes, _lifetime.Token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("下载文件大小不符");
                    await output.WriteAsync(bytes.AsMemory(0, count), _lifetime.Token);
                    int progress = (int)(total * 100 / release.Size);
                    if (progress != Progress) { Progress = progress; Notify($"正在下载… {Progress}%"); }
                }
                if (total != release.Size) throw new InvalidDataException("下载未完成");
            }
            await UpdateInstaller.VerifyAsync(path, hash, release.Version);
            Notify("下载完成，正在准备重启…");
            return new PreparedUpdate(job, path, hash, release.Version);
        }
        catch (Exception ex)
        {
            if (job != null) { try { File.Delete(Path.Combine(job, "new.exe")); } catch { } }
            AppPaths.Log("下载更新失败：" + ex.Message);
            Notify(ex is IOException or UnauthorizedAccessException or InvalidOperationException ? ex.Message : "下载失败，请稍后重试");
            throw;
        }
        finally { IsBusy = IsDownloading = false; Notify(); }
    }
    public void Dispose() { _lifetime.Cancel(); _http.Dispose(); _lifetime.Dispose(); }
    internal void BeginInstall() { IsBusy = true; Notify("正在保存并重启…"); }
    internal void InstallFailed() { IsBusy = false; Notify("更新失败，请重试"); }
}
