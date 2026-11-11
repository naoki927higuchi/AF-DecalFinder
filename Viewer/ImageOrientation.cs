namespace AF.DecalFinder;

// EXIF Orientation (tag 0x0112) is decided once here; the display bitmap and the OCR worker
// both receive the same value so coordinates always refer to the upright image.
internal static class ImageOrientation
{
    private const int OrientationTag = 0x0112;

    internal static int Normalize(int value) => value is >= 1 and <= 8 ? value : 1;

    internal static int Read(Image image)
    {
        if (Array.IndexOf(image.PropertyIdList, OrientationTag) < 0) return 1;
        try
        {
            var item = image.GetPropertyItem(OrientationTag);
            if (item?.Value is not { Length: >= 2 } value) return 1;
            // TIFF SHORT; GDI+ returns it in machine (little-endian) order.
            return Normalize(BitConverter.ToUInt16(value, 0));
        }
        catch (ArgumentException) { return 1; }
    }

    internal static bool SwapsAxes(int orientation) => Normalize(orientation) >= 5;

    internal static Size Apply(Size raw, int orientation) => SwapsAxes(orientation) ? new(raw.Height, raw.Width) : raw;

    internal static RotateFlipType RotateFlip(int orientation) => Normalize(orientation) switch
    {
        2 => RotateFlipType.RotateNoneFlipX,
        3 => RotateFlipType.Rotate180FlipNone,
        4 => RotateFlipType.RotateNoneFlipY,
        5 => RotateFlipType.Rotate90FlipX,
        6 => RotateFlipType.Rotate90FlipNone,
        7 => RotateFlipType.Rotate270FlipX,
        8 => RotateFlipType.Rotate270FlipNone,
        _ => RotateFlipType.RotateNoneFlipNone,
    };

    /// <summary>Reads only the header to get the upright size and orientation.</summary>
    internal static (Size Size, int Orientation) Probe(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        int orientation = Read(image);
        return (Apply(image.Size, orientation), orientation);
    }
}
