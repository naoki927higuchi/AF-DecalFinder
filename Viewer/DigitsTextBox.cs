namespace AF.DecalFinder;

internal sealed class DigitsTextBox : TextBox
{
    public DigitsTextBox() { ImeMode = ImeMode.Disable; }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && e.KeyChar is < '0' or > '9') e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        // Also filters pasted text, which does not pass through OnKeyPress.
        var filtered = new string(Text.Where(c => c is >= '0' and <= '9').ToArray());
        if (filtered != Text)
        {
            var caret = Text.Take(SelectionStart).Count(c => c is >= '0' and <= '9');
            Text = filtered;
            SelectionStart = Math.Min(caret, TextLength);
            return;
        }
        base.OnTextChanged(e);
    }
}
