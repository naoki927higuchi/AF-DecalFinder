using AF.DecalFinder;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AF.DecalFinder.Checks;

// 1.10.0: EXIF orientation is applied once on open; display, OCR, cache and transfer share the upright frame.
internal static class ExifChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Hidden)!.GetValue(o)!;
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
    static void Open(MainForm form, string path) => typeof(MainForm).GetMethod("OpenImage", Hidden)!.Invoke(form, [path]);
    static void Wait(MainForm form)
    {
        var timer = Stopwatch.StartNew();
        while (!form.ActiveOcrTask.IsCompleted)
        {
            Application.DoEvents(); Thread.Sleep(10);
            if (timer.Elapsed.TotalSeconds > 300) throw new TimeoutException("OCR timeout");
        }
        form.ActiveOcrTask.GetAwaiter().GetResult(); Application.DoEvents();
    }

    sealed class RecordingRunner : IOcrRunner
    {
        internal readonly List<ImageIdentity> Calls = [];
        public Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation)
        {
            Calls.Add(image);
            return Task.FromResult(new OcrCatalog(new Size(image.Width, image.Height), [new("7", new Rectangle(10, 60, 20, 20), 90, "seven")]));
        }
    }

    // Expected upright position of raw pixel (x,y) for each EXIF orientation (TIFF/EXIF definition).
    static Point Upright(int o, int x, int y, int w, int h) => o switch
    {
        2 => new(w - 1 - x, y), 3 => new(w - 1 - x, h - 1 - y), 4 => new(x, h - 1 - y),
        5 => new(y, x), 6 => new(h - 1 - y, x), 7 => new(h - 1 - y, w - 1 - x), 8 => new(y, w - 1 - x), _ => new(x, y)
    };

    static void SetOrientation(Image image, int orientation)
    {
        var item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        item.Id = 0x0112; item.Type = 3; item.Len = 2; item.Value = BitConverter.GetBytes((ushort)orientation);
        image.SetPropertyItem(item);
    }

    // Large flat blocks survive JPEG compression; the top-left quadrant is red, everything else white.
    static string MakeJpeg(string path, int orientation, int w = 160, int h = 100)
    {
        using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.White); g.FillRectangle(Brushes.Red, 0, 0, w / 2, h / 2); }
        if (orientation != 0) SetOrientation(bitmap, orientation);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    internal static void Run(string root, string? runtime)
    {
        var folder = Path.Combine(root, "viewer-checks", "exif-" + Guid.NewGuid()); Directory.CreateDirectory(folder);

        // 1. Orientation mapping is lossless and matches the EXIF definition for all 8 values.
        const int W = 5, H = 3;
        for (int o = 1; o <= 8; o++)
        {
            using var raw = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) raw.SetPixel(x, y, Color.FromArgb(255, x * 40, y * 80, 7));
            using var upright = DisplayBitmap.Create(raw, o);
            bool ok = upright.Size == ImageOrientation.Apply(new Size(W, H), o);
            for (int y = 0; y < H && ok; y++) for (int x = 0; x < W && ok; x++)
            {
                var p = Upright(o, x, y, W, H);
                var c = upright.GetPixel(p.X, p.Y);
                ok = c.R == x * 40 && c.G == y * 80;
            }
            Check(ok, $"Orientation {o}: upright size and every pixel follow the EXIF definition");
        }
        Check(ImageOrientation.Normalize(0) == 1 && ImageOrientation.Normalize(9) == 1 && ImageOrientation.Normalize(-3) == 1, "Out-of-range orientation values are treated as 1");

        // 2. Header probe and file read.
        var plain = MakeJpeg(Path.Combine(folder, "plain.jpg"), 0);
        var rotated = MakeJpeg(Path.Combine(folder, "rotated.jpg"), 6);
        Check(ImageOrientation.Probe(plain) == (new Size(160, 100), 1), "No EXIF tag: raw size, orientation 1");
        Check(ImageOrientation.Probe(rotated) == (new Size(100, 160), 6), "Orientation 6: width and height swapped");
        var png = Path.Combine(folder, "plain.png"); using (var b = new Bitmap(30, 20)) b.Save(png, ImageFormat.Png);
        Check(ImageOrientation.Probe(png) == (new Size(30, 20), 1), "PNG without EXIF: unchanged");
        var probed = ImageIdentity.Probe(rotated);
        Check(probed.Width == 100 && probed.Height == 160 && probed.Orientation == 6 && probed.Frame == 6, "ImageIdentity.Probe records upright size and orientation");

        // 3. Identity, cache and legacy data in the raw frame.
        Check(!probed.Matches(probed with { Orientation = 1 }) && probed.Matches(probed with { }), "Identities with different applied orientation never match");
        var legacyJson = JsonSerializer.Serialize(new { AbsolutePath = probed.AbsolutePath, Width = 100, Height = 160, FileSize = probed.FileSize, LastWriteUtc = probed.LastWriteUtc });
        var legacy = JsonSerializer.Deserialize<ImageIdentity>(legacyJson)!;
        Check(legacy.Frame == 1 && !legacy.Matches(probed), "Pre-1.10 identity without Orientation is the raw frame and does not match a rotated image");
        var store = new EditStore(Path.Combine(folder, "cache"));
        store.Save(probed, new OcrCatalog(new Size(100, 160), [new("3", new Rectangle(5, 120, 20, 20), 90, "three")]));
        using (var saved = JsonDocument.Parse(File.ReadAllText(store.PathFor(rotated))))
            Check(saved.RootElement.GetProperty("Image").GetProperty("Orientation").GetInt32() == 6 && !saved.RootElement.GetProperty("Image").TryGetProperty("Frame", out _),
                "Cache stores the applied orientation (and not the derived Frame)");
        Check(store.Load(probed)!.Detections.Single().Bounds == new Rectangle(5, 120, 20, 20), "Upright cache restores with the same identity");
        bool rejected = false; try { store.Load(probed with { Orientation = 1, Width = 160, Height = 100 }); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Cache from another coordinate frame is rejected (triggers archive and OCR)");

        // A raw-frame cache whose size happens to fit (180°) must also be rejected and never restored into the new frame.
        var turned = MakeJpeg(Path.Combine(folder, "turned.jpg"), 3);
        var turnedRaw = ImageIdentity.Read(turned, new Size(160, 100));
        var turnedUpright = ImageIdentity.Probe(turned);
        store.Save(turnedRaw, new OcrCatalog(new Size(160, 100), [new("5", new Rectangle(10, 10, 20, 20), 90, "five") { IsDone = true, IsModified = true, OriginalNumber = "5", OriginalBounds = new Rectangle(10, 10, 20, 20) }]));
        rejected = false; try { store.Load(turnedUpright); } catch (InvalidDataException) { rejected = true; }
        Check(turnedUpright.Size() == turnedRaw.Size() && rejected, "180° legacy cache with identical size is rejected");
        var archive = store.ArchiveInvalid(turned);
        var fresh = new OcrCatalog(new Size(160, 100), [new("5", new Rectangle(10, 10, 20, 20), 90, "new")]);
        Check(store.RestoreProtected(turnedUpright, fresh, archive) == 0 && !fresh.Detections.Single().IsDone, "Raw-frame work is not restored into the upright frame");

        // 4. Transfer matching respects the frame.
        var item = new TransferItem(Guid.NewGuid().ToString("N"), new EditStore.Document(2, turnedRaw with { AbsolutePath = Path.Combine("C:\\elsewhere", "turned.jpg") }, []));
        Check(!DataTransfer.Matches(item, turnedUpright) && DataTransfer.Matches(item, turnedRaw), "Transfer data binds only to an image in the same frame");
        var scanned = DataTransfer.Scan([folder], [item with { Data = item.Data with { Image = turnedUpright with { AbsolutePath = "C:\\x\\turned.jpg" } } }]);
        Check(scanned.Single().Frame == 3 && scanned.Single().AbsolutePath.EndsWith("turned.jpg"), "Folder scan reads orientation from the image header");

        // 5. Viewer: open applies the orientation, runs OCR on a missing cache with the same identity, then restores the cache.
        var runner = new RecordingRunner();
        var formStore = new EditStore(Path.Combine(folder, "form-cache"));
        var settings = new AppSettingsStore(Path.Combine(folder, "Settings.json"));
        using (var form = new MainForm(null, formStore, settings, runner))
        {
            form.Show(); Application.DoEvents();
            Open(form, rotated); Wait(form);
            var view = Field<ImageViewControl>(form, "view");
            var status = Field<ToolStripStatusLabel>(form, "status");
            var identity = Field<ImageIdentity>(form, "identity");
            Check(runner.Calls.Count == 1 && runner.Calls[0].Frame == 6 && runner.Calls[0].Width == 100 && runner.Calls[0].Height == 160, "Missing cache: OCR runs once with the upright identity");
            Check(identity.Orientation == 6 && identity.Width == 100 && status.Text!.Contains("保存済み"), "OCR result for the upright identity is saved");
            Check(view.VisibleImageBounds.Width <= 100 && view.VisibleImageBounds.Height <= 160, "Detail view uses the upright bitmap");
            Check(formStore.Load(identity) != null, "OCR result saved in the upright frame");
            Open(form, plain); Wait(form);
            Check(runner.Calls.Count == 2 && runner.Calls[1].Frame == 1, "Image without orientation keeps frame 1");
            Open(form, rotated); Wait(form);
            Check(runner.Calls.Count == 2 && status.Text!.Contains("100 × 160") && status.Text.Contains("キャッシュ復元済み"), "Reopening the rotated image restores the cache without OCR and reports the upright size");
            form.Close();
        }

        // Legacy raw-frame cache for the same path is archived and re-analysed in the upright frame.
        var legacyStore = new EditStore(Path.Combine(folder, "legacy-cache"));
        legacyStore.Save(ImageIdentity.Read(rotated, new Size(160, 100)), new OcrCatalog(new Size(160, 100), [new("1", new Rectangle(1, 1, 10, 10), 90, "raw") { IsModified = true, OriginalNumber = "1", OriginalBounds = new Rectangle(1, 1, 10, 10) }]));
        var legacyRunner = new RecordingRunner();
        using (var form = new MainForm(null, legacyStore, new AppSettingsStore(Path.Combine(folder, "Settings2.json")), legacyRunner))
        {
            form.Show(); Application.DoEvents(); Open(form, rotated); Wait(form);
            var list = Field<OcrResultsList>(form, "list");
            Check(legacyRunner.Calls.Count == 1 && list.Items.Cast<ListViewItem>().All(i => ((Detection)i.Tag!).Id != "raw"), "Pre-1.10 raw-frame cache is not applied; OCR runs again");
            Check(Directory.EnumerateFiles(legacyStore.DirectoryPath, "*.stale-*").Any(), "Old raw-frame data is archived, not deleted");
            form.Close();
        }

        // 6. Real OCR worker receives the orientation and returns upright coordinates.
        if (runtime != null)
        {
            var phone = Path.Combine(root, "inputs", "RG RX-78-2 ver 2.0 スマホ撮影.jpg");
            if (File.Exists(phone))
            {
                var copy = Path.Combine(folder, "phone.jpg"); File.Copy(phone, copy);
                var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(phone)));
                var image = ImageIdentity.Probe(copy);
                var catalog = new OcrRunner(runtime).RunAsync(image, CancellationToken.None).GetAwaiter().GetResult();
                Check(image.Frame == 6 && catalog.ImageSize == new Size(3000, 4000), "Real worker: smartphone photo (orientation 6) analysed as 3000 × 4000 upright");
                Check(catalog.Detections.All(d => d.Bounds.Right <= 3000 && d.Bounds.Bottom <= 4000), "Real worker: every bbox lies inside the upright image");
                Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(phone))) == before, "Input original unchanged");
                // 1.11.0: label circles in the upright photo are no longer outvoted by large drawing outlines.
                var truth = Path.Combine(root, "Evaluation192RG", "ground-truth.json");
                if (File.Exists(truth))
                {
                    using var gt = JsonDocument.Parse(File.ReadAllText(truth));
                    var labels = gt.RootElement.GetProperty("phone_upright").GetProperty("records").EnumerateArray()
                        .Select(r => (X: r.GetProperty("center")[0].GetDouble(), Y: r.GetProperty("center")[1].GetDouble(), R: r.GetProperty("radius").GetDouble())).ToArray();
                    int found = labels.Count(l => catalog.Detections.Any(d => d.Circle is { } c && Math.Sqrt((c.X - l.X) * (c.X - l.X) + (c.Y - l.Y) * (c.Y - l.Y)) <= l.R));
                    Console.WriteLine($"INFO: phone photo label circles found {found}/{labels.Length}");
                    Check(labels.Length == 24 && found == 24, "Real worker: all 24 verified label circles in the smartphone photo are candidates");
                }
                File.WriteAllText(Path.Combine(folder, "phone-ocr.txt"),
                    $"detections={catalog.Detections.Count} recognized={catalog.Detections.Count(d => !d.IsUnrecognized)}\n" +
                    string.Join("\n", catalog.Detections.Select(d => $"{d.Id}\t{(d.IsUnrecognized ? "?" : d.Number)}\t{d.Bounds}")));
                Console.WriteLine($"INFO: phone photo detections={catalog.Detections.Count} recognized={catalog.Detections.Count(d => !d.IsUnrecognized)}");
            }
        }
        Console.WriteLine($"SUCCESS: {checks} EXIF checks; data {folder}");
    }

    static Size Size(this ImageIdentity image) => new(image.Width, image.Height);
}
