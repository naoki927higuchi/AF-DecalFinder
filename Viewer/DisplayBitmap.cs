using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AF.DecalFinder;

internal static class DisplayBitmap
{
    // Convert once on load, not during pan. Preserve the original pixel matrix (no EXIF rotation).
    public static Bitmap Create(Image source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var g = Graphics.FromImage(result);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
