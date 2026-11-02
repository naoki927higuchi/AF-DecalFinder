using AF.DecalFinder;
using System.Reflection;
using System.Text.Json;
namespace AF.DecalFinder.Checks;
internal static class InventoryChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Hidden)!.GetValue(o)!;
    static void Key(Control o,Keys k,bool up=false)=>o.GetType().GetMethod(up?"OnKeyUp":"OnKeyDown",Hidden)!.Invoke(o,[new KeyEventArgs(k)]);
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","inventory-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"image.png");using(var b=new Bitmap(2000,1600))b.Save(path);
        var identity=ImageIdentity.Read(path,new Size(2000,1600));var store=new EditStore(Path.Combine(folder,"cache"));var settings=new AppSettingsStore(Path.Combine(folder,"settings.json"));
        var records=Enumerable.Range(1,6).Select(i=>new Detection(i==3?"":i.ToString(),new Rectangle(300+i*180,500,40,40),80,"r"+i){IsUnrecognized=i==3,IsDone=i==1}).ToList();
        store.Save(identity,new OcrCatalog(new(2000,1600),records));
        // Legacy cache lacks the optional state, and opening it must not rewrite it.
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(store.PathFor(path)))!;
        foreach(var row in json["Records"]!.AsArray())row!.AsObject().Remove("IsReviewed");
        File.WriteAllText(store.PathFor(path),json.ToJsonString());var legacy=File.ReadAllBytes(store.PathFor(path));
        Check(store.Load(identity)!.Detections.All(d=>!d.IsReviewed)&&legacy.SequenceEqual(File.ReadAllBytes(store.PathFor(path))),"Legacy records default to unreviewed without mutation");
        var reviewedOnly=new Detection("1",new(10,10,20,20),90,"review-only"){IsReviewed=true};
        var fresh=new OcrCatalog(new(2000,1600),[new("1",reviewedOnly.Bounds,90,"new-id")]);
        Check(fresh.PreserveWork(new(new(2000,1600),[reviewedOnly]))==1&&fresh.Detections.Single().Id=="review-only"&&fresh.Detections.Single().IsReviewed,"Same-image reanalysis preserves review-only work and its fixed ID");
        using(var form=new MainForm(path,store,settings))
        {
            form.Show();Application.DoEvents();var list=Field<OcrResultsList>(form,"list");var data=Field<OcrCatalog>(form,"catalog");var view=Field<ImageViewControl>(form,"view");
            Check(!form.InventoryMode&&list.Items.Count==6,"Startup is normal mode");
            int navigation=0;list.NavigateRequested+=_=>navigation++;
            form.SetInventoryMode(true);Application.DoEvents();Check(list.SelectedDetections.Single().Id=="r1"&&navigation==1,"Inventory enters at first unreviewed candidate with one pan");
            navigation=0;Key(list,Keys.Space);Application.DoEvents();
            Check(data.Detections[0].IsReviewed&&data.Detections[0].IsDone&&list.SelectedDetections.Single().Id=="r2"&&navigation==1&&list.Focused,"Space reviews one; keeps done separate; next row, focus and one pan");
            Key(list,Keys.Space);Application.DoEvents();Check(list.Items.Count==5,"Space autorepeat ignored until KeyUp");Key(list,Keys.Space,true);
            form.Undo();Application.DoEvents();Check(!data.Detections[0].IsReviewed&&list.Items.Count==6&&list.SelectedDetections.Single().Id=="r1","Undo review restores fixed ID selection and state");
            form.Undo();Check(list.Items.Count==6,"Undo is one-shot without redo");
            var command=typeof(MainForm).GetMethod("ProcessCmdKey",Hidden)!;view.Focus();
            command.Invoke(form,[Message.Create(form.Handle,0x100,(IntPtr)(int)Keys.Space,IntPtr.Zero),Keys.Space]);
            command.Invoke(form,[Message.Create(form.Handle,0x100,(IntPtr)(int)Keys.Space,IntPtr.Zero),Keys.Space]);
            Check(list.Items.Count==5&&list.Focused,"Global Space from image focuses next LV row and suppresses repeat");
            typeof(MainForm).GetMethod("ProcessKeyPreview",Hidden)!.Invoke(form,[Message.Create(form.Handle,0x101,(IntPtr)(int)Keys.Space,IntPtr.Zero)]);
            form.Undo();
            list.SelectDetection("r1",false);list.SelectDetection("r2",true);navigation=0;Key(list,Keys.Control|Keys.Enter);Application.DoEvents();
            Check(list.Items.Count==4&&data.Detections.Count(d=>d.IsReviewed)==2&&navigation==1,"Ctrl+Enter reviews entire selection in one action");
            Key(list,Keys.Control|Keys.Enter);Check(list.Items.Count==4,"Bulk confirmation autorepeat ignored");Key(list,Keys.Enter,true);
            form.Undo();Check(data.Detections.All(d=>!d.IsReviewed)&&list.SelectedItems.Count==2,"Bulk review undo restores both selected records");
            list.SelectDetection("r2",false);navigation=0;list.Rename("r2","A2");Application.DoEvents();
            Check(data.Detections.Single(d=>d.Id=="r2").IsReviewed&&!list.ContainsDetection("r2")&&navigation==1&&list.Focused,"Inventory label commit reviews and advances once");
            form.Undo();Check(data.Detections.Single(d=>d.Id=="r2").Number=="2"&&!data.Detections.Single(d=>d.Id=="r2").IsReviewed,"Label and review state undo together");
            form.ImageEditPrompt=(_,_)=>null;var imageEdit=Field<Action<Detection,Point>>(view,"DetectionEditRequested");var before=File.ReadAllBytes(store.PathFor(path));imageEdit(data.Detections[1],Point.Empty);
            Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor(path)))&&!data.Detections[1].IsReviewed,"Image-edit cancellation changes no review/cache state");
            form.ImageEditPrompt=(_,_)=>"あ2";imageEdit(data.Detections[1],Point.Empty);Check(data.Detections[1].IsReviewed&&data.Detections[1].Number=="あ2","Image editing uses shared inventory commit");form.Undo();
            var filter=Field<CheckBox>(form,"unknownOnly");filter.Checked=true;list.SelectDetection("r3",false);form.ReviewSelection(false);
            Check(list.Items.Count==0&&Field<ToolStripLabel>(form,"inventoryStatus").Text!="棚卸完了","Filtered zero rows is not completion while recognized unreviewed records remain");
            form.Undo();filter.Checked=false;
            var confirmation=Field<ToolStripMenuItem>(form,"deleteConfirmation");int prompts=0;form.ConfirmDeletion=n=>{prompts=n;return false;};list.SelectDetection("r4",false);Key(list,Keys.Delete);
            Check(prompts==1&&!data.Detections[3].IsDeleted,"Delete confirmation ON cancels safely");confirmation.Checked=false;prompts=0;
            list.SelectDetection("r4",false);list.SelectDetection("r5",true);navigation=0;Key(list,Keys.Delete);Application.DoEvents();
            Check(prompts==0&&data.Detections.Count(d=>d.IsDeleted)==2&&list.SelectedDetections.Single().Id=="r6"&&navigation==1,"Confirmation OFF deletes selection and advances once");
            form.Undo();Check(!data.Detections.Any(d=>d.IsDeleted)&&list.SelectedDetections.Select(d=>d.Id).ToHashSet().SetEquals(["r4","r5"]),"Multi-delete undo restores all records and selection");
            form.SetInventoryMode(false);list.SelectDetection("r6",false);list.RenameFromImage("r6","BC");
            Check(list.SelectedDetections.Any(d=>d.Id=="r6")&&!data.Detections[5].IsReviewed,"Normal image edit keeps target selection without reviewing");form.Undo();
            list.SelectDetection("r6",false);Key(list,Keys.Delete);Check(list.SelectedDetections.Single().Id=="r3","Normal last-row delete selects preceding sorted candidate");form.Undo();
            list.SelectDetection("r1",false);list.Items.Cast<ListViewItem>().Single(r=>((Detection)r.Tag!).Id=="r1").Checked=false;form.Undo();
            Check(data.Detections[0].IsDone,"Done checkbox undo restores previous done state");
            int original=data.Detections.Count;form.BeginAdd();form.AddAt("manual",new(1000,900));Check(data.Detections.Count==original+1,"Manual addition recorded");form.Undo();Check(data.Detections.Count==original,"Manual-add undo removes only new record");
            form.SetInventoryMode(true);Key(list,Keys.Control|Keys.A);Application.DoEvents();Key(list,Keys.Control|Keys.Enter);Key(list,Keys.Enter,true);
            Check(list.Items.Count==0&&list.SelectedItems.Count==0&&Field<ToolStripLabel>(form,"inventoryStatus").Text=="棚卸完了","All reviewed shows completion and empty selection");
            Check(store.Load(identity)!.Detections.All(d=>d.IsReviewed),"Review progress persisted per image");
            form.SetInventoryMode(false);Check(list.Items.Count==6,"Normal mode shows reviewed records");
            var commands=Field<Dictionary<string,List<ToolStripItem>>>(form,"commands");Check(commands["review"].All(i=>!i.Enabled)&&commands["undo"].All(i=>i.Enabled),"Mode/undo command enablement shared across entry points");
            commands["undo"][0].PerformClick();Check(data.Detections.All(d=>!d.IsReviewed),"Menu Undo uses transaction handler");
            commands["inventory"][1].PerformClick();list.SelectDetection("r1",false);commands["review"][1].PerformClick();Check(data.Detections[0].IsReviewed,"Toolbar review shares keyboard operation");
            var another=Path.Combine(folder,"other.png");File.Copy(path,another);store.Save(ImageIdentity.Read(another,new(2000,1600)),new(new(2000,1600),[]));
            typeof(MainForm).GetMethod("OpenImage",Hidden)!.Invoke(form,[another]);Application.DoEvents();
            Check(commands["undo"].All(i=>!i.Enabled),"Image switch clears one-transaction undo");
            form.Close();
        }
        using(var form=new MainForm(path,store,settings)){form.Show();Application.DoEvents();Check(!form.InventoryMode&&Field<OcrCatalog>(form,"catalog").Detections[0].IsReviewed&&!settings.LoadDeleteConfirmation(),"Restart restores review and delete preference, not inventory mode");Check(Field<Dictionary<string,List<ToolStripItem>>>(form,"commands")["undo"].All(i=>!i.Enabled),"Restart has no undo history");form.Close();}
        Console.WriteLine($"PASS: {count} inventory/undo checks");
    }
}
