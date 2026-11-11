using System.Diagnostics;
using System.Text.Json;

namespace AF.DecalFinder;

internal interface IOcrRunner
{
    Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation);
}

internal sealed class OcrRunner(string? runtimeDirectory = null) : IOcrRunner
{
    public async Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation)
    {
        var directory = Path.GetFullPath(runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "Ocr"));
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "runtime.json"), cancellation).ConfigureAwait(false));
        var python = config.RootElement.GetProperty("Python").GetString()!;
        // Portable runtime paths are relative to Ocr/, never to the caller's working directory or PATH.
        python = Path.GetFullPath(python, directory);
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(Path.Combine(directory, "worker.py"));
        start.ArgumentList.Add(image.AbsolutePath);
        // The viewer decides the EXIF orientation once; the worker applies exactly this value.
        start.ArgumentList.Add("--orientation");
        start.ArgumentList.Add(image.Frame.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment["OMP_THREAD_LIMIT"] = "1";
        start.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(start) ?? throw new IOException("OCRワーカーを起動できませんでした。");
        using var registration = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        var text = await output.ConfigureAwait(false);
        var errors = await error.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"OCR終了コード {process.ExitCode}: {errors}");
        return Parse(text, new Size(image.Width, image.Height));
    }

    internal static OcrCatalog Parse(string text, Size expected)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.GetProperty("image").GetProperty("width").GetInt32() != expected.Width ||
            root.GetProperty("image").GetProperty("height").GetInt32() != expected.Height)
            throw new InvalidDataException("OCR結果の画像寸法が一致しません。");
        var records = new List<Detection>();
        var ids = new HashSet<string>();
        foreach (var row in root.GetProperty("detections").EnumerateArray())
        {
            try
            {
                var number = row.GetProperty("number").GetString();
                bool unknown = row.TryGetProperty("is_unrecognized", out var flag) && flag.GetBoolean();
                var box = row.GetProperty("bbox").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var confidence = row.GetProperty("confidence").GetDouble();
                var id = row.GetProperty("id").GetString();
                if (!(unknown ? number == "" : Detection.ValidNumber(number)) || string.IsNullOrWhiteSpace(id) || ids.Contains(id) || box.Length != 4 ||
                    box[0] < 0 || box[1] < 0 || box[2] <= 0 || box[3] <= 0 || (long)box[0]+box[2] > expected.Width ||
                    (long)box[1]+box[3] > expected.Height || !double.IsFinite(confidence) || confidence is < 0 or > 100) continue;
                ids.Add(id);
                CircleEvidence? circle = null;
                if (row.TryGetProperty("candidate_circle", out var measured))
                {
                    // Optional malformed evidence must not discard an otherwise valid OCR record.
                    try
                    {
                        var c = measured.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                        if (c.Length == 3 && c.All(float.IsFinite) && c[0] >= 0 && c[0] < expected.Width &&
                            c[1] >= 0 && c[1] < expected.Height && c[2] > 0 && c[2]*2 <= Math.Min(expected.Width,expected.Height))
                            circle = new(c[0],c[1],c[2]*2,c[2]*2);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException) { }
                }
                records.Add(new(number!, new Rectangle(box[0],box[1],box[2],box[3]),confidence,id!) { IsUnrecognized = unknown, Circle = circle });
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { }
        }
        return new OcrCatalog(expected, records);
    }
}
