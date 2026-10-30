namespace AF.DecalFinder;

// Historical name retained for compatibility with UI test helpers.
internal sealed class DigitsTextBox : TextBox
{
    public DigitsTextBox() { ImeMode = ImeMode.NoControl; }
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); SelectAll(); }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); SelectAll(); }
}
