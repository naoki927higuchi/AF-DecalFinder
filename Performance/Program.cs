using AF.DecalFinder;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AF.DecalFinder.Performance;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var root = Path.GetFullPath(args[0]);
        var requestedVariant = args[1];
        var output = Path.GetFullPath(args[2]);
        var frames = args.Length > 3 ? int.Parse(args[3]) : 30;
        var repeats = args.Length > 4 ? int.Parse(args[4]) : 3;
        using var sourceFile = Image.FromFile(Path.Combine(root, "inputs", "600dpi.jpg"));
        using var source = requestedVariant is "pargb" or "improved" or "previous" or "before-editing" or "done" or "paired" ? DisplayBitmap.Create(sourceFile) : new Bitmap(sourceFile);
        var catalog = OcrCatalog.Load(Path.Combine(root, "results", "600dpi.json"));
        if (requestedVariant is "done" or "paired") foreach (var detection in catalog.Detections) detection.IsDone = true;
        if (requestedVariant == "quality") { Quality(sourceFile, source, catalog, output); return; }
        var results = new List<object>();
        foreach (double scale in new[] { .25, .5, 1, 2 })
        foreach (var operation in new[] { "detail-pan", "overview-drag" })
        foreach (var variant in requestedVariant == "paired" ? new[] { "before-editing", "done", "done", "before-editing" } : new[] { requestedVariant })
        {
            using var left = new Surface(variant, ImageViewMode.Overview, source, new Size(1838, 2016));
            using var right = new Surface(variant, ImageViewMode.Detail, source, new Size(1838, 2016));
            left.SetMarks(catalog); right.SetMarks(catalog);
            if (right.Control is ImageViewControl r && left.Control is ImageViewControl l)
            { r.ViewportChanged += () => l.SetNavigationBounds(r.VisibleImageBounds); l.NavigateRequested += r.CenterOn; }
            else if (right.Control is PreviousImageViewControl pr && left.Control is PreviousImageViewControl pl)
            { pr.ViewportChanged += () => pl.SetNavigationBounds(pr.VisibleImageBounds); pl.NavigateRequested += pr.CenterOn; }
            else if (right.Control is BeforeEditingImageViewControl er && left.Control is BeforeEditingImageViewControl el)
            { er.ViewportChanged += () => el.SetNavigationBounds(er.VisibleImageBounds); el.NavigateRequested += er.CenterOn; }
            else
            {
                var br = (BaselineImageViewControl)right.Control; var bl = (BaselineImageViewControl)left.Control;
                br.ViewportChanged += () => bl.SetNavigationBounds(br.VisibleImageBounds); bl.NavigateRequested += br.CenterOn;
            }
            var samples = new List<object>();
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                right.Call("SetZoom", scale); right.Call("CenterOn", new PointF(source.Width / 2f, source.Height / 2f));
                left.Flush(); right.Flush();
                var target = operation == "detail-pan" ? right : left;
                var start = new Point(919, 1008);
                target.Mouse("OnMouseDown", start);
                for (int i = 0; i < 6; i++) { target.Mouse("OnMouseMove", new Point(start.X + i * 3, start.Y + i)); left.Flush(); right.Flush(); }
                target.Mouse("OnMouseUp", start);
                right.Call("CenterOn", new PointF(source.Width / 2f, source.Height / 2f)); left.Flush(); right.Flush();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                left.Probe.Clear(); right.Probe.Clear(); left.Invalidations = right.Invalidations = 0;
                var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var cpu = process.TotalProcessorTime; var privateStart = process.PrivateMemorySize64; var workingStart = process.WorkingSet64;
                var allocation = GC.GetTotalAllocatedBytes(true);
                var elapsed = Stopwatch.StartNew();
                var steps = new List<double>();
                target.Mouse("OnMouseDown", start);
                for (int i = 0; i < frames; i++)
                {
                    var tick = Stopwatch.GetTimestamp();
                    // Bounded oscillating drag; same positions and same number of flushes in both variants.
                    int phase = i % 20; int dx = (phase <= 10 ? phase : 20 - phase) * 3;
                    target.Mouse("OnMouseMove", new Point(start.X + dx, start.Y + dx / 2));
                    left.Flush(); right.Flush();
                    steps.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
                }
                target.Mouse("OnMouseUp", start);
                elapsed.Stop(); process.Refresh();
                samples.Add(new {
                    repeat, frames, elapsedMs = elapsed.Elapsed.TotalMilliseconds,
                    response = Stats(steps), overview = Summarize(left), detail = Summarize(right),
                    cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                    cpuPercentMachine = (process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    privateStartBytes = privateStart, privateEndBytes = process.PrivateMemorySize64,
                    workingStartBytes = workingStart, workingEndBytes = process.WorkingSet64,
                    allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocation,
                    gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray()
                });
            }
            results.Add(new { scale, operation, variant, samples });
            Console.WriteLine($"Finished {variant}: {scale:P0} {operation}");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                variant = requestedVariant, sourceCommit = requestedVariant == "baseline" ? "6008a2e" : requestedVariant == "previous" ? "fde03af (1.4.0)" : requestedVariant == "before-editing" ? "0070af4 (1.4.1)" : "working tree (paired: before-editing = 0070af4)", doneCount = catalog.Detections.Count(d => d.IsDone), image = new { source.Width, source.Height, pixelFormat = source.PixelFormat.ToString() },
                modeledWindowPixels = "3840x2160 (32-inch 4K scenario; physical inches do not define render cost)",
                panePixels = "1838x2016 each", frames, repeats,
                methodology = "Reusable offscreen GDI+ targets; production OnMouseMove and OnPaint; invalidated controls flushed once per move, without OS event coalescing, compositor or display latency; timings are CPU-side surrogate responsiveness, not measured physical mouse-to-photon latency.",
                bitmapAccounting = "Counts application-owned bitmap create/dispose inside controls during measured drag. Two render targets and one shared decoded source are allocated before timing; GDI+ internal buffers are not counted.",
                os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                logicalProcessors = Environment.ProcessorCount, cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static object Stats(List<double> values) => new { count = values.Count, meanMs = values.Count == 0 ? 0 : values.Average(), p95Ms = values.Count == 0 ? 0 : values.Order().ElementAt((int)Math.Ceiling(values.Count * .95) - 1), maxMs = values.Count == 0 ? 0 : values.Max() };

    static void Quality(Image originalFile, Bitmap source, OcrCatalog catalog, string output)
    {
        using var converted = DisplayBitmap.Create(originalFile);
        var comparisons = new List<object>();
        bool differs = false;
        foreach (var mode in new[] { ImageViewMode.Overview, ImageViewMode.Detail })
        foreach (double scale in new[] { .25, .5, .75, 1, 1.5, 2 })
        foreach (var center in new[] { new PointF(2700.5f, 4350.5f), new PointF(3900, 3300), new PointF(0,0), new PointF(source.Width, source.Height) })
        {
            using var before = new Surface("baseline", mode, source, new Size(920, 1008));
            using var after = new Surface("improved", mode, converted, new Size(920, 1008));
            foreach (var s in new[] { before, after })
            {
                s.SetMarks(catalog); s.Call("SetZoom", scale);
                s.Call("CenterOn", center); s.Flush();
            }
            long error = 0; int max = 0, channels = 0, differentPixels = 0;
            for (int y = 0; y < 1008; y += 3)
            for (int x = 0; x < 920; x += 3)
            {
                var a = before.Pixel(x, y); var b = after.Pixel(x, y);
                var differences = new[] { Math.Abs(a.R-b.R), Math.Abs(a.G-b.G), Math.Abs(a.B-b.B) };
                if (differences.Any(d => d != 0)) differentPixels++;
                error += differences.Sum(); max = Math.Max(max, differences.Max()); channels += 3;
            }
            comparisons.Add(new { mode = mode.ToString(), scale, center = new { center.X, center.Y }, channels, differentPixels, meanAbsoluteChannelError = (double)error / channels, maxChannelError = max });
            differs |= max != 0;
        }
        File.WriteAllText(output, JsonSerializer.Serialize(comparisons, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(output));
        if (differs) throw new InvalidDataException("Sampled rendering pixels differ from baseline; inspect quality.json.");
    }
    static object Summarize(Surface s) => new { paint = Stats(s.Probe.PaintMs), image = Stats(s.Probe.ImageMs), overlay = Stats(s.Probe.OverlayMs), mouse = Stats(s.Probe.MouseMs), s.Invalidations, s.Probe.BitmapCreations, s.Probe.BitmapDisposals };

    sealed class Surface : IDisposable
    {
        public Control Control { get; }
        public RenderProbe Probe { get; } = new();
        readonly Bitmap target;
        readonly Graphics graphics;
        bool dirty = true;
        public int Invalidations;
        public Surface(string variant, ImageViewMode mode, Bitmap source, Size size)
        {
            Control = variant == "baseline" ? new BaselineImageViewControl { Mode = mode, Size = size, Probe = Probe }
                : variant == "previous" ? new PreviousImageViewControl { Mode = mode, Size = size, Probe = Probe }
                : variant == "before-editing" ? new BeforeEditingImageViewControl { Mode = mode, Size = size, Probe = Probe }
                : new ImageViewControl { Mode = mode, Size = size, Probe = Probe };
            Control.CreateControl();
            Control.Invalidated += (_, _) => { dirty = true; Invalidations++; };
            target = new Bitmap(size.Width, size.Height); graphics = Graphics.FromImage(target);
            Call("SetImage", source, false);
        }
        public void Call(string method, params object[] args) => Control.GetType().GetMethod(method)!.Invoke(Control, args);
        public void SetMarks(OcrCatalog catalog) { Call("SetHighlights", catalog.Find("49")); Call("SetSelections", catalog.Detections); if (Control is ImageViewControl view) view.SetDetections(catalog.Detections); }
        public void Mouse(string method, Point point) => Control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        public void Flush()
        {
            if (!dirty) return;
            dirty = false; graphics.Clear(Control.BackColor);
            Control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Control, new object[] { new PaintEventArgs(graphics, Control.ClientRectangle) });
        }
        public Color Pixel(int x, int y) => target.GetPixel(x, y);
        public void Dispose() { Control.Dispose(); graphics.Dispose(); target.Dispose(); }
    }
}
