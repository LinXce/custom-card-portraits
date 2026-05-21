using System;
using System.Text.Json;
using Godot;

namespace CustomCardPortraits.script;

public static class ConfigStore
{
	private const string ConfigPath = "user://custom_card_portraits_config.json";

	private sealed class ConfigData
	{
		public bool Enabled { get; set; } = true;
	}

	public static bool Enabled { get; private set; } = true;

	public static void Load()
	{
		try
		{
			var fa = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
			if (fa == null)
				return;
			string json = fa.GetAsText();
			fa.Close();
			if (string.IsNullOrWhiteSpace(json))
				return;
			var cfg = JsonSerializer.Deserialize<ConfigData>(json);
			if (cfg == null)
				return;
			Enabled = cfg.Enabled;
		}
		catch
		{
			// ignore
		}
	}

	public static void Save()
	{
		try
		{
			var cfg = new ConfigData { Enabled = Enabled };
			string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
			var fa = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Write);
			if (fa == null)
				return;
			fa.StoreString(json);
			fa.Close();
		}
		catch
		{
			// ignore
		}
	}

	public static void SetEnabled(bool enabled)
	{
		if (Enabled != enabled)
		{
			Enabled = enabled;
			Save();
		}
	}
}
