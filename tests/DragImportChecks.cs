using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskBox;
using DeskBox.Infrastructure;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

internal static class DragImportChecks
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }

    public static async Task Run(MainWindow main, string output, Action<bool, string> check)
    {
        main.CaptureMode = true;
        main.Navigate(null); main.ShowPanel(); await Task.Delay(400);
        var search = (TextBox)main.FindName("SearchBox");
        var settings = (Button)main.FindName("SettingsButton");
        check(Math.Abs(search.ActualHeight - settings.ActualHeight) < .1, "search matches actual toolbar button height");
        var host = (ItemsControl)main.FindName("ItemsHost");
        var destination = Path.Combine(output, "drag-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        async Task<DeskItem> MakeShortcut(string label)
        {
            string stored = label + ".lnk";
            check(await FileWorkQueue.RunAsync(() => ShortcutService.Create(Path.Combine(AppPaths.ItemsDir, stored), notepad, "--deskbox-test")), "create drag fixture on background STA: " + label);
            var item = new DeskItem { Name = label, Kind = ItemKind.Shortcut, StoredName = stored, Target = notepad, Arguments = "--deskbox-test" };
            ConfigService.Current.Items.Add(item); main.Refresh(); main.UpdateLayout();
            // Adding an item can recenter the whole grid. Wait for the layout animation
            // before sampling screen coordinates for the native drag operation.
            await Task.Delay(400);
            return item;
        }
        Border Tile(DeskItem item) => Descendants(host).OfType<Border>().First(b => b.Tag is ItemVm vm && ReferenceEquals(vm.Model, item));
        var target = new Border { Background = Brushes.SteelBlue, Child = new TextBlock { Text = "DeskBox 拖放验证", Margin = new Thickness(12) }, AllowDrop = true };
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        double dpi = VisualTreeHelper.GetDpi(main).DpiScaleX;
        var targetWindow = new Window { Content = target, Width = 200, Height = 140, Left = screen.WorkingArea.Left / dpi + 10, Top = screen.WorkingArea.Top / dpi + 10, WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true, ShowInTaskbar = false };
        DragDropEffects requested = DragDropEffects.Move;
        string? transferred = null;
        target.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? requested : DragDropEffects.None; e.Handled = true; };
        target.Drop += (_, e) =>
        {
            string path = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
            transferred = Path.Combine(destination, Path.GetFileName(path));
            if (requested == DragDropEffects.Move) File.Move(path, transferred); else File.Copy(path, transferred);
            e.Effects = requested; e.Handled = true;
        };
        targetWindow.Show(); await Task.Delay(150);
        try
        {
            var moved = await MakeShortcut("move");
            await OleDrag(main, Tile(moved), (ItemVm)Tile(moved).Tag, target, false);
            check(transferred != null && File.Exists(transferred) && !File.Exists(AppPaths.StoredFilePath(moved)) && !ConfigService.Current.Items.Contains(moved), "native OLE move transfers shortcut and removes its model");
            var resolved = await FileWorkQueue.RunAsync(() => ShortcutService.Resolve(transferred!));
            check(resolved?.Arguments == "--deskbox-test" && string.Equals(resolved.Target, notepad, StringComparison.OrdinalIgnoreCase), "dragged shortcut preserves launch target and arguments");
            requested = DragDropEffects.Copy; transferred = null;
            var copied = await MakeShortcut("copy");
            await OleDrag(main, Tile(copied), (ItemVm)Tile(copied).Tag, target, false);
            check(transferred != null && File.Exists(transferred) && File.Exists(AppPaths.StoredFilePath(copied)) && ConfigService.Current.Items.Contains(copied), "native OLE copy retains the original shortcut and model");
            transferred = null;
            var cancelled = await MakeShortcut("cancel");
            await OleDrag(main, Tile(cancelled), (ItemVm)Tile(cancelled).Tag, target, true);
            check(transferred == null && File.Exists(AppPaths.StoredFilePath(cancelled)) && ConfigService.Current.Items.Contains(cancelled), "cancelled native drag preserves shortcut and model");
            var grouped = await MakeShortcut("group");
            var group = ConfigService.Current.Items.First(i => i.IsGroup);
            await OleDrag(main, Tile(grouped), (ItemVm)Tile(grouped).Tag, Tile(group), false);
            check(group.Children.Contains(grouped) && !ConfigService.Current.Items.Contains(grouped) && File.Exists(AppPaths.StoredFilePath(grouped)), "dual-format drag groups the original model without reimporting its file");
            var optimized = await MakeShortcut("optimized");
            var data = ItemDragService.CreateData(optimized);
            File.Move(AppPaths.StoredFilePath(optimized), Path.Combine(destination, optimized.StoredName!));
            await main.CompleteExternalDragAsync(optimized, data, DragDropEffects.None);
            check(!ConfigService.Current.Items.Contains(optimized), "optimized Shell move reporting None still removes the departed shortcut");
        }
        finally { targetWindow.Close(); }

        string pending = Path.Combine(destination, "pending.lnk");
        check(await FileWorkQueue.RunAsync(() => ShortcutService.Create(pending, notepad)), "create pending import shortcut");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocked = FileWorkQueue.RunAsync(() => { entered.Set(); return release.Wait(TimeSpan.FromSeconds(5)); });
        check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(2))), "background file queue starts");
        var removedGroup = new DeskItem { Name = "pending group", Kind = ItemKind.Group };
        ConfigService.Current.Items.Add(removedGroup);
        try
        {
            typeof(MainWindow).GetMethod("ImportFiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(main, new object[] { new[] { pending }, removedGroup.Children, 0 });
            bool responsive = false;
            await main.Dispatcher.InvokeAsync(() => responsive = true);
            check(responsive && File.Exists(pending), "UI remains responsive while file work is queued");
            ConfigService.Current.Items.Remove(removedGroup);
            var finish = main.FinishFileOperationsAsync();
            check(!finish.IsCompleted, "exit waits for import and configuration persistence");
            release.Set(); await blocked; await finish.WaitAsync(TimeSpan.FromSeconds(5));
            var imported = ConfigService.Current.Items.Single(i => i.Name == "pending");
            check(!File.Exists(pending) && File.Exists(AppPaths.StoredFilePath(imported)) && !string.IsNullOrEmpty(imported.Target), "background shortcut import moves and resolves the file");
            ConfigService.Load();
            check(ConfigService.Current.Items.Any(i => i.Id == imported.Id), "deleted destination falls back to root and persists before exit");
        }
        finally { release.Set(); }
    }

    private static async Task OleDrag(MainWindow main, Border source, ItemVm item, FrameworkElement target, bool cancel)
    {
        GetCursorPos(out var original);
        Point point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
        var clock = Stopwatch.StartNew();
        QueryContinueDragEventHandler continueDrag = (_, e) =>
        {
            e.Action = clock.ElapsedMilliseconds < 300 ? DragAction.Continue : cancel ? DragAction.Cancel : DragAction.Drop;
            e.Handled = true;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        int offset = 0;
        timer.Tick += (_, _) => SetCursorPos((int)point.X + (++offset % 2), (int)point.Y);
        source.QueryContinueDrag += continueDrag;
        timer.Start();
        try { SetCursorPos((int)point.X, (int)point.Y); await main.DragItemAsync(source, item); }
        finally { timer.Stop(); source.QueryContinueDrag -= continueDrag; SetCursorPos(original.X, original.Y); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
