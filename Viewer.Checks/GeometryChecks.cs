using AF.DecalFinder;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AF.DecalFinder.Checks;

internal static class GeometryChecks
{
    private static int checks;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Hidden)!.GetValue(target)!;
    private static void Check(bool value,string label) { if(!value) throw new Exception(label); checks++;Console.WriteLine("PASS: "+label); }
    private static void Key(Control target,Keys key) => target.GetType().GetMethod("OnKeyDown",Hidden)!.Invoke(target,[new KeyEventArgs(key)]);
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","geometry-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"image.png");
        using(var image=new Bitmap(1000,800)){using var g=Graphics.FromImage(image);g.Clear(Color.White);image.Save(path);}
        var records=Enumerable.Range(0,6).Select(i=>new Detection(i is 0 or 5?"":"4",new Rectangle(410+i%3*150,355+i/3*150,10,12),80,"c"+i)
            {Circle=new(400+i%3*150,350+i/3*150,50,50),IsUnrecognized=i is 0 or 5,IsDone=i==0}).ToList();
        records.Add(new("?",new Rectangle(99,99,2,2),70,"tiny"){Circle=new(100,100,2,2)});
        records.Add(new("9",new Rectangle(0,500,300,300),60,"huge"){Circle=new(150,650,300,300)});
        var raw=records.Select(d=>(d.Id,d.Bounds,d.OriginalBounds,d.Confidence)).ToArray();
        var catalog=new OcrCatalog(new Size(1000,800),records);
        Check(catalog.RepresentativeSize is {Samples:8,Inliers:6,Size.Width:50,Size.Height:50},"Median rejects tiny/large outliers, mean is 50x50");
        Check(records.Count==8 && records.All(d=>d.UiBounds.Size==new SizeF(50,50)),"Outliers retained as records; every UI rectangle uses representative size");
        Check(records[0].UiCenter==new PointF(400,350) && records[0].UiBounds==new RectangleF(375,325,50,50),"Measured circle center beats off-center OCR text box");
        Check(records.Select(d=>(d.Id,d.Bounds,d.OriginalBounds,d.Confidence)).SequenceEqual(raw),"UI derivation leaves original coordinates/confidence unchanged");
        var protectedOld=new Detection("A",records[0].Bounds,80,"edited-old"){IsModified=true};
        var merge=new OcrCatalog(catalog.ImageSize,[new("1",records[0].Bounds,80,"new"){Circle=records[0].Circle}]);
        merge.PreserveWork(new OcrCatalog(catalog.ImageSize,[protectedOld]));
        Check(merge.Detections.Single().Id=="edited-old" && protectedOld.Circle==records[0].Circle && protectedOld.Bounds==records[0].Bounds,
            "Same-image reanalysis enriches protected legacy record only with exact-match measured circle");
        var noCircle=new Detection("old",new Rectangle(80,90,20,30),80,"old");
        Check(LabelGeometry.Bounds(noCircle,catalog.ImageSize,catalog.RepresentativeSize)==new RectangleF(65,80,50,50) && noCircle.Circle==null,"Missing center uses OCR center without inventing circle evidence");
        Check(new OcrCatalog(new Size(1000,800),[noCircle]).RepresentativeSize==null && noCircle.UiBounds==noCircle.Bounds,"Insufficient evidence falls back to original box");
        var manual=catalog.AddManual("A",new PointF(1.25f,2.5f));
        Check(manual.UiCenter==new PointF(1.25f,2.5f) && manual.UiBounds==new RectangleF(0,0,26.25f,27.5f),"Manual click center retained exactly; UI rectangle clipped at image edges");
        var store=new EditStore(Path.Combine(folder,"cache"));var identity=ImageIdentity.Read(path,catalog.ImageSize);store.Save(identity,catalog);
        var json=File.ReadAllText(store.PathFor(path));var loaded=store.Load(identity)!;
        Check(!json.Contains("UiBounds") && !json.Contains("RepresentativeSize"),"Derived UI geometry never persisted as source data");
        Check(loaded.Detections[0].Circle==records[0].Circle && loaded.Detections.Last().ManualCenter==manual.ManualCenter,"Measured circle and exact manual click survive cache roundtrip");
        Check(loaded.Detections.Take(8).Select(d=>(d.Id,d.Bounds,d.OriginalBounds,d.Confidence)).SequenceEqual(raw),"Cache roundtrip preserves all original OCR evidence");
        foreach(int schema in new[]{1,2})
        {
            var legacy=new EditStore.Document(schema,identity,[new("legacy","004",[100,100,12,20],65,false,true,true,"4",[100,100,12,20])]);
            File.WriteAllText(store.PathFor(path),JsonSerializer.Serialize(legacy));var bytes=File.ReadAllBytes(store.PathFor(path));
            var old=store.Load(identity)!;
            Check(old.Detections.Single() is {Number:"004",IsDone:true,IsModified:true,Circle:null} && old.Detections[0].UiBounds==old.Detections[0].Bounds && bytes.SequenceEqual(File.ReadAllBytes(store.PathFor(path))),"Legacy schema "+schema+" loads without destructive migration or guessed circle");
        }
        store.Save(identity,catalog);
        foreach(var mode in new[]{ImageViewMode.Detail,ImageViewMode.Overview})
        {
            using var canvas=new ImageViewControl{Mode=mode,Size=new Size(500,400)};
            var bitmap=new Bitmap(1000,800);using(var g=Graphics.FromImage(bitmap))g.Clear(Color.White);
            canvas.SetImage(bitmap);canvas.SetDetections(records);canvas.CreateControl();
            using var rendered=new Bitmap(500,400);
            var screen=canvas.ScreenBounds(records[0].UiBounds);
            var center=new Point((int)(screen.X+screen.Width/2),(int)(screen.Y+screen.Height/2));
            canvas.DrawToBitmap(rendered,canvas.ClientRectangle);
            var pixel=rendered.GetPixel(center.X,center.Y);
            Check(pixel.R is >=126 and <=128 && pixel.G==pixel.R,"50% black fill covers unified box: "+mode);
            Check(canvas.HitTestDetection(new Point((int)Math.Ceiling(screen.Left)+1,center.Y))?.Id=="c0" && canvas.HitTestDetection(new Point((int)Math.Floor(screen.Left)-1,center.Y))==null,"Hit test matches visible UI boundary: "+mode);
            canvas.SetHighlights([records[0]]);canvas.DrawToBitmap(rendered,canvas.ClientRectangle);
            Check(Enumerable.Range((int)Math.Floor(screen.Left)-1,3).Any(x=>{var p=rendered.GetPixel(x,center.Y);return p.R>200&&p.B<80;}),"Red uses same UI boundary: "+mode);
            canvas.SetSelections([records[0]]);canvas.DrawToBitmap(rendered,canvas.ClientRectangle);
            Check(Enumerable.Range((int)Math.Floor(screen.Left)-1,3).Any(x=>{var p=rendered.GetPixel(x,center.Y);return p.B>200&&p.R<80;}),"Blue uses same boundary over red: "+mode);
        }
        using(var form=new MainForm(path,store,new AppSettingsStore(Path.Combine(folder,"settings.json"))))
        {
            form.Show();Application.DoEvents();var list=Field<OcrResultsList>(form,"list");var filter=Field<CheckBox>(form,"unknownOnly");var view=Field<ImageViewControl>(form,"view");
            var search=Field<DigitsTextBox>(form,"number");view.SetZoom(2);view.CenterOn(new PointF(500,400));var center=view.ImageCenter;
            list.SelectDetection("c0",false);filter.Checked=true;Application.DoEvents();
            Check(list.Items.Count==2 && list.SelectedDetections.Single().Id=="c0" && view.ImageCenter==center && view.Zoom==2,"Filter uses internal unknown flag, preserves visible fixed ID and viewport");
            list.SelectDetection("c5",true);list.Items[0].Checked=false;
            Check(store.Load(identity)!.Detections.Single(d=>d.Id=="c0").IsDone==false && list.SelectedItems.Count==2,"Filtered checkbox saves only target and keeps multiselection");
            Check(list.OrderedDetections.Count()==9,"Filter retains complete underlying dataset");
            search.Text="4";Check(Field<IReadOnlyList<Detection>>(view,"highlights").Count==4,"Search still highlights recognized records outside filtered LV");
            Key(search,Keys.Enter);Check(view.ImageCenter==new PointF(550,350),"Search Enter navigates first all-data hit while filter active");
            view.CenterOn(center);list.Rename("c0","あ");Application.DoEvents();
            Check(list.Items.Count==1 && list.SelectedDetections.Single().Id=="c5" && Field<IReadOnlyList<Detection>>(view,"selections").Count==1 && view.ImageCenter==center,"Correction leaves unknown list and retains other fixed-ID selection without pan");
            form.ConfirmDeletion=_=>false;Key(list,Keys.Delete);
            Check(list.Items.Count==1,"Filtered delete cancel retains record");
            form.ConfirmDeletion=_=>true;Key(list,Keys.Delete);Application.DoEvents();
            Check(list.Items.Count==0 && store.Load(identity)!.Detections.Single(d=>d.Id=="c5").IsDeleted,"Filtered deletion persists tombstone, not filter hiding");
            filter.Checked=false;Application.DoEvents();
            Check(list.Items.Count==8 && list.Items.Cast<ListViewItem>().Any(i=>i.Text=="あ"),"Filter OFF restores normal sorted records after correction");
            filter.Checked=true;
            typeof(MainForm).GetMethod("SelectFromImage",Hidden)!.Invoke(form,[Field<OcrCatalog>(form,"catalog").Detections.Single(d=>d.Id=="c1"),false]);Application.DoEvents();
            Check(!filter.Checked && list.SelectedDetections.Single().Id=="c1" && view.ImageCenter==center,"Recognized reverse lookup exits filter and selects fixed ID without pan");
            form.Close();
        }
        var results=new List<object>();
        foreach(var file in Directory.GetFiles(Path.Combine(root,"Evaluation181"),"*.json").Where(f=>f.EndsWith(".jpg.json")||f.EndsWith(".png.json")))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file));var size=new Size(doc.RootElement.GetProperty("image").GetProperty("width").GetInt32(),doc.RootElement.GetProperty("image").GetProperty("height").GetInt32());
            var data=OcrRunner.Parse(File.ReadAllText(file),size);var before=data.Detections.Select(d=>(d.Bounds,d.OriginalBounds,d.Confidence)).ToArray();
            var timer=Stopwatch.StartNew();for(int i=0;i<100;i++)data.RecalculateUiBounds();timer.Stop();
            Check(data.RepresentativeSize!=null && data.Detections.Select(d=>(d.Bounds,d.OriginalBounds,d.Confidence)).SequenceEqual(before),"8-image measured geometry/original evidence: "+Path.GetFileName(file));
            results.Add(new {Image=Path.GetFileName(file),Count=data.Detections.Count,Estimate=data.RepresentativeSize,MeanDerivationMs=timer.Elapsed.TotalMilliseconds/100});
        }
        Check(results.Count==8,"All eight originals measured");
        File.WriteAllText(Path.Combine(root,"Evaluation181","ui-measurements.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS: {checks} geometry/filter checks");
    }
}
