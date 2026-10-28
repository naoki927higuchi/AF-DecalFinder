namespace AF.DecalFinder;

// One image-to-screen mapping is shared by the bitmap and all rectangles.
internal sealed class ViewTransform
{
    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public void Fit(Size image, Size viewport)
    {
        if (image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return;
        Scale = Math.Min((double)viewport.Width / image.Width, (double)viewport.Height / image.Height);
        OffsetX = (viewport.Width - image.Width * Scale) / 2;
        OffsetY = (viewport.Height - image.Height * Scale) / 2;
    }

    public void ZoomAt(Point anchor, double factor, double minimum, double maximum)
    {
        var next = Math.Clamp(Scale * factor, minimum, maximum);
        var ratio = next / Scale;
        OffsetX = anchor.X - (anchor.X - OffsetX) * ratio;
        OffsetY = anchor.Y - (anchor.Y - OffsetY) * ratio;
        Scale = next;
    }

    public void Pan(double x, double y) { OffsetX += x; OffsetY += y; }

    public RectangleF ToScreen(Rectangle bounds) => new(
        (float)(OffsetX + bounds.X * Scale), (float)(OffsetY + bounds.Y * Scale),
        (float)(bounds.Width * Scale), (float)(bounds.Height * Scale));
}
