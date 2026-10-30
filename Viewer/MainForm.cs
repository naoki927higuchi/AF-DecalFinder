using System.Text.Json;

namespace AF.DecalFinder;

internal sealed class MainForm : Form
{
    private readonly ImageViewControl view = new() { Dock = DockStyle.Fill, Mode = ImageViewMode.Detail, AccessibleName = "拡大画像（左ドラッグでパン）" };
    private readonly ImageViewControl overview = new() { Dock = DockStyle.Fill, Mode = ImageViewMode.Overview, AccessibleName = "全体画像（クリック・青い破線枠のドラッグで移動）" };
    private readonly OcrResultsList list = new();
    private readonly SplitContainer panes = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
    private readonly SplitContainer images = new() { Dock = DockStyle.Fill, SplitterWidth = 6 };
    private readonly CheckBox showList = new() { Text = "一覧表示", Checked = true, AutoSize = true, Margin = new Padding(12, 7, 5, 0) };
    private readonly ComboBox zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85, AccessibleName = "拡大倍率" };
    private int savedListWidth = 150;
    private readonly Button floatList = new() { Text = "一覧を切り離す", AutoSize = true, Height = 30 };
    private Form? floatingList;
    private bool closing;
    private readonly DigitsTextBox number = new() { Width = 130, Enabled = false, AccessibleName = "検索番号" };
    private readonly Label count = new() { AutoSize = true, Text = "検索不可", Margin = new Padding(12, 8, 0, 0) };
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "画像を開いてください。" };
    private readonly string jsonPath = Path.Combine(AppContext.BaseDirectory, "results", "600dpi.json");
    private OcrCatalog? catalog;
    private static string AppTitle => $"AF-DecalFinder {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";

    public MainForm(string? initialImage)
    {
        Text = $"{AppTitle} — Phase 4.1";
        ClientSize = new Size(1200, 880);
        MinimumSize = new Size(850, 450);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "画像を開く…", AutoSize = true, Height = 30 };
        open.Click += (_, _) => SelectImage();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.AddRange([open, showList, floatList, new Label { Text = "番号", AutoSize = true, Margin = new Padding(16, 7, 5, 0) }, number, count]);
        var detailToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4), WrapContents = false };
        detailToolbar.Controls.AddRange([new Label { Text = "拡大画像", AutoSize = true, Margin = new Padding(3, 5, 8, 0) }, zoom]);
        zoom.Items.AddRange(new object[] { "25%", "50%", "75%", "100%", "150%", "200%" });
        zoom.SelectedIndex = 3;
        zoom.SelectedIndexChanged += (_, _) => view.SetZoom(new double[] { .25, .5, .75, 1, 1.5, 2 }[zoom.SelectedIndex]);
        panes.Panel1.Controls.Add(list);
        panes.Panel2.Controls.Add(images);
        images.Panel1.Controls.Add(overview);
        images.Panel1.Controls.Add(new Label { Text = "全体画像 — 青い破線：拡大表示範囲", Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleLeft });
        images.Panel2.Controls.Add(view);
        images.Panel2.Controls.Add(detailToolbar);
        var strip = new StatusStrip();
        strip.Items.Add(status);
        Controls.Add(panes);
        Controls.Add(toolbar);
        Controls.Add(strip);
        number.TextChanged += (_, _) => Search();
        list.SelectionChanged += selected => { overview.SetSelections(selected); view.SetSelections(selected); };
        list.NavigateRequested += detection => view.CenterOn(new PointF(detection.Bounds.X + detection.Bounds.Width / 2f, detection.Bounds.Y + detection.Bounds.Height / 2f));
        floatList.Click += (_, _) => { if (floatingList == null) FloatList(); else DockList(); };
        view.ViewportChanged += () => overview.SetNavigationBounds(view.VisibleImageBounds);
        overview.NavigateRequested += view.CenterOn;
        showList.CheckedChanged += (_, _) =>
        {
            if (!showList.Checked && floatingList == null && !panes.Panel1Collapsed)
                savedListWidth = panes.SplitterDistance;
            ApplyListVisibility();
        };
        InstallDropHandlers(this);
        Shown += (_, _) =>
        {
            panes.Panel1MinSize = 60;
            panes.Panel2MinSize = 350;
            panes.SplitterDistance = 150;
            images.Panel1MinSize = 120;
            images.Panel2MinSize = 120;
            images.SplitterDistance = (images.ClientSize.Width - images.SplitterWidth) / 2;
            if (!string.IsNullOrWhiteSpace(initialImage)) OpenImage(initialImage);
        };
    }

    private void ApplyListVisibility()
    {
        panes.Panel1Collapsed = !showList.Checked || floatingList != null;
        if (!panes.Panel1Collapsed)
            panes.SplitterDistance = Math.Clamp(savedListWidth, panes.Panel1MinSize, Math.Max(panes.Panel1MinSize, panes.ClientSize.Width - panes.SplitterWidth - panes.Panel2MinSize));
        floatList.Text = floatingList == null ? "一覧を切り離す" : "一覧を戻す";
        floatList.Enabled = showList.Checked;
        if (floatingList != null)
        {
            if (showList.Checked) floatingList.Show(this);
            else floatingList.Hide();
        }
    }

    private void FloatList()
    {
        if (!panes.Panel1Collapsed) savedListWidth = panes.SplitterDistance;
        floatingList = new Form
        {
            Text = "OCR一覧 — Enter：フォーカス行へ移動", ClientSize = new Size(260, 600),
            MinimumSize = new Size(200, 250), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual
        };
        var area = Screen.FromControl(this).WorkingArea;
        floatingList.Location = new Point(Math.Clamp(Left + 30, area.Left, Math.Max(area.Left, area.Right - floatingList.Width)),
            Math.Clamp(Top + 80, area.Top, Math.Max(area.Top, area.Bottom - floatingList.Height)));
        floatingList.FormClosing += (_, _) =>
        {
            if (!closing) ReturnListToDock();
        };
        list.MoveTo(floatingList);
        ApplyListVisibility();
        list.Focus();
    }

    private void ReturnListToDock()
    {
        list.MoveTo(panes.Panel1);
        floatingList = null;
        ApplyListVisibility();
    }

    private void DockList()
    {
        floatingList?.Close(); // FormClosing reparents the existing list before its host is disposed.
        list.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        closing = !e.Cancel;
    }

    private void InstallDropHandlers(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = GetDroppedImage(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) =>
        {
            if (GetDroppedImage(e.Data) is string path) OpenImage(path);
        };
        foreach (Control child in control.Controls) InstallDropHandlers(child);
    }

    internal static string? GetDroppedImage(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
        return IsImageExtension(files[0]) ? files[0] : null;
    }

    private static bool IsImageExtension(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    private void SelectImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "マーキング指示図を開く", Filter = "画像ファイル (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenImage(dialog.FileName);
    }

    private void OpenImage(string path)
    {
        Bitmap loaded;
        try
        {
            if (!IsImageExtension(path)) throw new InvalidDataException("JPGまたはPNGを選択してください。");
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            // Clone to release the source file immediately. Do not apply EXIF rotation:
            // JSON coordinates refer to the original pixel matrix.
            loaded = DisplayBitmap.Create(source);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"画像を開けませんでした。\n{ex.Message}", "画像の読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        catalog = null;
        list.SetDetections(Array.Empty<Detection>());
        number.Clear();
        number.Enabled = false;
        overview.SetImage(loaded, takeOwnership: false);
        view.SetImage(loaded);
        zoom.SelectedIndex = 3;
        count.Text = "検索不可";
        Text = $"{AppTitle} — {Path.GetFileName(path)}";
        var description = $"{Path.GetFileName(path)}  |  {loaded.Width} × {loaded.Height} px";
        try
        {
            var candidate = OcrCatalog.Load(jsonPath);
            if (candidate.ImageSize != loaded.Size)
            {
                status.Text = $"{description}  |  寸法不一致：検索不可";
                MessageBox.Show(this, $"画像とOCR結果JSONの寸法が一致しません。赤枠表示を無効にしました。\n\n画像：{loaded.Width} × {loaded.Height}\nJSON：{candidate.ImageSize.Width} × {candidate.ImageSize.Height}", "寸法不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catalog = candidate;
            list.SetDetections(catalog.Detections);
            number.Enabled = true;
            status.Text = $"{description}  |  OCR結果 {catalog.Detections.Count}件  |  全体：クリック・枠ドラッグ / 拡大：左ドラッグ";
            Search();
            number.Focus();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            status.Text = $"{description}  |  OCR結果JSONがありません：検索不可（{jsonPath}）";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            status.Text = $"{description}  |  OCR結果JSONを読めません：検索不可";
            MessageBox.Show(this, $"OCR結果JSONを読み込めませんでした。\n{jsonPath}\n\n{ex.Message}", "JSONの読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Search()
    {
        var matches = catalog?.Find(number.Text) ?? Array.Empty<Detection>();
        view.SetHighlights(matches);
        overview.SetHighlights(matches);
        count.Text = catalog == null ? "検索不可" : $"{matches.Count}件";
    }
}
