using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DeskBox.Infrastructure;
using DeskBox.Services;

internal static class UpdateChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token);
    }
    private static string Fixture(string tag = "v9.1.0", string? digest = null, bool prerelease = false, long size = 1024) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft = false, prerelease,
        assets = new object[] {
            new { name = "DeskBox.exe", size, digest = digest ?? "sha256:" + new string('a', 64), browser_download_url = $"https://github.com/AA1MING/DeskBox/releases/download/{tag}/DeskBox.exe" },
            new { name = "SHA256SUMS.txt", browser_download_url = $"https://github.com/AA1MING/DeskBox/releases/download/{tag}/SHA256SUMS.txt" }
        }
    });
    private static void Reject(Action action, Action<bool, string> check, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, name);
    }
    public static async Task Run(Action<bool, string> check)
    {
        check(UpdateService.ParseRelease(Fixture(), new Version(1, 2, 0))?.Version == new Version(9, 1, 0, 0), "release version and GitHub assets parse");
        check(UpdateService.ParseRelease(Fixture("v1.2.0"), new Version(1, 2, 0)) == null, "same version is not an update");
        check(UpdateService.ParseRelease(Fixture("v1.1.1"), new Version(1, 2, 0)) == null, "older release never downgrades");
        check(UpdateService.ParseRelease(Fixture("v1.10.0"), new Version(1, 9, 0)) != null, "version comparison is numeric");
        check(UpdateService.ParseRelease(Fixture(prerelease: true), new Version(1, 2, 0)) == null, "prerelease is excluded");
        Reject(() => UpdateService.ParseVersion("v1.3.0-beta"), check, "invalid stable tag rejected");
        Reject(() => UpdateService.ParseRelease(Fixture().Replace("https://github.com/AA1MING/DeskBox/", "https://example.com/"), new Version(1, 2, 0)), check, "foreign download URLs rejected");
        Reject(() => UpdateService.ParseRelease(Fixture(digest: "sha256:bad"), new Version(1, 2, 0)), check, "malformed asset digest rejected");
        string fallback = Fixture().Replace("\"sha256:" + new string('a', 64) + "\"", "null");
        check(UpdateService.ParseRelease(fallback, new Version(1, 2, 0)) is { Sha256: null, Checksums: not null }, "checksum manifest supports releases without digest");
        string sum = new string('A', 64);
        check(UpdateService.ParseChecksums(sum + "  DeskBox.exe\r\n") == sum.ToLowerInvariant(), "checksum file parses exact executable");
        Reject(() => UpdateService.ParseChecksums(sum + "  other.exe"), check, "unrelated checksum is rejected");
        Reject(() => UpdateService.ParseChecksums(sum + "  DeskBox.exe\n" + sum + "  DeskBox.exe"), check, "duplicate checksums rejected");

        int requests = 0;
        var reply = new TaskCompletionSource<HttpResponseMessage>();
        using (var service = new UpdateService(new HttpClient(new Handler((request, _) =>
        {
            requests++;
            check(request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/AA1MING/DeskBox/releases/latest", "checks only configured repository");
            check(request.Headers.UserAgent.Count > 0 && request.Headers.Authorization == null, "public update check requires no user token");
            return reply.Task;
        }))))
        {
            var first = service.CheckAsync();
            await service.CheckAsync();
            check(service.IsBusy && requests == 1, "simultaneous checks are deduplicated");
            reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture()) });
            await first;
            check(!service.IsBusy && service.Available?.Tag == "v9.1.0", "successful check exposes available version");
        }
        using (var service = new UpdateService(new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))))))
        {
            await service.CheckAsync();
            check(!service.IsBusy && service.Available == null && service.Status.Contains("失败"), "GitHub rate limit leaves UI available for retry");
        }

        string assembly = typeof(DeskBox.App).Assembly.Location;
        using (var input = File.OpenRead(assembly))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
            await UpdateInstaller.VerifyAsync(assembly, hash, UpdateService.CurrentVersion);
            check(true, "real application metadata and hash validate");
            bool badHash = false, badVersion = false;
            try { await UpdateInstaller.VerifyAsync(assembly, new string('0', 64), UpdateService.CurrentVersion); } catch (InvalidDataException) { badHash = true; }
            try { await UpdateInstaller.VerifyAsync(assembly, hash, new Version(9, 9, 9, 0)); } catch (InvalidDataException) { badVersion = true; }
            check(badHash && badVersion, "corrupted downloads and wrong program versions rejected");
        }
        byte[] payload = await File.ReadAllBytesAsync(assembly);
        string payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string actualTag = "v" + UpdateService.CurrentVersion.ToString(3);
        foreach (string mode in new[] { "valid", "corrupt", "short", "inconsistent" })
        {
            string metadata = Fixture(actualTag, "sha256:" + payloadHash, size: payload.Length);
            using var service = new UpdateService(new HttpClient(new Handler((request, _) =>
            {
                HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("/latest") ? new StringContent(metadata) :
                    request.RequestUri.AbsolutePath.EndsWith("/SHA256SUMS.txt") ? new StringContent((mode == "inconsistent" ? new string('0', 64) : payloadHash) + "  DeskBox.exe") :
                    new ByteArrayContent(mode == "short" ? payload[..^1] : mode == "corrupt" ? new byte[payload.Length] : payload);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            })), new Version(0, 1, 0), () => new UpdateEnvironment(Path.Combine(AppPaths.BaseDir, "DeskBox.exe"), AppPaths.BaseDir, true));
            await service.CheckAsync();
            if (mode == "valid")
            {
                var download = await service.DownloadAsync();
                check(File.Exists(download.Download) && download.Sha256 == payloadHash && service.Progress == 100 && !service.IsBusy, "download validates bytes, manifest, metadata and progress");
            }
            else
            {
                bool rejected = false;
                try { await service.DownloadAsync(); } catch (InvalidDataException) { rejected = true; }
                check(rejected && !service.IsBusy && !service.IsDownloading, "download rejects " + mode + " and allows retry");
            }
        }
        string job = Path.Combine(AppPaths.BaseDir, "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        string manifest = Path.Combine(job, "plan.json");
        var plan = new UpdatePlan(999999, 0, Path.Combine(job, "DeskBox.exe"), Path.Combine(job, "new.exe"), new string('a', 64), "1.2.0", AppPaths.BaseDir);
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan));
        check(UpdateInstaller.ReadPlan(manifest) == plan, "update plan preserves target and data directory");
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan with { Download = Path.Combine(job, "..", "other.exe") }));
        Reject(() => UpdateInstaller.ReadPlan(manifest), check, "update plan rejects download path escape");
        ConfigService.Current.CheckUpdatesOnStartup = false;
        ConfigService.Save(); ConfigService.Load();
        check(!ConfigService.Current.CheckUpdatesOnStartup, "startup update preference persists");
        string config = File.ReadAllText(AppPaths.ConfigFile).Replace("\"CheckUpdatesOnStartup\": false,", "");
        File.WriteAllText(AppPaths.ConfigFile, config); ConfigService.Load();
        check(ConfigService.Current.CheckUpdatesOnStartup, "existing configuration enables startup checks by default");
    }
}
