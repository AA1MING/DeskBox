using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskBox;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

internal static class InteractionChecks
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }

    internal static async Task Run(MainWindow main, DeskItem folder, string output, Action<bool, string> check)
    {
        var host = (ItemsControl)main.FindName("ItemsHost");
        var area = (FrameworkElement)main.FindName("ItemsArea");
        var coldItem = new DeskItem { Name = "首次打开", Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe") };
        var coldFolder = new DeskItem { Name = "未访问文件夹", Kind = ItemKind.Group, Children = new() { coldItem } };
        ConfigService.Current.Items.Add(coldFolder);
        bool placeholderDisplayed = false;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) =>
        {
            placeholderDisplayed |= host.Items.Cast<ItemVm>().Any(vm => vm.Model == coldItem && ReferenceEquals(vm.Icon, IconService.Placeholder));
        };
        ((System.Collections.Specialized.INotifyCollectionChanged)host.Items).CollectionChanged += changed;
        main.Navigate(coldFolder.Id);
        for (int i = 0; ConfigService.Current.LastGroupId != coldFolder.Id && i < 60; i++) await Task.Delay(50);
        ((System.Collections.Specialized.INotifyCollectionChanged)host.Items).CollectionChanged -= changed;
        check(ConfigService.Current.LastGroupId == coldFolder.Id && !placeholderDisplayed, "first folder visit prepares local icons before inserting visible items");
        main.Navigate(null); await Task.Delay(200);
        ConfigService.Current.Items.Remove(coldFolder); main.Refresh();
        main.Navigate(folder.Id); await Task.Delay(400);
        var first = (ItemVm)host.Items[0];
        var icon = first.Icon;
        check(icon != null && !ReferenceEquals(icon, IconService.Placeholder), "folder enters with prepared icons");
        main.Navigate(null); await Task.Delay(200);
        main.Navigate(folder.Id);
        check(ReferenceEquals(host.Items[0], first) && ReferenceEquals(first.Icon, icon), "reopening a folder immediately reuses its view model and image");
        main.UpdateLayout();
        check(area.Opacity == 1 && Descendants(host).OfType<Border>().Where(b => b.Tag is ItemVm).All(b => b.Opacity == 1), "cached folder and tiles never restart from transparent");
        main.Navigate(null); await Task.Delay(400);
        var tile = Descendants(host).OfType<Border>().First(b => b.Tag is ItemVm vm && vm.Model.Id == folder.Id);
        var image = Descendants(tile).OfType<Image>().Single();
        var text = Descendants(tile).OfType<TextBlock>().Single();
        var transforms = (TransformGroup)image.RenderTransform;
        var scale = transforms.Children.OfType<ScaleTransform>().Single();
        var lift = transforms.Children.OfType<TranslateTransform>().Single();
        Size size = tile.RenderSize;
        var position = tile.TranslatePoint(new Point(), host);
        var source = image.Source;
        GetCursorPos(out var original);
        try
        {
            var away = ((TextBox)main.FindName("SearchBox")).PointToScreen(new Point(10, 10));
            SetCursorPos((int)away.X, (int)away.Y); await Task.Delay(350);
            Diagnostics.CaptureWindow(main, Path.Combine(output, "hover-before.png"));
            var point = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
            SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(350);
            check(tile.IsMouseOver && scale.ScaleX > 1.1 && scale.ScaleY > 1.1 && lift.Y < -2, "pointer hover smoothly enlarges and lifts the icon");
            check(tile.RenderSize == size && tile.TranslatePoint(new Point(), host) == position && text.RenderTransform.Value.IsIdentity, "hover keeps the grid and label in place");
            check(ReferenceEquals(image.Source, source), "hover animation reuses the existing icon bitmap");
            Diagnostics.CaptureWindow(main, Path.Combine(output, "hover-after.png"));
            SetCursorPos((int)away.X, (int)away.Y); await Task.Delay(400);
            check(Math.Abs(scale.ScaleX - 1) < .001 && Math.Abs(scale.ScaleY - 1) < .001 && Math.Abs(lift.Y) < .001, "pointer exit returns the icon to its original size and position");
            main.Navigate(folder.Id); main.Navigate(null); await Task.Delay(400);
            check(ConfigService.Current.LastGroupId == null && host.Items.Count == ConfigService.Current.Items.Count, "rapid navigation keeps the most recent destination");
        }
        finally { SetCursorPos(original.X, original.Y); }
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
