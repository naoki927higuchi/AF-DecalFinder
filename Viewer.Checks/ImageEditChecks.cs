using AF.DecalFinder;
using System.Reflection;
using System.Diagnostics;
namespace AF.DecalFinder.Checks;
internal static class ImageEditChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Hidden)!.GetValue(o)!;
    static void Key(Control o,Keys key)=>o.GetType().GetMethod("OnKeyDown",Hidden)!.Invoke(o,[new KeyEventArgs(key)]);
    static int count;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS: "+text);}
    static void Pump(int ms){var t=Stopwatch.StartNew();while(t.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(5);}}
    sealed class Canvas:ImageViewControl
    {
        protected override bool AddToSelection=>true;
        internal void Down(Point p,int clicks=1)=>OnMouseDown(new(MouseButtons.Left,clicks,p.X,p.Y,0));
        internal void Up(Point p,int clicks=1)=>OnMouseUp(new(MouseButtons.Left,clicks,p.X,p.Y,0));
        internal void MovePointer(Point p)=>OnMouseMove(new(MouseButtons.Left,0,p.X,p.Y,0));
        internal void Double(Point p){Down(p);Up(p);Down(p,2);Up(p,2);Application.DoEvents();}
    }
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","image-edit-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"RX-78.png");File.Copy(Path.Combine(root,"inputs","RX-78 ver 3.0.png"),path);
        using var source=new Bitmap(path);var identity=ImageIdentity.Read(path,source.Size);
        var store=new EditStore(Path.Combine(folder,"cache"));var settings=new AppSettingsStore(Path.Combine(folder,"settings.json"));
        store.Save(identity,OcrRunner.Parse(File.ReadAllText(Path.Combine(root,"Evaluation181","RX-78 ver 3.0.png.json")),source.Size));
        string correctedId="";int deleted=0;
        using(var form=new MainForm(path,store,settings))
        using(var host=new Form{ClientSize=new Size(800,600)})
        using(var canvas=new Canvas{Dock=DockStyle.Fill,Mode=ImageViewMode.Detail})
        {
            form.Show();Application.DoEvents();var list=Field<OcrResultsList>(form,"list");var filter=Field<CheckBox>(form,"unknownOnly");var view=Field<ImageViewControl>(form,"view");var data=Field<OcrCatalog>(form,"catalog");
            filter.Checked=true;Key(list,Keys.Control|Keys.A);Application.DoEvents();var selected=list.SelectedDetections.Select(d=>d.Id).ToHashSet();
            Check(selected.Count==237,"RX-78 unknown Ctrl+A selects 237 candidates");
            host.Controls.Add(canvas);host.Show();canvas.SetImage(new Bitmap(source));canvas.SetDetections(data.Detections);canvas.SetZoom(2);
            canvas.ControlClickTarget=view.ControlClickTarget;canvas.DetectionClicked+=Field<Action<Detection,bool>>(view,"DetectionClicked");canvas.DetectionEditRequested+=Field<Action<Detection,Point>>(view,"DetectionEditRequested");
            Point At(Detection d){canvas.CenterOn(d.UiCenter);var box=canvas.ScreenBounds(d.UiBounds);return Point.Round(new PointF(box.X+box.Width/2,box.Y+box.Height/2));}
            var unknown=data.ActiveDetections.First(d=>d.IsUnrecognized&&canvas.HitTestDetection(At(d))==d);var point=At(unknown);var center=view.ImageCenter;var zoom=view.Zoom;var canvasCenter=canvas.ImageCenter;
            var saved=File.ReadAllBytes(store.PathFor(path));int prompts=0;
            form.ImageEditPrompt=(d,p)=>{prompts++;Check(d.Id==unknown.Id&&selected.SetEquals(list.SelectedDetections.Select(x=>x.Id)),"Dialog opens for hit fixed ID without changing selection");return null;};
            canvas.Down(point);canvas.Up(point);
            Check(selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"First Ctrl click is deferred, not toggled before double click");
            canvas.Down(point,2);canvas.Up(point,2);Pump(SystemInformation.DoubleClickTime+60);
            Check(prompts==1&&selected.SetEquals(list.SelectedDetections.Select(d=>d.Id))&&saved.SequenceEqual(File.ReadAllBytes(store.PathFor(path)))&&unknown.IsUnrecognized,"Cancel leaves record, complete selection and cache bytes unchanged");
            Check(view.ImageCenter==center&&view.Zoom==zoom&&canvas.ImageCenter==canvasCenter,"Cancel preserves both viewports and zoom");
            point=At(unknown);canvas.Down(point);canvas.Up(point);Pump(SystemInformation.DoubleClickTime+60);
            Check(list.SelectedItems.Count==selected.Count-1&&!list.SelectedDetections.Any(d=>d.Id==unknown.Id),"Real WinForms timer commits a single Ctrl click after interval");
            canvas.Down(point);canvas.Up(point);Pump(SystemInformation.DoubleClickTime+60);
            Check(selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"Second single click restores target selection");
            canvas.Down(point);canvas.Up(point);canvas.Down(point,2);canvas.MovePointer(new(point.X+30,point.Y));canvas.MovePointer(point);canvas.Up(point,2);Pump(SystemInformation.DoubleClickTime+60);
            Check(prompts==1&&selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"Double-click second press dragged beyond threshold neither edits nor toggles");
            typeof(MainForm).GetMethod("FloatList",Hidden)!.Invoke(form,null);Application.DoEvents();
            form.ImageEditPrompt=(d,p)=>{prompts++;Check(selected.SetEquals(list.SelectedDetections.Select(x=>x.Id)),"Floating edit opens without altering selection");return "あA183";};
            point=At(unknown);canvas.Double(point);correctedId=unknown.Id;
            Check(unknown.Number=="あA183"&&!unknown.IsUnrecognized&&unknown.IsModified&&selected.SetEquals(list.SelectedDetections.Select(d=>d.Id)),"Normal edit keeps corrected target and other selections");
            Check(!filter.Checked&&list.ContainsDetection(correctedId)&&list.Items.Count==357,"Normal edit reveals corrected fixed ID for selection retention");
            filter.Checked=true;selected.Remove(correctedId);
            Check(store.Load(identity)!.Find("あA183").Single().Id==correctedId&&view.ImageCenter==center&&view.Zoom==zoom,"Correction persisted with fixed ID and viewport unchanged");
            var known=data.ActiveDetections.First(d=>!d.IsUnrecognized&&d.Id!=correctedId&&canvas.HitTestDetection(At(d))==d);var knownValue=known.Number;saved=File.ReadAllBytes(store.PathFor(path));
            form.ImageEditPrompt=(d,p)=>{prompts++;Check(d.Id==known.Id,"Hidden recognized record can be directly edited");return null;};canvas.Double(At(known));
            Check(known.Number==knownValue&&saved.SequenceEqual(File.ReadAllBytes(store.PathFor(path)))&&selected.SetEquals(list.SelectedDetections.Select(d=>d.Id))&&filter.Checked,"Recognized edit cancellation preserves filter and all state");
            form.ImageEditPrompt=(d,p)=>"BC183";canvas.Double(At(known));
            Check(known.Number=="BC183"&&known.IsModified&&selected.Append(known.Id).ToHashSet().SetEquals(list.SelectedDetections.Select(d=>d.Id))&&!filter.Checked,"Recognized edit reveals target and preserves unrelated selections");
            filter.Checked=true;
            Check(store.Load(identity)!.Find("BC183").Single().Id==known.Id,"Recognized edit saved under original fixed ID");
            typeof(MainForm).GetMethod("DockList",Hidden)!.Invoke(form,null);form.ConfirmDeletion=n=>{deleted=n;return true;};Key(list,Keys.Delete);Application.DoEvents();
            Check(deleted==236&&data.Detections.Count(d=>d.IsDeleted)==236&&!unknown.IsDeleted,"Delete removes remaining unknown selection, preserving corrected record");host.Close();form.Close();
        }
        using(var restart=new MainForm(path,store,settings))
        {restart.Show();Application.DoEvents();var data=Field<OcrCatalog>(restart,"catalog");Check(data.Find("あA183").Single().Id==correctedId&&data.Detections.Count(d=>d.IsDeleted)==deleted,"Restart restores correction and deletions");restart.Close();}
        foreach(var initial in new[]{"","A102"})
        {
            var area=Screen.PrimaryScreen!.WorkingArea;
            using var dialog=new NumberDialog(initial,new Point(area.Right-1,area.Bottom-1));dialog.Show();Application.DoEvents();
            var input=Field<DigitsTextBox>(dialog,"input");
            Check(input.Focused&&input.Text==initial&&input.SelectionLength==initial.Length&&area.Contains(dialog.Bounds),"Dialog initial text/select-all/focus and work-area clipping: "+initial);
            input.Text="   ";Check(!((Button)dialog.AcceptButton!).Enabled,"Whitespace cannot confirm");input.Text="いB2";Check(((Button)dialog.AcceptButton!).Enabled,"Mixed Unicode label accepted");dialog.Close();
        }
        foreach(var key in new[]{Keys.Enter,Keys.Escape})
        {
            using var dialog=new NumberDialog("A1",Screen.PrimaryScreen!.WorkingArea.Location);
            dialog.Shown+=(_,_)=>dialog.BeginInvoke(()=>typeof(Form).GetMethod("ProcessDialogKey",Hidden)!.Invoke(dialog,[key]));
            Check(dialog.ShowDialog()==(key==Keys.Enter?DialogResult.OK:DialogResult.Cancel),"Modal dialog keyboard result: "+key);
        }
        Console.WriteLine($"PASS: {count} image-edit checks");
    }
}
