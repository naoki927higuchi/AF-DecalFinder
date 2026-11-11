using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AF.DecalFinder;

internal static class DisplayBitmap
{
    // Convert once on load, not during pan. EXIF orientation is applied losslessly (rotate/flip only),
    // so coordinates refer to the upright image, exactly as the OCR worker sees it.
    public static Bitmap Create(Image source, int orientation = 1)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            }
            var flip = ImageOrientation.RotateFlip(orientation);
            if (flip != RotateFlipType.RotateNoneFlipNone) result.RotateFlip(flip);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
