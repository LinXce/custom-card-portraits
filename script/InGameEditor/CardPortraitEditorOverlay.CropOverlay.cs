using System;
using Godot;

namespace CustomCardPortraits.script.InGameEditor;

public sealed partial class CardPortraitEditorOverlay
{
	private sealed partial class CropOverlay : Control
	{
		private enum SelectionMode
		{
			None,
			Creating,
			Moving,
			ResizeLeft,
			ResizeRight,
			ResizeTop,
			ResizeBottom,
			ResizeTopLeft,
			ResizeTopRight,
			ResizeBottomLeft,
			ResizeBottomRight
		}

		private readonly TextureRect _target;
		private Image? _work;
		private bool _dragging;
		private bool _hasSelection;
		private Vector2I _start;
		private Vector2I _end;
		private SelectionMode _interactionMode = SelectionMode.None;
		private SelectionMode _hoverMode = SelectionMode.None;
		private Vector2I _interactionStartImage;
		private Rect2I _interactionStartRect;

		private float _zoom = 1f;
		private Vector2 _offset = Vector2.Zero;
		private bool _panning;
		private Vector2 _panStart;

		public CropOverlay(TextureRect target)
		{
			_target = target;
			MouseFilter = MouseFilterEnum.Stop;
		}

		public void SetWorkImage(Image? img)
		{
			_work = img;
			_hasSelection = false;
			_dragging = false;
			_interactionMode = SelectionMode.None;
			_hoverMode = SelectionMode.None;
			_zoom = 1f;
			_offset = Vector2.Zero;
			QueueRedraw();
		}

		public Image? GetWorkImage() => _work;

		public void ClearSelection()
		{
			_hasSelection = false;
			_dragging = false;
			_interactionMode = SelectionMode.None;
			_hoverMode = SelectionMode.None;
			QueueRedraw();
		}

		public Rect2I? GetCropRect()
		{
			if (!_hasSelection)
				return null;
			var r = Normalize(_start, _end);
			if (r.Size.X < 2 || r.Size.Y < 2)
				return null;
			return r;
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (_work == null)
				return;

			if (@event is InputEventMouseButton mb)
			{
				if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
				{
					float oldZoom = _zoom;
					float factor = mb.ButtonIndex == MouseButton.WheelUp ? 1.12f : (1f / 1.12f);
					float newZoom = Mathf.Clamp(_zoom * factor, 0.1f, 10f);
					Vector2 local = mb.Position;
					if (TryGetDisplayRect(out Rect2 disp, out float baseScale))
					{
						float oldScale = baseScale * oldZoom;
						float newScale = baseScale * newZoom;

						Vector2 imgSize = new Vector2(_work.GetWidth(), _work.GetHeight());
						Vector2 oldDrawSize = imgSize * oldScale;
						Vector2 oldCenter = (GetLocalSize() - oldDrawSize) * 0.5f;
						Vector2 pointImg = (local - oldCenter - _offset) / oldScale;
						Vector2 newDrawSize = imgSize * newScale;
						Vector2 newCenter = (GetLocalSize() - newDrawSize) * 0.5f;
						_offset = local - newCenter - pointImg * newScale;
						ClampOffset(newDrawSize, GetLocalSize());
						_zoom = newZoom;
						QueueRedraw();
					}
				}
				else if (mb.ButtonIndex == MouseButton.Left)
				{
					if (mb.Pressed)
					{
						if (Input.IsKeyPressed(Key.Space))
						{
							_panning = true;
							_panStart = mb.Position;
						}
						else if (TryMapToImage(mb.Position, out Vector2I p) && TryGetDisplayRect(out Rect2 disp, out float scale))
						{
							SelectionMode mode = _hasSelection ? HitTestSelection(mb.Position, disp, scale) : SelectionMode.None;
							if (mode == SelectionMode.None)
								BeginSelection(SelectionMode.Creating, p);
							else
								BeginSelection(mode, p);
							QueueRedraw();
						}
					}
					else
					{
						if (_panning)
							_panning = false;
						FinishSelection();
						QueueRedraw();
					}
				}
				else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
				{
					_hasSelection = false;
					_interactionMode = SelectionMode.None;
					QueueRedraw();
				}
			}
			else if (@event is InputEventMouseMotion mm)
			{
				if (_panning)
				{
					_offset += mm.Relative;
					if (TryGetDisplayRect(out Rect2 disp, out float scale))
					{
						Vector2 imgSize = new Vector2(_work.GetWidth(), _work.GetHeight()) * scale;
						ClampOffset(imgSize, GetLocalSize());
					}
					QueueRedraw();
				}
				else if (_dragging && TryMapToImage(mm.Position, out Vector2I p))
				{
					UpdateSelection(p);
					QueueRedraw();
				}
				else if (TryGetDisplayRect(out Rect2 disp, out float scale))
				{
					UpdateHoverMode(mm.Position, disp, scale);
				}
			}
		}

		private void BeginSelection(SelectionMode mode, Vector2I imagePoint)
		{
			_interactionMode = mode;
			_dragging = true;
			_interactionStartImage = imagePoint;
			_interactionStartRect = _hasSelection ? Normalize(_start, _end) : new Rect2I(imagePoint.X, imagePoint.Y, 1, 1);
			if (mode == SelectionMode.Creating)
			{
				_hasSelection = true;
				_start = imagePoint;
				_end = imagePoint;
			}
		}

		private void FinishSelection()
		{
			_dragging = false;
			_interactionMode = SelectionMode.None;
		}

		private void UpdateSelection(Vector2I currentImagePoint)
		{
			if (_interactionMode == SelectionMode.Creating)
			{
				_end = currentImagePoint;
				_hasSelection = true;
				return;
			}

			if (_interactionMode == SelectionMode.Moving)
			{
				MoveSelection(currentImagePoint - _interactionStartImage);
				return;
			}

			if (_interactionMode != SelectionMode.None)
				ResizeSelection(currentImagePoint);
		}

		private void MoveSelection(Vector2I delta)
		{
			if (_work == null)
				return;

			int imgW = _work.GetWidth();
			int imgH = _work.GetHeight();
			Rect2I r = _interactionStartRect;
			int x = Math.Clamp(r.Position.X + delta.X, 0, Math.Max(0, imgW - r.Size.X));
			int y = Math.Clamp(r.Position.Y + delta.Y, 0, Math.Max(0, imgH - r.Size.Y));
			_start = new Vector2I(x, y);
			_end = new Vector2I(x + r.Size.X, y + r.Size.Y);
		}

		private void ResizeSelection(Vector2I currentImagePoint)
		{
			if (_work == null)
				return;

			int imgW = _work.GetWidth();
			int imgH = _work.GetHeight();
			Rect2I r = _interactionStartRect;
			int left = r.Position.X;
			int top = r.Position.Y;
			int right = r.Position.X + r.Size.X;
			int bottom = r.Position.Y + r.Size.Y;

			int x = Math.Clamp(currentImagePoint.X, 0, Math.Max(0, imgW - 1));
			int y = Math.Clamp(currentImagePoint.Y, 0, Math.Max(0, imgH - 1));

			switch (_interactionMode)
			{
				case SelectionMode.ResizeLeft:
					left = Math.Min(x, right - 1);
					break;
				case SelectionMode.ResizeRight:
					right = Math.Max(x + 1, left + 1);
					break;
				case SelectionMode.ResizeTop:
					top = Math.Min(y, bottom - 1);
					break;
				case SelectionMode.ResizeBottom:
					bottom = Math.Max(y + 1, top + 1);
					break;
				case SelectionMode.ResizeTopLeft:
					left = Math.Min(x, right - 1);
					top = Math.Min(y, bottom - 1);
					break;
				case SelectionMode.ResizeTopRight:
					right = Math.Max(x + 1, left + 1);
					top = Math.Min(y, bottom - 1);
					break;
				case SelectionMode.ResizeBottomLeft:
					left = Math.Min(x, right - 1);
					bottom = Math.Max(y + 1, top + 1);
					break;
				case SelectionMode.ResizeBottomRight:
					right = Math.Max(x + 1, left + 1);
					bottom = Math.Max(y + 1, top + 1);
					break;
			}

			left = Math.Clamp(left, 0, Math.Max(0, imgW - 1));
			top = Math.Clamp(top, 0, Math.Max(0, imgH - 1));
			right = Math.Clamp(right, left + 1, imgW);
			bottom = Math.Clamp(bottom, top + 1, imgH);
			_start = new Vector2I(left, top);
			_end = new Vector2I(right, bottom);
		}

		private void UpdateHoverMode(Vector2 localPos, Rect2 disp, float scale)
		{
			if (!_hasSelection || _dragging || _panning)
			{
				_hoverMode = SelectionMode.None;
				QueueRedraw();
				return;
			}

			SelectionMode mode = HitTestSelection(localPos, disp, scale);
			if (_hoverMode != mode)
			{
				_hoverMode = mode;
				QueueRedraw();
			}
		}

		private SelectionMode HitTestSelection(Vector2 localPos, Rect2 disp, float scale)
		{
			if (!_hasSelection)
				return SelectionMode.None;

			Rect2I r = Normalize(_start, _end);
			Vector2 topLeft = disp.Position + new Vector2(r.Position.X * scale, r.Position.Y * scale);
			Vector2 bottomRight = disp.Position + new Vector2((r.Position.X + r.Size.X) * scale, (r.Position.Y + r.Size.Y) * scale);
			Rect2 localRect = new Rect2(topLeft, bottomRight - topLeft);
			if (!localRect.HasPoint(localPos))
				return SelectionMode.None;

			const float edge = 14f;
			bool nearLeft = Math.Abs(localPos.X - localRect.Position.X) <= edge;
			bool nearRight = Math.Abs(localPos.X - (localRect.Position.X + localRect.Size.X)) <= edge;
			bool nearTop = Math.Abs(localPos.Y - localRect.Position.Y) <= edge;
			bool nearBottom = Math.Abs(localPos.Y - (localRect.Position.Y + localRect.Size.Y)) <= edge;

			if (nearLeft && nearTop) return SelectionMode.ResizeTopLeft;
			if (nearRight && nearTop) return SelectionMode.ResizeTopRight;
			if (nearLeft && nearBottom) return SelectionMode.ResizeBottomLeft;
			if (nearRight && nearBottom) return SelectionMode.ResizeBottomRight;
			if (nearLeft) return SelectionMode.ResizeLeft;
			if (nearRight) return SelectionMode.ResizeRight;
			if (nearTop) return SelectionMode.ResizeTop;
			if (nearBottom) return SelectionMode.ResizeBottom;
			return SelectionMode.Moving;
		}

		private Vector2 GetLocalSize()
		{
			return _target.Size;
		}

		private void ClampOffset(Vector2 drawSize, Vector2 containerSize)
		{
			if (drawSize.X <= containerSize.X)
				_offset.X = 0f;
			else
			{
				float max = (drawSize.X - containerSize.X) * 0.5f;
				_offset.X = Mathf.Clamp(_offset.X, -max, max);
			}

			if (drawSize.Y <= containerSize.Y)
				_offset.Y = 0f;
			else
			{
				float max = (drawSize.Y - containerSize.Y) * 0.5f;
				_offset.Y = Mathf.Clamp(_offset.Y, -max, max);
			}
		}

		public override void _Draw()
		{
			if (_work == null || !_hasSelection)
				return;

			if (!TryGetDisplayRect(out Rect2 disp, out float scale))
				return;

			Rect2I rImg = Normalize(_start, _end);
			Vector2 pos = disp.Position + new Vector2(rImg.Position.X * scale, rImg.Position.Y * scale);
			Vector2 size = new Vector2(rImg.Size.X * scale, rImg.Size.Y * scale);
			Color outer = new Color(0.92f, 0.92f, 0.92f, 0.95f);
			Color inner = new Color(0.62f, 0.62f, 0.62f, 0.95f);
			DrawRect(new Rect2(pos, size), new Color(0.95f, 0.95f, 0.95f, 0.08f), filled: true);
			DrawRect(new Rect2(pos, size), outer, filled: false, width: 2);
			DrawRect(new Rect2(pos + Vector2.One, size - Vector2.One * 2f), inner, filled: false, width: 1);
			DrawRuleOfThirds(pos, size, inner);
			DrawHoverFeedback(pos, size);

			const float handleSize = 6f;
			DrawHandle(pos, handleSize);
			DrawHandle(new Vector2(pos.X + size.X * 0.5f, pos.Y), handleSize);
			DrawHandle(new Vector2(pos.X + size.X, pos.Y), handleSize);
			DrawHandle(new Vector2(pos.X, pos.Y + size.Y * 0.5f), handleSize);
			DrawHandle(new Vector2(pos.X + size.X, pos.Y + size.Y * 0.5f), handleSize);
			DrawHandle(new Vector2(pos.X, pos.Y + size.Y), handleSize);
			DrawHandle(new Vector2(pos.X + size.X * 0.5f, pos.Y + size.Y), handleSize);
			DrawHandle(new Vector2(pos.X + size.X, pos.Y + size.Y), handleSize);
		}

		private void DrawHandle(Vector2 center, float size)
		{
			Vector2 half = Vector2.One * (size * 0.5f);
			DrawRect(new Rect2(center - half, Vector2.One * size), Colors.White, filled: true);
			DrawRect(new Rect2(center - half, Vector2.One * size), new Color(0.45f, 0.45f, 0.45f, 0.95f), filled: false, width: 1);
		}

		private void DrawRuleOfThirds(Vector2 pos, Vector2 size, Color color)
		{
			float x1 = pos.X + size.X / 3f;
			float x2 = pos.X + size.X * 2f / 3f;
			float y1 = pos.Y + size.Y / 3f;
			float y2 = pos.Y + size.Y * 2f / 3f;
			DrawDashedLine(new Vector2(x1, pos.Y), new Vector2(x1, pos.Y + size.Y), color);
			DrawDashedLine(new Vector2(x2, pos.Y), new Vector2(x2, pos.Y + size.Y), color);
			DrawDashedLine(new Vector2(pos.X, y1), new Vector2(pos.X + size.X, y1), color);
			DrawDashedLine(new Vector2(pos.X, y2), new Vector2(pos.X + size.X, y2), color);
		}

		private void DrawDashedLine(Vector2 from, Vector2 to, Color color)
		{
			const float dash = 8f;
			const float gap = 6f;
			Vector2 delta = to - from;
			float length = delta.Length();
			if (length <= 0.001f)
				return;

			Vector2 dir = delta / length;
			float offset = 0f;
			while (offset < length)
			{
				float seg = Math.Min(dash, length - offset);
				Vector2 a = from + dir * offset;
				Vector2 b = from + dir * (offset + seg);
				DrawLine(a, b, color, 1f);
				offset += dash + gap;
			}
		}

		private void DrawHoverFeedback(Vector2 pos, Vector2 size)
		{
			if (_hoverMode == SelectionMode.None)
				return;

			Color accent = new Color(1f, 0.85f, 0.2f, 0.95f);
			switch (_hoverMode)
			{
				case SelectionMode.Moving:
					DrawRect(new Rect2(pos, size), new Color(accent.R, accent.G, accent.B, 0.10f), filled: true);
					DrawRect(new Rect2(pos, size), accent, filled: false, width: 3);
					break;
				case SelectionMode.ResizeLeft:
					DrawEdgeHighlight(new Rect2(pos.X - 3f, pos.Y, 6f, size.Y), accent);
					break;
				case SelectionMode.ResizeRight:
					DrawEdgeHighlight(new Rect2(pos.X + size.X - 3f, pos.Y, 6f, size.Y), accent);
					break;
				case SelectionMode.ResizeTop:
					DrawEdgeHighlight(new Rect2(pos.X, pos.Y - 3f, size.X, 6f), accent);
					break;
				case SelectionMode.ResizeBottom:
					DrawEdgeHighlight(new Rect2(pos.X, pos.Y + size.Y - 3f, size.X, 6f), accent);
					break;
				case SelectionMode.ResizeTopLeft:
					DrawCornerHighlight(pos, accent);
					break;
				case SelectionMode.ResizeTopRight:
					DrawCornerHighlight(new Vector2(pos.X + size.X, pos.Y), accent);
					break;
				case SelectionMode.ResizeBottomLeft:
					DrawCornerHighlight(new Vector2(pos.X, pos.Y + size.Y), accent);
					break;
				case SelectionMode.ResizeBottomRight:
					DrawCornerHighlight(pos + size, accent);
					break;
			}
		}

		private void DrawEdgeHighlight(Rect2 rect, Color color)
		{
			DrawRect(rect, new Color(color.R, color.G, color.B, 0.28f), filled: true);
			DrawRect(rect, color, filled: false, width: 1);
		}

		private void DrawCornerHighlight(Vector2 center, Color color)
		{
			const float size = 12f;
			Vector2 half = Vector2.One * (size * 0.5f);
			DrawRect(new Rect2(center - half, Vector2.One * size), new Color(color.R, color.G, color.B, 0.38f), filled: true);
			DrawRect(new Rect2(center - half, Vector2.One * size), color, filled: false, width: 2);
		}

		private bool TryMapToImage(Vector2 localPos, out Vector2I p)
		{
			p = default;
			if (_work == null)
				return false;
			if (!TryGetDisplayRect(out Rect2 disp, out float scale))
				return false;

			if (!disp.HasPoint(localPos))
				return false;

			Vector2 rel = localPos - disp.Position;
			int x = (int)Mathf.Floor(rel.X / scale);
			int y = (int)Mathf.Floor(rel.Y / scale);
			x = Math.Clamp(x, 0, _work.GetWidth() - 1);
			y = Math.Clamp(y, 0, _work.GetHeight() - 1);
			p = new Vector2I(x, y);
			return true;
		}

		private bool TryGetDisplayRect(out Rect2 rect, out float scale)
		{
			rect = default;
			scale = 1;
			if (_work == null)
				return false;

			Vector2 size = _target.Size;
			if (size.X <= 1 || size.Y <= 1)
				return false;

			float imgW = _work.GetWidth();
			float imgH = _work.GetHeight();
			if (imgW <= 0 || imgH <= 0)
				return false;

			float baseScale = MathF.Min(size.X / imgW, size.Y / imgH);
			scale = baseScale * _zoom;
			Vector2 drawSize = new Vector2(imgW * scale, imgH * scale);
			Vector2 centerOffset = (size - drawSize) * 0.5f;
			Vector2 finalOffset = centerOffset + _offset;
			rect = new Rect2(finalOffset, drawSize);
			return true;
		}

		private static Rect2I Normalize(Vector2I a, Vector2I b)
		{
			int x1 = Math.Min(a.X, b.X);
			int y1 = Math.Min(a.Y, b.Y);
			int x2 = Math.Max(a.X, b.X);
			int y2 = Math.Max(a.Y, b.Y);
			return new Rect2I(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
		}
	}
}
