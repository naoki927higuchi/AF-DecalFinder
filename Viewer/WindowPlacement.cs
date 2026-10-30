namespace AF.DecalFinder;

internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    internal static WindowPlacement From(Rectangle bounds, bool maximized) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);
    internal Rectangle Fit(IReadOnlyList<Rectangle> areas, Size minimum)
    {
        // Choose the monitor with most overlap, then the nearest monitor for detached displays.
        var area = areas.OrderByDescending(a =>
            Math.Max(0L, Math.Min((long)X + Width, a.Right) - Math.Max(X, a.Left)) *
            Math.Max(0L, Math.Min((long)Y + Height, a.Bottom) - Math.Max(Y, a.Top)))
            .ThenBy(a => Math.Pow((double)X - Math.Clamp((double)X, a.Left, a.Right), 2)
                + Math.Pow((double)Y - Math.Clamp((double)Y, a.Top, a.Bottom), 2)).First();
        int width = Math.Clamp(Width, Math.Min(minimum.Width, area.Width), area.Width);
        int height = Math.Clamp(Height, Math.Min(minimum.Height, area.Height), area.Height);
        return new Rectangle(Math.Clamp(X, area.Left, area.Right - width), Math.Clamp(Y, area.Top, area.Bottom - height), width, height);
    }
}
