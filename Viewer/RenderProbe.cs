namespace AF.DecalFinder;

// Opt-in diagnostics for the benchmark only. Normal application instances have no probe.
internal sealed class RenderProbe
{
    public List<double> PaintMs { get; } = new();
    public List<double> ImageMs { get; } = new();
    public List<double> OverlayMs { get; } = new();
    public List<double> MouseMs { get; } = new();
    public int BitmapCreations;
    public int BitmapDisposals;
    public void Clear()
    {
        PaintMs.Clear(); ImageMs.Clear(); OverlayMs.Clear(); MouseMs.Clear();
        BitmapCreations = BitmapDisposals = 0;
    }
}
