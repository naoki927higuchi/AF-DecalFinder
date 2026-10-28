using AF.DecalFinder;
using System.Text.Json;

namespace AF.DecalFinder.Checks;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label); checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var root = Path.GetFullPath(args[0]);
        var catalog = OcrCatalog.Load(Path.Combine(root, "results", "600dpi.json"));
        Check(catalog.ImageSize == new Size(7783, 6579) && catalog.Detections.Count == 181, "Phase 1 JSON: dimensions and 181 records");
        Check(catalog.Find("102").Count == 1 && catalog.Find("102")[0].Bounds == new Rectangle(4249, 3354, 49, 44), "102 exact match and source pixel bbox");
        Check(catalog.Find("49").Count == 2 && catalog.Find("118").Count == 2, "All separately located duplicate numbers retained");
        Check(catalog.Find("10").All(d => d.Number == "10") && catalog.Find("999999").Count == 0 && catalog.Find("0102").Count == 0 && catalog.Find("").Count == 0, "Exact string matching and empty query");

        var transform = new ViewTransform();
        transform.Fit(catalog.ImageSize, new Size(1000, 800));
        var point = new Point(570, 360);
        var imageX = (point.X - transform.OffsetX) / transform.Scale;
        var imageY = (point.Y - transform.OffsetY) / transform.Scale;
        transform.ZoomAt(point, 5.2, .001, 16);
        Check(Math.Abs(transform.OffsetX + imageX * transform.Scale - point.X) < 1e-8 &&
            Math.Abs(transform.OffsetY + imageY * transform.Scale - point.Y) < 1e-8, "Wheel zoom preserves image point under cursor");
        var before = transform.ToScreen(catalog.Find("102")[0].Bounds);
        transform.Pan(137, -83);
        var after = transform.ToScreen(catalog.Find("102")[0].Bounds);
        Check(Math.Abs(after.X - before.X - 137) < .001 && Math.Abs(after.Y - before.Y + 83) < .001 && after.Size == before.Size, "Pan translates bbox without resizing");

        using var input = new DigitsTextBox();
        input.Text = "a10+2 日本３";
        Check(input.Text == "102", "Paste filters ASCII non-digits including full-width digits");
        input.Text = ""; Check(input.Text.Length == 0, "Empty input allowed");

        var drop = new DataObject(); drop.SetData(DataFormats.FileDrop, new[] { "test.png" });
        Check(MainForm.GetDroppedImage(drop) == "test.png", "Single PNG file drop accepted");
        drop.SetData(DataFormats.FileDrop, new[] { "test.jpg", "second.png" });
        Check(MainForm.GetDroppedImage(drop) == null, "Multiple file drop rejected");
        drop.SetData(DataFormats.FileDrop, new[] { "test.json" });
        Check(MainForm.GetDroppedImage(drop) == null, "Non-image drop rejected");

        using (var form = new MainForm(null))
        {
            var originalPath = Path.Combine(root, "inputs", "600dpi.jpg");
            var originalDrop = new DataObject(); originalDrop.SetData(DataFormats.FileDrop, new[] { originalPath });
            typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(form, new object[] { new DragEventArgs(originalDrop, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
            var search = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<DigitsTextBox>().Single();
            Check(form.Text.EndsWith("600dpi.jpg") && search.Enabled, "Actual form Drop handler loads original and enables search");
            search.Text = "49";
            Check(form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Label>().Any(l => l.Text == "2件"), "Drop-to-search integration finds two matches");
            using var exclusive = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            Check(exclusive.Length > 0, "Displayed image does not lock source file");
        }

        var temp = Path.Combine(root, "viewer-checks"); Directory.CreateDirectory(temp);
        var cases = new[] {
            "{", "{}", "null",
            "{\"schema_version\":2}",
            "{\"schema_version\":1,\"image\":{\"width\":10,\"height\":10},\"detections\":[{\"number\":\"1\",\"bbox\":[9,0,2,1],\"confidence\":90}]}",
            "{\"schema_version\":1,\"image\":{\"width\":10,\"height\":10},\"detections\":[{\"number\":\"abc\",\"bbox\":[0,0,2,1],\"confidence\":90}]}"
        };
        for (int i = 0; i < cases.Length; i++)
        {
            var file = Path.Combine(temp, $"invalid-{i}.json"); File.WriteAllText(file, cases[i]);
            var rejected = false;
            try { OcrCatalog.Load(file); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            Check(rejected, $"Malformed/schema-invalid JSON case {i} rejected");
        }
        var missing = false;
        try { OcrCatalog.Load(Path.Combine(temp, "missing.json")); } catch (FileNotFoundException) { missing = true; }
        Check(missing, "Missing JSON reported distinctly");

        // Exercise actual paint/mouse handlers and inspect rendered screen pixels.
        using var canvas = new TestCanvas { Size = new Size(400, 300) };
        var bitmap = new Bitmap(400, 300);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.White);
        canvas.SetImage(bitmap);
        var highlights = new[] { new Detection("49", new Rectangle(100, 100, 30, 20), 90), new Detection("49", new Rectangle(200, 100, 30, 20), 90) };
        canvas.SetHighlights(highlights);
        using var original = canvas.Render();
        Check(RedNear(original, 100, 100) && RedNear(original, 200, 100), "Both hits drawn red");
        Check(original.GetPixel(115, 110).ToArgb() == Color.White.ToArgb(), "Highlight interior is not filled");
        canvas.Wheel(200, 150, 120); // scale 1.2, offset -40,-30
        canvas.PanBy(30, 20); // offset -10,-10
        using var zoomed = canvas.Render();
        Check(RedNear(zoomed, 110, 110) && RedNear(zoomed, 230, 110), "Painted boxes follow real wheel+pan handlers");
        var redWidth = Enumerable.Range(105, 11).Count(x => IsRed(zoomed.GetPixel(x, 120)));
        Check(redWidth is >= 1 and <= 3, "Screen pen stays approximately 2px after zoom");
        canvas.SetHighlights(Array.Empty<Detection>());
        using var cleared = canvas.Render();
        Check(!RedNear(cleared, 110, 110), "Clearing query clears highlights without refitting");
        canvas.SetHighlights(highlights);
        using var restored = canvas.Render();
        Check(RedNear(restored, 110, 110), "Changing matches does not zoom or move image");
        using var png = new Bitmap(120, 80); png.Save(Path.Combine(temp, "mismatch.png"));
        File.WriteAllText(Path.Combine(temp, "broken.jpg"), "not an image");
        Console.WriteLine($"PASS: {checks} checks");
    }

    private static bool IsRed(Color c) => c.R > 200 && c.G < 80 && c.B < 80;
    private static bool RedNear(Bitmap image, int x, int y) => Enumerable.Range(-2, 5).Any(dx => Enumerable.Range(-2, 5).Any(dy => IsRed(image.GetPixel(x + dx, y + dy))));

    private sealed class TestCanvas : ImageViewControl
    {
        public Bitmap Render() { var b = new Bitmap(Width, Height); using var g = Graphics.FromImage(b); g.Clear(BackColor); OnPaint(new PaintEventArgs(g, ClientRectangle)); return b; }
        public void Wheel(int x, int y, int delta) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, x, y, delta));
        public void PanBy(int dx, int dy)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 100 + dx, 100 + dy, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 100 + dx, 100 + dy, 0));
        }
    }
}
