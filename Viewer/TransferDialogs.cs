namespace AF.DecalFinder;

internal sealed class ExportSelectionDialog : Form
{
    private readonly CheckedListBox list=new(){Dock=DockStyle.Fill,CheckOnClick=true};
    internal TransferItem[] Selected=>list.CheckedItems.Cast<TransferItem>().ToArray();
    internal ExportSelectionDialog(TransferItem[] images)
    {
        Text="エクスポートする画像（画像原本は含みません）";Width=650;Height=430;StartPosition=FormStartPosition.CenterParent;
        foreach(var image in images)list.Items.Add(image,true);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};
        var all=new Button{Text="全画像"};all.Click+=(_,_)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,true);};
        var none=new Button{Text="選択解除"};none.Click+=(_,_)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);};
        var ok=new Button{Text="出力",DialogResult=DialogResult.OK};var cancel=new Button{Text="キャンセル",DialogResult=DialogResult.Cancel};
        buttons.Controls.AddRange([all,none,ok,cancel]);Controls.Add(list);Controls.Add(buttons);AcceptButton=ok;CancelButton=cancel;
    }
}
internal sealed class ItemChoiceDialog<T> : Form where T:class
{
    private readonly ListBox list=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    internal T? Selected=>list.SelectedItem as T;
    internal ItemChoiceDialog(string title,IEnumerable<T> items)
    {
        Text=title;Width=750;Height=360;StartPosition=FormStartPosition.CenterParent;
        foreach(var item in items)list.Items.Add(item);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};
        var ok=new Button{Text="選択",DialogResult=DialogResult.OK,Enabled=false};var cancel=new Button{Text="キャンセル",DialogResult=DialogResult.Cancel};
        list.SelectedIndexChanged+=(_,_)=>ok.Enabled=list.SelectedItem!=null;
        bar.Controls.AddRange([ok,cancel]);Controls.Add(list);Controls.Add(bar);AcceptButton=ok;CancelButton=cancel;
    }
}
internal sealed class FolderDialog : Form
{
    private readonly ListBox folders=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    internal string[] Folders=>folders.Items.Cast<string>().ToArray();
    internal FolderDialog(IEnumerable<string> paths)
    {
        Text="画像検索フォルダ（サブフォルダも検索）";Width=700;Height=380;StartPosition=FormStartPosition.CenterParent;
        folders.Items.AddRange(paths.Cast<object>().ToArray());
        var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};
        var add=new Button{Text="追加…"};add.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog();if(dialog.ShowDialog(this)==DialogResult.OK&&!Folders.Contains(dialog.SelectedPath,StringComparer.OrdinalIgnoreCase))folders.Items.Add(dialog.SelectedPath);};
        var remove=new Button{Text="登録解除"};remove.Click+=(_,_)=>{if(folders.SelectedIndex>=0)folders.Items.RemoveAt(folders.SelectedIndex);};
        var ok=new Button{Text="保存",DialogResult=DialogResult.OK};var cancel=new Button{Text="キャンセル",DialogResult=DialogResult.Cancel};
        bar.Controls.AddRange([add,remove,ok,cancel]);Controls.Add(folders);Controls.Add(bar);AcceptButton=ok;CancelButton=cancel;
    }
}
