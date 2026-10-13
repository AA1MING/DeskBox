using System.Collections.Generic;

namespace DeskBox.Models;

public sealed class AppConfig
{
	public bool HasStarted { get; set; }

	public int Version { get; set; } = 1;

	public string ThemeMode { get; set; } = "System";

	public string Backdrop { get; set; } = "Glass";

	public bool UseAccentColor { get; set; } = true;

	public int IconSize { get; set; } = 48;

	public bool ShowLabels { get; set; } = true;

	public bool MonochromeIcons { get; set; }

	public string Hotkey { get; set; } = string.Empty;

	public bool AutoStart { get; set; }

    public bool CheckUpdatesOnStartup { get; set; } = true;

	public bool RestrictedNoticeShown { get; set; }

	public bool PinnedOpen { get; set; }

	public string SortMode { get; set; } = "Manual";

	public double WidthRatio { get; set; } = 0.6;

	public double HeightRatio { get; set; } = 0.7;

	public string? LastGroupId { get; set; }

	public List<DeskItem> Items { get; set; } = new List<DeskItem>();
}
