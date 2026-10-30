using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AF.DecalFinder;

internal sealed class OcrResultsList : ListView
{
    private bool updating;
    private int navigationGeneration;
    private bool navigationQueued;
    private bool explicitEdit;
    private bool editing;
    private NativeWindow? labelWindow;
    // .NET 9 WinForms #14657: retain the accessibility callback owner until the
    // native editor has gone away and a second, non-reentrant cleanup can run.
    private static readonly HashSet<NativeWindow> pendingLabelWindows = [];
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private readonly Dictionary<string, ListViewItem> rowsById = new(StringComparer.Ordinal);
    public event Action<IReadOnlyList<Detection>>? SelectionChanged;
    public event Action<Detection>? NavigateRequested;
    public event Action? DataChanged;
    public event Action<string>? EditRejected;
    public IReadOnlyList<Detection> SelectedDetections => SelectedItems.Cast<ListViewItem>().Select(row => (Detection)row.Tag!).ToArray();

    public OcrResultsList()
    {
        Dock = DockStyle.Fill;
        View = View.Details;
        FullRowSelect = true;
        MultiSelect = true;
        HideSelection = false;
        LabelEdit = true;
        CheckBoxes = true;
        ImeMode = ImeMode.Disable;
        AccessibleName = "OCR検出番号一覧";
        Columns.Add("番号", 120);
        SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            SelectionChanged?.Invoke(SelectedDetections);
            QueueSelectionNavigation();
        };
        Resize += (_, _) => Columns[0].Width = Math.Max(40, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        ItemCheck += (_, e) =>
        {
            if (updating) return;
            var record = (Detection)Items[e.Index].Tag!;
            record.IsDone = e.NewValue == CheckState.Checked;
            DataChanged?.Invoke();
        };
        BeforeLabelEdit += (_, e) => { e.CancelEdit = !explicitEdit; editing = !e.CancelEdit; };
        AfterLabelEdit += (_, e) =>
        {
            RetireLabelWindow();
            editing = false;
            e.CancelEdit = true; // Sorting waits for native edit teardown; the model/save must not wait.
            if (e.Label == null) return;
            var record = (Detection)Items[e.Item].Tag!;
            var value = e.Label;
            if (!Detection.ValidNumber(value))
            {
                BeginInvoke(() => EditRejected?.Invoke("番号には空欄でない半角数字を入力してください。"));
                return;
            }
            if (record.Number == value) return;
            record.Number = value; record.IsModified = true;
            DataChanged?.Invoke(); // Closing or switching images cannot lose a queued edit.
            BeginInvoke(() =>
            {
                if (rowsById.TryGetValue(record.Id, out var current) && ReferenceEquals(current.Tag, record)) RefreshEditedRow(current);
            });
        };
    }

    internal bool Rename(string id, string value)
    {
        if (!rowsById.TryGetValue(id, out var row)) return false;
        if (!Detection.ValidNumber(value)) { EditRejected?.Invoke("番号には空欄でない半角数字を入力してください。"); return false; }
        var record = (Detection)row.Tag!;
        if (record.Number == value) return true;
        record.Number = value; record.IsModified = true;
        RefreshEditedRow(row);
        DataChanged?.Invoke();
        return true;
    }

    private void CancelSelectionNavigation()
    {
        navigationGeneration++;
        navigationQueued = false;
    }

    private void QueueSelectionNavigation()
    {
        if (navigationQueued || !IsHandleCreated || IsDisposed) return;
        navigationQueued = true;
        int generation = navigationGeneration;
        BeginInvoke(() =>
        {
            if (IsDisposed || generation != navigationGeneration) return;
            navigationQueued = false;
            if (!updating && SelectedItems.Count > 0 && SelectedItems[0].Tag is Detection target)
                NavigateRequested?.Invoke(target);
        });
    }

    private void RefreshEditedRow(ListViewItem row)
    {
        CancelSelectionNavigation();
        updating = true;
        try
        {
        row.Text = ((Detection)row.Tag!).Number;
        ListViewItemSorter = new DetectionComparer();
        Sort();
        // Existing row objects and their selected state survive native sorting.
        row.Selected = true; row.Focused = true; row.EnsureVisible();
        }
        finally { updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }

    private sealed class DetectionComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (Detection)((ListViewItem)x!).Tag!; var b = (Detection)((ListViewItem)y!).Tag!;
            int c = BigInteger.Parse(a.Number, CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(b.Number, CultureInfo.InvariantCulture));
            if (c == 0) c = a.Bounds.Y.CompareTo(b.Bounds.Y);
            if (c == 0) c = a.Bounds.X.CompareTo(b.Bounds.X);
            return c == 0 ? StringComparer.Ordinal.Compare(a.Id, b.Id) : c;
        }
    }

    private void EditRow(ListViewItem? row)
    {
        if (row == null) return;
        explicitEdit = true;
        try
        {
            row.BeginEdit();
            labelWindow = NativeWindow.FromHandle(SendMessage(Handle, 0x1018, IntPtr.Zero, IntPtr.Zero)); // LVM_GETEDITCONTROL
            if (labelWindow != null) pendingLabelWindows.Add(labelWindow);
        }
        finally { explicitEdit = false; }
    }

    private void RetireLabelWindow()
    {
        var retired = labelWindow; labelWindow = null;
        if (retired == null) return;
        // Standard ListView releases the editor during LVN_ENDLABELEDIT. Cleanup
        // after that message also removes hooks reinstalled during its release.
        BeginInvoke(() =>
        {
            if (retired.Handle == IntPtr.Zero)
            {
                retired.ReleaseHandle();
                pendingLabelWindows.Remove(retired);
            }
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is 0x201 or 0x203) // WM_LBUTTONDOWN / WM_LBUTTONDBLCLK
        {
            var point = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            var hit = HitTest(point);
            if (hit.Item != null && hit.Location.HasFlag(ListViewHitTestLocations.StateImage))
            {
                // Bypass native multi-selection checkbox propagation. Exactly one state icon changes.
                if (m.Msg == 0x201) { Focus(); hit.Item.Checked = !hit.Item.Checked; }
                return;
            }
            if (m.Msg == 0x203 && hit.Item != null) { EditRow(hit.Item); return; }
        }
        base.WndProc(ref m);
    }

    public void SetDetections(IReadOnlyList<Detection> detections, bool preserveSelection = false)
    {
        var selected = preserveSelection ? SelectedDetections.Select(d => d.Id).ToHashSet(StringComparer.Ordinal) : [];
        var focusedId = preserveSelection ? (FocusedItem?.Tag as Detection)?.Id : null;
        CancelSelectionNavigation();
        updating = true;
        BeginUpdate();
        try
        {
            Items.Clear();
            rowsById.Clear();
            foreach (var detection in detections.OrderBy(d => BigInteger.Parse(d.Number, CultureInfo.InvariantCulture))
                .ThenBy(d => d.Bounds.Y).ThenBy(d => d.Bounds.X).ThenBy(d => d.Id, StringComparer.Ordinal))
            {
                var row = new ListViewItem(detection.Number) { Tag = detection, Checked = detection.IsDone };
                Items.Add(row);
                rowsById.Add(detection.Id, row);
                row.Selected = selected.Contains(detection.Id);
                if (detection.Id == focusedId) row.Focused = true;
            }
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }

    internal void SelectDetection(string id, bool add)
    {
        if (!rowsById.TryGetValue(id, out var row)) return;
        CancelSelectionNavigation();
        updating = true;
        BeginUpdate();
        try
        {
            if (!add)
                foreach (var selected in SelectedItems.Cast<ListViewItem>().ToArray()) selected.Selected = false;
            row.Selected = true;
            row.Focused = true;
            row.EnsureVisible();
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections); // Painting only; Enter navigation is a separate event.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (editing) { base.OnKeyDown(e); return; }
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.F2)
        {
            if (e.KeyCode == Keys.F2 && SelectedItems.Count > 0) EditRow(SelectedItems[0]);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
        {
            CancelSelectionNavigation();
            // Multiple selection is not a navigation target: the focused selected row is.
            var target = FocusedItem is { Selected: true } focused ? focused
                : SelectedItems.Count == 1 ? SelectedItems[0] : null;
            if (target?.Tag is Detection detection) NavigateRequested?.Invoke(detection);
            e.Handled = e.SuppressKeyPress = true;
        }
        if ((e.Control && e.KeyCode == Keys.A) || e.KeyCode == Keys.Escape)
        {
            CancelSelectionNavigation();
            updating = true;
            BeginUpdate();
            try
            {
                foreach (ListViewItem row in Items) row.Selected = e.KeyCode == Keys.A;
            }
            finally { EndUpdate(); updating = false; }
            SelectionChanged?.Invoke(SelectedDetections);
            QueueSelectionNavigation();
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
        CancelSelectionNavigation();
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
