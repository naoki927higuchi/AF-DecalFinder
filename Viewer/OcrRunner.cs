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
        var directory = runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "Ocr");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "runtime.json"), cancellation).ConfigureAwait(false));
        var python = config.RootElement.GetProperty("Python").GetString()!;
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(Path.Combine(directory, "worker.py"));
        start.ArgumentList.Add(image.AbsolutePath);
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
                records.Add(new(number!, new Rectangle(box[0],box[1],box[2],box[3]),confidence,id!) { IsUnrecognized = unknown });
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { }
        }
        return new OcrCatalog(expected, records);
    }
}
