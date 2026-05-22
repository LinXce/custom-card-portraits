using System;
using System.Collections.Generic;
using System.Reflection;
using CustomCardPortraits.script.InGameEditor;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace CustomCardPortraits.script.Patches;

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen._Ready))]
public static class InspectCardScreenReadyPatch
{
	private const string ButtonName = "CustomCardPortraits_EditCardPortraitButton";
	private static readonly FieldInfo CardsField = typeof(NInspectCardScreen).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo IndexField = typeof(NInspectCardScreen).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)!;

	[HarmonyPostfix]
	public static void Postfix(NInspectCardScreen __instance)
	{
		if (__instance == null)
			return;

		if (__instance.GetNodeOrNull<Control>(ButtonName) != null)
			return;

		var button = CreateEditButton(__instance);
		__instance.AddChild(button);
		button.Visible = false;
	}

	private static TextureButton CreateEditButton(NInspectCardScreen screen)
	{
		var button = new TextureButton
		{
			Name = ButtonName,
			FocusMode = Control.FocusModeEnum.All,
			ZIndex = 100,
			ZAsRelative = true,
			MouseFilter = Control.MouseFilterEnum.Stop
		};

		var normalTex = GD.Load<Texture2D>("res://asset/image/button.png");
		var outlineTex = GD.Load<Texture2D>("res://asset/image/button_outline.png");
		if (normalTex != null)
		{
			button.TextureNormal = normalTex;
			button.Size = normalTex.GetSize();
			button.CustomMinimumSize = button.Size;
		}
		if (outlineTex != null)
		{
			button.TextureHover = outlineTex;
			button.TexturePressed = outlineTex;
		}

		button.AnchorLeft = 1f;
		button.AnchorTop = 1f;
		button.AnchorRight = 1f;
		button.AnchorBottom = 1f;
		button.OffsetLeft = -300f;
		button.OffsetTop = -200f;
		button.OffsetRight = -130f;
		button.OffsetBottom = -134f;

        var label = new Label
        {
            Text = "修改卡面",
            FocusMode = Control.FocusModeEnum.None,
            HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Size = button.Size,
			ZIndex = 101,
        };
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		label.AddThemeFontSizeOverride("font_size", 22);
		label.AddThemeColorOverride("font_color", StsColors.cream);
		label.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);
		label.AddThemeConstantOverride("outline_size", 6);

		button.AddChild(label);

        button.Pressed += () => OnEditButtonPressed(screen);
        return button;
	}

	private static void OnEditButtonPressed(NInspectCardScreen screen)
	{
		CardModel? card = GetCurrentCard(screen);
		if (card == null)
			return;

		CardPortraitEditorOverlay.ShowEditor(screen.GetTree(), card);
	}

	private static CardModel? GetCurrentCard(NInspectCardScreen screen)
	{
		var cards = CardsField.GetValue(screen) as List<CardModel>;
		if (cards == null || cards.Count == 0)
			return null;

		int index = (int)IndexField.GetValue(screen)!;
		if (index < 0 || index >= cards.Count)
			return null;

		return cards[index];
	}
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen.Open))]
public static class InspectCardScreenOpenPatch
{
	[HarmonyPostfix]
	public static void Postfix(NInspectCardScreen __instance)
	{
		if (__instance == null)
			return;

		var button = __instance.GetNodeOrNull<TextureButton>("CustomCardPortraits_EditCardPortraitButton");
		if (button == null)
			return;

		button.Visible = true;
		button.Modulate = new Color(1f, 1f, 1f, 0f);
		Vector2 targetPosition = button.Position;
		button.Position = targetPosition + Vector2.Up * 24f;
		var tween = __instance.CreateTween().SetParallel();
		tween.TweenProperty(button, "position", targetPosition, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.TweenProperty(button, "modulate:a", 1f, 0.16f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
	}
}

[HarmonyPatch(typeof(NInspectCardScreen), nameof(NInspectCardScreen.Close))]
public static class InspectCardScreenClosePatch
{
	[HarmonyPostfix]
	public static void Postfix(NInspectCardScreen __instance)
	{
		if (__instance == null)
			return;

		var button = __instance.GetNodeOrNull<TextureButton>("CustomCardPortraits_EditCardPortraitButton");
		if (button != null)
			button.Visible = false;
	}
}