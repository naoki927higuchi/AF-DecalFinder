using AF.DecalFinder;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Diagnostics;

namespace AF.DecalFinder.Checks;
internal static class FrameChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Hidden)!.GetValue(o)!;
    static int count;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS: "+text);}
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","frames-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var settings=new AppSettingsStore(Path.Combine(folder,"Settings.json"));
        Check(settings.LoadFrameWidth()==2,"Missing settings default to 2px");
        foreach(var json in new[]{"{}","{\"FrameWidthPx\":0}","{\"FrameWidthPx\":5}","{\"FrameWidthPx\":\"3\"}","{\"FrameWidthPx\":null}"})
        {File.WriteAllText(settings.FilePath,json);Check(settings.LoadFrameWidth()==2,"Absent/invalid frame value falls back safely: "+json);}
        File.WriteAllText(settings.FilePath,"{\"Other\":{\"value\":42}}");
        settings.RecordOpened(Path.Combine(folder,"missing.png"));
        settings.SaveWindow(new WindowPlacement(50,60,1000,700,false));
        settings.SaveFrameWidth(4);
        Check(settings.LoadFrameWidth()==4 && settings.Load().RecentImages.Count==1 && JsonNode.Parse(File.ReadAllText(settings.FilePath))!["Other"]!["value"]!.GetValue<int>()==42,"Frame save preserves history, placement and unknown fields");
        using(var form=new MainForm(null,new EditStore(Path.Combine(folder,"cache")),settings))
        {
            form.Show();Application.DoEvents();var combo=Field<ComboBox>(form,"frameWidth");var view=Field<ImageViewControl>(form,"view");var overview=Field<ImageViewControl>(form,"overview");
            Check(combo.Items.Count==4 && combo.SelectedIndex==3 && view.FrameWidthPx==4 && overview.FrameWidthPx==4,"MainForm restores dropdown and both views");
            int invalidations=0;view.Invalidated+=(_,_)=>invalidations++;overview.Invalidated+=(_,_)=>invalidations++;
            combo.SelectedIndex=0;
            Check(view.FrameWidthPx==1 && overview.FrameWidthPx==1 && invalidations>=2 && settings.LoadFrameWidth()==1,"Changing dropdown redraws both views and saves immediately");form.Close();
        }
        using(var restored=new MainForm(null,new EditStore(Path.Combine(folder,"cache")),settings))
        {restored.Show();Application.DoEvents();Check(Field<ComboBox>(restored,"frameWidth").SelectedIndex==0 && Field<ImageViewControl>(restored,"view").FrameWidthPx==1,"Restart restores selected width");restored.Close();}
        foreach(var mode in new[]{ImageViewMode.Detail,ImageViewMode.Overview})
        foreach(double scale in new[]{.25,1,2})
        {
            using var canvas=new ImageViewControl{Mode=mode,Size=mode==ImageViewMode.Overview?new Size((int)(400*scale),(int)(400*scale)):new Size(600,600)};
            using var source=new Bitmap(400,400);using(var g=Graphics.FromImage(source))g.Clear(Color.White);
            canvas.SetImage(source,false);canvas.CreateControl();if(mode==ImageViewMode.Detail)canvas.SetZoom(scale);
            var record=new Detection("4",new Rectangle(160,160,80,80),90,"test"){IsDone=true};canvas.SetDetections([record]);
            var original=record.Bounds;var ui=record.UiBounds;var center=canvas.ImageCenter;
            foreach(int width in new[]{1,2,3,4})
            foreach(bool blue in new[]{false,true})
            {
                canvas.FrameWidthPx=width;canvas.SetHighlights([record]);canvas.SetSelections(blue?[record]:[]);
                using var rendered=new Bitmap(canvas.Width,canvas.Height);canvas.DrawToBitmap(rendered,canvas.ClientRectangle);
                var box=canvas.ScreenBounds(record.UiBounds);int y=(int)(box.Top+box.Height/2),x=(int)box.Left;
                int pixels=Enumerable.Range(x-6,13).Count(px=>{var c=rendered.GetPixel(px,y);return blue?c.B>200&&c.R<40:c.R>200&&c.B<40;});
                Check(pixels==width,$"{mode} {scale:P0} {(blue?"blue":"red")} requested={width}px measured={pixels}px");
                var fill=rendered.GetPixel((int)(box.Left+box.Width/2),y);
                Check(fill.R is >=126 and <=128 && fill.R==fill.G && fill.G==fill.B && record.Bounds==original && record.UiBounds==ui && canvas.ImageCenter==center && canvas.HitTestDetection(new Point(x+1,y))==record && canvas.HitTestDetection(new Point(x-1,y))==null,"Fill, source/UI geometry, hit region and viewport unchanged");
            }
        }
        // Same image, viewport and overlays, alternating widths to reduce order/warmup bias.
        using(var bitmap=new Bitmap(Path.Combine(root,"inputs","RX-78 ver 3.0.png")))
        using(var canvas=new ImageViewControl{Mode=ImageViewMode.Detail,Size=new Size(1000,700)})
        using(var target=new Bitmap(1000,700))
        {
            var records=OcrRunner.Parse(File.ReadAllText(Path.Combine(root,"Evaluation181","RX-78 ver 3.0.png.json")),bitmap.Size).Detections;
            canvas.SetImage(bitmap,false);canvas.CreateControl();canvas.SetDetections(records);canvas.SetHighlights(records);canvas.SetSelections(records);
            foreach(var scale in new[]{.25,1,2})
            {
                canvas.SetZoom(scale);var timings=new Dictionary<int,List<double>>{{2,[]},{4,[]}};
                for(int i=0;i<50;i++)foreach(var width in i%2==0?new[]{2,4}:new[]{4,2})
                {
                    canvas.FrameWidthPx=width;var timer=Stopwatch.StartNew();canvas.DrawToBitmap(target,canvas.ClientRectangle);timer.Stop();
                    if(i>=10)timings[width].Add(timer.Elapsed.TotalMilliseconds);
                }
                foreach(var (width,times) in timings)Console.WriteLine($"PAINT RX-78 {scale:P0} {width}px mean={times.Average():F3}ms p95={times.Order().ElementAt(37):F3}ms n=40 (DrawToBitmap included)");
            }
        }
        Console.WriteLine($"PASS: {count} frame width checks");
    }
}
