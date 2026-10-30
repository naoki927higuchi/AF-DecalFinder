namespace AF.DecalFinder;

internal sealed class NumberDialog : Form
{
    private readonly DigitsTextBox input = new() { Dock = DockStyle.Top, AccessibleName = "追加する番号" };
    internal string Number => input.Text;
    public NumberDialog(string initial)
    {
        Text = "番号追加"; ClientSize = new Size(300, 110); Padding = new Padding(12);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
        var ok = new Button { Text = "追加", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([cancel, ok]); Controls.AddRange([input, buttons]);
        AcceptButton = ok; CancelButton = cancel;
        input.TextChanged += (_, _) => ok.Enabled = Detection.ValidLabel(input.Text);
        input.Text = initial;
    }
}
