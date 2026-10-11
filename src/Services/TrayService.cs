using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Resources;
using DeskBox.Infrastructure;

namespace DeskBox.Services;

public sealed class TrayService : IDisposable
{
	private readonly NotifyIcon _notifyIcon;

	private ContextMenu? _menu;

	public Func<bool>? AutoStartStateProvider { get; set; }

	public Action<bool>? AutoStartSetter { get; set; }

	private static Style? MenuStyle => System.Windows.Application.Current?.TryFindResource("DeskMenuStyle") as Style;

	private static Style? ItemStyle => System.Windows.Application.Current?.TryFindResource("DeskMenuItemStyle") as Style;

	public event Action? ToggleRequested;

	public event Action? SettingsRequested;

	public event Action? ExitRequested;

	public event Action? HelpRequested;

	public event Action? OpenDataFolderRequested;

	public TrayService()
	{
		_notifyIcon = new NotifyIcon
		{
			Icon = LoadIcon(),
			Text = "DeskBox 桌面收纳盒",
			Visible = true
		};
		_notifyIcon.MouseClick += OnMouseClick;
	}

	private static Icon LoadIcon()
	{
		try
		{
			StreamResourceInfo resourceStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
			if (resourceStream != null)
			{
				using (Stream stream = resourceStream.Stream)
				{
					return (new Icon(stream).Clone() as Icon) ?? SystemIcons.Application;
				}
			}
		}
		catch
		{
		}
		try
		{
			Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
			if (icon != null)
			{
				return icon;
			}
		}
		catch
		{
		}
		return SystemIcons.Application;
	}

	private void OnMouseClick(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ToggleRequested?.Invoke();
		}
		else if (e.Button == MouseButtons.Right)
		{
			ShowMenu();
		}
	}

	public void ShowMenu()
	{
		_menu = BuildMenu();
		_menu.Placement = PlacementMode.MousePoint;
		_menu.IsOpen = true;
	}

	private static MenuItem NewItem(string header, Action action, bool checkable = false, bool isChecked = false)
	{
		MenuItem menuItem = new MenuItem();
		menuItem.Header = header;
		menuItem.Style = ItemStyle;
		menuItem.IsCheckable = checkable;
		menuItem.IsChecked = isChecked;
		menuItem.Click += delegate
		{
			action();
		};
		return menuItem;
	}

	private ContextMenu BuildMenu()
	{
		ContextMenu contextMenu = new ContextMenu
		{
			StaysOpen = false,
			Style = MenuStyle
		};
		MenuItem autoStart = NewItem("开机自启动", () =>
		{
		}, checkable: true, AutoStartStateProvider?.Invoke() ?? false);
		autoStart.Click += delegate
		{
			AutoStartSetter?.Invoke(autoStart.IsChecked);
		};
		contextMenu.Items.Add(NewItem("打开面板", () =>
		{
			ToggleRequested?.Invoke();
		}));
		contextMenu.Items.Add(NewItem("设置…", () =>
		{
			SettingsRequested?.Invoke();
		}));
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(autoStart);
		contextMenu.Items.Add(NewItem("打开数据文件夹", () =>
		{
			OpenDataFolderRequested?.Invoke();
		}));
		contextMenu.Items.Add(NewItem("使用说明", () =>
		{
			HelpRequested?.Invoke();
		}));
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(NewItem("退出", () =>
		{
			ExitRequested?.Invoke();
		}));
		return contextMenu;
	}

	public void ApplyTheme(bool dark, System.Windows.Media.Color accent)
	{
	}

	public void ShowBalloon(string title, string text)
	{
		try
		{
			_notifyIcon.BalloonTipTitle = title;
			_notifyIcon.BalloonTipText = text;
			_notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
			_notifyIcon.ShowBalloonTip(4000);
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		_notifyIcon.Visible = false;
		_notifyIcon.Dispose();
	}
}
