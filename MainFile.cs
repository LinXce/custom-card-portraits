using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;

namespace CustomCardPortraits;

[ModInitializer("Init")]
public static class CustomCardPortraitsMod
{
	private const string ModName = "CustomCardPortraits";

	public static void Init()
	{
		Harmony harmony = new Harmony(ModName);
		harmony.PatchAll();

		try
		{
			script.ConfigStore.Load();
			script.CustomPortraitStore.Warmup();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CustomCardPortraits] Init failed: {ex.Message}");
		}
	}
}
