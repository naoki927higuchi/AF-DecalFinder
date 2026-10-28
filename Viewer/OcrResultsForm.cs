using System.Globalization;
using System.Numerics;

namespace AF.DecalFinder;

internal sealed class OcrResultsForm : Form
{
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, LabelEdit = false,
        AccessibleName = "OCR検出結果"
    };
    private readonly ToolStripStatusLabel count = new();
    private bool updating;

    public event Action<Detection?>? SelectionChanged;
    public Detection? SelectedDetection => list.SelectedItems.Count == 1
        ? list.SelectedItems[0].Tag as Detection : null;

    public OcrResultsForm()
    {
        Text = "OCR結果一覧";
        ClientSize = new Size(560, 540);
        MinimumSize = new Size(450, 260);
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = false;
        list.Columns.Add("ID", 135);
        list.Columns.Add("番号", 90);
        list.Columns.Add("X", 90);
        list.Columns.Add("Y", 90);
        list.Columns.Add("信頼度", 100);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (!updating) SelectionChanged?.Invoke(SelectedDetection);
        };
        // Escape provides an explicit way to clear selection even if all rows fill the window.
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            foreach (ListViewItem item in list.SelectedItems) item.Selected = false;
            e.Handled = true;
        };
        var strip = new StatusStrip();
        strip.Items.Add(count);
        Controls.Add(list);
        Controls.Add(strip);
        SetDetections(Array.Empty<Detection>());
    }

    public void SetDetections(IReadOnlyList<Detection> detections)
    {
        updating = true;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            // Numeric comparison supports long OCR strings and preserves original text/IDs.
            var sorted = detections.OrderBy(d => BigInteger.Parse(d.Number, CultureInfo.InvariantCulture))
                .ThenBy(d => d.Bounds.Y).ThenBy(d => d.Bounds.X).ThenBy(d => d.Id, StringComparer.Ordinal);
            foreach (var detection in sorted)
            {
                var row = new ListViewItem(new[]
                {
                    detection.Id, detection.Number,
                    detection.Bounds.X.ToString(CultureInfo.InvariantCulture),
                    detection.Bounds.Y.ToString(CultureInfo.InvariantCulture),
                    detection.Confidence.ToString("F2", CultureInfo.InvariantCulture)
                });
                row.Tag = detection; // Keep the catalog record itself, never an index-derived ID.
                list.Items.Add(row);
            }
            count.Text = $"{detections.Count}件  |  選択：青枠 / Esc：選択解除";
        }
        finally
        {
            list.EndUpdate();
            updating = false;
        }
        SelectionChanged?.Invoke(null);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SelectionChanged?.Invoke(null);
        base.OnFormClosed(e);
    }
}
