using System.Drawing.Drawing2D;

namespace AF.DecalFinder;

public class ImageViewControl : Control
{
    private Bitmap? bitmap;
    private IReadOnlyList<Detection> highlights = Array.Empty<Detection>();
    private Detection? selectedDetection;
    private readonly ViewTransform transform = new();
    private bool fitted = true;
    private Point? dragPoint;
    private Size previousSize;

    public ImageViewControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(48, 50, 54);
        TabStop = true;
        AccessibleName = "画像ビュー（ホイールでズーム、左ドラッグで移動）";
    }

    // Takes ownership of the bitmap. No OCR or file-selection logic lives here.
    public void SetImage(Bitmap image)
    {
        var old = bitmap;
        bitmap = image;
        highlights = Array.Empty<Detection>();
        selectedDetection = null;
        FitImage();
        old?.Dispose();
    }

    public void SetHighlights(IReadOnlyList<Detection> matches)
    {
        highlights = matches;
        Invalidate(); // Search never modifies the transform.
    }

    public void SetSelection(Detection? detection)
    {
        selectedDetection = detection;
        Invalidate(); // Selection never changes the image transform or red search matches.
    }

    public void FitImage()
    {
        fitted = true;
        if (bitmap != null) transform.Fit(bitmap.Size, ClientSize);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (bitmap != null)
        {
            if (fitted) transform.Fit(bitmap.Size, ClientSize);
            else transform.Pan((ClientSize.Width - previousSize.Width) / 2.0, (ClientSize.Height - previousSize.Height) / 2.0);
        }
        previousSize = ClientSize;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (bitmap == null)
        {
            TextRenderer.DrawText(e.Graphics, "画像を開くか、JPG / PNGをここへドロップしてください。", Font,
                ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var destination = transform.ToScreen(new Rectangle(Point.Empty, bitmap.Size));
        e.Graphics.DrawImage(bitmap, destination, new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
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
        if (selectedDetection != null)
        {
            var box = transform.ToScreen(selectedDetection.Bounds);
            if (box.IntersectsWith(ClientRectangle))
            {
                using var selectionPen = new Pen(Color.Blue, 2f);
                e.Graphics.DrawRectangle(selectionPen, box.X, box.Y, box.Width, box.Height);
            }
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (bitmap == null) return;
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
        dragPoint = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragPoint is not Point previous) return;
        transform.Pan(e.X - previous.X, e.Y - previous.Y);
        dragPoint = e.Location;
        fitted = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndDrag();
    }

    private void EndDrag() { dragPoint = null; Capture = false; Cursor = Cursors.Default; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) bitmap?.Dispose();
        base.Dispose(disposing);
    }
}
