using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CustomCardPortraits.Editor;

public sealed class MainForm : Form
{
	private readonly ListBox _cards = new() { Dock = DockStyle.Left, Width = 320 };

	private readonly PictureBox _originalPreview = new()
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = SystemColors.ControlDark
	};

	private readonly PictureBox _workPreview = new()
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = SystemColors.ControlDark
	};

	private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
	private readonly Button _pickSts2Folder = new() { Text = "选择 Slay the Spire 2 文件夹…" };
	private readonly Button _pickImage = new() { Text = "选择图片…" };
	private readonly Button _cropAndSave = new() { Text = "裁切并保存" };
	private readonly Button _openTarget = new() { Text = "打开覆盖目录" };

	private string? _sts2Root;
	private string? _cardAtlasSpritesDir;
	private List<CardEntry> _entries = new();

	private Bitmap? _originalBitmap;
	private Bitmap? _workBitmap;

	// Crop state is stored in image coordinates (based on _workBitmap)
	private bool _isDragging;
	private Point _dragStartImg;
	private Rectangle? _cropRectImg;

	public MainForm()
	{
		Text = "CustomCardPortraits 外部编辑器";
		MinimumSize = new Size(1100, 720);

		var rightTop = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 44,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(8, 8, 8, 0)
		};
		rightTop.Controls.Add(_pickSts2Folder);
		rightTop.Controls.Add(_pickImage);
		rightTop.Controls.Add(_cropAndSave);
		rightTop.Controls.Add(_openTarget);

		var split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			SplitterDistance = 320,
			Panel1MinSize = 200,
			Panel2MinSize = 200
		};
		split.Panel1.Controls.Add(WrapWithHeader("原图（游戏资源）", _originalPreview));
		split.Panel2.Controls.Add(WrapWithHeader("覆盖/工作图（可裁切）", _workPreview));

		var right = new Panel { Dock = DockStyle.Fill };
		right.Controls.Add(split);
		right.Controls.Add(rightTop);
		right.Controls.Add(_status);

		Controls.Add(right);
		Controls.Add(_cards);

		_cards.SelectedIndexChanged += (_, _) => RefreshSelection();
		_pickSts2Folder.Click += (_, _) => ChooseSts2Folder();
		_pickImage.Click += (_, _) => PickWorkImage();
		_cropAndSave.Click += (_, _) => CropAndSave();
		_openTarget.Click += (_, _) => OpenOverrideRoot();

		_workPreview.Paint += (_, e) => DrawCropOverlay(e.Graphics);
		_workPreview.MouseDown += (_, e) => BeginCropDrag(e.Location);
		_workPreview.MouseMove += (_, e) => ContinueCropDrag(e.Location);
		_workPreview.MouseUp += (_, _) => EndCropDrag();

		_pickImage.Enabled = false;
		_cropAndSave.Enabled = false;
		_openTarget.Enabled = false;

		TryAutoDetectSts2Folder();
	}

	private static Control WrapWithHeader(string title, Control content)
	{
		var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
		var header = new Label
		{
			Dock = DockStyle.Top,
			Height = 20,
			Text = title,
			TextAlign = ContentAlignment.MiddleLeft
		};
		panel.Controls.Add(content);
		panel.Controls.Add(header);
		content.Dock = DockStyle.Fill;
		return panel;
	}

	private void TryAutoDetectSts2Folder()
	{
		string? dir = AppContext.BaseDirectory;
		for (int i = 0; i < 8 && dir != null; i++)
		{
			var cand = Path.Combine(dir, "Slay the Spire 2");
			if (Directory.Exists(cand) && File.Exists(Path.Combine(cand, "project.godot")))
			{
				SetSts2Root(cand);
				return;
			}
			dir = Directory.GetParent(dir)?.FullName;
		}

		SetStatus("未自动定位到 StS2 文件夹，请手动选择。", isError: false);
	}

	private void ChooseSts2Folder()
	{
		using var dlg = new FolderBrowserDialog
		{
			Description = "请选择包含 project.godot 的 Slay the Spire 2 文件夹",
			UseDescriptionForTitle = true,
			ShowNewFolderButton = false
		};

		if (dlg.ShowDialog(this) != DialogResult.OK)
			return;

		if (!File.Exists(Path.Combine(dlg.SelectedPath, "project.godot")))
		{
			MessageBox.Show(this, "所选文件夹未找到 project.godot。请选游戏工程根目录（工作区里的 Slay the Spire 2 文件夹）。", "无效文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		SetSts2Root(dlg.SelectedPath);
	}

	private void SetSts2Root(string sts2Root)
	{
		_sts2Root = sts2Root;
		_cardAtlasSpritesDir = Path.Combine(_sts2Root, "images", "atlases", "card_atlas.sprites");
		if (!Directory.Exists(_cardAtlasSpritesDir))
		{
			_entries = new();
			_cards.Items.Clear();
			_pickImage.Enabled = false;
			_cropAndSave.Enabled = false;
			_openTarget.Enabled = false;
			SetStatus($"未找到目录: {_cardAtlasSpritesDir}", isError: true);
			return;
		}

		ReloadCardList();
		_pickImage.Enabled = true;
		_cropAndSave.Enabled = true;
		_openTarget.Enabled = true;
		SetStatus($"已定位 StS2: {_sts2Root}", isError: false);
	}

	private void ReloadCardList()
	{
		if (_cardAtlasSpritesDir == null)
			return;

		_entries = ScanCards(_cardAtlasSpritesDir);
		_cards.BeginUpdate();
		try
		{
			_cards.Items.Clear();
			foreach (var e in _entries)
				_cards.Items.Add(e.Display);
		}
		finally
		{
			_cards.EndUpdate();
		}

		if (_cards.Items.Count > 0)
			_cards.SelectedIndex = 0;
	}

	private static List<CardEntry> ScanCards(string cardAtlasSpritesDir)
	{
		var results = new List<CardEntry>();
		foreach (var poolDir in Directory.EnumerateDirectories(cardAtlasSpritesDir))
		{
			string pool = Path.GetFileName(poolDir);
			if (string.Equals(pool, ".generated", StringComparison.OrdinalIgnoreCase))
				continue;

			foreach (var tres in Directory.EnumerateFiles(poolDir, "*.tres", SearchOption.TopDirectoryOnly))
			{
				string id = Path.GetFileNameWithoutExtension(tres);
				if (string.Equals(id, "beta", StringComparison.OrdinalIgnoreCase))
					continue;

				results.Add(new CardEntry(pool, id));
			}
		}

		results.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
		return results;
	}

	private void RefreshSelection()
	{
		DisposePreviewBitmaps();
		_cropRectImg = null;
		_workPreview.Invalidate();

		var entry = GetSelectedEntry();
		if (entry == null)
		{
			SetStatus("未选择卡牌。", isError: false);
			return;
		}

		LoadOriginal(entry);
		LoadOverrideAsWork(entry);

		string overridePath = entry.GetOverrideAbsolutePath();
		SetStatus(File.Exists(overridePath)
			? $"已加载覆盖图，可拖拽裁切。保存路径: {overridePath}"
			: $"未找到覆盖图。可选择图片→裁切并保存。保存路径: {overridePath}",
			isError: false);
	}

	private void LoadOriginal(CardEntry entry)
	{
		_originalBitmap?.Dispose();
		_originalBitmap = null;
		_originalPreview.Image = null;

		if (_sts2Root == null)
			return;

		string path = entry.GetOriginalAbsolutePath(_sts2Root);
		if (!File.Exists(path))
			return;

		_originalBitmap = LoadBitmapNoLock(path);
		_originalPreview.Image = _originalBitmap;
	}

	private void LoadOverrideAsWork(CardEntry entry)
	{
		_workBitmap?.Dispose();
		_workBitmap = null;
		_workPreview.Image = null;

		string overridePath = entry.GetOverrideAbsolutePath();
		if (!File.Exists(overridePath))
			return;

		_workBitmap = LoadBitmapNoLock(overridePath);
		_workPreview.Image = _workBitmap;
	}

	private void PickWorkImage()
	{
		var entry = GetSelectedEntry();
		if (entry == null)
			return;

		using var dlg = new OpenFileDialog
		{
			Title = "选择要导入的图片（png/jpg）",
			Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
			CheckFileExists = true,
			Multiselect = false
		};

		if (dlg.ShowDialog(this) != DialogResult.OK)
			return;

		try
		{
			_workBitmap?.Dispose();
			_workBitmap = LoadBitmapNoLock(dlg.FileName);
			_workPreview.Image = _workBitmap;
			_cropRectImg = null;
			_workPreview.Invalidate();
			SetStatus("已加载工作图：在下方预览中拖拽框选裁切区域。", isError: false);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			SetStatus($"加载失败: {ex.Message}", isError: true);
		}
	}

	private void CropAndSave()
	{
		var entry = GetSelectedEntry();
		if (entry == null)
			return;
		if (_workBitmap == null)
		{
			SetStatus("没有工作图。请先点击“选择图片…”。", isError: true);
			return;
		}

		try
		{
			string target = entry.GetOverrideAbsolutePath();
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);

			using Bitmap output = CreateCroppedBitmap(_workBitmap, _cropRectImg);
			output.Save(target, System.Drawing.Imaging.ImageFormat.Png);
			SetStatus($"已保存覆盖图: {target}", isError: false);
			LoadOverrideAsWork(entry);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			SetStatus($"保存失败: {ex.Message}", isError: true);
		}
	}

	private static Bitmap CreateCroppedBitmap(Bitmap source, Rectangle? cropRectImg)
	{
		if (cropRectImg == null)
			return new Bitmap(source);

		Rectangle r = NormalizeRect(cropRectImg.Value);
		r.Intersect(new Rectangle(0, 0, source.Width, source.Height));
		if (r.Width <= 0 || r.Height <= 0)
			return new Bitmap(source);

		var dst = new Bitmap(r.Width, r.Height);
		using var g = Graphics.FromImage(dst);
		g.DrawImage(source, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
		return dst;
	}

	private void OpenOverrideRoot()
	{
		try
		{
			string root = CardEntry.GetOverrideRootAbsolutePath();
			Directory.CreateDirectory(root);
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = root,
				UseShellExecute = true,
			});
		}
		catch (Exception ex)
		{
			SetStatus($"打开目录失败: {ex.Message}", isError: true);
		}
	}

	private void BeginCropDrag(Point mouse)
	{
		if (_workBitmap == null)
			return;
		if (!TryMapToImagePoint(_workPreview, _workBitmap, mouse, out Point imgPt))
			return;

		_isDragging = true;
		_dragStartImg = imgPt;
		_cropRectImg = new Rectangle(imgPt.X, imgPt.Y, 1, 1);
		_workPreview.Invalidate();
	}

	private void ContinueCropDrag(Point mouse)
	{
		if (!_isDragging || _workBitmap == null)
			return;
		if (!TryMapToImagePoint(_workPreview, _workBitmap, mouse, out Point imgPt))
			return;

		_cropRectImg = new Rectangle(
			Math.Min(_dragStartImg.X, imgPt.X),
			Math.Min(_dragStartImg.Y, imgPt.Y),
			Math.Abs(_dragStartImg.X - imgPt.X),
			Math.Abs(_dragStartImg.Y - imgPt.Y));
		_workPreview.Invalidate();
	}

	private void EndCropDrag()
	{
		_isDragging = false;
		if (_cropRectImg is { } r)
			SetStatus($"已选择裁切区域: {r.Width}x{r.Height}（像素）", isError: false);
	}

	private void DrawCropOverlay(Graphics g)
	{
		if (_workBitmap == null || _cropRectImg == null)
			return;

		Rectangle rImg = NormalizeRect(_cropRectImg.Value);
		if (rImg.Width <= 0 || rImg.Height <= 0)
			return;

		if (!TryGetImageDisplayRect(_workPreview, _workBitmap, out Rectangle display))
			return;

		float scaleX = (float)display.Width / _workBitmap.Width;
		float scaleY = (float)display.Height / _workBitmap.Height;

		var rCtl = new Rectangle(
			display.X + (int)Math.Round(rImg.X * scaleX),
			display.Y + (int)Math.Round(rImg.Y * scaleY),
			Math.Max(1, (int)Math.Round(rImg.Width * scaleX)),
			Math.Max(1, (int)Math.Round(rImg.Height * scaleY)));

		using var pen = new Pen(Color.Red, 2);
		g.DrawRectangle(pen, rCtl);
	}

	private static Rectangle NormalizeRect(Rectangle r)
	{
		int x1 = Math.Min(r.Left, r.Right);
		int y1 = Math.Min(r.Top, r.Bottom);
		int x2 = Math.Max(r.Left, r.Right);
		int y2 = Math.Max(r.Top, r.Bottom);
		return new Rectangle(x1, y1, x2 - x1, y2 - y1);
	}

	private static bool TryMapToImagePoint(PictureBox pb, Bitmap img, Point mouse, out Point imgPt)
	{
		imgPt = default;
		if (!TryGetImageDisplayRect(pb, img, out Rectangle display))
			return false;
		if (!display.Contains(mouse))
			return false;

		float scaleX = (float)img.Width / display.Width;
		float scaleY = (float)img.Height / display.Height;
		int x = (int)Math.Floor((mouse.X - display.X) * scaleX);
		int y = (int)Math.Floor((mouse.Y - display.Y) * scaleY);
		x = Math.Clamp(x, 0, img.Width - 1);
		y = Math.Clamp(y, 0, img.Height - 1);
		imgPt = new Point(x, y);
		return true;
	}

	private static bool TryGetImageDisplayRect(PictureBox pb, Bitmap img, out Rectangle rect)
	{
		rect = default;
		if (pb.Width <= 0 || pb.Height <= 0)
			return false;

		float imgAspect = (float)img.Width / img.Height;
		float boxAspect = (float)pb.Width / pb.Height;

		int drawWidth;
		int drawHeight;
		int offsetX;
		int offsetY;
		if (imgAspect > boxAspect)
		{
			drawWidth = pb.Width;
			drawHeight = (int)Math.Round(pb.Width / imgAspect);
			offsetX = 0;
			offsetY = (pb.Height - drawHeight) / 2;
		}
		else
		{
			drawHeight = pb.Height;
			drawWidth = (int)Math.Round(pb.Height * imgAspect);
			offsetY = 0;
			offsetX = (pb.Width - drawWidth) / 2;
		}

		rect = new Rectangle(offsetX, offsetY, drawWidth, drawHeight);
		return rect.Width > 0 && rect.Height > 0;
	}

	private static Bitmap LoadBitmapNoLock(string path)
	{
		using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var bmp = new Bitmap(fs);
		return new Bitmap(bmp);
	}

	private void DisposePreviewBitmaps()
	{
		_originalPreview.Image = null;
		_workPreview.Image = null;
		_originalBitmap?.Dispose();
		_workBitmap?.Dispose();
		_originalBitmap = null;
		_workBitmap = null;
	}

	private CardEntry? GetSelectedEntry()
	{
		int idx = _cards.SelectedIndex;
		if (idx < 0 || idx >= _entries.Count)
			return null;
		return _entries[idx];
	}

	private void SetStatus(string text, bool isError)
	{
		_status.Text = text;
		_status.ForeColor = isError ? Color.Maroon : SystemColors.ControlText;
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		DisposePreviewBitmaps();
		base.OnFormClosed(e);
	}

	private sealed class CardEntry
	{
		public string Pool { get; }
		public string Id { get; }
		public string Display => $"{Pool}/{Id}";

		public CardEntry(string pool, string id)
		{
			Pool = pool;
			Id = id;
		}

		public string GetOriginalAbsolutePath(string sts2Root)
		{
			return Path.Combine(sts2Root, "images", "packed", "card_portraits", Pool.ToLowerInvariant(), $"{Id.ToLowerInvariant()}.png");
		}

		public string GetOverrideAbsolutePath()
		{
			return Path.Combine(GetOverrideRootAbsolutePath(), "images", "packed", "card_portraits", Pool.ToLowerInvariant(), $"{Id.ToLowerInvariant()}.png");
		}

		public static string GetOverrideRootAbsolutePath()
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			return Path.Combine(appData, "Godot", "app_userdata", "SlayTheSpire2", "CustomCardPortraits");
		}
	}
}
