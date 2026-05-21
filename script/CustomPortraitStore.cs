using System;
using System.Collections.Concurrent;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace CustomCardPortraits.script;

public static class CustomPortraitStore
{
	private sealed record CacheEntry(Texture2D Texture, DateTime LastWriteUtc);

	private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

	private const string RootOverrideDir = "user://CustomCardPortraits";

	// Mirror the game's portrait PNG layout:
	// res://images/packed/card_portraits/<pool>/<card>.png
	private const string PortraitRootRelative = "images/packed/card_portraits";

	public static void Warmup()
	{
		// No-op for now, but keeps init call explicit.
	}

	public static bool TryGetOverride(CardModel model, out Texture2D? texture)
	{
		texture = null;
		if (model == null)
			return false;

		try
		{
			string pool = (model.Pool?.Title ?? string.Empty).Trim().ToLowerInvariant();
			string id = model.Id.Entry.Trim().ToLowerInvariant();
			if (string.IsNullOrEmpty(pool) || string.IsNullOrEmpty(id))
				return false;

			string rel = $"{PortraitRootRelative}/{pool}/{id}.png";
			string vpath = $"{RootOverrideDir}/{rel}";
			string abs = ProjectSettings.GlobalizePath(vpath);
			if (!File.Exists(abs))
				return false;

			DateTime writeUtc = File.GetLastWriteTimeUtc(abs);
			string key = rel;
			if (_cache.TryGetValue(key, out CacheEntry? entry) && entry.Texture != null && entry.LastWriteUtc == writeUtc)
			{
				texture = entry.Texture;
				return true;
			}

			byte[] bytes = File.ReadAllBytes(abs);
			Image img = new Image();
			Error err = img.LoadPngFromBuffer(bytes);
			if (err != Error.Ok)
			{
				GD.PrintErr($"[CustomCardPortraits] Failed to load override png: {vpath} ({err})");
				_cache.TryRemove(key, out _);
				return false;
			}
			ImageTexture tex = ImageTexture.CreateFromImage(img);
			_cache[key] = new CacheEntry(tex, writeUtc);
			texture = tex;
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CustomCardPortraits] TryGetOverride failed: {ex.Message}");
			return false;
		}
	}
}
