using AF.DecalFinder;
using System.Reflection;
namespace AF.DecalFinder.Checks;
internal static class ImageIsolationChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Hidden)!.GetValue(o)!;
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"viewer-checks","isolation-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var source=Path.Combine(root,"inputs","600dpi.jpg");
        var other=Path.Combine(folder,"gundam-size.png");
        using(var b=new Bitmap(5009,5809)) b.Save(other);
        var json=Path.Combine(root,"results","600dpi.json");
        check(Phase1Seed.Load(other,new Size(5009,5809),"absent.json")==null,"Unanalysed image never reads Temjin JSON");
        var changed=Path.Combine(folder,"same-size.jpg");
        File.Copy(source,changed);using(var f=new FileStream(changed,FileMode.Append))f.WriteByte(0);
        check(Phase1Seed.Load(changed,new Size(7783,6579),json)==null,"Same dimensions alone cannot authorize Phase 1 data");
        check(Phase1Seed.Load(source,new Size(7783,6579),json)!.Detections.Count==181,"Verified original receives 181 Phase 1 detections");
        var store=new EditStore(Path.Combine(folder,"cache"));
        var settings=new AppSettingsStore(Path.Combine(folder,"Settings.json"));
        using(var form=new MainForm(source,store,settings))
        {
            form.Show();Application.DoEvents();
            var list=Field<OcrResultsList>(form,"list");var search=Field<DigitsTextBox>(form,"number");
            var view=Field<ImageViewControl>(form,"view");
            var initial=(Detection)list.Items[0].Tag!;
            list.Rename(initial.Id,"9998");list.Items.Cast<ListViewItem>().Single(i=>((Detection)i.Tag!).Id==initial.Id).Checked=true;
            form.BeginAdd();form.AddAt("9997",new PointF(4000,3000));
            search.Text="9998";
            void Open(string path) { typeof(MainForm).GetMethod("OpenImage",Hidden)!.Invoke(form,[path]);Application.DoEvents(); }
            Open(other);
            check(list.Items.Count==0 && !search.Enabled && search.Text=="","Switch to 5009x5809 clears list and disables search");
            check(Field<IReadOnlyList<Detection>>(view,"detections").Count==0 && Field<IReadOnlyList<Detection>>(view,"highlights").Count==0 && Field<IReadOnlyList<Detection>>(view,"selections").Count==0,"Image switch clears reverse lookup and all overlays");
            check(Field<Bitmap>(view,"bitmap").Size==new Size(5009,5809),"Unanalysed image remains displayed");
            form.BeginAdd();form.AddAt("8888",new PointF(2000,2000));
            Open(source);
            check(list.Items.Count==182 && list.Items.Cast<ListViewItem>().Any(i=>i.Text=="9998" && i.Checked) && list.Items.Cast<ListViewItem>().Any(i=>i.Text=="9997"),"Return to Temjin restores corrected/manual/done records");
            check(!list.Items.Cast<ListViewItem>().Any(i=>i.Text=="8888"),"Other image manual entry never leaks to Temjin");
            Open(other);
            check(list.Items.Count==1 && list.Items[0].Text=="8888","Other image restores its own independent cache");
            var cachePath=store.PathFor(other);var bytes=File.ReadAllBytes(cachePath);
            bool stale=false;try{store.Load(ImageIdentity.Read(other,new Size(5009,5809)) with{LastWriteUtc=DateTime.UtcNow.AddDays(1)});}catch(InvalidDataException){stale=true;}
            check(stale && bytes.SequenceEqual(File.ReadAllBytes(cachePath)),"Invalid cache is rejected without deleting work data");
            form.Close();
        }
        using var reader=new BinaryReader(File.OpenRead(Path.Combine(root,"Viewer","Assets","App.ico")));
        reader.ReadUInt16();check(reader.ReadUInt16()==1 && reader.ReadUInt16()==7,"ICO has seven icon entries");
        var frames=new List<(int size,int length,int offset)>();
        for(int i=0;i<7;i++){int size=reader.ReadByte();reader.ReadBytes(7);int length=reader.ReadInt32();int offset=reader.ReadInt32();frames.Add((size==0?256:size,length,offset));}
        check(frames.Select(f=>f.size).SequenceEqual(new[]{16,24,32,48,64,128,256}),"ICO contains every requested resolution");
        foreach(var frame in frames)
        {
            reader.BaseStream.Position=frame.offset;using var mem=new MemoryStream(reader.ReadBytes(frame.length));using var bmp=new Bitmap(mem);
            check(bmp.Width==frame.size && bmp.Height==frame.size && bmp.GetPixel(0,0).A==0,"ICO frame preserves size and transparent corner: "+frame.size);
        }
        using var extracted=Icon.ExtractAssociatedIcon(Path.Combine(root,"bin","Release-1.6.2","AF-DecalFinder.exe"));
        check(extracted!=null,"Windows extracts application icon from Release EXE");
    }
}
