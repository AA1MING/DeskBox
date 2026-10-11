using System.Diagnostics;
using System.IO;
using DeskBox.Infrastructure;

internal static class ExecutablePathChecks
{
    internal static int Probe(string root)
    {
        File.WriteAllText(Path.Combine(root, "ready"), Environment.ProcessPath);
        for (int i = 0; i < 200; i++)
        {
            if (File.Exists(Path.Combine(root, "go")))
            {
                File.WriteAllText(Path.Combine(root, "resolved"), ExecutablePath.Current());
                return 0;
            }
            Thread.Sleep(50);
        }
        return 1;
    }

    internal static async Task Run(Action<bool, string> check)
    {
        check(string.Equals(ExecutablePath.Current(), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase), "mapped image resolves the current executable");
        string root = Path.Combine(AppPaths.BaseDir, "renamed-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string file in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        string original = Path.Combine(root, "DeskBox.Tests.exe");
        string renamed = Path.Combine(root, "改名后的 DeskBox ' .exe");
        var start = new ProcessStartInfo(original) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        start.ArgumentList.Add("--path-probe"); start.ArgumentList.Add(root);
        using var child = Process.Start(start) ?? throw new Exception("Could not start path probe");
        try
        {
            for (int i = 0; !File.Exists(Path.Combine(root, "ready")) && i < 100 && !child.HasExited; i++) await Task.Delay(50);
            check(File.Exists(Path.Combine(root, "ready")), "isolated rename probe starts");
            File.Move(original, renamed);
            File.WriteAllText(original, "unrelated file occupying the old name");
            File.WriteAllText(Path.Combine(root, "go"), "go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            check(child.ExitCode == 0 && File.ReadAllText(Path.Combine(root, "resolved")) == renamed, "update path follows renamed running image instead of a file at its old name");
            check(File.ReadAllText(original) == "unrelated file occupying the old name", "rename resolution leaves unrelated files unchanged");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }
}
