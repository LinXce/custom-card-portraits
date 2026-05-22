using System;
using CustomCardPortraits.script.InGameEditor;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Helpers;

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
		var button = __instance.GetNodeOrNull<Control>(ButtonName);

		if (!isOurMod)
		{
			if (button != null)
				button.Visible = false;
			return;
		}

		if (button == null)
		{
			var tb = new TextureButton
			{
				Name = ButtonName,
				FocusMode = Control.FocusModeEnum.All,
				Size = new Vector2(240, 44),
				ZIndex = 100,
				ZAsRelative = true,
			};

			// Load original game button textures (fallback to plain button if not found)
			var normalTex = GD.Load<Texture2D>("res://images/packed/common_ui/event_button.png");
			var outlineTex = GD.Load<Texture2D>("res://images/packed/common_ui/event_button_outline.png");
			if (normalTex != null)
			{
				tb.TextureNormal = normalTex;
				tb.Size = normalTex.GetSize();
			}
			if (outlineTex != null)
			{
				tb.TextureHover = outlineTex;
				tb.TexturePressed = outlineTex;
			}

			tb.Pressed += () => CardPortraitEditorOverlay.ShowEditor(__instance.GetTree());

			var label = new Label
			{
				Text = "打开卡图编辑器",
				FocusMode = Control.FocusModeEnum.None,
				Size = tb.Size,
				ZIndex = 101,
			};

			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;

			// Apply game's locale font and label theme so text matches original UI
			label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
			label.AddThemeFontSizeOverride("font_size", 24);
			label.AddThemeColorOverride("font_color", StsColors.cream);
			label.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);

			tb.AddChild(label);
			button = tb;
			__instance.AddChild(tb);
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
