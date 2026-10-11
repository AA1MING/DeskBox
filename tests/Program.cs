using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using DeskBox;
using DeskBox.Controls;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Theme;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
        _passed++;
    }
    [STAThread]
    public static int Main()
    {
        var output = Path.GetFullPath(Path.Combine("publish", "verification-" + UpdateService.CurrentVersion.ToString(3)));
        Directory.CreateDirectory(output);
        var data = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "config.json"), "{}");
        Environment.SetEnvironmentVariable("DESKBOX_HOME", data);
        AppPaths.Init();
        File.Delete(AppPaths.ConfigFile);
        ConfigService.Load();
        Check(!ConfigService.Current.HasStarted, "new data directory is recognized as first use");
        ThemeService.Refresh();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/DeskBox;component/Theme/styles.xaml") });
        app.Startup += async (_, _) =>
        {
            try
            {
                await Run(output);
                Console.WriteLine($"RESULT {_passed} passed, 0 failed");
                app.Shutdown(0);
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static DeskItem Item(string name, bool group = false) => new()
    {
        Name = name, Kind = group ? ItemKind.Group : ItemKind.File,
        Target = group ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe")
    };
    private static async Task Run(string output)
    {
        await UpdateChecks.Run(Check);
        var original = new[] { Item("微信"), Item("百度"), Item("Chrome"), Item("app10"), Item("App2"), Item("资料", true), Item("Tools", true) };
        foreach (var culture in new[] { "en-US", "zh-CN" })
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var names = NameService.Sort(original, "Name").Select(i => i.Name).ToArray();
            Check(names.SequenceEqual(new[] { "Tools", "资料", "App2", "app10", "Chrome", "百度", "微信" }), "group-first, English-first pinyin/numeric ordering under " + culture);
        }
        Check(NameService.Comparer.Compare("应用2", "应用10") < 0, "natural Chinese numbering");
        Check(NameService.Comparer.Compare("app999999999999999999999", "app1000000000000000000000") < 0, "large numbers do not overflow");
        Check(NameService.Comparer.Compare("APPLE", "apple") == 0, "case-insensitive ordering");
        foreach (var query in new[] { "微信", "weixin", "wx", "WEI XIN" }) Check(NameService.Matches("微信", query), "search " + query);
        Check(NameService.Matches("网易云音乐", "wyyyy"), "polyphonic phrase initials");
        Check(NameService.Matches("重庆银行", "chongqingyinhang"), "polyphonic full pinyin");
        Check(!NameService.Matches("微信", "nonexistent"), "unmatched query");
        var root = Item("应用", true); var nested = Item("工作", true); var wechat = Item("微信");
        nested.Children.Add(wechat); root.Children.Add(nested);
        var tree = new List<DeskItem> { root, Item("微信说明.txt") };
        var result = NameService.Search(tree, "wx").ToArray();
        Check(result.Length == 2 && ReferenceEquals(result[0], wechat), "recursive search retains original model");
        Check(root.Children.Single() == nested && nested.Children.Single() == wechat, "search preserves parent lists");

        ConfigService.Current.Items = original.ToList();
        ConfigService.Current.Items.Add(root);
        ConfigService.Current.SortMode = "Name";
        ConfigService.Current.ThemeMode = "Dark";
        ConfigService.Current.HasStarted = true;
        ConfigService.Save(); ConfigService.Load();
        Check(ConfigService.Current.HasStarted && ConfigService.Current.Items.Count == 8, "startup state and tree persist");
        string legacy = File.ReadAllText(AppPaths.ConfigFile).Replace("\"HasStarted\": true,", "");
        File.WriteAllText(AppPaths.ConfigFile, legacy); ConfigService.Load();
        Check(ConfigService.Current.HasStarted && ConfigService.Current.Items.Count == 8, "upgrade recognizes existing installation without losing data");
        ConfigService.Save();
        ThemeBrushes.Apply();
        var main = new MainWindow { CaptureMode = true };
        main.ShowPanel();
        await Task.Delay(1200);
        Check(main.IsVisible, "first panel show completes");
        var rootBorder = (Border)main.FindName("RootBorder");
        Check(rootBorder.Opacity > .99, "panel fades in completely");
        var settingsButton = (Button)main.FindName("SettingsButton");
        Check(settingsButton.Focus() && settingsButton.IsKeyboardFocused, "toolbar buttons retain keyboard focus");
        Check(settingsButton.FocusVisualStyle == null, "toolbar focus does not create a dotted adorner");
        var host = (ItemsControl)main.FindName("ItemsHost");
        Check(host.Items.Count == 8 && host.Items.Cast<DeskBox.ViewModels.ItemVm>().All(i => i.Icon != null), "icons loaded before initial display");
        Check(host.Items.Cast<DeskBox.ViewModels.ItemVm>().All(i => !ReferenceEquals(i.Icon, IconService.Placeholder)), "initial local icons are fully prepared");
        var search = (TextBox)main.FindName("SearchBox");
        search.Text = "wx"; await Task.Delay(550);
        Check(host.Items.Count == 2, "top search finds nested applications");
        Check(ConfigService.Current.Items.Count == 8 && ConfigService.Current.LastGroupId == null, "search does not persist a results list");
        search.Clear(); await Task.Delay(550);
        main.Navigate(root.Id); await Task.Delay(400);
        Check(host.Items.Count == 1, "open original group");
        search.Text = "weixin"; await Task.Delay(500);
        search.Clear(); await Task.Delay(500);
        Check(host.Items.Count == 1 && ConfigService.Current.LastGroupId == root.Id, "clear search restores original group");
        main.Navigate(null); await Task.Delay(400);
        main.HidePanel(); await Task.Delay(70); main.ShowPanel(); await Task.Delay(500);
        Check(main.IsVisible && rootBorder.Opacity > .99, "rapid hide/show cancels stale hide completion");
        main.HidePanel(); await Task.Delay(500);
        Check(!main.IsVisible, "panel hides after fade completes");
        main.ShowPanel(); await Task.Delay(500);

        foreach (string material in new[] { "Mica", "Acrylic", "Solid", "Acrylic", "Mica", "Solid" })
        {
            object firstViewModel = host.Items[0];
            ConfigService.Current.Backdrop = material;
            ThemeBrushes.Apply(); main.ApplyTheme();
            await Task.Delay(350);
            int hr = Native.DwmGetWindowAttribute(new WindowInteropHelper(main).Handle, 38, out int actual, 4);
            int expected = material == "Mica" ? 2 : material == "Acrylic" ? 3 : 1;
            if (ThemeService.BuildNumber >= 22621 && BackdropService.TransparencyEnabled)
                Check(hr == 0 && actual == expected, "native material switches to " + material);
            var source = (HwndSource)PresentationSource.FromVisual(main);
            Check(source.CompositionTarget.BackgroundColor.A == (material == "Solid" || !BackdropService.TransparencyEnabled ? 255 : 0), "WPF render target alpha for " + material);
            Check(ReferenceEquals(firstViewModel, host.Items[0]), "theme switch reuses existing item models");
            if (material is "Mica" or "Acrylic" or "Solid") Diagnostics.CaptureWindow(main, Path.Combine(output, material.ToLowerInvariant() + ".png"));
        }
        main.HidePanel(); await Task.Delay(400);
        var settings = new SettingsWindow(); settings.Show(); await Task.Delay(700);
        ConfigService.Current.Backdrop = "Acrylic"; ThemeBrushes.Apply(); settings.ApplyTheme(); main.ApplyTheme();
        await Task.Delay(350);
        byte expectedAlpha = BackdropService.TransparencyEnabled ? (byte)0 : (byte)255;
        Check(((HwndSource)PresentationSource.FromVisual(settings)).CompositionTarget.BackgroundColor.A == expectedAlpha, "settings exposes the material or opaque fallback");
        var settingsContent = (ScrollViewer)settings.FindName("SettingsContent");
        Check(settingsContent.Opacity > .99 && settingsContent.ActualHeight > 100, "settings content is visible after entry animation");
        Check(settings.Icon != null && main.Icon != null, "window icons resolve from packaged WPF resources");
        var scrollBar = (ScrollBar)settingsContent.Template.FindName("PART_VerticalScrollBar", settingsContent);
        var track = (Track)scrollBar.Template.FindName("PART_Track", scrollBar);
        var thumb = track.Thumb ?? throw new Exception("settings scrollbar has no thumb");
        Check(scrollBar.Background is SolidColorBrush { Color.A: 0 }, "settings scrollbar uses a draggable thumb and transparent track");
        double oldOffset = settingsContent.VerticalOffset;
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(track.IncreaseRepeatButton, null);
        await Task.Delay(400);
        Check(settingsContent.VerticalOffset > oldOffset, "scrollbar page click advances settings content");
        settingsContent.ScrollToTop(); await Task.Delay(100);
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 50));
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 50, false));
        await Task.Delay(400);
        Check(settingsContent.VerticalOffset > 0, "scrollbar thumb drag advances settings content");
        settingsContent.ScrollToTop(); await Task.Delay(100);
        var thumbFill = (Border)thumb.Template.FindName("ThumbFill", thumb);
        Color darkThumb = ((SolidColorBrush)thumbFill.Background).Color;
        ConfigService.Current.ThemeMode = "Light"; ThemeBrushes.Apply(); settings.ApplyTheme();
        await Task.Delay(400);
        Check(((SolidColorBrush)thumbFill.Background).Color != darkThumb, "scrollbar follows live theme changes");
        Diagnostics.CaptureWindow(settings, Path.Combine(output, "settings-light.png"));
        ConfigService.Current.ThemeMode = "Dark"; ThemeBrushes.Apply(); settings.ApplyTheme();
        await Task.Delay(400);
        Native.DwmGetWindowAttribute(new WindowInteropHelper(settings).Handle, 14, out int cloaked, 4);
        Check(cloaked == 0, "settings window is uncloaked");
        Diagnostics.CaptureWindow(settings, Path.Combine(output, "settings.png"));
        settings.Close(); await Task.Delay(400);
        Check(!settings.IsVisible && SettingsWindow.Current == null, "settings close animation completes");
        var menu = new MaterialMenu { Style = (Style)Application.Current.FindResource("DeskMenuStyle") };
        main.ShowPanel(); await Task.Delay(400);
        menu.PlacementTarget = (Button)main.FindName("SortButton");
        menu.Placement = PlacementMode.Bottom;
        bool invoked = false;
        var openItem = new MenuItem { Header = "打开", Style = (Style)Application.Current.FindResource("DeskMenuItemStyle") };
        openItem.Click += (_, _) => invoked = true;
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuItem { Header = "设置", Style = (Style)Application.Current.FindResource("DeskMenuItemStyle") });
        typeof(MainWindow).GetMethod("OpenMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, new object?[] { menu, null });
        await Task.Delay(500);
        var menuWindow = Application.Current.Windows.Cast<Window>().Single(w => ReferenceEquals(w.Content, menu));
        Check(menuWindow.IsVisible && !menuWindow.AllowsTransparency, "menu uses non-layered material window");
        Check(menuWindow.Owner == main && main.IsVisible, "panel menu retains its owner and panel visibility");
        Check(((HwndSource)PresentationSource.FromVisual(menuWindow)).CompositionTarget.BackgroundColor.A == expectedAlpha, "menu exposes the material or opaque fallback");
        Diagnostics.CaptureWindow(menuWindow, Path.Combine(output, "menu.png"));
        openItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(400);
        Check(invoked && !menu.IsOpen, "menu action fires and dismissal animation completes");
        var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        confirmTimer.Tick += (_, _) =>
        {
            confirmTimer.Stop();
            var dialog = Application.Current.Windows.OfType<InputDialog>().Single();
            ((TextBox)dialog.FindName("InputBox")).Text = "新的名称";
            Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "确定")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        };
        confirmTimer.Start();
        string? renamed = InputDialog.Prompt(null, "名称测试", "名称", "旧名称");
        Check(renamed == "新的名称", "animated input dialog preserves its confirmed result");
        var cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        bool panelStayedVisible = false;
        cancelTimer.Tick += (_, _) =>
        {
            cancelTimer.Stop();
            panelStayedVisible = main.IsVisible;
            Application.Current.Windows.OfType<InputDialog>().Single().Close();
        };
        cancelTimer.Start();
        main.CaptureMode = false;
        var cancelled = typeof(MainWindow).GetMethod("Prompt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, new object[] { "取消测试", "名称", "旧名称" });
        Check(cancelled == null, "title-bar close animates and cancels the input dialog");
        Check(panelStayedVisible, "input dialog does not trigger automatic hiding of its panel owner");
        await DragImportChecks.Run(main, output, Check);
        main.Close();
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
