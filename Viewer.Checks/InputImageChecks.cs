using AF.DecalFinder;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AF.DecalFinder.Checks;
internal static class InputImageChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Hidden)!.GetValue(o)!;
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","inputs-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        using var form=new MainForm(null,new EditStore(Path.Combine(folder,"cache")),new AppSettingsStore(Path.Combine(folder,"Settings.json")));
        form.Show();Application.DoEvents();
        var report=new StringBuilder("# 追加入力画像の確認\n\n実行日時："+DateTimeOffset.Now.ToString("u")+"\n\nRelease 1.6.2。専用キャッシュ・設定で実MainFormの画像読込処理を順次実行。時間は画像読込と表示イベント処理を含む1回の参考値（性能比較ではない）。DPIはGDI+が返す値。\n\n|画像|寸法px|DPI X/Y|LV件数|読込ms|原本SHA256不変|\n|---|---|---|---:|---:|---|\n");
        var files=Directory.GetFiles(Path.Combine(root,"inputs")).Where(p=>Path.GetFileName(p)!="icon.png" && new[]{".jpg",".png",".jpeg"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Order().ToArray();
        foreach(var path in files)
        {
            byte[] before;using(var stream=File.OpenRead(path))before=SHA256.HashData(stream);
            Size size;float dx,dy;using(var source=Image.FromFile(path)){size=source.Size;dx=source.HorizontalResolution;dy=source.VerticalResolution;}
            var timer=Stopwatch.StartNew();typeof(MainForm).GetMethod("OpenImage",Hidden)!.Invoke(form,[path]);Application.DoEvents();timer.Stop();
            var list=Field<OcrResultsList>(form,"list");var view=Field<ImageViewControl>(form,"view");var overview=Field<ImageViewControl>(form,"overview");
            bool original=Convert.ToHexString(before)==Phase1Seed.SourceSha256;
            if(list.Items.Count!=(original?181:0) || Field<DigitsTextBox>(form,"number").Enabled!=original)throw new Exception("OCR isolation failure: "+path);
            if(Field<Bitmap>(view,"bitmap").Size!=size)throw new Exception("Display size failure: "+path);
            foreach(double zoom in new[]{.25,.5,1,2}) {view.SetZoom(zoom);view.CenterOn(new PointF(size.Width-1,size.Height-1));Application.DoEvents();if(view.Zoom!=zoom || view.VisibleImageBounds.IsEmpty || overview.VisibleImageBounds.IsEmpty)throw new Exception("View failure: "+path);}
            byte[] after;using(var stream=File.OpenRead(path))after=SHA256.HashData(stream);
            if(!before.SequenceEqual(after))throw new Exception("Source changed: "+path);
            report.AppendLine($"|{Path.GetFileName(path)}|{size.Width}×{size.Height}|{dx:F2}/{dy:F2}|{list.Items.Count}|{timer.Elapsed.TotalMilliseconds:F0}|一致|");
            Console.WriteLine("PASS: "+Path.GetFileName(path));
        }
        report.AppendLine("\n全画像の切替で寸法警告なし。25/50/100/200%と端へのパン、両ビューの有効表示範囲を確認。600dpi原本と同一の画像だけ181件、それ以外は未解析・空一覧・検索不可。OCRは実行していない。原本の前後SHA256は全件一致。目視による画質比較・各画像のOCR精度評価は今回の対象外。\n");
        form.Close();File.WriteAllText(Path.Combine(root,"INPUT-IMAGE-CHECKS.md"),report.ToString());Console.WriteLine($"PASS: {files.Length} input images");
    }
}
