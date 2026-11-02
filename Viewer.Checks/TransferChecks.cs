using AF.DecalFinder;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace AF.DecalFinder.Checks;
internal static class TransferChecks
{
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class NoOcr:IOcrRunner {internal int Calls;public Task<OcrCatalog> RunAsync(ImageIdentity image,CancellationToken token){Calls++;throw new Exception("Imported data must avoid OCR");}}
    internal static void Run(string root)
    {
        var folder=Path.Combine(root,"viewer-checks","transfer-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        string Make(string sub,string name,Color color,int w=100){var dir=Path.Combine(folder,sub);Directory.CreateDirectory(dir);var p=Path.Combine(dir,name);using(var b=new Bitmap(w,80)){using var g=Graphics.FromImage(b);g.Clear(color);b.Save(p);}return p;}
        var original=Make("source","Labels.png",Color.White);var second=Make("source","Other.png",Color.White);
        var originalId=ImageIdentity.Read(original,new(100,80));var sourceStore=new EditStore(Path.Combine(folder,"source-cache"));var sourceSettings=new AppSettingsStore(Path.Combine(folder,"source-settings.json"));
        var records=new List<Detection>{new("A",new(10,10,20,20),80,"fixed-1"){IsModified=true,IsDone=true,IsReviewed=true,Circle=new(20,20,24,24),OriginalNumber="4"},
            new("",new(40,20,10,10),0,"fixed-2"){IsUnrecognized=true,IsDeleted=true},new("あ",new(60,30,12,12),0,"manual"){IsManual=true,ManualCenter=new(66,36),OriginalNumber=null,OriginalBounds=null}};
        sourceStore.Save(originalId,new(new(100,80),records));sourceStore.Save(ImageIdentity.Read(second,new(100,80)),new(new(100,80),[new("2",new(10,10,10,10),90,"other")]));
        sourceSettings.SaveFrameWidth(4);sourceSettings.SaveDeleteConfirmation(false);sourceSettings.SaveSearchFolders([Path.GetDirectoryName(original)!]);sourceSettings.RecordOpened(original);sourceSettings.SaveWindow(new(1,2,1000,700,true));
        var export=new DataTransfer(sourceStore,sourceSettings);var items=export.CachedItems();Check(items.Length==2,"Export inventory enumerates primary image caches, not backups");
        var allZip=Path.Combine(folder,"all.zip");export.Export(allZip,items);
        using(var zip=ZipFile.OpenRead(allZip)){Check(zip.Entries.Count==1&&zip.Entries[0].FullName=="manifest.json","ZIP contains JSON manifest only, no originals");using var stream=zip.Entries[0].Open();var data=JsonSerializer.Deserialize<TransferBundle>(stream)!;Check(data.Images.Length==2,"All-image export includes both images");}
        var selectedZip=Path.Combine(folder,"selected.zip");export.Export(selectedZip,[items.Single(i=>i.FileName=="Labels.png")]);
        var destinationStore=new EditStore(Path.Combine(folder,"destination-cache"));var destinationSettings=new AppSettingsStore(Path.Combine(folder,"destination-settings.json"));
        File.WriteAllText(destinationSettings.FilePath,"{\"UnknownFuture\":42}");destinationSettings.RecordOpened(second);destinationSettings.SaveWindow(new(55,66,900,650,false));
        var transfer=new DataTransfer(destinationStore,destinationSettings);Check(transfer.Import(selectedZip)==1,"Selected export/import carries one image");
        Check(transfer.PendingItems().Length==1&&destinationSettings.LoadSearchFolders().Length==0,"Import without image folders is durable pending data");
        Check(destinationSettings.LoadFrameWidth()==4&&!destinationSettings.LoadDeleteConfirmation(),"Portable preferences imported");
        Check(destinationSettings.Load().LastImagePath==second&&destinationSettings.LoadWindow()!.X==55&&JsonNode.Parse(File.ReadAllText(destinationSettings.FilePath))!["UnknownFuture"]!.GetValue<int>()==42,"Destination history, placement and unknown settings preserved");
        Check(new DataTransfer(destinationStore,destinationSettings).PendingItems().Length==1,"Pending imports survive service/application recreation");
        var moved=Make("destination/nested","labels.PNG",Color.Red);File.SetLastWriteTimeUtc(moved,DateTime.UtcNow.AddDays(-8));var movedId=ImageIdentity.Read(moved,new(100,80));
        Check(DataTransfer.Matches(transfer.PendingItems()[0],movedId)&&originalId.LastWriteUtc!=movedId.LastWriteUtc,"Matching uses case-insensitive filename and dimensions, ignores path/time/content");
        var wrong=Make("wrong","Labels.png",Color.Blue,101);Check(!DataTransfer.Matches(transfer.PendingItems()[0],ImageIdentity.Read(wrong,new(101,80))),"Different dimensions do not match");
        destinationSettings.SaveSearchFolders([Path.Combine(folder,"destination")]);var found=DataTransfer.Scan(destinationSettings.LoadSearchFolders(),transfer.PendingItems());
        Check(found.Count==1&&found[0].AbsolutePath==Path.GetFullPath(moved),"Recursive search finds nested image");
        transfer.Bind(transfer.PendingItems()[0],movedId,_=>throw new Exception("No collision expected"));var restored=destinationStore.Load(movedId)!;
        Check(transfer.PendingItems().Length==0&&restored.Detections.Count==3,"Unique association registers destination cache and retires pending item");
        var corrected=restored.Detections.Single(d=>d.Id=="fixed-1");
        Check(corrected.Number=="A"&&corrected.IsReviewed&&corrected.IsDone&&corrected.IsModified&&corrected.OriginalNumber=="4"&&corrected.Circle==records[0].Circle,"Correction/review/done/source text and circle preserved");
        Check(restored.Detections.Single(d=>d.Id=="fixed-2").IsDeleted&&restored.Detections.Single(d=>d.Id=="manual").ManualCenter==new PointF(66,36),"Deletion and manual point preserved");
        var before=File.ReadAllBytes(destinationStore.PathFor(moved));transfer.Import(selectedZip);int conflicts=0;
        Check(!transfer.Bind(transfer.PendingItems()[0],movedId,_=>{conflicts++;return ImportConflict.Skip;})&&conflicts==1&&before.SequenceEqual(File.ReadAllBytes(destinationStore.PathFor(moved))),"Skip collision preserves existing cache byte-for-byte");
        restored.Detections[0].Number="local";destinationStore.Save(movedId,restored);var local=File.ReadAllBytes(destinationStore.PathFor(moved));transfer.Import(selectedZip);
        Check(transfer.Bind(transfer.PendingItems()[0],movedId,_=>ImportConflict.Replace)&&destinationStore.Load(movedId)!.Find("A").Count==1,"Replace collision applies import");
        Check(Directory.GetFiles(destinationStore.DirectoryPath,"*.bak").Any(p=>File.ReadAllBytes(p).SequenceEqual(local)),"Replace backs up previous local work");
        transfer.Import(selectedZip);var duplicate=Make("destination/another","Labels.png",Color.Green);found=DataTransfer.Scan(destinationSettings.LoadSearchFolders(),transfer.PendingItems());
        Check(found.Count(i=>DataTransfer.Matches(transfer.PendingItems()[0],i))==2,"Multiple candidates remain distinguishable for manual binding");
        transfer.Bind(transfer.PendingItems()[0],ImageIdentity.Read(duplicate,new(100,80)),_=>ImportConflict.Skip);Check(destinationStore.Load(ImageIdentity.Read(duplicate,new(100,80)))!=null,"Explicit choice binds one of multiple candidates");
        destinationSettings.SaveSearchFolders([]);Check(destinationSettings.LoadSearchFolders().Length==0,"Folder removal persists locally without deleting images");
        var invalidZip=Path.Combine(folder,"invalid.zip");using(var zip=ZipFile.Open(invalidZip,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open());writer.Write("{\"Version\":99}");}
        before=File.ReadAllBytes(destinationStore.PathFor(moved));int pending=transfer.PendingItems().Length;
        try{transfer.Import(invalidZip);throw new Exception("Invalid archive accepted");}catch(InvalidDataException){}
        Check(before.SequenceEqual(File.ReadAllBytes(destinationStore.PathFor(moved)))&&pending==transfer.PendingItems().Length,"Invalid import changes neither local cache nor pending records");
        var malformedZip=Path.Combine(folder,"malformed-record.zip");var good=items[0];var bad=good with{Data=good.Data with{Records=[good.Data.Records[0] with{Bbox=[99999,0,10,10]}]}};
        using(var zip=ZipFile.Open(malformedZip,ZipArchiveMode.Create)){using var stream=zip.CreateEntry("manifest.json").Open();JsonSerializer.Serialize(stream,new TransferBundle(1,new JsonObject{["FrameWidthPx"]=1},[good,bad]));}
        var settingsBefore=File.ReadAllBytes(destinationSettings.FilePath);
        try{transfer.Import(malformedZip);throw new Exception("Malformed bbox accepted");}catch(InvalidDataException){}
        Check(before.SequenceEqual(File.ReadAllBytes(destinationStore.PathFor(moved)))&&pending==transfer.PendingItems().Length&&settingsBefore.SequenceEqual(File.ReadAllBytes(destinationSettings.FilePath)),"Whole ZIP validated before publishing data or changing preferences");
        var catalogDocument=JsonSerializer.Deserialize<EditStore.Document>(File.ReadAllText(destinationStore.PathFor(moved)))!;
        Check(catalogDocument.Image.AbsolutePath==Path.GetFullPath(moved)&&catalogDocument.Image.LastWriteUtc==movedId.LastWriteUtc,"Bound cache carries destination absolute path and UTC timestamp");
        // Open-image path shared by D&D/dialog/history/startup: pending import must bind before OCR.
        var lateStore=new EditStore(Path.Combine(folder,"late-cache"));var lateSettings=new AppSettingsStore(Path.Combine(folder,"late-settings.json"));var lateTransfer=new DataTransfer(lateStore,lateSettings);lateTransfer.Import(selectedZip);var runner=new NoOcr();
        using(var form=new MainForm(moved,lateStore,lateSettings,runner)){form.Show();Application.DoEvents();var catalog=(OcrCatalog?)typeof(MainForm).GetField("catalog",Hidden)!.GetValue(form);Check(catalog?.Find("A").Count==1&&runner.Calls==0&&lateTransfer.PendingItems().Length==0,"Later explicit open binds pending data without OCR despite different timestamp");form.Close();}
        using(var form=new MainForm(null,lateStore,lateSettings,runner)){form.Show();Application.DoEvents();Check(runner.Calls==0&&typeof(MainForm).GetField("catalog",Hidden)!.GetValue(form)!=null,"Restart/last-image path restores migrated local cache without OCR");form.Close();}
        Check(File.Exists(original)&&File.Exists(moved)&&new FileInfo(original).LastWriteTimeUtc==originalId.LastWriteUtc,"Migration never changes source originals");
        Console.WriteLine($"PASS: {count} transfer checks");
    }
}
