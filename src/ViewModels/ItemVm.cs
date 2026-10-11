using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using DeskBox.Models;

namespace DeskBox.ViewModels;

public sealed class ItemVm : INotifyPropertyChanged
{
	private bool _isDropTarget;

	private bool _isRenaming;

	public DeskItem Model { get; }

	public string Name => Model.Name;

	public bool IsGroup => Model.IsGroup;

	public ImageSource? Icon { get; }

	public double IconSize { get; }

	public double CellWidth { get; }

	public double CellHeight { get; }

	public Visibility LabelVisibility { get; }

	public bool IsDropTarget
	{
		get
		{
			return _isDropTarget;
		}
		set
		{
			if (_isDropTarget != value)
			{
				_isDropTarget = value;
				OnPropertyChanged("IsDropTarget");
			}
		}
	}

	public bool IsRenaming
	{
		get
		{
			return _isRenaming;
		}
		set
		{
			if (_isRenaming != value)
			{
				_isRenaming = value;
				OnPropertyChanged("IsRenaming");
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ItemVm(DeskItem model, ImageSource? icon, double iconSize, double cellWidth, double cellHeight, bool showLabels)
	{
		Model = model;
		Icon = icon;
		IconSize = iconSize;
		CellWidth = cellWidth;
		CellHeight = cellHeight;
		LabelVisibility = ((!showLabels) ? Visibility.Collapsed : Visibility.Visible);
	}

	public void RaiseNameChanged()
	{
		OnPropertyChanged("Name");
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
