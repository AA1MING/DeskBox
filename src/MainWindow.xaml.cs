using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Rectangle = System.Drawing.Rectangle;
using System.IO;
using System.Linq;
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
	private const string InternalFormat = "DeskBox.ItemId";

	private readonly ObservableCollection<ItemVm> _items = new ObservableCollection<ItemVm>();

	private string? _currentGroupId;

	private ItemVm? _pressed;

	private Point _dragOrigin;

	private bool _dragged;

	private bool _dialogOpen;

	private bool _menuOpen;

	private bool _pinned;

	private bool _dragActive;

	private DispatcherTimer? _hideWatch;

	private DateTime _watchStart;

	private DateTime? _releasedAt;

	private int _visibilityToken;

	public bool CaptureMode { get; set; }

	public MainWindow()
	{
		InitializeComponent();
		ItemsHost.ItemsSource = _items;
		_currentGroupId = ConfigService.Current.LastGroupId;
		_pinned = ConfigService.Current.PinnedOpen;
		SourceInitialized += OnSourceInitialized;
		Deactivated += OnDeactivated;
		PreviewKeyDown += OnPreviewKeyDown;
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
		if (!_dialogOpen && !_menuOpen && !_pinned && !CaptureMode)
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
		PinButton.ToolTip = (_pinned ? "已固定：点面板外部不会自动收起，再点一次取消" : "固定住面板，方便从桌面连续拖东西进来");
		PinButton.Background = (System.Windows.Media.Brush)FindResource(_pinned ? "AccentFill" : "ControlFill");
		PinButton.Foreground = (System.Windows.Media.Brush)FindResource(_pinned ? "AccentText" : "TextFill");
	}

	private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if ((int)e.Key == 13)
		{
			e.Handled = true;
			if (_currentGroupId != null)
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
	}

	public void TogglePanel()
	{
		if (IsVisible)
		{
			HidePanel();
		}
		else
		{
			ShowPanel();
		}
	}

	public void ShowPanel()
	{
		StopHideWatch();
		Refresh();
		int token = ++_visibilityToken;
		if (!IsVisible)
		{
			PositionPanel(ensureVisible: false);
			Show();
		}
		else
		{
			PositionPanel();
		}
		Topmost = true;
		Activate();
		AnimatePanelIn(token);
		((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
		{
			Activate();
			Focus();
		}), (DispatcherPriority)5, Array.Empty<object>());
	}

	public void HidePanel()
	{
		if (IsVisible)
		{
			AnimatePanelOut(++_visibilityToken);
		}
	}

	private void PositionPanel(bool ensureVisible = true)
	{
		try
		{
			Rectangle workingArea = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
			int num = Math.Max(560, (int)((double)workingArea.Width * ConfigService.Current.WidthRatio));
			int num2 = Math.Max(400, (int)((double)workingArea.Height * ConfigService.Current.HeightRatio));
			int x = workingArea.X + (workingArea.Width - num) / 2;
			int y = workingArea.Y + (workingArea.Height - num2) / 2;
			uint flags = (ensureVisible ? 80u : 16u);
			Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, num, num2, flags);
		}
		catch (Exception ex)
		{
			AppPaths.Log("定位面板失败：" + ex.Message);
		}
	}

	private static TimeSpan Ms(double ms)
	{
		return TimeSpan.FromMilliseconds(ms);
	}

	private void AnimatePanelIn(int token)
	{
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		RootBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, Ms(200.0))
		{
			EasingFunction = easingFunction
		});
		RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.965, 1.0, Ms(230.0))
		{
			EasingFunction = easingFunction
		});
		RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.965, 1.0, Ms(230.0))
		{
			EasingFunction = easingFunction
		});
		RootSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16.0, 0.0, Ms(230.0))
		{
			EasingFunction = easingFunction
		});
	}

	private void AnimatePanelOut(int token)
	{
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseIn
		};
		DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, Ms(130.0))
		{
			EasingFunction = easingFunction
		};
		DoubleAnimation animation = new DoubleAnimation(RootScale.ScaleX, 0.975, Ms(130.0))
		{
			EasingFunction = easingFunction
		};
		doubleAnimation.Completed += delegate
		{
			if (token == _visibilityToken)
			{
				Hide();
			}
		};
		RootBorder.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
		RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
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

	public void Navigate(string? groupId)
	{
		bool flag = groupId != null;
		bool flag2 = _currentGroupId != null;
		_currentGroupId = groupId;
		ConfigService.Current.LastGroupId = groupId;
		Refresh();
		AnimateContentIn(flag && !flag2);
	}

	private void AnimateContentIn(bool forward)
	{
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		double fromValue = (forward ? 34.0 : (-34.0));
		ItemsArea.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, Ms(190.0))
		{
			EasingFunction = easingFunction
		});
		AreaSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromValue, 0.0, Ms(240.0))
		{
			EasingFunction = easingFunction
		});
	}

	public void Refresh()
	{
		AppConfig current = ConfigService.Current;
		int num = Math.Clamp(current.IconSize, 24, 128);
		int num2 = num + 46;
		int num3 = num + (current.ShowLabels ? 46 : 22);
		IconService.Monochrome = current.MonochromeIcons;
		IconService.MonochromeColor = (ThemeBrushes.IsDarkNow ? Colors.White : System.Windows.Media.Color.FromRgb(26, 26, 26));
		_items.Clear();
		foreach (DeskItem item in CurrentList())
		{
			_items.Add(new ItemVm(item, ResolveIcon(item, num), num, num2, num3, current.ShowLabels));
		}
		DeskItem deskItem = CurrentGroup();
		BackButton.Visibility = ((_currentGroupId == null) ? Visibility.Collapsed : Visibility.Visible);
		TitleText.Text = deskItem?.Name ?? "收纳盒";
		SubtitleText.Text = ((_items.Count == 0) ? string.Empty : $"{_items.Count} 个项目");
		EmptyHint.Visibility = ((_items.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (!AppPaths.DataDirUsable)
		{
			SetStatus("⚠ 没有可写的数据目录，现在收纳的东西重启后会丢失 —— 请用正常方式启动 DeskBox");
		}
		else if (AppPaths.Degraded)
		{
			SetStatus("⚠ 正运行在受限环境，收纳可能只能复制、开机自启会失败（详见托盘提示）");
		}
		else
		{
			SetStatus(string.Empty);
		}
		current.LastGroupId = _currentGroupId;
	}

	private static ImageSource? ResolveIcon(DeskItem item, int size)
	{
		if (item.IsGroup)
		{
			return IconService.GetFolderIcon(size);
		}
		string text = AppPaths.StoredFilePath(item);
		if (!string.IsNullOrEmpty(text) && (File.Exists(text) || Directory.Exists(text)))
		{
			return IconService.GetIcon(text, size);
		}
		if (!string.IsNullOrEmpty(item.Target) && (File.Exists(item.Target) || Directory.Exists(item.Target)))
		{
			return IconService.GetIcon(item.Target, size);
		}
		return null;
	}

	private void ApplySort(string mode)
	{
		ConfigService.Current.SortMode = mode;
		List<DeskItem> list = CurrentList();
		StringComparer currentCultureIgnoreCase = StringComparer.CurrentCultureIgnoreCase;
		List<DeskItem> collection = mode switch
		{
			"Name" => list.OrderBy((DeskItem i) => i.Name, currentCultureIgnoreCase).ToList(), 
			"Type" => list.OrderBy((DeskItem i) => (int)i.Kind).ThenBy((DeskItem i) => i.Name, currentCultureIgnoreCase).ToList(), 
			"Frequent" => list.OrderByDescending((DeskItem i) => i.ClickCount).ThenBy((DeskItem i) => i.Name, currentCultureIgnoreCase).ToList(), 
			_ => list.ToList(), 
		};
		list.Clear();
		list.AddRange(collection);
		ConfigService.Save();
		Refresh();
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
		DropOverlay.Visibility = Visibility.Collapsed;
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
		bool flag = IsAcceptable(e.Data);
		e.Effects = (flag ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None);
		e.Handled = true;
		if (flag)
		{
			_dragActive = true;
		}
		if (!flag || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return;
		}
		DropOverlay.Visibility = Visibility.Visible;
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
		if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			ImportFiles((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop), CurrentList(), CurrentList().Count);
		}
		else if (e.Data.GetDataPresent("DeskBox.ItemId"))
		{
			string id = (string)e.Data.GetData("DeskBox.ItemId");
			DeskItem deskItem = FindItem(ConfigService.Current.Items, id);
			if (deskItem != null)
			{
				RemoveFromTree(deskItem);
				CurrentList().Add(deskItem);
				Persist();
			}
		}
	}

	private void Item_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		if (sender is Border { Tag: ItemVm tag })
		{
			bool flag = IsAcceptable(e.Data);
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
		bool after = !flag && position.X > border.ActualWidth / 2.0;
		List<DeskItem> list = (flag ? tag.Model.Children : CurrentList());
		if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
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

	private void ImportFiles(string[] files, List<DeskItem> destination, int index)
	{
		ItemStore.ResetImportStats();
		List<DeskItem> list = ItemStore.ImportAll(files);
		if (list.Count == 0)
		{
			SetStatus("没能收纳进来：文件可能正被占用，或程序运行在受限环境里。");
			return;
		}
		destination.InsertRange(Math.Clamp(index, 0, destination.Count), list);
		Persist();
		SetStatus((ItemStore.CopiedInsteadOfMoved > 0) ? $"已收纳 {list.Count} 项；其中 {ItemStore.CopiedInsteadOfMoved} 项只做了复制，原文件仍在原处。" : $"已收纳 {list.Count} 项，原位置的文件已经搬走。");
	}

	private void Persist()
	{
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
		if (Math.Abs(position.X - _dragOrigin.X) < 6.0 && Math.Abs(position.Y - _dragOrigin.Y) < 6.0)
		{
			return;
		}
		ItemVm pressed = _pressed;
		_pressed = null;
		_dragged = true;
		System.Windows.DataObject dataObject = new System.Windows.DataObject();
		dataObject.SetData("DeskBox.ItemId", pressed.Model.Id);
		try
		{
			DragDrop.DoDragDrop((DependencyObject)sender, dataObject, System.Windows.DragDropEffects.Move);
		}
		catch (Exception ex)
		{
			AppPaths.Log("拖动失败：" + ex.Message);
		}
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
			string text = InputDialog.Prompt(this, "新建文件夹", "文件夹名称", "新建文件夹");
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
		string text = InputDialog.Prompt(this, "重命名", "新名称", vm.Model.Name);
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
			DeleteFilesRecursive(vm.Model);
			RemoveFromTree(vm.Model);
			Persist();
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
		if (position.HasValue)
		{
			menu.Placement = PlacementMode.Relative;
			Point value = position.Value;
			menu.HorizontalOffset = value.X;
			value = position.Value;
			menu.VerticalOffset = value.Y;
			menu.PlacementTarget = this;
		}
		_menuOpen = true;
		menu.Closed += delegate
		{
			_menuOpen = false;
		};
		menu.IsOpen = true;
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		Navigate(null);
	}

	private void NewFolder_Click(object sender, RoutedEventArgs e)
	{
		string text = InputDialog.Prompt(this, "新建文件夹", "文件夹名称", "新建文件夹");
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
		AddMode("Manual", "保持手动拖拽顺序");
		AddMode("Name", "按名称排序（A→Z）");
		AddMode("Type", "按类型分组");
		AddMode("Frequent", "按使用频率");
		menu.Items.Add(new Separator());
		AddMenuItem(menu, "紧凑排列（消除空位）", () =>
		{
			List<DeskItem> list = CurrentList();
			List<DeskItem> collection = list.ToList();
			list.Clear();
			list.AddRange(collection);
			ConfigService.Save();
			Refresh();
		});
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
