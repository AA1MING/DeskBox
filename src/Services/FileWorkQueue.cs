using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DeskBox.Services;

/// <summary>Serial file work on an STA thread keeps Shell COM and file IO off the UI thread.</summary>
public static class FileWorkQueue
{
    private static readonly BlockingCollection<Action> Jobs = new();
    private static readonly Lazy<Thread> Worker = new(() =>
    {
        var thread = new Thread(() => { foreach (var job in Jobs.GetConsumingEnumerable()) job(); })
        { IsBackground = true, Name = "DeskBox.FileWork" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    });

    public static Task<T> RunAsync<T>(Func<T> action)
    {
        _ = Worker.Value;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Jobs.Add(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }
}
