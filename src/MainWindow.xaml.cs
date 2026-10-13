using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Rectangle = System.Drawing.Rectangle;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ContextMenu = DeskBox.Controls.MaterialMenu;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeskBox.Infrastructure;
using DeskBox.Interop;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Theme;
using DeskBox.ViewModels;

namespace DeskBox;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly ObservableCollection<ItemVm> _items = new ObservableCollection<ItemVm>();

	private string? _currentGroupId;

	private ItemVm? _pressed;

	private Point _dragOrigin;

	private bool _dragged;

	private bool _dialogOpen;

	private bool _externalDialogOpen;

	private bool _menuOpen;
    private int _openMenuCount;

	private bool _pinned;

	private bool _dragActive;

	private DispatcherTimer? _hideWatch;

	private DateTime _watchStart;

	private DateTime? _releasedAt;

	private int _visibilityToken;
    private bool _panelOpen;
    private string _searchQuery = "";
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<string, ItemVm> _viewModels = new();
    private readonly Dictionary<string, Task<ImageSource?>> _iconTasks = new();
    private readonly Dictionary<string, string> _requestedIcons = new();
    private Task _iconsReady = Task.CompletedTask;
    private Task _iconWarmTask = Task.CompletedTask;
    private bool _iconWarmStarted;
    private readonly SemaphoreSlim _iconLoader = new(1, 1);
    private bool _searchComposing;
    private bool _dropOverlayShowing;
    private bool _dragging;
    private bool _internalDropCompleted;
    private bool _acceptingImports = true;
    private int _pendingImports;
    private int _navigationToken;
    private TaskCompletionSource? _importsFinished;

	private Native.LowLevelMouseProc? _mouseHookProc;

	private nint _mouseHook;

	private int _downX;

	private int _downY;

	public bool CaptureMode { get; set; }

	public MainWindow()
	{
		InitializeComponent();
		ItemsHost.ItemsSource = _items;
		_currentGroupId = ConfigService.Current.LastGroupId;
		_pinned = ConfigService.Current.PinnedOpen;
		SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { if (_panelOpen) BackdropService.Cloak(this, true); };
		Deactivated += OnDeactivated;
		Closed += (_, _) => UninstallMouseHook();
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };
        TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox, (_, _) => { _searchComposing = true; _searchTimer.Stop(); });
        TextCompositionManager.AddPreviewTextInputHandler(SearchBox, (_, _) => { _searchComposing = false; _searchTimer.Start(); });
		PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
		ApplyTheme();
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 26 || msg == 794 || msg == 800)
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
			{
				ThemeService.Refresh();
				(System.Windows.Application.Current as App)?.ApplyThemeEverywhere();
			}), (DispatcherPriority)4, Array.Empty<object>());
		}
		return IntPtr.Zero;
	}

	private void OnDeactivated(object? sender, EventArgs e)
	{
        if (_dragging) return;
		if (!_dialogOpen && !_externalDialogOpen && !_menuOpen && !_pinned && !CaptureMode)
		{
			if (Native.IsLeftButtonDown())
			{
				ArmHideWatch();
			}
			else
			{
				HidePanel();
			}
		}
	}

	/// <summary>
	/// 安装全局低级鼠标钩子。用钩子而不是窗口失活来判定“点击面板外部”，
	/// 这样即使窗口在某些系统/全屏程序下拿不到前台焦点，点击外面也能稳定收起。
	/// </summary>
	private void InstallMouseHook()
	{
		if (_mouseHook != IntPtr.Zero) return;
		try
		{
			_mouseHookProc = MouseHookProc;
			_mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseHookProc, Native.GetModuleHandle(null), 0u);
			if (_mouseHook == IntPtr.Zero) AppPaths.Log("安装鼠标钩子失败：" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
		}
		catch (Exception ex)
		{
			_mouseHook = IntPtr.Zero;
			AppPaths.Log("安装鼠标钩子异常：" + ex.Message);
		}
	}

	private void UninstallMouseHook()
	{
		if (_mouseHook == IntPtr.Zero) return;
		try { Native.UnhookWindowsHookEx(_mouseHook); }
		catch (Exception ex) { AppPaths.Log("卸载鼠标钩子异常：" + ex.Message); }
		_mouseHook = IntPtr.Zero;
		_mouseHookProc = null;
	}

	private nint MouseHookProc(int nCode, nint wParam, nint lParam)
	{
		try
		{
			if (nCode >= 0)
			{
				int message = wParam.ToInt32();
				var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
				if (message == Native.WM_LBUTTONDOWN)
				{
					_downX = data.pt.X;
					_downY = data.pt.Y;
				}
				else if (message == Native.WM_LBUTTONUP || message == Native.WM_RBUTTONUP
					|| message == Native.WM_MBUTTONUP || message == Native.WM_XBUTTONUP)
				{
					int x = data.pt.X;
					int y = data.pt.Y;
					bool leftClean = message != Native.WM_LBUTTONUP
						|| (Math.Abs(x - _downX) <= 4 && Math.Abs(y - _downY) <= 4);
					if (leftClean && ShouldCloseForOutsideClick(x, y)) BeginCloseFromOutsideClick();
				}
			}
		}
		catch (Exception ex)
		{
			AppPaths.Log("鼠标钩子处理异常：" + ex.Message);
		}
		return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
	}

	/// <summary>判断某个屏幕坐标是否属于面板本身（面板窗口或它的弹层、子窗口）。</summary>
	private bool ShouldCloseForOutsideClick(int x, int y)
	{
		if (!IsVisible || !_panelOpen || _pinned || CaptureMode || _menuOpen || _dialogOpen || _externalDialogOpen) return false;
		nint hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
		if (hit == IntPtr.Zero) return false;
		nint root = Native.GetAncestor(hit, Native.GA_ROOT);
		if (root == IntPtr.Zero) root = hit;
		if (!OutsideClickWouldClose(root)) return false;
		if (Native.IsPointOnTaskbar(x, y)) return false; // 交给托盘图标自己切换，不要重复收起
		return true;
	}

	/// <summary>
	/// 纯判定：给定命中的顶层窗口句柄，是否应视为“点击面板外部”。
	/// 抽出来是为了不依赖桌面命中测试即可回归验证。
	/// </summary>
	internal bool OutsideClickWouldClose(nint rootHwnd)
	{
		if (!IsVisible || !_panelOpen || _pinned || CaptureMode || _menuOpen || _dialogOpen || _externalDialogOpen) return false;
		if (rootHwnd == IntPtr.Zero) return false;
		nint self = new WindowInteropHelper(this).Handle;
		if (rootHwnd == self) return false;
		foreach (Window owned in OwnedWindows)
		{
			if (owned.IsVisible && rootHwnd == new WindowInteropHelper(owned).Handle) return false;
		}
		return true;
	}

	private void BeginCloseFromOutsideClick()
	{
		Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
		{
			if (_panelOpen && !_pinned && !_menuOpen && !_dialogOpen && !_externalDialogOpen && IsVisible) HidePanel();
		}));
	}

	private void ArmHideWatch()
	{
		_dragActive = false;
		_releasedAt = null;
		_watchStart = DateTime.UtcNow;
		if (_hideWatch == null)
		{
			_hideWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(150.0), (DispatcherPriority)4, (EventHandler)OnHideWatchTick, ((DispatcherObject)this).Dispatcher);
		}
		_hideWatch.Start();
	}

	private void StopHideWatch()
	{
		DispatcherTimer hideWatch = _hideWatch;
		if (hideWatch != null)
		{
			hideWatch.Stop();
		}
		_dragActive = false;
		_releasedAt = null;
	}

	private void OnHideWatchTick(object? sender, EventArgs e)
	{
		if (_dragActive)
		{
			return;
		}
		if (Native.IsLeftButtonDown())
		{
			_releasedAt = null;
			return;
		}
		DateTime valueOrDefault = _releasedAt.GetValueOrDefault();
		if (!_releasedAt.HasValue)
		{
			valueOrDefault = DateTime.UtcNow;
			_releasedAt = valueOrDefault;
		}
		double totalMilliseconds = (DateTime.UtcNow - _releasedAt.Value).TotalMilliseconds;
		double totalSeconds = (DateTime.UtcNow - _watchStart).TotalSeconds;
		if (totalMilliseconds > 500.0 || totalSeconds > 20.0)
		{
			StopHideWatch();
			if (!_pinned)
			{
				HidePanel();
			}
		}
	}

	private void Pin_Click(object sender, RoutedEventArgs e)
	{
		_pinned = !_pinned;
		ConfigService.Current.PinnedOpen = _pinned;
		ConfigService.Save();
		UpdatePinVisual();
	}

	private void UpdatePinVisual()
	{
		PinButton.Content = (_pinned ? "\ue77a" : "\ue718");
		PinButton.ToolTip = (_pinned ? "取消固定" : "固定面板");
		PinButton.Background = (System.Windows.Media.Brush)FindResource(_pinned ? "AccentFill" : "ControlFill");
		PinButton.Foreground = (System.Windows.Media.Brush)FindResource(_pinned ? "AccentText" : "TextFill");
	}

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SearchBox.IsKeyboardFocusWithin) return;
        if (e.OriginalSource is Visual visual && HeaderSearch.IsAncestorOf(visual)) return;
        RootBorder.Focus();
    }

	private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }
        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            ApplySearch();
            if (_items.Count > 0) ActivateItem(_items[0]);
            return;
        }
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			if (SearchBox.Text.Length > 0)
            {
                SearchBox.Clear();
                ApplySearch();
            }
            else if (_currentGroupId != null)
			{
				Navigate(null);
			}
			else
			{
				HidePanel();
			}
		}
	}

	public void ApplyTheme()
	{
		bool isDarkNow = ThemeBrushes.IsDarkNow;
		if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
		{
			BackdropService.Apply(this, ConfigService.Current.Backdrop, isDarkNow);
		}
		UpdatePinVisual();
		Refresh();
        if (IsVisible) PositionPanel();
	}

    public void TogglePanel()
    {
        if (_panelOpen) HidePanel(); else ShowPanel();
    }

    public async void ShowPanel()
    {
        int token = ++_visibilityToken;
        _panelOpen = true;
        StopHideWatch();
        try
        {
            new WindowInteropHelper(this).EnsureHandle();
            bool hidden = !IsVisible;
            if (hidden)
            {
                RootBorder.BeginAnimation(UIElement.OpacityProperty, null);
                RootBorder.Opacity = 1;
                RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                RootSlide.BeginAnimation(TranslateTransform.YProperty, null);
                RootScale.ScaleX = RootScale.ScaleY = Motion.EnterScale;
                RootSlide.Y = Motion.EnterOffset;
                BackdropService.Cloak(this, true);
            }
            Refresh();
            PositionPanel(false);
            await Task.WhenAny(Task.WhenAll(_iconsReady, _iconWarmTask), Task.Delay(3000));
            if (token != _visibilityToken || !_panelOpen || Dispatcher.HasShutdownStarted) return;
            if (hidden) Show();
            await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ContextIdle);
            if (token != _visibilityToken || !_panelOpen) return;
            PositionPanel(false);
            InstallMouseHook();
            Native.DwmFlush();
            BackdropService.Cloak(this, false);
            Topmost = true;
            Activate();
            int enterMs = (int)Motion.EnterDuration.TotalMilliseconds;
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Tween(RootScale.ScaleX, 1, milliseconds: enterMs));
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Tween(RootScale.ScaleY, 1, milliseconds: enterMs));
            RootBorder.BeginAnimation(UIElement.OpacityProperty, Motion.Tween(RootBorder.Opacity, 1, milliseconds: enterMs));
            RootSlide.BeginAnimation(TranslateTransform.YProperty, Motion.Tween(RootSlide.Y, 0, milliseconds: enterMs));
            Focus();
        }
        catch (Exception ex)
        {
            _panelOpen = false;
            BackdropService.Cloak(this, false);
            AppPaths.Log("显示面板失败：" + ex);
        }
    }

    /// <summary>外部窗口（如设置）打开期间冻结自动收起，避免它抢焦点时把面板一起关掉。</summary>
    public void SetExternalDialogOpen(bool open) => _externalDialogOpen = open;

    public void HidePanel()
    {
        ++_navigationToken;
        _panelOpen = false;
        int token = ++_visibilityToken;
        if (!IsVisible) return;
        UninstallMouseHook();
        int exitMs = (int)Motion.ExitDuration.TotalMilliseconds;
        RootBorder.BeginAnimation(UIElement.OpacityProperty, Motion.TweenOut(RootBorder.Opacity, Motion.ExitOpacity, () =>
        {
            if (token == _visibilityToken && !_panelOpen)
            {
                RootBorder.BeginAnimation(UIElement.OpacityProperty, null);
                RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                RootSlide.BeginAnimation(TranslateTransform.YProperty, null);
                Hide();
            }
        }, exitMs));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.TweenOut(RootScale.ScaleX, Motion.ExitScale, milliseconds: exitMs));
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.TweenOut(RootScale.ScaleY, Motion.ExitScale, milliseconds: exitMs));
        RootSlide.BeginAnimation(TranslateTransform.YProperty, Motion.TweenOut(RootSlide.Y, Motion.ExitOffset, milliseconds: exitMs));
    }

    private void PositionPanel(bool ensureVisible = true)
    {
        try
        {
            var point = System.Windows.Forms.Cursor.Position;
            Rectangle work = Screen.FromPoint(point).WorkingArea;
            int width = Math.Min(work.Width, Math.Max(560, (int)(work.Width * ConfigService.Current.WidthRatio)));
            int height = Math.Min(work.Height, Math.Max(400, (int)(work.Height * ConfigService.Current.HeightRatio)));
            var monitor = Native.MonitorFromPoint(new Native.POINT { X = point.X, Y = point.Y }, 2);
            double scale = Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96d : 1;
            Width = width / scale;
            Height = height / scale;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST,
                work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height, 16);
        }
        catch (Exception ex) { AppPaths.Log("定位面板失败：" + ex.Message); }
    }

	private static DeskItem? FindItem(List<DeskItem> list, string id)
	{
		foreach (DeskItem item in list)
		{
			if (item.Id == id)
			{
				return item;
			}
			DeskItem deskItem = FindItem(item.Children, id);
			if (deskItem != null)
			{
				return deskItem;
			}
		}
		return null;
	}

	private DeskItem? CurrentGroup()
	{
		if (_currentGroupId != null)
		{
			return FindItem(ConfigService.Current.Items, _currentGroupId);
		}
		return null;
	}

	private List<DeskItem> CurrentList()
	{
		return CurrentGroup()?.Children ?? ConfigService.Current.Items;
	}

    public async void Navigate(string? groupId)
    {
        int token = ++_navigationToken;
        _searchTimer.Stop();
        bool forward = groupId != null && groupId != _currentGroupId;
        var destination = groupId == null ? ConfigService.Current.Items : FindItem(ConfigService.Current.Items, groupId)?.Children;
        if (destination == null) return;
        // 图标在应用启动时会在后台全部预热；进入分组时再等待一次，确保第一帧就是真实图标。
        await PrepareIconsAsync(destination);

        if (token != _navigationToken || Dispatcher.HasShutdownStarted) return;
        if (groupId != null && FindItem(ConfigService.Current.Items, groupId) == null) return;
        _searchTimer.Stop();
        _searchQuery = "";
        SearchBox.Clear();
        _currentGroupId = groupId;
        ConfigService.Current.LastGroupId = groupId;
        ConfigService.Save();
        Motion.ScrollToTop(Scroll);
        Refresh();
        AnimateContentIn(forward);
    }

    private void AnimateContentIn(bool forward)
    {
        ItemsArea.BeginAnimation(UIElement.OpacityProperty, null);
        ItemsArea.Opacity = 1;
        double from = AreaSlide.HasAnimatedProperties && Math.Abs(AreaSlide.X) > .01 ? AreaSlide.X : forward ? 4 : -4;
        AreaSlide.BeginAnimation(TranslateTransform.XProperty, Motion.Tween(from, 0, milliseconds: 180));
    }

    private void Header_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool narrow = e.NewSize.Width < 640;
        Grid.SetRow(HeaderActions, narrow ? 1 : 0);
        Grid.SetColumn(HeaderActions, narrow ? 0 : 3);
        Grid.SetColumnSpan(HeaderActions, narrow ? 4 : 1);
        HeaderActions.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint == null) return;
        ++_navigationToken;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _searchTimer.Stop();
        if (!_searchComposing) _searchTimer.Start();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        ApplySearch();
        SearchBox.Focus();
    }

    private void ApplySearch()
    {
        _searchTimer.Stop();
        string query = SearchBox.Text.Trim();
        if (query == _searchQuery) return;
        _searchQuery = query;
        Motion.ScrollToTop(Scroll);
        Refresh();
        AnimateContentIn(true);
    }

    private async void Item_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border { Tag: ItemVm { IsGroup: true } vm } tile) return;
        await Task.Delay(120);
        foreach (var child in vm.Model.Children.Take(32).ToArray())
        {
            if (!tile.IsMouseOver || !tile.IsLoaded || Dispatcher.HasShutdownStarted) break;
            await PrepareItem(child).Ready;
        }
    }

    private Task PrepareIconsAsync(IEnumerable<DeskItem> items) =>
        Task.WhenAll(items.Select(model => PrepareItem(model).Ready));

    private (ItemVm Vm, Task Ready) PrepareItem(DeskItem model)
    {
        var config = ConfigService.Current;
        int size = Math.Clamp(config.IconSize, 32, 96);
        var color = ThemeBrushes.IsDarkNow ? Colors.White : System.Windows.Media.Color.FromRgb(26, 26, 26);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!_viewModels.TryGetValue(model.Id, out var vm) || !ReferenceEquals(vm.Model, model))
            _viewModels[model.Id] = vm = new ItemVm(model, IconService.Placeholder, size, size + 46, size + 46, config.ShowLabels);
        vm.UpdateLayout(size, config.ShowLabels);
        string iconKey = IconKey(model, size, dpi, config, color);
        if (!_iconTasks.TryGetValue(iconKey, out var task))
            _iconTasks[iconKey] = task = LoadIconAsync(model, size, config.MonochromeIcons, color, dpi);
        _requestedIcons[model.Id] = iconKey;
        // 图标已经准备好时同步落到视图模型上，文件夹切换时不会再先显示占位图。
        if (task.IsCompletedSuccessfully && task.Result != null)
        {
            vm.SetIcon(task.Result);
            return (vm, Task.CompletedTask);
        }
        return (vm, UpdateIconAsync(vm, task, iconKey));
    }

    private static string IconKey(DeskItem model, int size, double dpi, AppConfig config, Color color) =>
        $"{model.Id}|{model.StoredName}|{model.Target}|{size}|{dpi}|{config.MonochromeIcons}|{(config.MonochromeIcons ? color.ToString() : "color")}";

    /// <summary>
    /// 启动后一次性把收纳盒里所有分组的图标全部准备好，之后打开面板或进入任何文件夹都无需等待。
    /// 图标提取在 <see cref="LoadIconAsync"/> 里已经切到线程池，这里只在 UI 线程上编排，避免字典并发访问。
    /// </summary>
    public Task WarmAllIconsAsync()
    {
        if (_iconWarmStarted) return _iconWarmTask;
        _iconWarmStarted = true;
        _iconWarmTask = WarmAllIconsAsyncCore();
        return _iconWarmTask;
    }

    private async Task WarmAllIconsAsyncCore()
    {
        try
        {
            foreach (var model in FlattenItems(ConfigService.Current.Items))
            {
                if (Dispatcher.HasShutdownStarted) return;
                await PrepareItem(model).Ready;
            }
        }
        catch (Exception ex) { AppPaths.Log("预热图标失败：" + ex.Message); }
    }

    private static IEnumerable<DeskItem> FlattenItems(IEnumerable<DeskItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenItems(item.Children)) yield return child;
        }
    }

    public void Refresh()
    {
        if (ItemsHost == null) return;
        AppConfig config = ConfigService.Current;
        bool searching = _searchQuery.Length > 0;
        var desired = searching ? NameService.Sort(NameService.Search(config.Items, _searchQuery), "Name") : NameService.Sort(CurrentList(), config.SortMode);
        var positions = new Dictionary<string, Point>();
        if (IsVisible)
            for (int i = 0; i < _items.Count; i++)
                if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement visual)
                    positions[_items[i].Model.Id] = visual.TranslatePoint(new Point(), ItemsHost);
        var ids = desired.Select(i => i.Id).ToHashSet();
        for (int i = _items.Count - 1; i >= 0; i--) if (!ids.Contains(_items[i].Model.Id)) _items.RemoveAt(i);
        var loads = new List<Task>();
        for (int index = 0; index < desired.Count; index++)
        {
            var (vm, ready) = PrepareItem(desired[index]);
            int previous = _items.IndexOf(vm);
            if (previous < 0) _items.Insert(index, vm); else if (previous != index) _items.Move(previous, index);
            loads.Add(ready);
        }
        _iconsReady = Task.WhenAll(loads);
        BackButton.Visibility = !searching && _currentGroupId != null ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = searching ? "搜索结果" : CurrentGroup()?.Name ?? "收纳盒";
        SubtitleText.Text = _items.Count == 0 ? "" : $"{_items.Count} 项";
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = searching ? "没有找到匹配的项目" : "拖入文件或快捷方式";
        EmptyDetail.Text = searching ? "试试名称、拼音或首字母" : "文件将移动到收纳目录";
        SetStatus(!AppPaths.DataDirUsable ? "数据目录不可写，请重新启动。" : AppPaths.Degraded ? "当前使用临时数据目录。" : "");
        if (positions.Count > 0)
        {
            ItemsHost.UpdateLayout();
            for (int i = 0; i < _items.Count; i++)
                if (positions.TryGetValue(_items[i].Model.Id, out var old) && ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement visual)
                {
                    visual.RenderTransform = Transform.Identity;
                    var current = visual.TranslatePoint(new Point(), ItemsHost);
                    var move = new TranslateTransform(old.X - current.X, old.Y - current.Y);
                    visual.RenderTransform = move;
                    move.BeginAnimation(TranslateTransform.XProperty, Motion.Tween(move.X, 0));
                    move.BeginAnimation(TranslateTransform.YProperty, Motion.Tween(move.Y, 0));
                }
        }
    }

    private async Task UpdateIconAsync(ItemVm vm, Task<ImageSource?> task, string key)
    {
        try
        {
            var icon = await task;
            if (icon != null && !Dispatcher.HasShutdownStarted && _requestedIcons.TryGetValue(vm.Model.Id, out var requested) && requested == key) vm.SetIcon(icon);
        }
        catch (Exception ex) { AppPaths.Log("加载图标失败：" + ex.Message); }
    }

    private async Task<ImageSource?> LoadIconAsync(DeskItem model, int size, bool mono, System.Windows.Media.Color color, double dpi)
    {
        await _iconLoader.WaitAsync();
        try { return await Task.Run(() => IconService.GetForItem(model, size, mono, color, dpi)); }
        finally { _iconLoader.Release(); }
    }

    private void ApplySort(string mode)
    {
        ConfigService.Current.SortMode = mode;
        SortTree(ConfigService.Current.Items, mode);
        ConfigService.Save();
        Refresh();
    }

    private static void SortTree(List<DeskItem> items, string mode)
    {
        var sorted = NameService.Sort(items, mode);
        items.Clear(); items.AddRange(sorted);
        foreach (var item in items) SortTree(item.Children, mode);
    }

	private void RemoveFromTree(DeskItem target)
	{
		Remove(ConfigService.Current.Items, target);
		static bool Remove(List<DeskItem> list, DeskItem deskItem)
		{
			if (list.Remove(deskItem))
			{
				return true;
			}
			foreach (DeskItem item in list)
			{
				if (item.Children.Count > 0 && Remove(item.Children, deskItem))
				{
					return true;
				}
			}
			return false;
		}
	}

	private static bool ContainsItem(DeskItem root, string id)
	{
		foreach (DeskItem child in root.Children)
		{
			if (child.Id == id || ContainsItem(child, id))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsAcceptable(System.Windows.IDataObject data)
	{
		if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return data.GetDataPresent("DeskBox.ItemId");
		}
		return true;
	}

	private void ClearDropTargets()
	{
		foreach (ItemVm item in _items)
		{
			item.IsDropTarget = false;
		}
        _dropOverlayShowing = false;
        if (DropOverlay.Visibility == Visibility.Visible)
            Motion.Fade(DropOverlay, 0, () => { if (!_dropOverlayShowing) DropOverlay.Visibility = Visibility.Collapsed; });
	}

	private void SetDropTarget(ItemVm target)
	{
		foreach (ItemVm item in _items)
		{
			item.IsDropTarget = item == target;
		}
	}

	private void Area_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		bool flag = IsAcceptable(e.Data) && (_searchQuery.Length == 0 || ItemDragService.IsExternalFileDrop(e.Data));
		e.Effects = (flag ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None);
		e.Handled = true;
		if (flag)
		{
			_dragActive = true;
		}
		if (!flag || !ItemDragService.IsExternalFileDrop(e.Data))
		{
			return;
		}
        if (!_dropOverlayShowing)
        {
            _dropOverlayShowing = true;
            DropOverlay.Visibility = Visibility.Visible;
            Motion.Fade(DropOverlay, 1);
        }
		foreach (ItemVm item in _items)
		{
			item.IsDropTarget = false;
		}
	}

	private void Area_DragLeave(object sender, System.Windows.DragEventArgs e)
	{
		Point position = e.GetPosition(ItemsArea);
		if (position.X < 0.0 || position.Y < 0.0 || position.X > ItemsArea.ActualWidth || position.Y > ItemsArea.ActualHeight)
		{
			ClearDropTargets();
			_dragActive = false;
		}
	}

	private void Area_Drop(object sender, System.Windows.DragEventArgs e)
	{
		ClearDropTargets();
		StopHideWatch();
		e.Handled = true;
        if (_searchQuery.Length > 0 && !ItemDragService.IsExternalFileDrop(e.Data)) return;
		if (ItemDragService.IsExternalFileDrop(e.Data))
		{
			ImportFiles((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop), CurrentList(), CurrentList().Count);
		}
		else if (e.Data.GetDataPresent("DeskBox.ItemId"))
		{
            _internalDropCompleted = true;
			string id = (string)e.Data.GetData("DeskBox.ItemId");
			DeskItem deskItem = FindItem(ConfigService.Current.Items, id);
			if (deskItem != null)
			{
				RemoveFromTree(deskItem);
				CurrentList().Add(deskItem);
				ConfigService.Current.SortMode = "Manual";
                Persist();
			}
		}
	}

	private void Item_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		if (sender is Border { Tag: ItemVm tag })
		{
			bool flag = IsAcceptable(e.Data) && (_searchQuery.Length == 0 || tag.IsGroup);
			e.Effects = (flag ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None);
			e.Handled = true;
			if (flag)
			{
				_dragActive = true;
				SetDropTarget(tag);
			}
		}
	}

	private void Item_DragLeave(object sender, System.Windows.DragEventArgs e)
	{
		ClearDropTargets();
	}

	private void Item_Drop(object sender, System.Windows.DragEventArgs e)
	{
		if (!(sender is Border { Tag: ItemVm tag } border))
		{
			return;
		}
		e.Handled = true;
		ClearDropTargets();
		StopHideWatch();
		Point position = e.GetPosition(border);
		bool flag = tag.IsGroup && position.X > border.ActualWidth * 0.26 && position.X < border.ActualWidth * 0.74;
		if (_searchQuery.Length > 0 && !flag) return;
		bool after = !flag && position.X > border.ActualWidth / 2.0;
		List<DeskItem> list = (flag ? tag.Model.Children : CurrentList());
		if (ItemDragService.IsExternalFileDrop(e.Data))
		{
			string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
			int index = (flag ? list.Count : IndexFor(list, tag.Model, after));
			ImportFiles(files, list, index);
		}
		else
		{
			if (!e.Data.GetDataPresent("DeskBox.ItemId"))
			{
				return;
			}
            _internalDropCompleted = true;
			string id = (string)e.Data.GetData("DeskBox.ItemId");
			DeskItem deskItem = FindItem(ConfigService.Current.Items, id);
			if (deskItem != null && deskItem != tag.Model && (!deskItem.IsGroup || !ContainsItem(deskItem, tag.Model.Id)))
			{
				RemoveFromTree(deskItem);
				if (flag)
				{
					list.Add(deskItem);
				}
				else
				{
					list.Insert(IndexFor(list, tag.Model, after), deskItem);
				}
				ConfigService.Current.SortMode = "Manual";
                Persist();
			}
		}
	}

	private static int IndexFor(List<DeskItem> list, DeskItem target, bool after)
	{
		int num = list.IndexOf(target);
		if (num < 0)
		{
			return list.Count;
		}
		return Math.Clamp(after ? (num + 1) : num, 0, list.Count);
	}

    private async void ImportFiles(string[] files, List<DeskItem> destination, int index)
    {
        if (!_acceptingImports) return;
        if (++_pendingImports == 1) _importsFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SetStatus("正在收纳…");
        try
        {
            var result = await FileWorkQueue.RunAsync(() =>
            {
                ItemStore.ResetImportStats();
                var items = ItemStore.ImportAll(files);
                return (Items: items, Copied: ItemStore.CopiedInsteadOfMoved);
            });
            if (result.Items.Count == 0) { SetStatus("收纳失败，请检查文件占用或访问权限。"); return; }
            if (!ContainsList(ConfigService.Current.Items, destination)) destination = ConfigService.Current.Items;
            destination.InsertRange(Math.Clamp(index, 0, destination.Count), result.Items);
            // 新收纳进来的文件先在后台补全图标，避免首次显示占位图。
            foreach (var model in result.Items) _ = PrepareItem(model).Ready;
            Persist();
            SetStatus(result.Copied > 0 ? $"已收纳 {result.Items.Count} 项；{result.Copied} 项仅复制，原文件保留。" : $"已移动收纳 {result.Items.Count} 项。");
        }
        catch (Exception ex)
        {
            AppPaths.Log("收纳失败：" + ex);
            SetStatus("收纳失败，请检查文件占用或访问权限。");
        }
        finally
        {
            if (--_pendingImports == 0) _importsFinished?.TrySetResult();
            else SetStatus("正在收纳…");
        }
    }

    private static bool ContainsList(List<DeskItem> root, List<DeskItem> target) =>
        ReferenceEquals(root, target) || root.Any(item => ContainsList(item.Children, target));

    public Task FinishFileOperationsAsync()
    {
        _acceptingImports = false;
        return _importsFinished?.Task ?? Task.CompletedTask;
    }

    internal void ResumeFileOperations() => _acceptingImports = true;

	private void Persist()
	{
        if (ConfigService.Current.SortMode != "Manual") SortTree(ConfigService.Current.Items, ConfigService.Current.SortMode);
        ConfigService.Save();
		Refresh();
	}

	private void SetStatus(string text)
	{
		StatusText.Text = text;
		FooterPanel.Visibility = (string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void Item_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is Border { Tag: ItemVm tag })
		{
			_pressed = tag;
			_dragOrigin = e.GetPosition(this);
			_dragged = false;
		}
	}

	private void Item_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_pressed == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Point position = e.GetPosition(this);
		if (Math.Abs(position.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		ItemVm pressed = _pressed;
		_pressed = null;
		_dragged = true;
        _ = DragItemAsync((FrameworkElement)sender, pressed);
    }

    internal async Task DragItemAsync(FrameworkElement source, ItemVm item)
    {
        if (_dragging) return;
        _dragging = true;
        _internalDropCompleted = false;
        StopHideWatch();
        var data = ItemDragService.CreateData(item.Model);
		try
		{
            var allowed = ItemDragService.FilePath(data) != null ? System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.Move;
            var result = DragDrop.DoDragDrop(source, data, allowed);
            if (!_internalDropCompleted) await CompleteExternalDragAsync(item.Model, data, result);
		}
		catch (Exception ex)
		{
			AppPaths.Log("拖动失败：" + ex.Message);
		}
        finally
        {
            _dragging = false;
            ClearDropTargets();
            StopHideWatch();
            if (!IsActive && !_pinned && !CaptureMode && !_externalDialogOpen) HidePanel();
        }
	}

    internal async Task CompleteExternalDragAsync(DeskItem item, System.Windows.IDataObject data, System.Windows.DragDropEffects result)
    {
        string? path = ItemDragService.FilePath(data);
        if (path == null || !string.Equals(path, AppPaths.StoredFilePath(item), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(path) && ItemDragService.ReportsMove(data, result))
            for (int attempt = 0; attempt < 20 && File.Exists(path); attempt++) await Task.Delay(25);
        // Optimized Shell moves can report None; only remove the model after the file actually leaves.
        if (!File.Exists(path)) { RemoveFromTree(item); Persist(); }
    }

	private void Item_MouseUp(object sender, MouseButtonEventArgs e)
	{
		ItemVm pressed = _pressed;
		bool dragged = _dragged;
		_pressed = null;
		_dragged = false;
		if (!((pressed == null) | dragged))
		{
			ActivateItem(pressed);
		}
	}

	private void ActivateItem(ItemVm vm)
	{
		if (vm.IsGroup)
		{
			Navigate(vm.Model.Id);
			return;
		}
		vm.Model.ClickCount++;
		ConfigService.Save();
		HidePanel();
		ItemStore.Open(vm.Model);
	}

	private void Item_RightButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is Border { Tag: ItemVm tag })
		{
			e.Handled = true;
			ShowItemMenu(tag);
		}
	}

	private void Area_RightButtonUp(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		ContextMenu contextMenu = CreateMenu();
		AddMenuItem(contextMenu, "新建文件夹", () =>
		{
			string text = Prompt("新建文件夹", "文件夹名称", "新建文件夹");
			if (!string.IsNullOrWhiteSpace(text))
			{
				CurrentList().Add(new DeskItem
				{
					Name = text.Trim(),
					Kind = ItemKind.Group
				});
				Persist();
			}
		});
		AddMenuItem(contextMenu, "一键排序…", () =>
		{
			ShowSortMenu(null);
		});
		AddMenuItem(contextMenu, "设置…", () =>
		{
			(System.Windows.Application.Current as App)?.OpenSettings();
		});
		contextMenu.Items.Add(new Separator());
		AddMenuItem(contextMenu, "打开数据文件夹", OpenDataFolder);
		OpenMenu(contextMenu, e.GetPosition(this));
	}

	private void ShowItemMenu(ItemVm vm)
	{
		ContextMenu contextMenu = CreateMenu();
		if (vm.IsGroup)
		{
			AddMenuItem(contextMenu, "打开", () =>
			{
				Navigate(vm.Model.Id);
			});
			contextMenu.Items.Add(new Separator());
			AddMenuItem(contextMenu, "重命名", () =>
			{
				RenameItem(vm);
			});
			AddMenuItem(contextMenu, "删除", () =>
			{
				DeleteItem(vm);
			});
		}
		else
		{
			AddMenuItem(contextMenu, "打开", () =>
			{
				HidePanel();
				ItemStore.Open(vm.Model);
			});
			AddMenuItem(contextMenu, "以管理员身份运行", () =>
			{
				HidePanel();
				ItemStore.OpenAsAdministrator(vm.Model);
			});
			AddMenuItem(contextMenu, "打开文件所在位置", () =>
			{
				ItemStore.OpenContainingFolder(vm.Model);
			});
			contextMenu.Items.Add(new Separator());
			AddMenuItem(contextMenu, "重命名", () =>
			{
				RenameItem(vm);
			});
			AddMenuItem(contextMenu, "导出到桌面", () =>
			{
				ItemStore.ExportToDesktop(vm.Model);
			});
			AddMenuItem(contextMenu, "删除到回收站", () =>
			{
				DeleteItem(vm);
			});
			contextMenu.Items.Add(new Separator());
			AddMenuItem(contextMenu, "属性", () =>
			{
				ItemStore.ShowProperties(vm.Model);
			});
		}
		OpenMenu(contextMenu, null);
	}

	private void RenameItem(ItemVm vm)
	{
		string text = Prompt("重命名", "新名称", vm.Model.Name);
		if (!string.IsNullOrWhiteSpace(text))
		{
			vm.Model.Name = text.Trim();
			Persist();
		}
	}

	private void DeleteItem(ItemVm vm)
	{
		string message = (vm.IsGroup ? ("确定要删除文件夹“" + vm.Model.Name + "”以及里面的全部项目吗？（文件会进入回收站）") : ("确定要把“" + vm.Model.Name + "”删除到回收站吗？"));
		if (Confirm(message))
		{
			void Remove()
            {
                DeleteFilesRecursive(vm.Model);
                RemoveFromTree(vm.Model);
                Persist();
            }
            var container = ItemsHost.ItemContainerGenerator.ContainerFromItem(vm) as UIElement;
            if (container != null) Motion.Fade(container, 0, Remove); else Remove();
		}
	}

	private static void DeleteFilesRecursive(DeskItem item)
	{
		foreach (DeskItem child in item.Children)
		{
			DeleteFilesRecursive(child);
		}
		string text = AppPaths.StoredFilePath(item);
		if (!string.IsNullOrEmpty(text) && (File.Exists(text) || Directory.Exists(text)))
		{
			RecycleBin.Delete(text);
		}
	}

    private string? Prompt(string title, string label, string defaultValue)
    {
        _dialogOpen = true;
        try { return InputDialog.Prompt(this, title, label, defaultValue); }
        finally
        {
            _dialogOpen = false;
            if (IsVisible) Activate();
        }
    }

	private bool Confirm(string message)
	{
		_dialogOpen = true;
		try
		{
			return System.Windows.MessageBox.Show(this, message, "DeskBox", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
		}
		finally
		{
			_dialogOpen = false;
		}
	}

	private void OpenDataFolder()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "\"" + AppPaths.BaseDir + "\"",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppPaths.Log("打开数据目录失败：" + ex.Message);
		}
	}

	private ContextMenu CreateMenu()
	{
		return new ContextMenu
		{
			Style = (Style)FindResource("DeskMenuStyle"),
			Placement = PlacementMode.MousePoint
		};
	}

	private void AddMenuItem(ContextMenu menu, string header, Action action, bool enabled = true)
	{
		MenuItem menuItem = new MenuItem
		{
			Header = header,
			Style = (Style)FindResource("DeskMenuItemStyle"),
			IsEnabled = enabled
		};
		menuItem.Click += delegate
		{
			action();
		};
		menu.Items.Add(menuItem);
	}

	private void OpenMenu(ContextMenu menu, Point? position)
	{
        menu.PlacementTarget ??= this;
		if (position.HasValue)
		{
			menu.Placement = PlacementMode.Relative;
			Point value = position.Value;
			menu.HorizontalOffset = value.X;
			value = position.Value;
			menu.VerticalOffset = value.Y;
			menu.PlacementTarget = this;
		}
		_openMenuCount++;
        _menuOpen = true;
		menu.Closed += delegate
		{
			_openMenuCount--;
            _menuOpen = _openMenuCount > 0;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (!_menuOpen && !_externalDialogOpen && !IsActive && !_pinned && !_dialogOpen && !CaptureMode &&
                    !OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) HidePanel();
            }));
		};
		menu.IsOpen = true;
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		Navigate(null);
	}

	private void NewFolder_Click(object sender, RoutedEventArgs e)
	{
		string text = Prompt("新建文件夹", "文件夹名称", "新建文件夹");
		if (!string.IsNullOrWhiteSpace(text))
		{
			CurrentList().Add(new DeskItem
			{
				Name = text.Trim(),
				Kind = ItemKind.Group
			});
			Persist();
		}
	}

	private void Sort_Click(object sender, RoutedEventArgs e)
	{
		ShowSortMenu(SortButton);
	}

	private void ShowSortMenu(UIElement? target)
	{
		ContextMenu menu = CreateMenu();
		AddMode("Manual", "手动排序");
		AddMode("Name", "按名称（字母 / 拼音）");
		AddMode("Frequent", "按使用频率");
		if (target != null)
		{
			menu.PlacementTarget = target;
			menu.Placement = PlacementMode.Bottom;
			OpenMenu(menu, null);
		}
		else
		{
			OpenMenu(menu, Mouse.GetPosition(this));
		}
		void AddMode(string key, string header)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = header,
				IsCheckable = true,
				IsChecked = string.Equals(ConfigService.Current.SortMode, key, StringComparison.OrdinalIgnoreCase),
				Style = (Style)FindResource("DeskMenuItemStyle")
			};
			menuItem.Click += delegate
			{
				ApplySort(key);
			};
			menu.Items.Add(menuItem);
		}
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		(System.Windows.Application.Current as App)?.OpenSettings();
	}

	public void LoadDemoData()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		string system32 = Path.Combine(folderPath, "System32");
		DeskItem deskItem = new DeskItem
		{
			Name = "游戏",
			Kind = ItemKind.Group
		};
		deskItem.Children.Add(Make("dxdiag.exe"));
		deskItem.Children.Add(Make("charmap.exe"));
		DeskItem deskItem2 = new DeskItem
		{
			Name = "常用工具",
			Kind = ItemKind.Group
		};
		deskItem2.Children.Add(Make("calc.exe"));
		deskItem2.Children.Add(Make("notepad.exe"));
		deskItem2.Children.Add(Make("cmd.exe"));
		ConfigService.Current.Items.Add(Make("notepad.exe"));
		ConfigService.Current.Items.Add(Make("calc.exe"));
		ConfigService.Current.Items.Add(Make("charmap.exe"));
		ConfigService.Current.Items.Add(Make("magnify.exe"));
		ConfigService.Current.Items.Add(Make("cmd.exe"));
		ConfigService.Current.Items.Add(Make("dxdiag.exe"));
		ConfigService.Current.Items.Add(Make("cleanmgr.exe"));
		ConfigService.Current.Items.Add(deskItem);
		ConfigService.Current.Items.Add(deskItem2);
		_currentGroupId = null;
		DeskItem Make(string fileName, ItemKind kind = ItemKind.File)
		{
			string target = Path.Combine(system32, fileName);
			return new DeskItem
			{
				Name = Path.GetFileNameWithoutExtension(fileName),
				Kind = kind,
				Target = target
			};
		}
	}

	public void CaptureTo(string path)
	{
		Diagnostics.CaptureWindow(this, path);
	}
}
