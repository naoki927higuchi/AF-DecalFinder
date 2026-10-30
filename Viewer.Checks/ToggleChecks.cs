using AF.DecalFinder;
using System.Reflection;
using System.Text.Json;

namespace AF.DecalFinder.Checks;
internal static class ToggleChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Hidden)!.GetValue(target)!;
    static void Key(Control target,Keys key) => target.GetType().GetMethod("OnKeyDown",Hidden)!.Invoke(target,[new KeyEventArgs(key)]);
    static int count;
    static void Check(bool ok,string label) { if(!ok) throw new Exception(label); count++;Console.WriteLine("PASS: "+label); }
    sealed class Canvas : ImageViewControl
    {
        internal bool Ctrl=true;
        protected override bool AddToSelection => Ctrl;
        internal void Down(Point p) => OnMouseDown(new(MouseButtons.Left,1,p.X,p.Y,0));
        internal void MovePointer(Point p) => OnMouseMove(new(MouseButtons.Left,0,p.X,p.Y,0));
        internal void Up(Point p) => OnMouseUp(new(MouseButtons.Left,1,p.X,p.Y,0));
        internal void ClickAt(Point p) { Down(p);Up(p);CommitControlClick();Application.DoEvents(); }
    }
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","toggle-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"RX-78 ver 3.0.png");File.Copy(Path.Combine(root,"inputs","RX-78 ver 3.0.png"),path);
        using var source=new Bitmap(path);var identity=ImageIdentity.Read(path,source.Size);
        var raw=File.ReadAllText(Path.Combine(root,"Evaluation181","RX-78 ver 3.0.png.json"));
        var catalog=OcrRunner.Parse(raw,source.Size);var store=new EditStore(Path.Combine(folder,"cache"));store.Save(identity,catalog);
        var settings=new AppSettingsStore(Path.Combine(folder,"Settings.json"));
        string[] retained=[];int unknownCount=0;
        using(var form=new MainForm(path,store,settings))
        {
            form.Show();Application.DoEvents();
            var list=Field<OcrResultsList>(form,"list");var filter=Field<CheckBox>(form,"unknownOnly");var view=Field<ImageViewControl>(form,"view");
            var actual=Field<OcrCatalog>(form,"catalog");filter.Checked=true;Key(list,Keys.Control|Keys.A);Application.DoEvents();
            unknownCount=actual.ActiveDetections.Count(d=>d.IsUnrecognized);
            Check(unknownCount>2 && list.SelectedItems.Count==unknownCount && Field<IReadOnlyList<Detection>>(view,"selections").Count==unknownCount,"RX-78: filtered Ctrl+A selects all unknown records and blue overlays");
            using var host=new Form{ClientSize=new Size(800,600)};using var canvas=new Canvas{Dock=DockStyle.Fill,Mode=ImageViewMode.Detail};host.Controls.Add(canvas);host.Show();
            canvas.SetImage(new Bitmap(source));canvas.SetDetections(actual.Detections);canvas.SetZoom(2);
            canvas.ControlClickTarget=view.ControlClickTarget;
            canvas.DetectionClicked+=Field<Action<Detection,bool>>(view,"DetectionClicked");
            var near=new Detection("1",new Rectangle(500,500,50,50),90,"near");
            var far=new Detection("",new Rectangle(510,500,50,50),0,"far"){IsUnrecognized=true};
            canvas.SetDetections([near,far]);canvas.CenterOn(new PointF(525,525));
            var overlap=Point.Round(new PointF(canvas.Width/2f,canvas.Height/2f));
            Check(canvas.HitTestDetection(overlap)==near && canvas.HitTestDetection(overlap,d=>d.IsUnrecognized)==far,"Overlap: nearest center wins after hidden records are excluded");
            canvas.SetDetections([]);var beforeMiss=list.SelectedItems.Count;canvas.ClickAt(overlap);
            Check(list.SelectedItems.Count==beforeMiss && filter.Checked,"Ctrl miss preserves selection and filter");
            canvas.SetDetections(actual.Detections);
            Point At(Detection d){canvas.CenterOn(d.UiCenter);var b=canvas.ScreenBounds(d.UiBounds);return Point.Round(new PointF(b.X+b.Width/2,b.Y+b.Height/2));}
            var candidates=actual.ActiveDetections.Where(d=>d.IsUnrecognized).Where(d=>canvas.HitTestDetection(At(d),canvas.ControlClickTarget)?.Id==d.Id).Take(2).ToArray();
            var a=candidates[0];var b=candidates[1];var center=view.ImageCenter;var zoom=view.Zoom;
            var p=At(a);var imageCenter=canvas.ImageCenter;canvas.ClickAt(p);
            Check(list.SelectedItems.Count==unknownCount-1 && !list.SelectedDetections.Any(d=>d.Id==a.Id),"Ctrl click deselects exactly one fixed ID");
            Check(view.ImageCenter==center && view.Zoom==zoom && canvas.ImageCenter==imageCenter && canvas.Zoom==2,"Toggle changes neither image center nor zoom");
            canvas.ClickAt(At(b));Check(list.SelectedItems.Count==unknownCount-2 && !list.SelectedDetections.Any(d=>d.Id==a.Id),"Second toggle preserves other selections");
            canvas.ClickAt(At(a));Check(list.SelectedItems.Count==unknownCount-1 && list.SelectedDetections.Any(d=>d.Id==a.Id),"Repeat Ctrl click adds target back");
            var known=actual.ActiveDetections.First(d=>!d.IsUnrecognized);p=At(known);
            Check(canvas.ControlClickTarget!(known)==false,"Filtered recognized records excluded before hit ranking");
            var selected=list.SelectedDetections.Select(d=>d.Id).ToHashSet();
            p=At(a);canvas.Down(p);canvas.MovePointer(new(p.X+40,p.Y+20));canvas.MovePointer(p);canvas.Up(p);Application.DoEvents();
            Check(selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"Ctrl drag returning to origin never toggles selection");
            canvas.CenterOn(new PointF(source.Width/2f,source.Height/2f));var panStart=canvas.ImageCenter;
            canvas.Down(new(300,300));canvas.MovePointer(new(340,320));canvas.Up(new(340,320));Application.DoEvents();
            Check(canvas.ImageCenter!=panStart && selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"Ctrl drag pans image without altering selections");
            p=At(a);canvas.Down(p);canvas.Up(new(p.X+6,p.Y));Application.DoEvents();
            Check(selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"MouseUp displacement over threshold without MouseMove never toggles");
            typeof(MainForm).GetMethod("FloatList",Hidden)!.Invoke(form,null);Application.DoEvents();canvas.ClickAt(At(a));
            Check(list.SelectedItems.Count==unknownCount-2 && filter.Checked,"Floating list supports toggle and keeps unknown filter");
            typeof(MainForm).GetMethod("DockList",Hidden)!.Invoke(form,null);Application.DoEvents();
            Check(list.Items.Count==unknownCount && list.SelectedItems.Count==unknownCount-2,"Deselected unknowns remain listed after redocking");
            int asked=0;form.ConfirmDeletion=n=>{asked=n;return false;};Key(list,Keys.Delete);
            Check(asked==unknownCount-2 && list.Items.Count==unknownCount,"Delete confirmation count and cancellation");
            retained=[a.Id,b.Id];form.ConfirmDeletion=n=>{asked=n;return true;};list.ContextMenuStrip!.Items[0].PerformClick();Application.DoEvents();
            Check(list.Items.Count==2 && list.Items.Cast<ListViewItem>().All(r=>retained.Contains(((Detection)r.Tag!).Id)),"Delete only selected unknowns, retaining both deselected records");
            Check(store.Load(identity)!.Detections.Count(d=>d.IsDeleted)==unknownCount-2,"Logical deletions autosaved to image cache");
            filter.Checked=false;canvas.Ctrl=false;canvas.SetDetections(actual.Detections);canvas.ClickAt(At(known));
            Check(list.SelectedDetections.Single().Id==known.Id,"Normal click remains single-record reverse lookup");
            host.Close();form.Close();
        }
        using(var restored=new MainForm(path,store,settings))
        {
            restored.Show();Application.DoEvents();Field<CheckBox>(restored,"unknownOnly").Checked=true;
            var list=Field<OcrResultsList>(restored,"list");
            Check(list.Items.Count==2 && list.Items.Cast<ListViewItem>().All(r=>retained.Contains(((Detection)r.Tag!).Id)),"Restart restores deletion and preserves deselected unknown IDs");restored.Close();
        }
        Console.WriteLine($"PASS: {count} toggle checks; RX-78 unknowns={unknownCount}; {folder}");
    }
}
