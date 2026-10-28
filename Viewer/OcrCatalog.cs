using System.Text.Json;

namespace AF.DecalFinder;

public sealed record Detection(string Number, Rectangle Bounds, double Confidence, string Id = "");

public sealed class OcrCatalog
{
    public Size ImageSize { get; }
    public IReadOnlyList<Detection> Detections { get; }

    private OcrCatalog(Size imageSize, List<Detection> detections)
    {
        ImageSize = imageSize;
        Detections = detections.AsReadOnly();
    }

    public IReadOnlyList<Detection> Find(string number) => string.IsNullOrEmpty(number)
        ? Array.Empty<Detection>()
        : Detections.Where(d => string.Equals(d.Number, number, StringComparison.Ordinal)).ToArray();

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
