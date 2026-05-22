using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace CustomCardPortraits.script;

public static class ConfigStore
{
	private const string ConfigPath = "user://custom_card_portraits_config.json";
	private static readonly Dictionary<string, bool> CardOverrides = new(StringComparer.OrdinalIgnoreCase);

	private sealed class ConfigData
	{
		public bool Enabled { get; set; } = true;
		public Dictionary<string, bool> CardOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
			CardOverrides.Clear();
			if (cfg.CardOverrides != null)
			{
				foreach (var pair in cfg.CardOverrides)
					CardOverrides[pair.Key] = pair.Value;
			}
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
			var cfg = new ConfigData
			{
				Enabled = Enabled,
				CardOverrides = new Dictionary<string, bool>(CardOverrides, StringComparer.OrdinalIgnoreCase)
			};
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

	public static bool IsCardOverrideEnabled(string pool, string id)
	{
		string key = GetCardKey(pool, id);
		return !CardOverrides.TryGetValue(key, out bool enabled) || enabled;
	}

	public static void SetCardOverrideEnabled(string pool, string id, bool enabled)
	{
		string key = GetCardKey(pool, id);
		if (CardOverrides.TryGetValue(key, out bool current) && current == enabled)
			return;

		CardOverrides[key] = enabled;
		Save();
	}

	private static string GetCardKey(string pool, string id)
	{
		return $"{pool.Trim().ToLowerInvariant()}/{id.Trim().ToLowerInvariant()}";
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
