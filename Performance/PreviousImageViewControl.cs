using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace AF.DecalFinder;



public class PreviousImageViewControl : Control
{
    private Bitmap? bitmap;
    private Bitmap? overviewImage;
    private IReadOnlyList<Detection> highlights = Array.Empty<Detection>();
    private IReadOnlyList<Detection> selections = Array.Empty<Detection>();
    private bool ownsBitmap = true;
    internal RenderProbe? Probe;
    private PointF navigationGrabOffset;
    private Point? navigationStart;
    private bool navigationMoved;
    private RectangleF navigationBounds;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ImageViewMode Mode { get; init; }
    public event Action? ViewportChanged;
    public event Action<PointF>? NavigateRequested;
    public double Zoom => transform.Scale;
    public PointF ImageCenter => transform.ToImage(new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
    public RectangleF VisibleImageBounds => bitmap == null ? RectangleF.Empty : RectangleF.Intersect(
        new RectangleF(transform.ToImage(PointF.Empty), new SizeF((float)(ClientSize.Width / Zoom), (float)(ClientSize.Height / Zoom))),
        new RectangleF(0, 0, bitmap.Width, bitmap.Height));
    public RectangleF ScreenBounds(Rectangle bounds) => transform.ToScreen(bounds);
    private readonly ViewTransform transform = new();
    private bool fitted = true;
    private Point? dragPoint;
    private Size previousSize;

    public PreviousImageViewControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(48, 50, 54);
        TabStop = true;
        AccessibleName = "画像ビュー（ホイールでズーム、左ドラッグで移動）";
    }

    // The detail view owns the bitmap; the overview shares it without a second full-size copy.
    public void SetImage(Bitmap image, bool takeOwnership = true)
    {
        var old = bitmap;
        var disposeOld = ownsBitmap;
        bitmap = image;
        ClearOverviewImage();
        ownsBitmap = takeOwnership;
        highlights = Array.Empty<Detection>();
        selections = Array.Empty<Detection>();
        navigationBounds = RectangleF.Empty;
        EndDrag();
        if (Mode == ImageViewMode.Detail)
        {
            transform.SetViewport(image.Size, ClientSize, 1, new PointF(image.Width / 2f, image.Height / 2f));
            ViewportChanged?.Invoke();
            Invalidate();
        }
        else FitImage();
        if (disposeOld && !ReferenceEquals(old, image) && old != null) { old.Dispose(); if (Probe != null) Probe.BitmapDisposals++; }
    }

    public void SetHighlights(IReadOnlyList<Detection> matches)
    {
        highlights = matches;
        Invalidate(); // Search never modifies the transform.
    }

    public void SetSelection(Detection? detection)
    {
        SetSelections(detection == null ? Array.Empty<Detection>() : new[] { detection });
    }

    public void SetSelections(IReadOnlyList<Detection> detections)
    {
        selections = detections;
        Invalidate(); // Selection never changes the image transform or red search matches.
    }

    public void SetNavigationBounds(RectangleF bounds)
    {
        if (navigationBounds == bounds) return;
        var old = Rectangle.Ceiling(transform.ToScreen(navigationBounds));
        navigationBounds = bounds;
        var next = Rectangle.Ceiling(transform.ToScreen(bounds));
        var dirty = Rectangle.Union(old, next);
        dirty.Inflate(3, 3);
        Invalidate(dirty);
    }

    public void CenterOn(PointF center)
    {
        if (bitmap == null || Mode != ImageViewMode.Detail) return;
        transform.SetViewport(bitmap.Size, ClientSize, Zoom, center);
        ViewportChanged?.Invoke();
        Invalidate();
    }

    public void SetZoom(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (bitmap == null || Mode != ImageViewMode.Detail) return;
        transform.SetViewport(bitmap.Size, ClientSize, scale, ImageCenter);
        ViewportChanged?.Invoke();
        Invalidate();
    }

    public void FitImage()
    {
        ClearOverviewImage();
        fitted = true;
        if (bitmap != null) transform.Fit(bitmap.Size, ClientSize);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClearOverviewImage();
        if (bitmap != null)
        {
            if (Mode == ImageViewMode.Detail)
            {
                var center = transform.ToImage(new PointF(previousSize.Width / 2f, previousSize.Height / 2f));
                transform.SetViewport(bitmap.Size, ClientSize, Zoom, center);
                ViewportChanged?.Invoke();
            }
            else if (Mode == ImageViewMode.Overview || fitted) transform.Fit(bitmap.Size, ClientSize);
            else transform.Pan((ClientSize.Width - previousSize.Width) / 2.0, (ClientSize.Height - previousSize.Height) / 2.0);
        }
        previousSize = ClientSize;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Probe == null) { PaintImage(e); return; }
        var start = Stopwatch.GetTimestamp();
        PaintImage(e);
        Probe.PaintMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private void PaintImage(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (bitmap == null)
        {
            TextRenderer.DrawText(e.Graphics, "画像を開くか、JPG / PNGをここへドロップしてください。", Font,
                ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var imageStart = Probe == null ? 0 : Stopwatch.GetTimestamp();
        DrawBitmap(e.Graphics, e.ClipRectangle);
        Probe?.ImageMs.Add(Stopwatch.GetElapsedTime(imageStart).TotalMilliseconds);
        var overlayStart = Probe == null ? 0 : Stopwatch.GetTimestamp();
        // Draw directly in screen coordinates: zoom and monitor DPI don't scale the 2px pen.
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Color.Red, 2f);
        foreach (var match in highlights)
        {
            var box = transform.ToScreen(match.Bounds);
            if (box.IntersectsWith(ClientRectangle))
                e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        }
        // Last pass gives blue priority where it overlaps red, with the same screen-space pen width.
        using var selectionPen = new Pen(Color.Blue, 2f);
        foreach (var selectedDetection in selections)
        {
            var box = transform.ToScreen(selectedDetection.Bounds);
            if (box.IntersectsWith(ClientRectangle))
            {
                e.Graphics.DrawRectangle(selectionPen, box.X, box.Y, box.Width, box.Height);
            }
        }
        if (Mode == ImageViewMode.Overview && !navigationBounds.IsEmpty)
        {
            var box = transform.ToScreen(navigationBounds);
            using var navigationPen = new Pen(Color.Blue, 2f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(navigationPen, box.X, box.Y, box.Width, box.Height);
        }
        Probe?.OverlayMs.Add(Stopwatch.GetElapsedTime(overlayStart).TotalMilliseconds);
    }

    private void DrawBitmap(Graphics graphics, Rectangle clip)
    {
        if (bitmap == null) return;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (Mode == ImageViewMode.Overview && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            if (overviewImage == null)
            {
                overviewImage = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                if (Probe != null) Probe.BitmapCreations++;
                using var g = Graphics.FromImage(overviewImage);
                g.Clear(BackColor);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bitmap, transform.ToScreen(new Rectangle(Point.Empty, bitmap.Size)),
                    new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
            }
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(overviewImage, new Rectangle(Point.Empty, overviewImage.Size),
                0, 0, overviewImage.Width, overviewImage.Height, GraphicsUnit.Pixel);
            return;
        }
        // GDI+ quantizes cropped sampling differently at non-power-of-two scales.
        // Keep the full-source path for these scales rather than change image appearance.
        if (Zoom != .25 && Zoom != .5 && Zoom != 1 && Zoom != 2)
        {
            graphics.DrawImage(bitmap, transform.ToScreen(new Rectangle(Point.Empty, bitmap.Size)),
                new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
            return;
        }
        var visible = Rectangle.Intersect(ClientRectangle, clip);
        if (visible.IsEmpty) return;
        var topLeft = transform.ToImage(visible.Location);
        var bottomRight = transform.ToImage(new PointF(visible.Right, visible.Bottom));
        var padding = (int)Math.Ceiling(2 / Zoom) + 2;
        var alignment = Zoom == .25 ? 4 : Zoom == .5 ? 2 : 1;
        // Include interpolation neighbors, keeping source coordinates integral and the original sampling phase.
        var source = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            Rectangle.FromLTRB((int)Math.Floor((topLeft.X - padding) / alignment) * alignment, (int)Math.Floor((topLeft.Y - padding) / alignment) * alignment,
                (int)Math.Ceiling((bottomRight.X + padding) / alignment) * alignment, (int)Math.Ceiling((bottomRight.Y + padding) / alignment) * alignment));
        if (source.IsEmpty) return;
        var destination = transform.ToScreen(source);
        if (Zoom == 1 && destination.X == MathF.Truncate(destination.X) && destination.Y == MathF.Truncate(destination.Y))
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(bitmap, destination, source, GraphicsUnit.Pixel);
    }

    private void ClearOverviewImage()
    {
        if (overviewImage == null) return;
        overviewImage.Dispose(); overviewImage = null;
        if (Probe != null) Probe.BitmapDisposals++;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (bitmap == null || Mode != ImageViewMode.Free) return;
        var fit = Math.Min((double)ClientSize.Width / bitmap.Width, (double)ClientSize.Height / bitmap.Height);
        if (fit <= 0) return;
        transform.ZoomAt(e.Location, Math.Pow(1.2, e.Delta / 120.0), Math.Min(fit * .1, 1), Math.Max(16, fit));
        fitted = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || bitmap == null) return;
        if (Mode == ImageViewMode.Overview)
        {
            var point = transform.ToImage(e.Location);
            if (point.X < 0 || point.Y < 0 || point.X > bitmap.Width || point.Y > bitmap.Height) return;
            navigationStart = e.Location;
            navigationMoved = false;
            navigationGrabOffset = navigationBounds.Contains(point)
                ? new PointF(point.X - (navigationBounds.Left + navigationBounds.Width / 2), point.Y - (navigationBounds.Top + navigationBounds.Height / 2))
                : PointF.Empty;
            if (navigationGrabOffset == PointF.Empty) NavigateRequested?.Invoke(point);
        }
        dragPoint = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Probe == null) { MovePointer(e); return; }
        var start = Stopwatch.GetTimestamp();
        MovePointer(e);
        Probe.MouseMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private void MovePointer(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragPoint is not Point previous) return;
        if (Mode == ImageViewMode.Overview)
        {
            if (navigationStart is Point start && (Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) > 2)) navigationMoved = true;
            if (!navigationMoved) return;
            var point = transform.ToImage(e.Location);
            NavigateRequested?.Invoke(new PointF(point.X - navigationGrabOffset.X, point.Y - navigationGrabOffset.Y));
            dragPoint = e.Location;
            return;
        }
        transform.Pan(e.X - previous.X, e.Y - previous.Y);
        if (Mode == ImageViewMode.Detail && bitmap != null)
        {
            transform.SetViewport(bitmap.Size, ClientSize, Zoom, ImageCenter);
            ViewportChanged?.Invoke();
        }
        dragPoint = e.Location;
        fitted = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && Mode == ImageViewMode.Overview && navigationStart != null && !navigationMoved)
            NavigateRequested?.Invoke(transform.ToImage(e.Location));
        if (e.Button == MouseButtons.Left) EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndDrag();
    }

    private void EndDrag() { dragPoint = null; navigationStart = null; Capture = false; Cursor = Cursors.Default; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearOverviewImage();
        if (disposing && ownsBitmap && bitmap != null) { bitmap.Dispose(); if (Probe != null) Probe.BitmapDisposals++; }
        base.Dispose(disposing);
    }
}
