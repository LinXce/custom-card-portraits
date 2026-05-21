using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace CustomCardPortraits.script.Patches;

[HarmonyPatch(typeof(CardModel), "get_Portrait")]
public static class CardPortraitPatch
{
	[HarmonyPostfix]
	public static void Postfix(CardModel __instance, ref Texture2D __result)
	{
		if (__instance == null)
			return;
		if (!ConfigStore.Enabled)
			return;
		if (CustomPortraitStore.TryGetOverride(__instance, out Texture2D? overrideTex) && overrideTex != null)
		{
			__result = overrideTex;
		}
	}
}
