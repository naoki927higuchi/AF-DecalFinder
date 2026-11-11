using System.Text.Json;

namespace AF.DecalFinder;

internal sealed partial class MainForm : Form
{
    private readonly ImageViewControl view = new() { Dock = DockStyle.Fill, Mode = ImageViewMode.Detail, AccessibleName = "拡大画像（左ドラッグでパン）" };
    private readonly ImageViewControl overview = new() { Dock = DockStyle.Fill, Mode = ImageViewMode.Overview, AccessibleName = "全体画像（クリック・青い破線枠のドラッグで移動）" };
    private readonly OcrResultsList list = new();
    private readonly SplitContainer panes = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
    private readonly SplitContainer images = new() { Dock = DockStyle.Fill, SplitterWidth = 6 };
    private readonly CheckBox showList = new() { Text = "一覧表示", Checked = true, AutoSize = true, Margin = new Padding(12, 7, 5, 0) };
    private readonly CheckBox unknownOnly = new() { Text = "未認識のみ表示", AutoSize = true, Margin = new Padding(8, 7, 5, 0) };
    private readonly ComboBox zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85, AccessibleName = "拡大倍率" };
    private readonly ComboBox frameWidth = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, AccessibleName = "枠線幅" };
    private bool restoringFrameWidth;
    private readonly CheckBox highlightFill = new() { Text = "強調塗りつぶし", AutoSize = true, Margin = new Padding(8, 7, 5, 0) };
    private int savedListWidth = 150;
    private readonly Button floatList = new() { Text = "一覧を切り離す", AutoSize = true, Height = 30 };
    private Form? floatingList;
    private bool closing;
    private FormWindowState lastVisibleState = FormWindowState.Normal;
    private readonly DigitsTextBox number = new() { Width = 130, Enabled = false, AccessibleName = "検索番号" };
    private readonly Label count = new() { AutoSize = true, Text = "検索不可", Margin = new Padding(12, 8, 0, 0) };
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "画像を開いてください。" };
    private readonly IOcrRunner ocr;
    private CancellationTokenSource? analysisCancellation;
    private int analysisGeneration;
    internal Task ActiveOcrTask { get; private set; } = Task.CompletedTask;
    internal bool IsAnalyzing { get; private set; }
    internal Func<int, bool>? ConfirmDeletion;
    internal Func<Detection, Point, string?>? ImageEditPrompt;
    private OcrCatalog? catalog;
    private readonly EditStore store;
    private readonly AppSettingsStore settings;
    private ImageHistory history = new(null, Array.Empty<string>());
    private readonly ToolStripMenuItem reanalyze = new("OCRを再解析…") { Enabled = false };
    private readonly ToolStripMenuItem recentImages = new("最近使った画像");
    private ImageIdentity? identity;
    private bool dirty;
    private bool adding;
    private bool listWasVisible;
    private readonly Button addNumber = new() { Text = "番号追加", Dock = DockStyle.Top, Height = 30, Enabled = false };
    private readonly Label addPrompt = new() { Text = "追加したい箇所をクリックしてください", AutoSize = true, Visible = false, Margin = new Padding(5, 8, 0, 0) };
    private readonly Button cancelAdd = new() { Text = "キャンセル", AutoSize = true, Visible = false };
    private static string AppTitle => $"AF-DecalFinder {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";

    public MainForm(string? initialImage, EditStore? editStore = null, AppSettingsStore? settingsStore = null, IOcrRunner? ocrRunner = null)
    {
        store = editStore ?? new EditStore();
        ocr = ocrRunner ?? new OcrRunner();
        settings = settingsStore ?? new AppSettingsStore();
        Text = AppTitle;
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("AF.DecalFinder.App.ico")!)
            Icon = new Icon(iconStream);
        ClientSize = new Size(1200, 880);
        MinimumSize = new Size(850, 450);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "画像を開く…", AutoSize = true, Height = 30 };
        open.Click += (_, _) => SelectImage();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.AddRange([open, showList, floatList, new Label { Text = "番号", AutoSize = true, Margin = new Padding(16, 7, 5, 0) }, number, count, addPrompt, cancelAdd]);
        toolbar.AutoSize = true; toolbar.WrapContents = true;
        toolbar.Controls.Add(unknownOnly);
        unknownOnly.CheckedChanged += (_, _) => list.FilterUnrecognized(unknownOnly.Checked);
        toolbar.Controls.AddRange([new Label { Text = "枠線幅", AutoSize = true, Margin = new Padding(12, 7, 5, 0) }, frameWidth]);
        frameWidth.Items.AddRange(new object[] { "1 px", "2 px", "3 px", "4 px" });
        frameWidth.SelectedIndex = 1;
        frameWidth.SelectedIndexChanged += (_, _) =>
        {
            int width = frameWidth.SelectedIndex + 1;
            view.FrameWidthPx = overview.FrameWidthPx = width;
            if (!restoringFrameWidth)
            {
                try { settings.SaveFrameWidth(width); }
                catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
            }
        };
        toolbar.Controls.Add(highlightFill);
        highlightFill.CheckedChanged += (_, _) =>
        {
            view.HighlightFill = overview.HighlightFill = highlightFill.Checked;
            if (!restoringFrameWidth)
            {
                try { settings.SaveHighlightFill(highlightFill.Checked); }
                catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
            }
        };
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
        var strip = new StatusStrip { ShowItemToolTips = true };
        strip.Items.Add(status);
        Controls.Add(panes);
        Controls.Add(toolbar);
        Controls.Add(strip);
        var menu = new MenuStrip { ShowItemToolTips = true };
        var fileMenu = new ToolStripMenuItem("ファイル");
        fileMenu.DropDownItems.Add("画像を開く…", null, (_, _) => SelectImage());
        fileMenu.DropDownItems.Add(recentImages);
        fileMenu.DropDownItems.Add(reanalyze);
        reanalyze.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "現在のデータを退避してOCRを再解析します。画像内容が同一の場合、手動修正・追加・削除・済みを維持し、未修正のOCR結果を更新します。実行しますか？",
                "OCR再解析", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Reanalyze();
        };
        menu.Items.Add(fileMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
        InitializeCommands(menu, fileMenu);
        UpdateHistoryMenu();
        number.TextChanged += (_, _) => Search();
        number.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            var first = list.OrderedDetections
                .FirstOrDefault(d => !d.IsUnrecognized && !d.IsDeleted && number.Text.Length > 0 && d.Number == number.Text);
            if (first != null) view.CenterOn(first.UiCenter);
        };
        list.SelectionChanged += selected => { overview.SetSelections(selected); view.SetSelections(selected); UpdateCommands(); };
        list.BeforeDataChange += BeginMutation;
        list.EditHandler = CommitLabel;
        list.ReviewRequested += ReviewSelection;
        list.UndoRequested += Undo;
        view.ControlClickTarget = record => list.ContainsDetection(record.Id);
        view.DetectionClicked += (record, control) =>
        {
            if (control) list.SelectDetection(record.Id, add: true, toggle: true);
            else SelectFromImage(record, false);
        };
        overview.DetectionClicked += SelectFromImage;
        view.DetectionEditRequested += (record, point) =>
        {
            if (IsAnalyzing || transferBusy || adding || catalog == null) return;
            list.SuppressPendingNavigation();
            string? value;
            if (ImageEditPrompt != null) value = ImageEditPrompt(record, point);
            else
            {
                using var dialog = new NumberDialog(record.IsUnrecognized ? "" : record.Number, point);
                value = dialog.ShowDialog(this) == DialogResult.OK ? dialog.Number : null;
            }
            if (value != null && catalog.Detections.Any(d => ReferenceEquals(d, record) && !d.IsDeleted))
                list.RenameFromImage(record.Id, value);
        };
        list.DataChanged += Changed;
        list.DeleteRequested += selected =>
        {
            if (!confirmDelete || (ConfirmDeletion?.Invoke(selected.Count) ?? (MessageBox.Show(this, $"選択した{selected.Count}件を誤検出として削除しますか？", "削除の確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK))) DeleteDetections(selected);
        };
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
        list.NavigateRequested += detection => view.CenterOn(detection.UiCenter);
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); // Apply WinForms DPI autoscaling before restoring physical screen bounds.
        try { confirmDelete=settings.LoadDeleteConfirmation(); deleteConfirmation.Checked=confirmDelete; }
        catch(Exception ex) when(SettingsError(ex)) { NotifySettingsError(ex); }
        restoringFrameWidth = true;
        try { frameWidth.SelectedIndex = settings.LoadFrameWidth() - 1; highlightFill.Checked = settings.LoadHighlightFill(); }
        catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
        finally { restoringFrameWidth = false; }
        try
        {
            if (settings.LoadWindow() is { Width: > 0, Height: > 0 } saved)
            {
                var bounds = saved.Fit(Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), MinimumSize);
                MinimumSize = new Size(Math.Min(MinimumSize.Width, bounds.Width), Math.Min(MinimumSize.Height, bounds.Height));
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
                lastVisibleState = saved.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                WindowState = lastVisibleState;
            }
        }
        catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Visible && WindowState != FormWindowState.Minimized) lastVisibleState = WindowState;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (WindowState == FormWindowState.Normal)
            BeginInvoke(() =>
            {
                if (IsDisposed || WindowState != FormWindowState.Normal) return;
                Bounds = WindowPlacement.From(Bounds, false).Fit(Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), MinimumSize);
            });
    }

    private static bool SettingsError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or InvalidOperationException or ArgumentException or System.Security.SecurityException;

    private void NotifySettingsError(Exception ex) => MessageBox.Show(this,
        $"アプリ設定を読み書きできませんでした。画像の閲覧・編集は続行できます。\n{settings.FilePath}\n{ex.Message}",
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
            UseWaitCursor = IsAnalyzing, Text = "OCR一覧 — Enter：フォーカス行へ移動", ClientSize = new Size(260, 600),
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
        if(transferBusy){e.Cancel=true;return;}
        if (dirty && !ConfirmLeave()) { e.Cancel = true; return; }
        base.OnFormClosing(e);
        closing = !e.Cancel;
        if (closing)
        {
            analysisCancellation?.Cancel();
            var normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var state = WindowState == FormWindowState.Minimized ? lastVisibleState : WindowState;
            try { settings.SaveWindow(WindowPlacement.From(normalBounds, state == FormWindowState.Maximized)); }
            catch (Exception ex) when (SettingsError(ex)) { NotifySettingsError(ex); }
        }
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
        if(transferBusy)return;
        if (dirty && !ConfirmLeave()) return;
        Bitmap loaded;
        ImageIdentity loadedIdentity;
        try
        {
            if (!IsImageExtension(path)) throw new InvalidDataException("JPGまたはPNGを選択してください。");
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            // Clone to release the source file immediately. Apply the EXIF orientation (smartphone photos);
            // JSON coordinates refer to the upright image and the OCR worker receives the same orientation.
            int orientation = ImageOrientation.Read(source);
            loaded = DisplayBitmap.Create(source, orientation);
            loadedIdentity = ImageIdentity.Read(path, loaded.Size, orientation);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"画像を開けませんでした。\n{ex.Message}", "画像の読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        analysisCancellation?.Cancel();
        analysisCancellation?.Dispose();
        analysisCancellation = new CancellationTokenSource();
        int generation = ++analysisGeneration;
        RememberImage(loadedIdentity.AbsolutePath);
        EndAdd();
        dirty = false;
        undoState = null;
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
        ActiveOcrTask = LoadAnalysisAsync(loadedIdentity, description, generation, analysisCancellation.Token);
    }

    private void SetAnalyzing(bool value)
    {
        unknownOnly.Enabled = !value;
        IsAnalyzing = value;
        UseWaitCursor = value;
        if (floatingList != null) floatingList.UseWaitCursor = value;
        list.Enabled = !value;
        reanalyze.Enabled = !value && identity != null;
        addNumber.Enabled = !value && identity != null;
        number.Enabled = !value && catalog != null;
        UpdateCommands();
    }

    private async Task LoadAnalysisAsync(ImageIdentity image, string description, int generation, CancellationToken token, bool force = false)
    {
        SetAnalyzing(true);
        status.ToolTipText = "";
        status.Text = $"{description}  |  解析データ確認中…";
        string? archive = null;
        try
        {
            OcrCatalog? candidate;
            if(!force)ResolveOpenedImage(image);
            try
            {
                if (force) { archive = store.ArchiveInvalid(image.AbsolutePath); candidate = null; }
                else candidate = store.Load(image);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
            {
                // Copy first. If preservation fails, do not overwrite the user's old work.
                archive = store.ArchiveInvalid(image.AbsolutePath);
                candidate = null;
            }
            bool restored = candidate != null;
            if (candidate == null)
            {
                status.Text = $"{description}  |  OCR解析中…";
                candidate = await ocr.RunAsync(image, token);
            }
            if (token.IsCancellationRequested || generation != analysisGeneration || closing || IsDisposed) return;
            if (candidate.ImageSize != new Size(image.Width, image.Height))
                throw new InvalidDataException("OCR結果の画像寸法が一致しません。");
            if (!image.Matches(image.Reread(candidate.ImageSize)))
                throw new IOException("解析中に画像が更新されました。画像を開き直してください。");
            int preserved = !restored && archive != null ? store.RestoreProtected(image, candidate, archive) : 0;
            catalog = candidate;
            list.SetDetections(catalog.Detections);
            overview.SetDetections(catalog.Detections);
            view.SetDetections(catalog.Detections);
            status.Text = $"{description}  |  検出{catalog.ActiveDetections.Count}件  |  {(restored ? "キャッシュ復元済み" : "OCR完了")}";
            if (!restored)
            {
                dirty = true;
                if (SaveChanges() && archive != null)
                {
                    status.Text += $"  |  旧データ退避済み・同一画像の作業データ{preserved}件維持";
                    status.ToolTipText = $"退避先：{archive}\n画像のバイト列が同一と確認できた場合のみ修正・追加・削除・済みを維持します。";
                }
            }
            Search();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != analysisGeneration || closing || IsDisposed) return;
            status.Text = $"{description}  |  OCR失敗：{ex.Message}";
            status.ToolTipText = status.Text;
        }
        finally
        {
            if (generation == analysisGeneration && !closing && !IsDisposed) SetAnalyzing(false);
        }
    }

    internal void Reanalyze()
    {
        if (identity == null || IsAnalyzing || transferBusy) return;
        // Upgrade a valid legacy cache and record its content fingerprint before rerunning.
        if (catalog != null) { dirty = true; if (!SaveChanges()) return; }
        EndAdd();
        analysisCancellation?.Cancel(); analysisCancellation?.Dispose();
        analysisCancellation = new CancellationTokenSource();
        catalog = null;
        undoState = null;
        list.SetDetections([]); view.SetDetections([]); overview.SetDetections([]);
        number.Clear(); Search();
        var description = $"{Path.GetFileName(identity.AbsolutePath)}  |  {identity.Width} × {identity.Height} px";
        bool exists = File.Exists(store.PathFor(identity.AbsolutePath));
        ActiveOcrTask = LoadAnalysisAsync(identity, description, ++analysisGeneration, analysisCancellation.Token, force: exists);
    }

    internal void BeginAdd()
    {
        if (identity == null || adding || IsAnalyzing || transferBusy) return;
        listWasVisible = showList.Checked;
        if (floatingList == null && !panes.Panel1Collapsed) savedListWidth = panes.SplitterDistance;
        adding = true; view.AddingPoint = true;
        addPrompt.Visible = cancelAdd.Visible = true; showList.Enabled = false;
        ApplyListVisibility();
        UpdateCommands();
    }

    internal void EndAdd()
    {
        if (!adding) return;
        adding = false; view.AddingPoint = false;
        addPrompt.Visible = cancelAdd.Visible = false; showList.Enabled = true;
        showList.Checked = listWasVisible;
        ApplyListVisibility();
        UpdateCommands();
    }

    internal void AddAt(string value, PointF point)
    {
        if (!adding || identity == null) return;
        catalog ??= new OcrCatalog(new Size(identity.Width, identity.Height), []);
        BeginMutation();
        var record = catalog.AddManual(value, point);
        unknownOnly.Checked = false;
        list.SetDetections(catalog.Detections, preserveSelection: true);
        EndAdd();
        list.SelectDetection(record.Id, false);
        Changed();
    }

    internal void DeleteDetections(IReadOnlyList<Detection> selected)
    {
        if (catalog == null || IsAnalyzing || transferBusy) return;
        int index=list.FirstSelectedIndex;
        BeginMutation();
        var ids = selected.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var record in catalog.Detections.Where(d => ids.Contains(d.Id))) record.IsDeleted = true;
        list.SetDetections(catalog.Detections, preserveSelection: true);
        list.SelectNext(index, navigate: true);
        Changed();
    }

    private void SelectFromImage(Detection record, bool add)
    {
        if (!record.IsUnrecognized) unknownOnly.Checked = false;
        list.SelectDetection(record.Id, add);
    }

    private void Changed()
    {
        if (catalog == null) return;
        dirty = true;
        catalog.RecalculateUiBounds();
        number.Enabled = true;
        view.SetDetections(catalog.Detections); overview.SetDetections(catalog.Detections);
        Search(); // Invalidate both images; keeps transform and selection intact.
        SaveChanges();
        UpdateCommands();
    }

    private bool SaveChanges()
    {
        if (!dirty || identity == null || catalog == null) return true;
        try
        {
            store.Save(identity, catalog); dirty = false;
            status.Text = $"{Path.GetFileName(identity.AbsolutePath)}  |  検出結果 {catalog.ActiveDetections.Count}件  |  保存済み";
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
