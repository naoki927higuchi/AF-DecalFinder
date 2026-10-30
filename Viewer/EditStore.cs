using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AF.DecalFinder;

internal sealed record ImageIdentity(string AbsolutePath, int Width, int Height, long FileSize, DateTime LastWriteUtc)
{
    public static ImageIdentity Read(string path, Size size)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        return new(file.FullName, size.Width, size.Height, file.Length, file.LastWriteTimeUtc);
    }
    public bool Matches(ImageIdentity other) => string.Equals(AbsolutePath, other.AbsolutePath, StringComparison.OrdinalIgnoreCase)
        && Width == other.Width && Height == other.Height && FileSize == other.FileSize && LastWriteUtc == other.LastWriteUtc;
}

internal sealed class EditStore(string? directory = null)
{
    internal string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AF-DecalFinder", "Cache");
    internal string PathFor(string imagePath) => Path.Combine(DirectoryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(imagePath).ToUpperInvariant()))) + ".json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal string ArchiveInvalid(string imagePath)
    {
        var path = PathFor(imagePath);
        var archive = path + ".stale-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
        File.Copy(path, archive, overwrite: false);
        return archive;
    }
    internal sealed record Entry(string Id, string Number, int[] Bbox, double Confidence, bool IsManual, bool IsModified, bool IsDone,
        string? OriginalNumber, int[]? OriginalBbox);
    internal sealed record Document(int SchemaVersion, ImageIdentity Image, Entry[] Records);
    private static int[] Box(Rectangle r) => [r.X, r.Y, r.Width, r.Height];
    private static Rectangle ReadBox(int[]? b, ImageIdentity image)
    {
        if (b is not { Length: 4 } || b[0] < 0 || b[1] < 0 || b[2] <= 0 || b[3] <= 0 ||
            (long)b[0] + b[2] > image.Width || (long)b[1] + b[3] > image.Height)
            throw new InvalidDataException("保存データの矩形が不正です。");
        return new(b[0], b[1], b[2], b[3]);
    }
    internal OcrCatalog? Load(ImageIdentity image)
    {
        var path = PathFor(image.AbsolutePath);
        if (!File.Exists(path)) return null;
        var data = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
        if (data is not { SchemaVersion: 1, Image: not null, Records: not null })
            throw new InvalidDataException("保存データの形式に対応していません。");
        if (!image.Matches(data.Image)) throw new InvalidDataException("画像のサイズ・更新日時などが保存時と異なります。古い編集データは適用しません。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<Detection>();
        foreach (var entry in data.Records)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || !Detection.ValidNumber(entry.Number)
                || !double.IsFinite(entry.Confidence) || entry.Confidence is < 0 or > 100
                || (!entry.IsManual && (!Detection.ValidNumber(entry.OriginalNumber) || entry.OriginalBbox == null)))
                throw new InvalidDataException("保存データの番号・ID・OCR情報が不正です。");
            records.Add(new Detection(entry.Number, ReadBox(entry.Bbox, image), entry.Confidence, entry.Id) {
                IsManual = entry.IsManual, IsModified = entry.IsModified, IsDone = entry.IsDone,
                OriginalNumber = entry.OriginalNumber, OriginalBounds = entry.OriginalBbox == null ? null : ReadBox(entry.OriginalBbox, image) });
        }
        return new OcrCatalog(new Size(image.Width, image.Height), records);
    }
    internal void Save(ImageIdentity image, OcrCatalog catalog)
    {
        if (!image.Matches(ImageIdentity.Read(image.AbsolutePath, catalog.ImageSize)))
            throw new IOException("編集中に画像ファイルが更新されました。保存せず、現在の編集を保持しています。");
        Directory.CreateDirectory(DirectoryPath);
        var path = PathFor(image.AbsolutePath);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var data = new Document(1, image, catalog.Detections.Select(d => new Entry(d.Id, d.Number, Box(d.Bounds), d.Confidence,
            d.IsManual, d.IsModified, d.IsDone, d.OriginalNumber, d.OriginalBounds is Rectangle original ? Box(original) : null)).ToArray());
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, data, Options); stream.Flush(flushToDisk: true); }
            // Retain the previous file, including a stale/unreadable cache, rather than silently discarding it.
            if (File.Exists(path)) File.Replace(temp, path, path + "." + Guid.NewGuid().ToString("N") + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
