using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AF.DecalFinder;

internal sealed record TransferItem(string Id, EditStore.Document Data)
{
    internal string FileName => Path.GetFileName(Data.Image.AbsolutePath);
    public override string ToString() => $"{FileName}  —  {Data.Image.Width} × {Data.Image.Height} / {Data.Records.Length}件 — {Data.Image.AbsolutePath} [{Id[..Math.Min(8,Id.Length)]}]";
}
internal sealed record TransferBundle(int Version, JsonObject Settings, TransferItem[] Images);
internal enum ImportConflict { Skip, Replace }

// ZIPs contain JSON only. Pending imports are durable and independent of machine-local image paths.
internal sealed class DataTransfer(EditStore cache, AppSettingsStore settings, string? pendingDirectory=null)
{
    internal string PendingDirectory { get; }=pendingDirectory??Path.Combine(Path.GetDirectoryName(settings.FilePath)!,"PendingImports");
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    internal TransferItem[] CachedItems()
    {
        if(!Directory.Exists(cache.DirectoryPath))return [];
        return Directory.EnumerateFiles(cache.DirectoryPath,"*.json").Where(p=>
            Path.GetFileNameWithoutExtension(p).Length==64&&Path.GetFileNameWithoutExtension(p).All(Uri.IsHexDigit))
            .Select(p=>
            {
                var doc=JsonSerializer.Deserialize<EditStore.Document>(File.ReadAllText(p),Options)??throw new InvalidDataException($"キャッシュが空です：{p}");
                Validate(doc);return new TransferItem(Path.GetFileNameWithoutExtension(p),doc);
            }).ToArray();
    }
    internal TransferItem[] PendingItems() => !Directory.Exists(PendingDirectory)?[]:
        Directory.EnumerateFiles(PendingDirectory,"*.json",SearchOption.AllDirectories)
            .Where(p=>Path.GetFileName(Path.GetDirectoryName(p)!).StartsWith("batch-",StringComparison.Ordinal))
            .Select(p=>ReadItem(p)).ToArray();
    private static TransferItem ReadItem(string path)
    {
        var item=JsonSerializer.Deserialize<TransferItem>(File.ReadAllText(path),Options)??throw new InvalidDataException("未紐付けデータが空です。");
        if(!Guid.TryParseExact(item.Id,"N",out _))throw new InvalidDataException("未紐付けIDが不正です。");
        Validate(item.Data);return item;
    }
    private static void Validate(EditStore.Document doc)
    {
        if(doc is not {SchemaVersion:1 or 2,Image:not null,Records:not null}||
            doc.Image.Width<=0||doc.Image.Height<=0||string.IsNullOrWhiteSpace(doc.Image.AbsolutePath)||
            !Path.IsPathFullyQualified(doc.Image.AbsolutePath)||string.IsNullOrEmpty(Path.GetFileName(doc.Image.AbsolutePath)))throw new InvalidDataException("移行データの画像情報またはスキーマが不正です。");
        _=EditStore.ReadRecords(doc,doc.Image); // Validate every ID, label, confidence and original/edited box before writing anything.
        foreach(var d in doc.Records)
        {
            if(d.Circle is {} c && (!float.IsFinite(c.X)||!float.IsFinite(c.Y)||!float.IsFinite(c.Width)||!float.IsFinite(c.Height)||
                c.X<0||c.Y<0||c.X>=doc.Image.Width||c.Y>=doc.Image.Height||c.Width<=0||c.Height<=0||c.Width>doc.Image.Width||c.Height>doc.Image.Height))
                throw new InvalidDataException("○の実測情報が不正です。");
            if(d.ManualCenter is {} p && (!float.IsFinite(p.X)||!float.IsFinite(p.Y)||p.X<0||p.Y<0||p.X>=doc.Image.Width||p.Y>=doc.Image.Height))
                throw new InvalidDataException("手動クリック座標が不正です。");
        }
    }
    internal void Export(string zipPath,IReadOnlyList<TransferItem> images)
    {
        foreach(var item in images)Validate(item.Data);
        var bundle=new TransferBundle(1,settings.ExportSettings(),images.ToArray());
        var temp=zipPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using(var zip=new ZipArchive(file,ZipArchiveMode.Create,true))
                using(var stream=zip.CreateEntry("manifest.json",CompressionLevel.Optimal).Open())JsonSerializer.Serialize(stream,bundle,Options);
                file.Flush(true);
            }
            if(File.Exists(zipPath))File.Replace(temp,zipPath,zipPath+"."+Guid.NewGuid().ToString("N")+".bak");else File.Move(temp,zipPath);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal int Import(string zipPath)
    {
        TransferBundle bundle;
        using(var zip=ZipFile.OpenRead(zipPath))
        {
            if(zip.Entries.Count!=1||zip.Entries[0].FullName!="manifest.json"||zip.Entries[0].Length>256L*1024*1024)
                throw new InvalidDataException("対応する移行ZIPではありません（manifest.json、最大256MiB）。");
            using var stream=zip.Entries[0].Open();
            bundle=JsonSerializer.Deserialize<TransferBundle>(stream,Options)??throw new InvalidDataException("空の移行データです。");
        }
        if(bundle.Version!=1||bundle.Settings==null||bundle.Images==null)throw new InvalidDataException("未対応の移行形式です。");
        foreach(var item in bundle.Images){if(item==null)throw new InvalidDataException("空の画像情報です。");Validate(item.Data);}
        Directory.CreateDirectory(PendingDirectory);
        string id=Guid.NewGuid().ToString("N"), staging=Path.Combine(PendingDirectory,"staging-"+id), ready=Path.Combine(PendingDirectory,"batch-"+id);
        Directory.CreateDirectory(staging);
        // Publish the whole validated batch with one directory rename; failed staging never participates in matching.
        foreach(var item in bundle.Images)
        {
            var pending=item with {Id=Guid.NewGuid().ToString("N")};
            using var file=new FileStream(Path.Combine(staging,pending.Id+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(file,pending,Options);file.Flush(true);
        }
        Directory.Move(staging,ready);
        settings.ImportPortableSettings(bundle.Settings);
        return bundle.Images.Length;
    }
    internal static bool Matches(TransferItem item,ImageIdentity image) =>
        string.Equals(item.FileName,Path.GetFileName(image.AbsolutePath),StringComparison.OrdinalIgnoreCase)&&
        item.Data.Image.Width==image.Width&&item.Data.Image.Height==image.Height;
    internal bool Bind(TransferItem item,ImageIdentity image,Func<ImageIdentity,ImportConflict> conflict)
    {
        if(!Matches(item,image))throw new InvalidDataException("ファイル名または画像寸法が一致しません。");
        bool valid;
        try{valid=cache.Load(image)!=null;}catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException){valid=false;}
        if(valid&&conflict(image)==ImportConflict.Skip){Complete(item.Id);return false;}
        var data=EditStore.ReadRecords(item.Data,image);
        cache.Save(image,data); // Safe replace always retains the old cache, even when stale.
        Complete(item.Id);return true;
    }
    private void Complete(string id)
    {
        var source=Directory.EnumerateFiles(PendingDirectory,id+".json",SearchOption.AllDirectories).Single();
        File.Move(source,source+".bound"); // Keep import evidence for recovery; no longer pending.
    }
    internal TransferItem[] Matching(ImageIdentity image)=>PendingItems().Where(i=>Matches(i,image)).ToArray();
    internal static IReadOnlyList<ImageIdentity> Scan(IEnumerable<string> folders,IEnumerable<TransferItem> items)
    {
        var names=items.Select(i=>i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found=new Dictionary<string,ImageIdentity>(StringComparer.OrdinalIgnoreCase);
        var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
        foreach(var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!Directory.Exists(folder))continue;
            foreach(var path in Directory.EnumerateFiles(folder,"*",options))
            {
                if(!names.Contains(Path.GetFileName(path)))continue;
                if(!new[]{".jpg",".jpeg",".png"}.Contains(Path.GetExtension(path).ToLowerInvariant()))continue;
                try {using var stream=File.OpenRead(path);using var image=Image.FromStream(stream);found[Path.GetFullPath(path)]=ImageIdentity.Read(path,image.Size);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException){ }
            }
        }
        return found.Values.ToArray();
    }
}
