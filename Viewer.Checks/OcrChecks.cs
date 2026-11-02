using AF.DecalFinder;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AF.DecalFinder.Checks;

internal static class OcrChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Hidden)!.GetValue(o)!;
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
    static void Open(MainForm form, string path) => typeof(MainForm).GetMethod("OpenImage", Hidden)!.Invoke(form, [path]);
    static int Wait(MainForm form)
    {
        int ticks = 0;
        var timer = Stopwatch.StartNew();
        while (!form.ActiveOcrTask.IsCompleted)
        {
            Application.DoEvents(); Thread.Sleep(10); ticks++;
            if (timer.Elapsed.TotalSeconds > 120) throw new TimeoutException("OCR timeout");
        }
        form.ActiveOcrTask.GetAwaiter().GetResult(); Application.DoEvents(); return ticks;
    }
    sealed class CountingRunner(IOcrRunner inner) : IOcrRunner
    {
        public int Calls; public double Seconds;
        public async Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation)
        {
            Calls++; var timer = Stopwatch.StartNew();
            try { return await inner.RunAsync(image, cancellation); }
            finally { Seconds = timer.Elapsed.TotalSeconds; }
        }
    }
    sealed class DeferredRunner : IOcrRunner
    {
        public List<TaskCompletionSource<OcrCatalog>> Pending = [];
        public Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation)
        {
            // Deliberately ignores cancellation: generation check must still reject late results.
            var source = new TaskCompletionSource<OcrCatalog>(); Pending.Add(source); return source.Task;
        }
    }
    internal static void Run(string root, string? runtimeOverride = null)
    {
        var folder = Path.Combine(root, "viewer-checks", "ocr-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var runtime = runtimeOverride ?? Path.Combine(root, "bin", "Release-" + File.ReadAllText(Path.Combine(root, "VERSION.txt")).Trim(), "Ocr");
        var store = new EditStore(Path.Combine(folder, "cache"));
        var settings = new AppSettingsStore(Path.Combine(folder, "Settings.json"));
        var runner = new CountingRunner(new OcrRunner(runtime));
        var report = new StringBuilder("# " + File.ReadAllText(Path.Combine(root,"VERSION.txt")).Trim() + " OCR統合検証\n\n日時：" + DateTimeOffset.Now.ToString("u") + "\n\n専用の空キャッシュ・設定を使用。Release実MainFormから8原本を順次読み込み、UIメッセージを処理しながら完了待機。各値は1回測定。OCR秒はプロセス起動・Python import・画像decode・認識・JSON検証を含む。初回/再読込秒は画像decode・表示・キャッシュI/Oを含む。\n\n|画像|寸法|検出数|OCR秒|初回秒|キャッシュ再読込秒|待機中UI処理回数|\n|---|---|---:|---:|---:|---:|---:|\n");
        // Use the established evaluation set; additional user images must not change the regression fixtures.
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Evaluation180","ground-truth.json")));
        var files = fixtures.RootElement.GetProperty("images").EnumerateArray()
            .Select(i => Path.Combine(root,"inputs",i.GetProperty("file").GetString()!)).Distinct().Order().ToArray();
        Check(files.Length == 8, "Exactly eight provided inputs");
        using (var form = new MainForm(null, store, settings, runner))
        {
            form.Show(); Application.DoEvents();
            var list = Field<OcrResultsList>(form, "list");
            var view = Field<ImageViewControl>(form, "view");
            foreach (var path in files)
            {
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                var timer = Stopwatch.StartNew(); int calls = runner.Calls;
                Open(form, path);
                Check(form.IsAnalyzing && form.UseWaitCursor && !list.Enabled && !Field<DigitsTextBox>(form,"number").Enabled && list.Items.Count == 0, "Busy/empty/disabled immediately: " + Path.GetFileName(path));
                int ticks = Wait(form); timer.Stop(); double initial = timer.Elapsed.TotalSeconds;
                Check(runner.Calls == calls + 1 && !form.IsAnalyzing && !form.UseWaitCursor && list.Enabled, "Initial OCR and cursor reset: " + Path.GetFileName(path));
                var identity = Field<ImageIdentity>(form, "identity");
                var catalog = store.Load(identity)!;
                Check(catalog != null && catalog.Detections.Count == list.Items.Count && ticks > 0, "Saved image-specific OCR with responsive event pump");
                if (Path.GetFileName(path) == "600dpi.jpg")
                {
                    var phase1 = OcrCatalog.Load(Path.Combine(root, "results", "600dpi.json"));
                    Check(phase1.Detections.All(old => catalog!.Detections.Any(d => d.Number == old.Number && d.Bounds == old.Bounds)), "Phase 1: all 181 numbers and boxes retained");
                }
                File.WriteAllText(Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-detections.json"), JsonSerializer.Serialize(catalog!.Detections));
                timer.Restart(); Open(form, path); Wait(form); timer.Stop();
                Check(runner.Calls == calls + 1 && list.Items.Count == catalog.Detections.Count, "Reload skips OCR");
                report.AppendLine($"|{Path.GetFileName(path)}|{identity.Width}×{identity.Height}|{catalog.Detections.Count}|{runner.Seconds:F3}|{initial:F3}|{timer.Elapsed.TotalSeconds:F3}|{ticks}|");
                // Persist independent correction/done/manual data, then restore in a new form instance.
                string? edited = list.Items.Count == 0 ? null : ((Detection)list.Items[0].Tag!).Id;
                if (edited != null) { list.Rename(edited, "998877"); list.Items.Cast<ListViewItem>().Single(i => ((Detection)i.Tag!).Id == edited).Checked = true; }
                form.BeginAdd(); form.AddAt("887766", new PointF(identity.Width / 2, identity.Height / 2));
                using (var restarted = new MainForm(path, store, new AppSettingsStore(Path.Combine(folder, "restart.json")), runner))
                {
                    restarted.Show(); Wait(restarted);
                    var rows = Field<OcrResultsList>(restarted, "list");
                    Check(rows.Items.Count == catalog.Detections.Count + 1 && rows.Items.Cast<ListViewItem>().Any(i => i.Text == "887766" && ((Detection)i.Tag!).IsManual), "New form restores own manual record");
                    if (edited != null) Check(rows.Items.Cast<ListViewItem>().Any(i => ((Detection)i.Tag!).Id == edited && i.Text == "998877" && i.Checked), "New form restores correction/done/fixed ID");
                    restarted.Close();
                }
                Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Original SHA256 unchanged");
                foreach (double zoom in new[] { .25, .5, 1, 2 }) { view.SetZoom(zoom); view.CenterOn(new PointF(identity.Width - 1, identity.Height - 1)); Application.DoEvents(); Check(!view.VisibleImageBounds.IsEmpty && view.Zoom == zoom, "Zoom/pan after OCR"); }
            }
            // Change only a test copy's timestamp; never touch provided originals.
            var copy = Path.Combine(folder, "timestamp.jpg"); File.Copy(files.Single(p => Path.GetFileName(p) == "600dpi.jpg"), copy);
            Open(form, copy); Wait(form); int freshCount = list.Items.Count; form.BeginAdd(); form.AddAt("987654", new PointF(200, 200));
            var oldCache = File.ReadAllBytes(store.PathFor(copy));
            File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddSeconds(5));
            int beforeCalls = runner.Calls; Open(form, copy); Wait(form);
            Check(runner.Calls == beforeCalls + 1 && list.Items.Count == freshCount + 1 && list.Items.Cast<ListViewItem>().Any(i => i.Text == "987654"), "Timestamp change reruns OCR and preserves work for identical bytes");
            Check(Directory.GetFiles(store.DirectoryPath, "*.stale-*.json").Any(p => File.ReadAllBytes(p).SequenceEqual(oldCache)), "Stale edited data preserved byte-for-byte");
            var cachePath = store.PathFor(copy);
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(cachePath))!; doc["Image"]!["Width"] = 10;
            File.WriteAllText(cachePath, doc.ToJsonString()); beforeCalls = runner.Calls;
            Open(form, copy); Wait(form);
            Check(runner.Calls == beforeCalls + 1 && list.Items.Count == freshCount, "Wrong-size cache triggers fresh OCR");
            form.Close();
        }
        var smallA = Path.Combine(folder, "a.png"); var smallB = Path.Combine(folder, "b.png");
        using (var bitmap = new Bitmap(100, 80)) { bitmap.Save(smallA); bitmap.Save(smallB); }
        var deferred = new DeferredRunner();
        using (var form = new MainForm(null, new EditStore(Path.Combine(folder, "deferred")), new AppSettingsStore(Path.Combine(folder, "deferred.json")), deferred))
        {
            form.Show(); Open(form, smallA); var first = form.ActiveOcrTask; Open(form, smallB);
            deferred.Pending[0].SetResult(new OcrCatalog(new Size(100, 80), [new("1",new Rectangle(1,1,5,5),90,"old")])); Application.DoEvents();
            Check(form.IsAnalyzing && Field<OcrResultsList>(form,"list").Items.Count == 0, "Late previous result cannot replace current image");
            deferred.Pending[1].SetResult(new OcrCatalog(new Size(100, 80), [])); Wait(form);
            Check(!form.IsAnalyzing && Field<OcrResultsList>(form,"list").Items.Count == 0 && Field<DigitsTextBox>(form,"number").Enabled && Field<ToolStripStatusLabel>(form,"status").Text!.Contains("0件"), "Zero detections succeeds and saves");
            Open(form, smallA); deferred.Pending[2].SetException(new IOException("intentional worker failure")); Wait(form);
            Check(!form.UseWaitCursor && !form.IsAnalyzing && Field<ToolStripStatusLabel>(form,"status").Text!.Contains("intentional worker failure") && !Field<ImageViewControl>(form,"view").VisibleImageBounds.IsEmpty, "Failure restores cursor and keeps image available with reason");
            form.Close();
        }
        var parsed = OcrRunner.Parse("""{"image":{"width":100,"height":80},"detections":[null,{}, {"id":"bad","number":"2","bbox":[99,0,10,10],"confidence":80},{"id":"ok","number":"04","bbox":[1,1,10,10],"confidence":90}]}""", new Size(100,80));
        Check(parsed.Detections.Count == 1 && parsed.Detections[0].Number == "04", "Invalid OCR records skipped; leading zero preserved");
        using (var cancellation = new CancellationTokenSource())
        {
            var run = new OcrRunner(runtime).RunAsync(ImageIdentity.Read(files[0], new Size(3549,3000)), cancellation.Token);
            cancellation.CancelAfter(100);
            bool canceled = false; try { run.GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Real subprocess supports cancellation");
        }
        report.AppendLine($"\n自動確認 {checks}項目成功。全8画像で原本SHA256不変、初回OCR・キャッシュ再読込・編集/追加/済み復元・25/50/100/200%パンを確認。テムジン600dpiは181件の番号とbboxがPhase 1と全件一致。更新日時/寸法不一致では再解析し旧編集データを別ファイル退避。遅延結果混入・0件・失敗・不正レコード・実プロセス取消しを確認。\n\n検証データ（専用キャッシュと検出JSON）：`{folder}`\n");
        var reportName="OCR-INTEGRATION-"+File.ReadAllText(Path.Combine(root,"VERSION.txt")).Trim().Replace(".","")+".md";
        File.WriteAllText(Path.Combine(root, reportName), report.ToString());
        Console.WriteLine($"SUCCESS: {checks} checks; report {reportName}");
    }
}
