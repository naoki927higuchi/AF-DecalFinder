using System.Text.Json;

namespace AF.DecalFinder;

public sealed class Detection(string number, Rectangle bounds, double confidence, string id = "")
{
    public string Id { get; } = id;
    public string Number { get; internal set; } = number;
    public Rectangle Bounds { get; internal set; } = bounds;
    public double Confidence { get; } = confidence;
    public bool IsManual { get; init; }
    public bool IsModified { get; internal set; }
    public bool IsDone { get; internal set; }
    public bool IsUnrecognized { get; internal set; }
    public bool IsDeleted { get; internal set; }
    public bool IsReviewed { get; internal set; }
    public CircleEvidence? Circle { get; internal set; }
    public PointF? ManualCenter { get; init; }
    public RectangleF UiBounds { get; internal set; } = bounds;
    public PointF UiCenter => IsManual && ManualCenter is PointF clicked ? clicked
        : Circle is { } circle ? new PointF(circle.X, circle.Y)
        : new PointF(Bounds.X + Bounds.Width / 2f, Bounds.Y + Bounds.Height / 2f);
    public string DisplayNumber => IsUnrecognized ? "?" : Number;
    public static bool ValidLabel(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    // Preserve OCR evidence separately from editable values for a future explicit merge.
    public string? OriginalNumber { get; init; } = number;
    public Rectangle? OriginalBounds { get; init; } = bounds;
    public static bool ValidNumber(string? value) => !string.IsNullOrEmpty(value) && value.All(c => c is >= '0' and <= '9');
}

public sealed class OcrCatalog
{
    public Size ImageSize { get; }
    public IReadOnlyList<Detection> Detections { get; }

    public IReadOnlyList<Detection> ActiveDetections => records.Where(d => !d.IsDeleted).ToArray();
    public int DoneCount => records.Count(d => !d.IsDeleted && d.IsDone);
    private readonly List<Detection> records;
    internal Action CaptureUndo()
    {
        var saved = records.Select(d => (Record: d, d.Number, d.Bounds, d.IsModified, d.IsDone,
            d.IsUnrecognized, d.IsDeleted, d.IsReviewed, d.Circle)).ToArray();
        return () =>
        {
            records.Clear();
            foreach (var s in saved)
            {
                var d = s.Record;
                d.Number=s.Number; d.Bounds=s.Bounds; d.IsModified=s.IsModified; d.IsDone=s.IsDone;
                d.IsUnrecognized=s.IsUnrecognized; d.IsDeleted=s.IsDeleted; d.IsReviewed=s.IsReviewed; d.Circle=s.Circle;
                records.Add(d);
            }
            RecalculateUiBounds();
        };
    }
    public LabelSizeEstimate? RepresentativeSize { get; private set; }
    internal void RecalculateUiBounds()
    {
        RepresentativeSize = LabelGeometry.Estimate(records);
        foreach (var record in records) record.UiBounds = LabelGeometry.Bounds(record, ImageSize, RepresentativeSize);
    }
    internal OcrCatalog(Size imageSize, List<Detection> detections)
    {
        ImageSize = imageSize;
        records = detections;
        Detections = detections.AsReadOnly();
        RecalculateUiBounds();
    }

    internal Detection AddManual(string number, PointF center)
    {
        if (!Detection.ValidLabel(number)) throw new ArgumentException("空欄でないラベルを入力してください。");
        var source = records.Where(d => !d.IsManual && !d.IsDeleted).ToArray();
        int Median(Func<Detection, int> get, int fallback) => source.Length == 0 ? fallback : source.Select(get).Order().ElementAt(source.Length / 2);
        int width = Math.Clamp(Median(d => d.Bounds.Width, 45), 1, ImageSize.Width);
        int height = Math.Clamp(Median(d => d.Bounds.Height, 40), 1, ImageSize.Height);
        var box = new Rectangle(Math.Clamp((int)Math.Round(center.X - width / 2f), 0, ImageSize.Width - width),
            Math.Clamp((int)Math.Round(center.Y - height / 2f), 0, ImageSize.Height - height), width, height);
        var record = new Detection(number, box, 0, "manual-" + Guid.NewGuid().ToString("N"))
            { IsManual = true, ManualCenter = center, OriginalNumber = null, OriginalBounds = null };
        records.Add(record);
        RecalculateUiBounds();
        return record;
    }

    internal int PreserveWork(OcrCatalog old)
    {
        var protectedRecords = old.Detections.Where(d => d.IsModified || d.IsManual || d.IsDone || d.IsDeleted || d.IsReviewed).ToArray();
        foreach (var saved in protectedRecords.Where(d => !d.IsManual && d.Circle == null))
        {
            // Same-image reanalysis: attach actual new measurements only on an exact original-box match.
            // Never infer a center from the text bbox or an ambiguous neighboring circle.
            var evidence = records.Where(d => d.Bounds == (saved.OriginalBounds ?? saved.Bounds) && d.Circle != null)
                .Select(d => d.Circle!).Distinct().ToArray();
            if (evidence.Length == 1) saved.Circle = evidence[0];
        }
        records.RemoveAll(d => protectedRecords.Any(saved => d.Id == saved.Id || SamePosition(d.Bounds, saved.Bounds)));
        // Preserve every protected fixed ID, even when two user records overlap.
        records.AddRange(protectedRecords);
        RecalculateUiBounds();
        return protectedRecords.Length;
    }

    private static bool SamePosition(Rectangle a, Rectangle b)
    {
        var intersection = Rectangle.Intersect(a, b);
        double minimum = Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height);
        double dx = (a.Left + a.Width / 2d) - (b.Left + b.Width / 2d);
        double dy = (a.Top + a.Height / 2d) - (b.Top + b.Height / 2d);
        return intersection.Width > 0 && intersection.Height > 0 && intersection.Width * (double)intersection.Height / minimum >= .6
            && Math.Sqrt(dx * dx + dy * dy) <= Math.Max(b.Width, b.Height) * .6;
    }

    public IReadOnlyList<Detection> Find(string number) => string.IsNullOrEmpty(number)
        ? Array.Empty<Detection>()
        : Detections.Where(d => !d.IsDeleted && !d.IsUnrecognized && string.Equals(d.Number, number, StringComparison.Ordinal)).ToArray();

    public static OcrCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        try
        {
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1)
                throw new InvalidDataException("未対応のJSON形式です（schema_versionは1が必要です）。");
            var image = root.GetProperty("image");
            var width = image.GetProperty("width").GetInt32();
            var height = image.GetProperty("height").GetInt32();
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("JSONの画像寸法が不正です。");
            var detections = new List<Detection>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("detections").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidDataException("JSONの検出IDが空、または重複しています。");
                var number = item.GetProperty("number").GetString();
                if (string.IsNullOrEmpty(number) || number.Any(c => c is < '0' or > '9'))
                    throw new InvalidDataException("JSONの認識番号は半角数字の文字列である必要があります。");
                var box = item.GetProperty("bbox").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                if (box.Length != 4 || box[0] < 0 || box[1] < 0 || box[2] <= 0 || box[3] <= 0 ||
                    (long)box[0] + box[2] > width || (long)box[1] + box[3] > height)
                    throw new InvalidDataException("JSONのbboxが不正、または画像範囲外です。");
                var confidence = item.GetProperty("confidence").GetDouble();
                if (!double.IsFinite(confidence) || confidence is < 0 or > 100)
                    throw new InvalidDataException("JSONの信頼度が不正です。");
                detections.Add(new Detection(number, new Rectangle(box[0], box[1], box[2], box[3]), confidence, id));
            }
            return new OcrCatalog(new Size(width, height), detections);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("JSONの必須項目またはデータ型が不正です。", ex);
        }
    }
}
