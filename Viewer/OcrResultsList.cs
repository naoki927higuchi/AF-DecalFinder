using System.Globalization;
using System.Numerics;

namespace AF.DecalFinder;

internal sealed class OcrResultsList : ListView
{
    private bool updating;
    public event Action<IReadOnlyList<Detection>>? SelectionChanged;
    public event Action<Detection>? NavigateRequested;
    public IReadOnlyList<Detection> SelectedDetections => SelectedItems.Cast<ListViewItem>().Select(row => (Detection)row.Tag!).ToArray();

    public OcrResultsList()
    {
        Dock = DockStyle.Fill;
        View = View.Details;
        FullRowSelect = true;
        MultiSelect = true;
        HideSelection = false;
        LabelEdit = false;
        AccessibleName = "OCR検出番号一覧";
        Columns.Add("番号", 120);
        SelectedIndexChanged += (_, _) => { if (!updating) SelectionChanged?.Invoke(SelectedDetections); };
        Resize += (_, _) => Columns[0].Width = Math.Max(40, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
    }

    public void SetDetections(IReadOnlyList<Detection> detections)
    {
        updating = true;
        BeginUpdate();
        try
        {
            Items.Clear();
            foreach (var detection in detections.OrderBy(d => BigInteger.Parse(d.Number, CultureInfo.InvariantCulture))
                .ThenBy(d => d.Bounds.Y).ThenBy(d => d.Bounds.X).ThenBy(d => d.Id, StringComparer.Ordinal))
                Items.Add(new ListViewItem(detection.Number) { Tag = detection });
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
        {
            // Multiple selection is not a navigation target: the focused selected row is.
            var target = FocusedItem is { Selected: true } focused ? focused
                : SelectedItems.Count == 1 ? SelectedItems[0] : null;
            if (target?.Tag is Detection detection) NavigateRequested?.Invoke(detection);
            e.Handled = e.SuppressKeyPress = true;
        }
        if ((e.Control && e.KeyCode == Keys.A) || e.KeyCode == Keys.Escape)
        {
            updating = true;
            BeginUpdate();
            try
            {
                foreach (ListViewItem row in Items) row.Selected = e.KeyCode == Keys.A;
            }
            finally { EndUpdate(); updating = false; }
            SelectionChanged?.Invoke(SelectedDetections);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    internal void MoveTo(Control parent)
    {
        var selected = SelectedItems.Cast<ListViewItem>().ToHashSet();
        var focused = FocusedItem;
        var top = TopItem;
        updating = true;
        BeginUpdate();
        try
        {
            parent.Controls.Add(this);
            foreach (ListViewItem row in Items) row.Selected = selected.Contains(row);
            if (focused != null) focused.Focused = true;
            if (top != null) TopItem = top;
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }
}
