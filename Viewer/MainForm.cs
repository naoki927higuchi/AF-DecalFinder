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
    private readonly EditStore store;
    private readonly AppSettingsStore settings;
    private ImageHistory history = new(null, Array.Empty<string>());
    private readonly ToolStripMenuItem recentImages = new("最近使った画像");
    private ImageIdentity? identity;
    private bool dirty;
    private bool adding;
    private bool listWasVisible;
    private readonly Button addNumber = new() { Text = "番号追加", Dock = DockStyle.Top, Height = 30, Enabled = false };
    private readonly Label addPrompt = new() { Text = "追加したい箇所をクリックしてください", AutoSize = true, Visible = false, Margin = new Padding(5, 8, 0, 0) };
    private readonly Button cancelAdd = new() { Text = "キャンセル", AutoSize = true, Visible = false };
    private static string AppTitle => $"AF-DecalFinder {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";

    public MainForm(string? initialImage, EditStore? editStore = null, AppSettingsStore? settingsStore = null)
    {
        store = editStore ?? new EditStore();
        settings = settingsStore ?? new AppSettingsStore();
        Text = AppTitle;
        ClientSize = new Size(1200, 880);
        MinimumSize = new Size(850, 450);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "画像を開く…", AutoSize = true, Height = 30 };
        open.Click += (_, _) => SelectImage();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.AddRange([open, showList, floatList, new Label { Text = "番号", AutoSize = true, Margin = new Padding(16, 7, 5, 0) }, number, count, addPrompt, cancelAdd]);
        toolbar.AutoSize = true; toolbar.WrapContents = true;
        var detailToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4), WrapContents = false };
        detailToolbar.Controls.AddRange([new Label { Text = "拡大画像", AutoSize = true, Margin = new Padding(3, 5, 8, 0) }, zoom]);
        zoom.Items.AddRange(new object[] { "25%", "50%", "75%", "100%", "150%", "200%" });
        zoom.SelectedIndex = 3;
        zoom.SelectedIndexChanged += (_, _) => view.SetZoom(new double[] { .25, .5, .75, 1, 1.5, 2 }[zoom.SelectedIndex]);
        panes.Panel1.Controls.Add(list);
        panes.Panel1.Controls.Add(addNumber);
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
        var menu = new MenuStrip { ShowItemToolTips = true };
        var fileMenu = new ToolStripMenuItem("ファイル");
        fileMenu.DropDownItems.Add("画像を開く…", null, (_, _) => SelectImage());
        fileMenu.DropDownItems.Add(recentImages);
        menu.Items.Add(fileMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
        UpdateHistoryMenu();
        number.TextChanged += (_, _) => Search();
        number.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            var first = list.Items.Cast<ListViewItem>().Select(row => (Detection)row.Tag!)
                .FirstOrDefault(d => number.Text.Length > 0 && d.Number == number.Text);
            if (first != null) view.CenterOn(new PointF(first.Bounds.X + first.Bounds.Width / 2f, first.Bounds.Y + first.Bounds.Height / 2f));
        };
        list.SelectionChanged += selected => { overview.SetSelections(selected); view.SetSelections(selected); };
        view.DetectionClicked += (record, add) => list.SelectDetection(record.Id, add);
        overview.DetectionClicked += (record, add) => list.SelectDetection(record.Id, add);
        list.DataChanged += Changed;
        list.EditRejected += message => MessageBox.Show(this, message, "番号編集", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        addNumber.Click += (_, _) => BeginAdd();
        cancelAdd.Click += (_, _) => EndAdd();
        view.AddPointClicked += point =>
        {
            using var dialog = new NumberDialog(number.Text);
            if (dialog.ShowDialog(this) == DialogResult.OK && identity != null)
            {
                AddAt(dialog.Number, point);
            }
            else EndAdd();
        };
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
            try { history = settings.Load(); UpdateHistoryMenu(); }
            catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
            // Explicit command-line image wins. Restore once, only when the recorded file exists.
            if (!string.IsNullOrWhiteSpace(initialImage)) OpenImage(initialImage);
            else if (identity == null && history.LastImagePath is string last && File.Exists(last)) OpenImage(last);
        };
    }

    private static bool SettingsError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or InvalidOperationException or ArgumentException or System.Security.SecurityException;

    private void NotifySettingsError(Exception ex) => MessageBox.Show(this,
        $"画像履歴の設定を読み書きできませんでした。画像の閲覧・編集は続行できます。\n{settings.FilePath}\n{ex.Message}",
        "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void UpdateHistoryMenu()
    {
        foreach (var item in recentImages.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
        recentImages.DropDownItems.Clear();
        recentImages.Enabled = history.RecentImages.Count > 0;
        foreach (var path in history.RecentImages)
        {
            var item = new ToolStripMenuItem(Path.GetFileName(path).Replace("&", "&&")) { ToolTipText = path };
            item.Click += (_, _) => OpenImage(path);
            recentImages.DropDownItems.Add(item);
        }
    }

    private void RememberImage(string path)
    {
        try { history = settings.RecordOpened(path); UpdateHistoryMenu(); }
        catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
    }

    private void ApplyListVisibility()
    {
        panes.Panel1Collapsed = adding || !showList.Checked || floatingList != null;
        if (!panes.Panel1Collapsed)
            panes.SplitterDistance = Math.Clamp(savedListWidth, panes.Panel1MinSize, Math.Max(panes.Panel1MinSize, panes.ClientSize.Width - panes.SplitterWidth - panes.Panel2MinSize));
        floatList.Text = floatingList == null ? "一覧を切り離す" : "一覧を戻す";
        floatList.Enabled = showList.Checked && !adding;
        if (floatingList != null)
        {
            if (showList.Checked && !adding) floatingList.Show(this);
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
        floatingList.Controls.Add(addNumber);
        ApplyListVisibility();
        list.Focus();
    }

    private void ReturnListToDock()
    {
        list.MoveTo(panes.Panel1);
        panes.Panel1.Controls.Add(addNumber);
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
        if (dirty && !ConfirmLeave()) { e.Cancel = true; return; }
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
        if (dirty && !ConfirmLeave()) return;
        Bitmap loaded;
        ImageIdentity loadedIdentity;
        try
        {
            if (!IsImageExtension(path)) throw new InvalidDataException("JPGまたはPNGを選択してください。");
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            // Clone to release the source file immediately. Do not apply EXIF rotation:
            // JSON coordinates refer to the original pixel matrix.
            loaded = DisplayBitmap.Create(source);
            loadedIdentity = ImageIdentity.Read(path, loaded.Size);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"画像を開けませんでした。\n{ex.Message}", "画像の読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        RememberImage(loadedIdentity.AbsolutePath);
        EndAdd();
        dirty = false;
        identity = loadedIdentity;
        catalog = null;
        addNumber.Enabled = true;
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
            OcrCatalog? candidate = null;
            try { candidate = store.Load(identity); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                MessageBox.Show(this, $"編集データを復元できませんでした。元のOCR結果を使用します。\n{ex.Message}\n\n既存の保存ファイルは保持し、次回保存時にはバックアップします。", "編集データの警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            bool restored = candidate != null;
            candidate ??= OcrCatalog.Load(jsonPath);
            if (candidate.ImageSize != loaded.Size)
            {
                status.Text = $"{description}  |  寸法不一致：検索不可";
                MessageBox.Show(this, $"画像とOCR結果JSONの寸法が一致しません。赤枠表示を無効にしました。\n\n画像：{loaded.Width} × {loaded.Height}\nJSON：{candidate.ImageSize.Width} × {candidate.ImageSize.Height}", "寸法不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catalog = candidate;
            list.SetDetections(catalog.Detections);
            overview.SetDetections(catalog.Detections);
            view.SetDetections(catalog.Detections);
            number.Enabled = true;
            status.Text = $"{description}  |  検出結果 {catalog.Detections.Count}件  |  {(restored ? "編集データ復元済み" : "元のOCR結果")}  |  全体：クリック・枠ドラッグ / 拡大：左ドラッグ";
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

    internal void BeginAdd()
    {
        if (identity == null || adding) return;
        listWasVisible = showList.Checked;
        if (floatingList == null && !panes.Panel1Collapsed) savedListWidth = panes.SplitterDistance;
        adding = true; view.AddingPoint = true;
        addPrompt.Visible = cancelAdd.Visible = true; showList.Enabled = false;
        ApplyListVisibility();
    }

    internal void EndAdd()
    {
        if (!adding) return;
        adding = false; view.AddingPoint = false;
        addPrompt.Visible = cancelAdd.Visible = false; showList.Enabled = true;
        showList.Checked = listWasVisible;
        ApplyListVisibility();
    }

    internal void AddAt(string value, PointF point)
    {
        if (!adding || identity == null) return;
        catalog ??= new OcrCatalog(new Size(identity.Width, identity.Height), []);
        var record = catalog.AddManual(value, point);
        list.SetDetections(catalog.Detections, preserveSelection: true);
        EndAdd();
        list.SelectDetection(record.Id, false);
        Changed();
    }

    private void Changed()
    {
        if (catalog == null) return;
        dirty = true;
        number.Enabled = true;
        view.SetDetections(catalog.Detections); overview.SetDetections(catalog.Detections);
        Search(); // Invalidate both images; keeps transform and selection intact.
        SaveChanges();
    }

    private bool SaveChanges()
    {
        if (!dirty || identity == null || catalog == null) return true;
        try
        {
            store.Save(identity, catalog); dirty = false;
            status.Text = $"{Path.GetFileName(identity.AbsolutePath)}  |  検出結果 {catalog.Detections.Count}件  |  保存済み";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            status.Text = "未保存 — 保存に失敗しました。編集内容はこの画面に保持されています。";
            MessageBox.Show(this, $"編集データを保存できませんでした。\n{ex.Message}\n\n保存先：{store.DirectoryPath}", "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool ConfirmLeave()
    {
        if (SaveChanges()) return true;
        return MessageBox.Show(this, "未保存の変更があります。変更を破棄して続けますか？", "未保存の変更", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void Search()
    {
        var matches = catalog?.Find(number.Text) ?? Array.Empty<Detection>();
        view.SetHighlights(matches);
        overview.SetHighlights(matches);
        count.Text = catalog == null ? "検索不可" : $"{matches.Count}件";
    }
}
