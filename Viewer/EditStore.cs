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
        string? OriginalNumber, int[]? OriginalBbox, bool IsUnrecognized = false, bool IsDeleted = false,
        CircleEvidence? Circle = null, PointF? ManualCenter = null, bool IsReviewed = false);
    internal sealed record Document(int SchemaVersion, ImageIdentity Image, Entry[] Records, string? SourceSha256 = null);
    private readonly Dictionary<ImageIdentity, string> hashes = new();
    private string SourceHash(ImageIdentity image)
    {
        if (hashes.TryGetValue(image, out var hash)) return hash;
        using var stream = File.OpenRead(image.AbsolutePath);
        hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!image.Matches(ImageIdentity.Read(image.AbsolutePath, new Size(image.Width, image.Height))))
            throw new IOException("画像が更新されました。開き直してください。");
        hashes[image] = hash;
        return hash;
    }
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
        if (data is not { SchemaVersion: 1 or 2, Image: not null, Records: not null })
            throw new InvalidDataException("保存データの形式に対応していません。");
        if (!image.Matches(data.Image)) throw new InvalidDataException("画像のサイズ・更新日時などが保存時と異なります。古い編集データは適用しません。");
        return ReadRecords(data, image);
    }

    internal static OcrCatalog ReadRecords(Document data, ImageIdentity image)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<Detection>();
        foreach (var entry in data.Records)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id) || !(entry.IsUnrecognized ? entry.Number == "" : Detection.ValidLabel(entry.Number))
                || !double.IsFinite(entry.Confidence) || entry.Confidence is < 0 or > 100
                || (!entry.IsManual && (entry.OriginalNumber == null || entry.OriginalBbox == null)))
                throw new InvalidDataException("保存データの番号・ID・OCR情報が不正です。");
            records.Add(new Detection(entry.Number, ReadBox(entry.Bbox, image), entry.Confidence, entry.Id) {
                Circle = ValidCircle(entry.Circle,image) ? entry.Circle : null,
                ManualCenter = entry.ManualCenter is PointF p && ValidPoint(p,image) ? p : null,
                IsManual = entry.IsManual, IsModified = entry.IsModified, IsDone = entry.IsDone, IsUnrecognized = entry.IsUnrecognized, IsDeleted = entry.IsDeleted, IsReviewed = entry.IsReviewed,
                OriginalNumber = entry.OriginalNumber, OriginalBounds = entry.OriginalBbox == null ? null : ReadBox(entry.OriginalBbox, image) });
        }
        return new OcrCatalog(new Size(image.Width, image.Height), records);
    }
    private static bool ValidPoint(PointF p, ImageIdentity image) => float.IsFinite(p.X) && float.IsFinite(p.Y)
        && p.X >= 0 && p.Y >= 0 && p.X < image.Width && p.Y < image.Height;
    private static bool ValidCircle(CircleEvidence? c, ImageIdentity image) => c != null && ValidPoint(new(c.X,c.Y),image)
        && float.IsFinite(c.Width) && float.IsFinite(c.Height) && c.Width > 0 && c.Height > 0
        && c.Width <= image.Width && c.Height <= image.Height;
    internal int RestoreProtected(ImageIdentity image, OcrCatalog fresh, string archive)
    {
        Document? old;
        try { old = JsonSerializer.Deserialize<Document>(File.ReadAllText(archive), Options); }
        catch (JsonException) { return 0; }
        if (old is not { SchemaVersion: 2, Image: not null, Records: not null, SourceSha256: not null }
            || !string.Equals(old.Image.AbsolutePath, image.AbsolutePath, StringComparison.OrdinalIgnoreCase)
            || old.Image.Width != image.Width || old.Image.Height != image.Height
            || !string.Equals(old.SourceSha256, SourceHash(image), StringComparison.Ordinal)) return 0;
        try { return fresh.PreserveWork(ReadRecords(old, image)); }
        catch (InvalidDataException) { return 0; } // Archive remains intact; never import malformed records.
    }

    internal void Save(ImageIdentity image, OcrCatalog catalog)
    {
        if (!image.Matches(ImageIdentity.Read(image.AbsolutePath, catalog.ImageSize)))
            throw new IOException("編集中に画像ファイルが更新されました。保存せず、現在の編集を保持しています。");
        Directory.CreateDirectory(DirectoryPath);
        var path = PathFor(image.AbsolutePath);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var data = new Document(2, image, catalog.Detections.Select(d => new Entry(d.Id, d.Number, Box(d.Bounds), d.Confidence,
            d.IsManual, d.IsModified, d.IsDone, d.OriginalNumber, d.OriginalBounds is Rectangle original ? Box(original) : null, d.IsUnrecognized, d.IsDeleted,
            d.Circle, d.ManualCenter, d.IsReviewed)).ToArray(), SourceHash(image));
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
