using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Infrastructure;
using DeskBox.Models;

namespace DeskBox.Services;

public static class ConfigService
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters = { (JsonConverter)new JsonStringEnumConverter() },
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static AppConfig Current { get; private set; } = new AppConfig();

	public static void Load()
	{
		try
		{
			if (File.Exists(AppPaths.ConfigFile))
			{
				string json = File.ReadAllText(AppPaths.ConfigFile);
                Current = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
                // Existing installations predate the startup flag; do not show an upgrade as a first run.
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty(nameof(AppConfig.HasStarted), out _)) Current.HasStarted = true;
			}
            else Current = new AppConfig();
		}
		catch (Exception ex)
		{
			AppPaths.Log("配置读取失败，已使用默认配置：" + ex.Message);
			TryBackupBrokenConfig();
			Current = new AppConfig();
		}
		AppConfig current = Current;
        if (string.Equals(current.SortMode, "Type", StringComparison.OrdinalIgnoreCase)) current.SortMode = "Manual";
		if (string.Equals(current.Backdrop, "Mica", StringComparison.OrdinalIgnoreCase)) current.Backdrop = "Glass"; // 旧“云母”改名为“玻璃”
		if (current.Items == null)
		{
			List<DeskItem> list = (current.Items = new List<DeskItem>());
		}
	}

	private static void TryBackupBrokenConfig()
	{
		try
		{
			if (File.Exists(AppPaths.ConfigFile))
			{
				File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".broken", overwrite: true);
			}
		}
		catch
		{
		}
	}

	public static void Save()
	{
		try
		{
			string text = AppPaths.ConfigFile + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(Current, Options));
			File.Move(text, AppPaths.ConfigFile, overwrite: true);
		}
		catch (Exception ex)
		{
			AppPaths.Log("配置保存失败：" + ex.Message);
		}
	}
}
