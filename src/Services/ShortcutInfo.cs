namespace DeskBox.Services;

public sealed record ShortcutInfo(string Target, string Arguments, string WorkingDirectory, string IconPath, int IconIndex);
