using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using DeskBox.Interop;
using DeskBox.Theme;

namespace DeskBox;

public partial class InputDialog : Window, IComponentConnector
{
	private InputDialog()
	{
		InitializeComponent();
		SourceInitialized += delegate
		{
			ApplyTheme();
		};
		Loaded += delegate
		{
			InputBox.Focus();
			InputBox.SelectAll();
		};
	}

	public static string? Prompt(Window? owner, string title, string label, string defaultValue)
	{
		InputDialog inputDialog = new InputDialog
		{
			Title = title
		};
		inputDialog.LabelText.Text = label;
		inputDialog.InputBox.Text = defaultValue;
		if (owner != null && owner.IsVisible)
		{
			inputDialog.Owner = owner;
			inputDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		}
		else
		{
			inputDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		}
		if (inputDialog.ShowDialog() != true)
		{
			return null;
		}
		return inputDialog.InputBox.Text;
	}

	private void ApplyTheme()
	{
		bool isDarkNow = ThemeBrushes.IsDarkNow;
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			int value = (isDarkNow ? 1 : 0);
			Native.DwmSetWindowAttribute(handle, 20, ref value, 4);
		}
		Background = (Brush)Application.Current.Resources["WindowFill"];
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
		Close();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
