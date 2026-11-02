using AF.DecalFinder;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AF.DecalFinder.Checks;
internal static class PortableChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Hidden)!.GetValue(o)!;
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
    sealed class Counting(IOcrRunner runner) : IOcrRunner
    {
        internal int Calls;
        public Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation) { Calls++; return runner.RunAsync(image, cancellation); }
    }
    static int Wait(MainForm form)
    {
        var timer = Stopwatch.StartNew(); int ticks = 0;
        while (!form.ActiveOcrTask.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); ticks++; if (timer.Elapsed.TotalSeconds > 120) throw new TimeoutException(); }
        form.ActiveOcrTask.GetAwaiter().GetResult(); Application.DoEvents(); return ticks;
    }
    internal static void Run(string root, string package)
    {
        package = Path.GetFullPath(package);
        var dir = Path.Combine(root, "viewer-checks", "portable-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var names = new[] { "PATH", "PYTHONHOME", "PYTHONPATH", "DOTNET_ROOT", "DOTNET_MULTILEVEL_LOOKUP" };
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        var cwd = Environment.CurrentDirectory;
        try
        {
            Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
            Environment.SetEnvironmentVariable("PYTHONHOME", Path.Combine(dir,"missing"));
            Environment.SetEnvironmentVariable("PYTHONPATH", Path.Combine(dir,"missing"));
            Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.Combine(dir,"missing"));
            Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
            Environment.CurrentDirectory = dir;
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(package,"AF-DecalFinder.runtimeconfig.json")));
            Check(config.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks").GetArrayLength()==2,"Self-contained includes Core and Windows Desktop runtimes");
            Check(!Directory.GetFiles(package,"*ffmpeg*.dll",SearchOption.AllDirectories).Any(),"Unused FFmpeg video plugin absent");
            var path = Path.Combine(root,"inputs","600dpi.jpg"); var hash = SHA256.HashData(File.ReadAllBytes(path));
            // Relative runtimeDirectory as well as relative Python config must resolve from arbitrary cwd.
            var runner = new Counting(new OcrRunner(Path.GetRelativePath(dir,Path.Combine(package,"Ocr"))));
            var store = new EditStore(Path.Combine(dir,"cache")); var settings = new AppSettingsStore(Path.Combine(dir,"Settings.json"));
            using (var form = new MainForm(null,store,settings,runner))
            {
                form.Show(); Application.DoEvents();
                var sw=Stopwatch.StartNew(); typeof(MainForm).GetMethod("OpenImage",Hidden)!.Invoke(form,[path]);
                Check(form.IsAnalyzing && form.UseWaitCursor,"Packaged OCR starts asynchronously with busy cursor");
                int ticks=Wait(form); Console.WriteLine($"MEASURE initial={sw.Elapsed.TotalSeconds:F3}s uiTicks={ticks}");
                var list=Field<OcrResultsList>(form,"list");
                Check(runner.Calls==1 && ticks>0 && list.Items.Count==276,"Isolated Python OCR restores 276 candidates with responsive UI");
                Check(!form.IsAnalyzing && !form.UseWaitCursor,"Busy state clears after completion");
                var catalog=store.Load(ImageIdentity.Read(path,new(7783,6579)))!;
                var old=OcrCatalog.Load(Path.Combine(root,"results","600dpi.json"));
                Check(old.Detections.All(d=>catalog.Detections.Any(n=>n.Number==d.Number&&n.Bounds==d.Bounds)),"All Phase 1 181 numbers/bboxes retained");
                var id=((Detection)list.Items[0].Tag!).Id;
                list.Rename(id,"PortableTest"); list.Items.Cast<ListViewItem>().Single(i=>((Detection)i.Tag!).Id==id).Checked=true;
                form.BeginAdd(); form.AddAt("ManualTest",new(300,300));
                form.Close();
                sw.Restart();
                using var restored=new MainForm(path,store,settings,runner); restored.Show();Wait(restored);
                Console.WriteLine($"MEASURE cached={sw.Elapsed.TotalSeconds:F3}s");
                var rows=Field<OcrResultsList>(restored,"list");
                Check(runner.Calls==1,"Reload uses saved cache without Python execution");
                Check(rows.Items.Cast<ListViewItem>().Any(i=>i.Text=="PortableTest"&&i.Checked&&((Detection)i.Tag!).Id==id),"Edit/done/fixed ID restored");
                Check(rows.Items.Cast<ListViewItem>().Any(i=>i.Text=="ManualTest"&&((Detection)i.Tag!).IsManual),"Manual record restored");
                restored.SetInventoryMode(true);Check(rows.Items.Count>0,"Inventory mode works after portable OCR");restored.Close();
            }
            Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"600dpi source bytes unchanged");
        }
        finally { Environment.CurrentDirectory=cwd; foreach(var n in names) Environment.SetEnvironmentVariable(n,saved[n]); }
        Console.WriteLine($"PASS: {count} portable checks");
    }
}
