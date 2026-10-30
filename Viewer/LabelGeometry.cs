namespace AF.DecalFinder;

// Measured evidence only. Never fill this from a guessed OCR text-box center.
public sealed record CircleEvidence(float X, float Y, float Width, float Height);

public sealed record LabelSizeEstimate(SizeF Size, int Samples, int Inliers);

internal static class LabelGeometry
{
    internal const float RelativeTolerance = .30f;
    internal static LabelSizeEstimate? Estimate(IEnumerable<Detection> records)
    {
        // OCR character boxes and manual additions are not circle-size measurements.
        var sizes = records.Where(d => !d.IsDeleted && !d.IsManual && d.Circle != null)
            .Select(d => d.Circle!).Distinct().ToArray();
        if (sizes.Length < 5) return null;
        static float Median(IEnumerable<float> values)
        {
            var sorted = values.Order().ToArray();
            return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
        }
        float width = Median(sizes.Select(s => s.Width)), height = Median(sizes.Select(s => s.Height));
        var kept = sizes.Where(s => Math.Abs(s.Width-width) <= width*RelativeTolerance &&
            Math.Abs(s.Height-height) <= height*RelativeTolerance).ToArray();
        if (kept.Length < 5 || kept.Length < sizes.Length * .5) return null;
        var mean = new SizeF(kept.Average(s => s.Width), kept.Average(s => s.Height));
        double cvWidth = Math.Sqrt(kept.Average(s => Math.Pow(s.Width-mean.Width, 2))) / mean.Width;
        double cvHeight = Math.Sqrt(kept.Average(s => Math.Pow(s.Height-mean.Height, 2))) / mean.Height;
        return cvWidth <= .2 && cvHeight <= .2 ? new(mean, sizes.Length, kept.Length) : null;
    }

    internal static RectangleF Bounds(Detection record, Size image, LabelSizeEstimate? estimate)
    {
        if (estimate == null) return record.Bounds;
        var center = record.UiCenter;
        var size = estimate.Size;
        return RectangleF.Intersect(new RectangleF(0, 0, image.Width, image.Height),
            new RectangleF(center.X-size.Width/2, center.Y-size.Height/2, size.Width, size.Height));
    }
}
