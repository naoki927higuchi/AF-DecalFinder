using System.Text.Json;

namespace AF.DecalFinder;

internal sealed class MainForm : Form
{
    private readonly ImageViewControl view = new() { Dock = DockStyle.Fill };
    private readonly DigitsTextBox number = new() { Width = 130, Enabled = false, AccessibleName = "検索番号" };
    private readonly Label count = new() { AutoSize = true, Text = "検索不可", Margin = new Padding(12, 8, 0, 0) };
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "画像を開いてください。" };
    private readonly string jsonPath = Path.Combine(AppContext.BaseDirectory, "results", "600dpi.json");
    private OcrCatalog? catalog;

    public MainForm(string? initialImage)
    {
        Text = "AF-DecalFinder 1.1.0 — Phase 2";
        ClientSize = new Size(1200, 880);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "画像を開く…", AutoSize = true, Height = 30 };
        open.Click += (_, _) => SelectImage();
        var fit = new Button { Text = "全体表示", AutoSize = true, Height = 30 };
        fit.Click += (_, _) => view.FitImage();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.AddRange([open, fit, new Label { Text = "番号", AutoSize = true, Margin = new Padding(16, 7, 5, 0) }, number, count]);
        var strip = new StatusStrip();
        strip.Items.Add(status);
        Controls.Add(view);
        Controls.Add(toolbar);
        Controls.Add(strip);
        number.TextChanged += (_, _) => Search();
        InstallDropHandlers(this);
        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialImage)) OpenImage(initialImage);
        };
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
            loaded = new Bitmap(source);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"画像を開けませんでした。\n{ex.Message}", "画像の読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        catalog = null;
        number.Enabled = false;
        view.SetImage(loaded);
        count.Text = "検索不可";
        Text = $"AF-DecalFinder 1.1.0 — {Path.GetFileName(path)}";
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
            number.Enabled = true;
            status.Text = $"{description}  |  OCR結果 {catalog.Detections.Count}件  |  ホイール：ズーム / 左ドラッグ：移動";
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
        count.Text = catalog == null ? "検索不可" : $"{matches.Count}件";
    }
}
