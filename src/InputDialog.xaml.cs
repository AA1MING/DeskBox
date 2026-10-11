using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using DeskBox.Interop;
using DeskBox.Theme;
using DeskBox.Services;

namespace DeskBox;

public partial class InputDialog : Window, IComponentConnector
{
    private bool _closing;
	private InputDialog()
	{
		InitializeComponent();
        Motion.AttachWindow(this, DialogContent, false);
        Closing += (_, e) =>
        {
            if (_closing || !IsVisible || Dispatcher.HasShutdownStarted) return;
            e.Cancel = true;
            _closing = true;
            IsHitTestVisible = false;
            Motion.Exit(DialogContent, Close);
        };
        DialogContent.SetResourceReference(Panel.BackgroundProperty, "PanelFill");
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel_Click(this, new RoutedEventArgs()); } };
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

    private void ApplyTheme() => BackdropService.Apply(this, ConfigService.Current.Backdrop, ThemeBrushes.IsDarkNow);

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		if (_closing) return;
        _closing = true;
		IsHitTestVisible = false;
        Motion.Exit(DialogContent, () => DialogResult = true);
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		if (_closing) return;
        _closing = true;
		IsHitTestVisible = false;
        Motion.Exit(DialogContent, () => DialogResult = false);
	}
}
