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
    private string _displayedName;

	public DeskItem Model { get; }

	public string Name => Model.Name;

	public bool IsGroup => Model.IsGroup;

	public ImageSource? Icon { get; private set; }

	public double IconSize { get; private set; }

	public double CellWidth { get; private set; }

	public double CellHeight { get; private set; }

	public Visibility LabelVisibility { get; private set; }

	public void SetIcon(ImageSource? icon)
	{
        if (ReferenceEquals(Icon, icon)) return;
		Icon = icon;
		OnPropertyChanged(nameof(Icon));
	}

	public void UpdateLayout(int size, bool labels)
	{
        if (IconSize != size) { IconSize = size; OnPropertyChanged(nameof(IconSize)); }
        double width = size + 46, height = size + (labels ? 46 : 22);
        if (CellWidth != width) { CellWidth = width; OnPropertyChanged(nameof(CellWidth)); }
        if (CellHeight != height) { CellHeight = height; OnPropertyChanged(nameof(CellHeight)); }
        var visibility = labels ? Visibility.Visible : Visibility.Collapsed;
        if (LabelVisibility != visibility) { LabelVisibility = visibility; OnPropertyChanged(nameof(LabelVisibility)); }
        if (_displayedName != Model.Name) RaiseNameChanged();
	}

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
        _displayedName = model.Name;
		Icon = icon;
		IconSize = iconSize;
		CellWidth = cellWidth;
		CellHeight = cellHeight;
		LabelVisibility = ((!showLabels) ? Visibility.Collapsed : Visibility.Visible);
	}

	public void RaiseNameChanged()
	{
        _displayedName = Model.Name;
		OnPropertyChanged("Name");
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
