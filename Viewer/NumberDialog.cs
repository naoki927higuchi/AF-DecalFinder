namespace AF.DecalFinder;

internal sealed class NumberDialog : Form
{
    private readonly DigitsTextBox input = new() { Dock = DockStyle.Top, AccessibleName = "追加する番号" };
    internal string Number => input.Text;
    private readonly Point? anchor;
    public NumberDialog(string initial, Point? screenPoint = null)
    {
        anchor = screenPoint;
        Text = anchor == null ? "番号追加" : "番号編集"; ClientSize = new Size(300, 110); Padding = new Padding(12);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
        input.AccessibleName = anchor == null ? "追加する番号" : "編集する番号";
        var ok = new Button { Text = anchor == null ? "追加" : "OK", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([cancel, ok]); Controls.AddRange([input, buttons]);
        AcceptButton = ok; CancelButton = cancel;
        input.TextChanged += (_, _) => ok.Enabled = Detection.ValidLabel(input.Text);
        input.Text = initial;
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (anchor is Point point)
        {
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromPoint(point).WorkingArea;
            Location = new Point(Math.Clamp(point.X + 12, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(point.Y + 12, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); input.Focus(); input.SelectAll(); }
}
