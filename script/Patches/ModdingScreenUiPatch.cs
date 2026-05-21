using System;
using CustomCardPortraits.script.InGameEditor;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace CustomCardPortraits.script.Patches;

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class ModdingScreenUiPatch
{
	private const string ModId = "CustomCardPortraits";
	private const string ButtonName = "CustomCardPortraits_OpenPortraitEditor";

	[HarmonyPostfix]
	public static void Postfix(NModInfoContainer __instance, Mod mod)
	{
		if (__instance == null)
			return;

		bool isOurMod = string.Equals(mod?.manifest?.id, ModId, StringComparison.OrdinalIgnoreCase);
		var button = __instance.GetNodeOrNull<Button>(ButtonName);

		if (!isOurMod)
		{
			if (button != null)
				button.Visible = false;
			return;
		}

		if (button == null)
		{
			button = new Button
			{
				Name = ButtonName,
				Text = "打开卡图编辑器",
				FocusMode = Control.FocusModeEnum.All,
				Size = new Vector2(240, 44),
				ZIndex = 100,
				ZAsRelative = true,
			};

			button.Pressed += () => CardPortraitEditorOverlay.ShowEditor(__instance.GetTree());
			__instance.AddChild(button);
		}

		button.Position = GetButtonPos(__instance, button.Size);
		button.Visible = true;
	}

	private static Vector2 GetButtonPos(Control container, Vector2 buttonSize)
	{
		float marginLeft = 22;
		float marginBottom = 18;
		float y = container.Size.Y > 0 ? container.Size.Y - buttonSize.Y - marginBottom : 930;
		return new Vector2(marginLeft, MathF.Max(0, y));
	}
}
