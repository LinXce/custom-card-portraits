using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace CustomCardPortraits.script.InGameEditor;

public sealed partial class CardPortraitEditorOverlay
{
	private static Vector2 GetPanelSize(Vector2 viewportSize)
	{
		float margin = 60f;
		float maxWidth = MathF.Max(0f, viewportSize.X - margin);
		float maxHeight = MathF.Max(0f, viewportSize.Y - margin);
		float width = MathF.Min(1540f, maxWidth);
		float height = MathF.Min(940f, maxHeight);
		if (width <= 0f)
			width = viewportSize.X;
		if (height <= 0f)
			height = viewportSize.Y;
		return new Vector2(width, height);
	}

	private void UpdateLayout()
	{
		if (_panel == null || _topBar == null)
			return;
		_panel.Size = GetPanelSize(GetViewport().GetVisibleRect().Size);
		_panel.Position = new Vector2(200, 50);
		float topWidth = MathF.Max(0f, _panel.Size.X - 40f);
		_topBar.CustomMinimumSize = new Vector2(topWidth, 0f);
	}

	private static Texture2D? LoadOriginalPortrait(CardModel? model)
	{
		if (model == null)
			return null;
		string path = model.HasPortrait ? model.PortraitPath : CardModel.MissingPortraitPath;
		return ResourceLoader.Load<Texture2D>(path);
	}

	private static void ApplyPortraitOverride(NCard card, Texture2D? portrait)
	{
		if (portrait == null)
			return;
		var standard = card.GetNodeOrNull<TextureRect>("%Portrait");
		if (standard != null)
			standard.Texture = portrait;
		var ancient = card.GetNodeOrNull<TextureRect>("%AncientPortrait");
		if (ancient != null)
			ancient.Texture = portrait;
	}

	private static bool TryLoadOverrideImage(string pool, string id, out Image? image)
	{
		image = null;
		string overrideVirtual = $"{OverrideRootVirtual}/{pool}/{id}.png";
		string overrideAbs = ProjectSettings.GlobalizePath(overrideVirtual);
		if (!File.Exists(overrideAbs))
			return false;

		var img = new Image();
		var err = img.Load(overrideAbs);
		if (err != Error.Ok)
			return false;
		image = img;
		return true;
	}

	private static Control WrapCardPreview(NCard card, float scale)
	{
		var panel = CreateRoundedPanel();
		var center = new CenterContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		// Apply scale to the container instead of the NCard itself because NCard may reset its own scale
		card.Scale = Vector2.One;
		// center.Scale = Vector2.One * scale;
		card.MouseFilter = Control.MouseFilterEnum.Ignore;
		center.AddChild(card);
		panel.AddChild(center);
		return panel;
	}

	private static PanelContainer CreateRoundedPanel()
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.35f),
			CornerRadiusTopLeft = 16,
			CornerRadiusTopRight = 16,
			CornerRadiusBottomLeft = 16,
			CornerRadiusBottomRight = 16
		};
		style.ContentMarginLeft = 12;
		style.ContentMarginRight = 12;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private static NCard CreateCardPreview()
	{
		var scene = GD.Load<PackedScene>("res://scenes/cards/card.tscn");
		return scene.Instantiate<NCard>();
	}

	private static TextureButton CreateGameButton(string text, string name)
	{
		var button = new TextureButton
		{
			Name = name,
			FocusMode = Control.FocusModeEnum.All,
			CustomMinimumSize = new Vector2(180, 44)
		};

		var normalTex = GD.Load<Texture2D>("res://asset/image/button.png");
		var outlineTex = GD.Load<Texture2D>("res://asset/image/button_outline.png");
		if (normalTex != null)
			button.TextureNormal = normalTex;
		if (outlineTex != null)
		{
			button.TextureHover = outlineTex;
			button.TexturePressed = outlineTex;
		}

		var label = new Label
		{
			Text = text,
			FocusMode = Control.FocusModeEnum.None,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		ApplyGameLabelStyle(label, 22);
		button.AddChild(label);
		return button;
	}

	private static void ApplyGameLabelStyle(Label label, int fontSize)
	{
		label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", StsColors.cream);
		label.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);
		label.AddThemeConstantOverride("outline_size", 6);
	}

	private static void ApplyGameOptionStyle(OptionButton option, int fontSize)
	{
		option.AddThemeFontSizeOverride("font_size", fontSize);
		option.AddThemeColorOverride("font_color", StsColors.cream);
		option.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);
		option.AddThemeConstantOverride("outline_size", 6);
	}

	private static Rect2I ClampRect(Rect2I r, int w, int h)
	{
		int x = Math.Clamp(r.Position.X, 0, Math.Max(0, w - 1));
		int y = Math.Clamp(r.Position.Y, 0, Math.Max(0, h - 1));
		int rw = Math.Clamp(r.Size.X, 0, w - x);
		int rh = Math.Clamp(r.Size.Y, 0, h - y);
		return new Rect2I(x, y, rw, rh);
	}

	private void SetStatus(string text, bool isError)
	{
		_status.Text = text;
		_status.Modulate = isError ? new Color(1f, 0.4f, 0.4f) : Colors.White;
	}
}
