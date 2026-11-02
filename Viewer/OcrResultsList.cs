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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string text);
    private readonly Dictionary<string, ListViewItem> rowsById = new(StringComparer.Ordinal);
    private IReadOnlyList<Detection> allRecords = [];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool InventoryMode { get; set; }
    internal Func<string, string, bool, bool>? EditHandler;
    internal event Action? BeforeDataChange;
    internal event Action<bool>? ReviewRequested;
    internal event Action? UndoRequested;
    private readonly HashSet<Keys> heldCommands = [];
    internal bool OnlyUnrecognized { get; private set; }
    internal IEnumerable<Detection> OrderedDetections => allRecords.Where(d => !d.IsDeleted)
        .OrderBy(d => d, Comparer<Detection>.Create(CompareRecords));
    internal void FilterUnrecognized(bool value)
    {
        OnlyUnrecognized = value;
        SetDetections(allRecords, preserveSelection: true);
    }
    public event Action<IReadOnlyList<Detection>>? SelectionChanged;
    public event Action<Detection>? NavigateRequested;
    public event Action? DataChanged;
    public event Action<IReadOnlyList<Detection>>? DeleteRequested;
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
        ImeMode = ImeMode.NoControl;
        var menu = new ContextMenuStrip();
        menu.Items.Add("誤検出として削除", null, (_, _) => RequestDelete());
        menu.Opening+=(_,_)=>menu.Items[0].Enabled=SelectedItems.Count>0&&!editing&&Enabled;
        ContextMenuStrip = menu;
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
            if (record.IsDone == (e.NewValue == CheckState.Checked)) return;
            BeforeDataChange?.Invoke();
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
            if (!Detection.ValidLabel(value))
            {
                BeginInvoke(() => EditRejected?.Invoke("空欄でないラベルを入力してください。"));
                return;
            }
            if (EditHandler != null) { EditHandler(record.Id, value, true); return; }
            if (!record.IsUnrecognized && record.Number == value) return;
            record.Number = value; record.IsUnrecognized = false; record.IsModified = true;
            DataChanged?.Invoke(); // Closing or switching images cannot lose a queued edit.
            BeginInvoke(() =>
            {
                if (rowsById.TryGetValue(record.Id, out var current) && ReferenceEquals(current.Tag, record)) RefreshEditedRow(current);
            });
        };
    }

    internal bool Rename(string id, string value)
    {
        if (EditHandler != null) return EditHandler(id, value, false);
        if (!rowsById.TryGetValue(id, out var row)) return false;
        if (!Detection.ValidLabel(value)) { EditRejected?.Invoke("空欄でないラベルを入力してください。"); return false; }
        var record = (Detection)row.Tag!;
        if (!record.IsUnrecognized && record.Number == value) return true;
        record.Number = value; record.IsUnrecognized = false; record.IsModified = true;
        RefreshEditedRow(row);
        DataChanged?.Invoke();
        return true;
    }

    internal void SuppressPendingNavigation() => CancelSelectionNavigation();
    internal bool RenameFromImage(string id, string value)
    {
        if (EditHandler != null) return EditHandler(id, value, false);
        var record = allRecords.FirstOrDefault(d => d.Id == id && !d.IsDeleted);
        if (record == null || !Detection.ValidLabel(value)) return false;
        CancelSelectionNavigation();
        updating = true;
        try
        {
            record.Number = value; record.IsUnrecognized = false; record.IsModified = true;
            if (rowsById.TryGetValue(id, out var row)) row.Selected = false;
        }
        finally { updating = false; }
        SetDetections(allRecords, preserveSelection: true);
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
        if (OnlyUnrecognized && !((Detection)row.Tag!).IsUnrecognized)
        {
            SetDetections(allRecords, preserveSelection: true);
            return;
        }
        CancelSelectionNavigation();
        updating = true;
        try
        {
        row.Text = ((Detection)row.Tag!).DisplayNumber;
        ListViewItemSorter = new DetectionComparer();
        Sort();
        // Existing row objects and their selected state survive native sorting.
        row.Selected = true; row.Focused = true; row.EnsureVisible();
        }
        finally { updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }

    private static bool NumericLabel(string value) => value.Length > 0 && value.All(char.IsDigit);
    private static BigInteger NumericValue(string value)
    {
        BigInteger result = 0;
        foreach (char c in value) result = result * 10 + (int)char.GetNumericValue(c);
        return result;
    }
    private static int LabelGroup(Detection d) => d.IsUnrecognized ? 3 : NumericLabel(d.Number) ? 0
        : d.Number.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') ? 1 : 2;

    internal void RequestDelete()
    {
        if (editing || SelectedItems.Count == 0) return;
        CancelSelectionNavigation();
        DeleteRequested?.Invoke(SelectedDetections.ToArray());
    }

    private sealed class DetectionComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => CompareRecords((Detection)((ListViewItem)x!).Tag!, (Detection)((ListViewItem)y!).Tag!);
    }
    private static int CompareRecords(Detection a, Detection b)
    {
            int c = LabelGroup(a).CompareTo(LabelGroup(b));
            if (c == 0) c = LabelGroup(a) == 0
                ? NumericValue(a.Number).CompareTo(NumericValue(b.Number))
                : StringComparer.Ordinal.Compare(a.Number, b.Number);
            if (c == 0) c = a.Bounds.Y.CompareTo(b.Bounds.Y);
            if (c == 0) c = a.Bounds.X.CompareTo(b.Bounds.X);
            return c == 0 ? StringComparer.Ordinal.Compare(a.Id, b.Id) : c;
    }

    private void EditRow(ListViewItem? row)
    {
        if (row == null) return;
        explicitEdit = true;
        try
        {
            row.BeginEdit();
            var editHandle = SendMessage(Handle, 0x1018, IntPtr.Zero, IntPtr.Zero);
            if (((Detection)row.Tag!).IsUnrecognized) SetWindowText(editHandle, "");
            SendMessage(editHandle, 0x00B1, IntPtr.Zero, new IntPtr(-1)); // EM_SETSEL: select all
            labelWindow = NativeWindow.FromHandle(editHandle); // LVM_GETEDITCONTROL
            if (labelWindow != null) pendingLabelWindows.Add(labelWindow);
        }
        finally { explicitEdit = false; }
    }
    internal void EditSelected() { if (SelectedItems.Count > 0) { Focus(); EditRow(SelectedItems[0]); } }
    internal bool IsEditing => editing;

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
        allRecords = detections;
        var selected = preserveSelection ? SelectedDetections.Select(d => d.Id).ToHashSet(StringComparer.Ordinal) : [];
        var focusedId = preserveSelection ? (FocusedItem?.Tag as Detection)?.Id : null;
        CancelSelectionNavigation();
        updating = true;
        BeginUpdate();
        try
        {
            Items.Clear();
            rowsById.Clear();
            foreach (var detection in detections.Where(d => !d.IsDeleted && (!InventoryMode || !d.IsReviewed) && (!OnlyUnrecognized || d.IsUnrecognized)))
            {
                var row = new ListViewItem(detection.DisplayNumber) { Tag = detection, Checked = detection.IsDone };
                Items.Add(row);
                rowsById.Add(detection.Id, row);
                row.Selected = selected.Contains(detection.Id);
                if (detection.Id == focusedId) row.Focused = true;
            }
            ListViewItemSorter = new DetectionComparer();
            Sort();
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }

    internal bool ContainsDetection(string id) => rowsById.ContainsKey(id);
    internal int FirstSelectedIndex => SelectedIndices.Count > 0 ? SelectedIndices[0] : 0;
    internal int IndexOf(string id) => rowsById.TryGetValue(id, out var row) ? row.Index : FirstSelectedIndex;
    internal sealed record ViewState(string[] Selected, string? Focused, string? Top);
    internal ViewState CaptureView() => new(SelectedDetections.Select(d=>d.Id).ToArray(), (FocusedItem?.Tag as Detection)?.Id, (TopItem?.Tag as Detection)?.Id);
    internal void RestoreView(ViewState state)
    {
        CancelSelectionNavigation(); updating = true;
        try
        {
            foreach(ListViewItem row in Items) row.Selected=state.Selected.Contains(((Detection)row.Tag!).Id);
            if(state.Focused != null && rowsById.TryGetValue(state.Focused,out var focused)) focused.Focused=true;
            if(state.Top != null && rowsById.TryGetValue(state.Top,out var top)) TopItem=top;
        }
        finally { updating=false; }
        SelectionChanged?.Invoke(SelectedDetections);
    }
    internal void SelectNext(int index, bool navigate)
    {
        CancelSelectionNavigation();
        if(Items.Count > 0)
        {
            var record=(Detection)Items[Math.Clamp(index,0,Items.Count-1)].Tag!;
            SelectDetection(record.Id,false);
            if(navigate) NavigateRequested?.Invoke(record);
        }
        Focus();
    }

    internal void SelectDetection(string id, bool add, bool toggle = false)
    {
        if (!rowsById.TryGetValue(id, out var row)) return;
        CancelSelectionNavigation();
        updating = true;
        BeginUpdate();
        try
        {
            if (!add)
                foreach (var selected in SelectedItems.Cast<ListViewItem>().ToArray()) selected.Selected = false;
            row.Selected = toggle ? !row.Selected : true;
            row.Focused = true;
            row.EnsureVisible();
        }
        finally { EndUpdate(); updating = false; }
        SelectionChanged?.Invoke(SelectedDetections); // Painting only; Enter navigation is a separate event.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (editing) { base.OnKeyDown(e); return; }
        if (e.Control && e.KeyCode == Keys.Z) { UndoRequested?.Invoke(); e.Handled=e.SuppressKeyPress=true; return; }
        if (InventoryMode && (e.KeyCode == Keys.Space || (e.Control && e.KeyCode == Keys.Enter)))
        {
            if (heldCommands.Add(e.KeyCode)) ReviewRequested?.Invoke(e.KeyCode == Keys.Enter);
            e.Handled=e.SuppressKeyPress=true; return;
        }
        if (e.KeyCode == Keys.Delete) { RequestDelete(); e.Handled = e.SuppressKeyPress = true; return; }
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
    protected override void OnKeyUp(KeyEventArgs e) { heldCommands.Remove(e.KeyCode); base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { heldCommands.Clear(); base.OnLostFocus(e); }

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
