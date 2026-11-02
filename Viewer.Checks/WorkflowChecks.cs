using AF.DecalFinder;
using System.Reflection;
using System.Text.Json;

namespace AF.DecalFinder.Checks;
internal static class WorkflowChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden)!.GetValue(target)!;
    static void Key(Control target, Keys key) => target.GetType().GetMethod("OnKeyDown", Hidden)!.Invoke(target, [new KeyEventArgs(key)]);
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
    sealed class ReplacementRunner : IOcrRunner
    {
        internal int Calls;
        public Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(new OcrCatalog(new Size(image.Width,image.Height),
                [new("4",new Rectangle(100,100,40,40),90,"new-false"),new("2",new Rectangle(600,600,40,40),90,"new-good")]));
        }
    }
    internal static void Run(string root)
    {
        var folder = Path.Combine(root, "viewer-checks", "workflow-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "sample.png"); using (var bitmap = new Bitmap(1600,1200)) bitmap.Save(path);
        var identity = ImageIdentity.Read(path, new Size(1600,1200));
        var records = new List<Detection> {
            new("",new Rectangle(700,500,40,40),0,"unknown") {IsUnrecognized=true},
            new("4",new Rectangle(800,500,40,40),90,"four") {IsDone=true},
            new("04",new Rectangle(900,500,40,40),90,"zero-four"),
            new("10",new Rectangle(1000,500,40,40),90,"ten"),
            new("BC",new Rectangle(600,600,40,40),90,"bc"),
            new("A1",new Rectangle(700,600,40,40),90,"a"),
            new("あ",new Rectangle(800,600,40,40),90,"kana"),
            new("?",new Rectangle(900,600,40,40),90,"literal") };
        var catalog = new OcrCatalog(new Size(1600,1200), records);
        var store = new EditStore(Path.Combine(folder,"cache")); store.Save(identity,catalog);
        var saved = JsonDocument.Parse(File.ReadAllText(store.PathFor(path)));
        Check(saved.RootElement.GetProperty("SchemaVersion").GetInt32()==2 && saved.RootElement.GetProperty("SourceSha256").GetString()!.Length==64,"Schema 2 stores content evidence and state flags");
        var settings = new AppSettingsStore(Path.Combine(folder,"Settings.json"));
        using (var form = new MainForm(path,store,settings))
        {
            form.Show(); Application.DoEvents();
            var list = Field<OcrResultsList>(form,"list"); var search = Field<DigitsTextBox>(form,"number");
            var view = Field<ImageViewControl>(form,"view");
            Check(list.Items.Cast<ListViewItem>().Select(i=>((Detection)i.Tag!).Id).SequenceEqual(new[]{"four","zero-four","ten","a","bc","literal","kana","unknown"}),"Numeric/Latin/other/unrecognized sort and leading zero retained");
            search.Text="?";
            Check(Field<IReadOnlyList<Detection>>(view,"highlights").Single().Id=="literal","Literal question mark search excludes unrecognized placeholder");
            list.SelectDetection("unknown",false); Application.DoEvents();
            Check(list.SelectedDetections.Single().IsUnrecognized,"Unrecognized candidate selectable by fixed ID");
            var center=view.ImageCenter;var zoom=view.Zoom;
            Check(list.Rename("unknown","あ1"),"Unknown accepts kana/alphanumeric correction");Application.DoEvents();
            var changed=list.SelectedDetections.Single();
            Check(changed.Id=="unknown" && !changed.IsUnrecognized && changed.IsModified && changed.Number=="あ1" && view.ImageCenter==center && view.Zoom==zoom,"Correction keeps ID/selection/viewport and clears unknown state");
            using (var validation = new OcrResultsList())
            {
                validation.SetDetections([new("1",new Rectangle(0,0,10,10),90,"test")]);
                Check(!validation.Rename("test","") && !validation.Rename("test","   "),"Empty/whitespace-only edit rejected");
            }
            foreach (var value in new[]{"A1","あ","あ1","04"}) {search.Text=value;Check(Field<IReadOnlyList<Detection>>(view,"highlights").Count==1,"Exact string search: "+value);}
            search.Text="a1";Check(Field<IReadOnlyList<Detection>>(view,"highlights").Count==0,"Search does not normalize case");
            list.SelectDetection("four",false);list.SelectDetection("zero-four",true);Application.DoEvents();
            int asked=0;form.ConfirmDeletion=n=>{asked=n;return false;};Key(list,Keys.Delete);
            Check(asked==2 && list.Items.Count==8 && !list.SelectedDetections.Any(d=>d.IsDeleted),"Delete asks selected count; cancel changes nothing");
            form.ConfirmDeletion=n=>{asked=n;return true;};
            Check(list.ContextMenuStrip!.Items[0].Text=="誤検出として削除","Context menu exposes explicit false-positive deletion");
            list.ContextMenuStrip.Items[0].PerformClick();Application.DoEvents();
            Check(list.Items.Count==6 && list.SelectedItems.Count==1 && !list.SelectedDetections[0].IsDeleted,"Delete removes selected fixed IDs and selects next candidate");
            var after=store.Load(identity)!;
            Check(after.Detections.Count==8 && after.Detections.Count(d=>d.IsDeleted)==2 && after.DoneCount==0,"Logical deletion persisted; deleted done records excluded from count");
            Check(after.Find("4").Count==0 && after.Find("04").Count==0,"Deleted records excluded from search");
            var box=view.ScreenBounds(records[1].Bounds);
            Check(view.HitTestDetection(Point.Round(new PointF(box.X+box.Width/2,box.Y+box.Height/2)))==null,"Deleted record excluded from image reverse lookup");
            form.BeginAdd();form.AddAt("いB",new PointF(1100,700));
            Check(list.SelectedDetections.Single().IsManual && list.SelectedDetections.Single().Number=="いB","Manual addition accepts mixed label");
            Key(list,Keys.Control|Keys.A);Application.DoEvents();
            Check(list.SelectedItems.Count==7 && Field<IReadOnlyList<Detection>>(view,"selections").Count==7,"Ctrl+A selects all active records and blue overlays");
            list.SelectDetection("unknown",false);
            Field<Button>(form,"floatList").PerformClick();Application.DoEvents();
            var floatingCenter=view.ImageCenter;
            Check(list.Parent is Form && list.Rename("unknown","A1あ") && view.ImageCenter==floatingCenter && list.SelectedDetections.Single().Id=="unknown",
                "Floating list string correction keeps selected fixed ID and viewport");
            list.Rename("unknown","あ1");
            form.OwnedForms.Single().Close();Application.DoEvents();
            Check(list.Parent is not Form && list.SelectedDetections.Single() is {Id:"unknown",Number:"あ1"} && store.Load(identity)!.Find("あ1").Count==1,
                "Redocking retains corrected string, selection and persisted record");
            form.Close();
        }
        using(var restarted=new MainForm(null,new EditStore(store.DirectoryPath),settings))
        {
            restarted.Show();Application.DoEvents();var rows=Field<OcrResultsList>(restarted,"list");
            Check(rows.Items.Count==7 && rows.Items.Cast<ListViewItem>().Any(i=>i.Text=="あ1") && rows.Items.Cast<ListViewItem>().Any(i=>i.Text=="いB"),"Startup restores correction/manual data and keeps tombstones hidden");restarted.Close();
        }
        var archive=store.ArchiveInvalid(path);File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(5));
        var updated=ImageIdentity.Read(path,new Size(1600,1200));
        var fresh=new OcrCatalog(updated.Width==1600?new Size(1600,1200):Size.Empty,[new("4",records[1].Bounds,90,"new-false"),new("2",new Rectangle(200,200,40,40),80,"fresh")]);
        Check(store.RestoreProtected(updated,fresh,archive)>=4 && fresh.Detections.Any(d=>d.Id=="four" && d.IsDeleted) && !fresh.Detections.Any(d=>d.Id=="new-false"),"Same bytes reanalysis retains tombstone ID and suppresses rediscovered false positive");
        Check(fresh.Detections.Any(d=>d.Id=="unknown" && d.Number=="あ1") && fresh.Detections.Any(d=>d.IsManual && d.Number=="いB"),"Same bytes reanalysis retains edits/manual additions");
        using(var bitmap=new Bitmap(1600,1200)){using var g=Graphics.FromImage(bitmap);g.Clear(Color.Red);bitmap.Save(path);}
        var changedImage=ImageIdentity.Read(path,new Size(1600,1200));
        Check(store.RestoreProtected(changedImage,new OcrCatalog(new Size(1600,1200),[]),archive)==0 && File.Exists(archive),"Changed image bytes never receive old coordinates; archive remains");
        // A legacy cache is read without modification; first save safely migrates it.
        var legacyPath=Path.Combine(folder,"legacy.png");File.Copy(path,legacyPath);var legacyIdentity=ImageIdentity.Read(legacyPath,new Size(1600,1200));
        var legacy=new EditStore.Document(1,legacyIdentity,[new("legacy","004",[1,1,20,20],80,false,true,true,"4",[1,1,20,20])]);
        File.WriteAllText(store.PathFor(legacyPath),JsonSerializer.Serialize(legacy));var original=File.ReadAllBytes(store.PathFor(legacyPath));
        var migrated=store.Load(legacyIdentity)!;Check(migrated.Detections.Single() is {Id:"legacy",Number:"004",IsDone:true,IsModified:true} && original.SequenceEqual(File.ReadAllBytes(store.PathFor(legacyPath))),"Legacy schema loads all work without destructive migration");
        store.Save(legacyIdentity,migrated);Check(Directory.GetFiles(store.DirectoryPath,"*.bak").Any(p=>File.ReadAllBytes(p).SequenceEqual(original)),"First schema 2 save backs up exact legacy document");
        var parsed=OcrRunner.Parse("""{"image":{"width":100,"height":100},"detections":[{"id":"u","number":"","bbox":[10,10,20,20],"confidence":0,"is_unrecognized":true}]}""",new Size(100,100));
        Check(parsed.Detections.Single().IsUnrecognized && parsed.Find("?").Count==0,"Worker unread circle remains a searchable-by-position record");
        var overlap=new OcrCatalog(new Size(100,100),[new("1",new Rectangle(10,10,30,30),90,"fresh")]);
        overlap.PreserveWork(new OcrCatalog(new Size(100,100),
            [new("A",new Rectangle(10,10,30,30),0,"manual") {IsManual=true},
             new("1",new Rectangle(10,10,30,30),90,"deleted") {IsDeleted=true}]));
        Check(overlap.Detections.Count==2 && overlap.Detections.Any(d=>d.Id=="manual") && overlap.Detections.Any(d=>d.Id=="deleted"),"Overlapping protected records keep every independent fixed ID");
        var rerunStore=new EditStore(Path.Combine(folder,"rerun"));
        rerunStore.Save(changedImage,new OcrCatalog(new Size(1600,1200),
            [new("4",new Rectangle(100,100,40,40),90,"deleted") {IsDeleted=true},
             new("Ａ1",new Rectangle(500,500,40,40),90,"edited") {IsModified=true,IsDone=true}]));
        var replacement=new ReplacementRunner();
        using(var form=new MainForm(path,rerunStore,new AppSettingsStore(Path.Combine(folder,"rerun-settings.json")),replacement))
        {
            form.Show();Application.DoEvents();var detail=Field<ImageViewControl>(form,"view");
            detail.SetZoom(2);detail.CenterOn(new PointF(700,600));var center=detail.ImageCenter;
            Check(replacement.Calls==0,"Valid cache never automatically reruns OCR after upgrade");
            form.Reanalyze();Application.DoEvents();
            var data=rerunStore.Load(changedImage)!;
            Check(replacement.Calls==1 && data.Detections.Any(d=>d.Id=="deleted" && d.IsDeleted) && data.Find("4").Count==0,"Explicit reanalysis keeps deletion and removes repeated false candidate");
            Check(data.Find("Ａ1").Single() is {Id:"edited",IsModified:true,IsDone:true} && data.Find("2").Count==1,"Explicit reanalysis retains work and adds fresh candidates without normalization");
            Check(detail.Zoom==2 && detail.ImageCenter==center && !form.IsAnalyzing,"Explicit reanalysis preserves viewport and restores controls");
            form.Close();
        }
        Console.WriteLine($"PASS: {count} workflow checks");
    }
}
