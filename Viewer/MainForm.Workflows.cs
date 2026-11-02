namespace AF.DecalFinder;

internal sealed partial class MainForm
{
    private sealed record UndoState(OcrCatalog Catalog, Action Restore, OcrResultsList.ViewState View);
    private UndoState? undoState;
    private int mutationGeneration;
    internal bool InventoryMode => list.InventoryMode;
    private bool confirmDelete = true;
    private readonly ToolStripMenuItem deleteConfirmation = new("削除前に確認する") { Checked=true, CheckOnClick=true };
    private readonly Dictionary<string,List<ToolStripItem>> commands = new();
    private readonly ToolStripLabel inventoryStatus = new();
    private readonly List<Image> commandIcons = [];
    private readonly HashSet<Keys> heldReviewKeys = [];

    private void InitializeCommands(MenuStrip menu, ToolStripMenuItem file)
    {
        var edit=new ToolStripMenuItem("編集"); var inventory=new ToolStripMenuItem("棚卸"); var preferences=new ToolStripMenuItem("設定・移行");
        menu.Items.AddRange([edit,inventory,preferences]);
        var bar=new ToolStrip { Dock=DockStyle.Top, GripStyle=ToolStripGripStyle.Hidden, AccessibleName="作業コマンド" };
        void Add(string id,string title,ToolStripMenuItem parent,Action action,bool onBar,string shortcut="")
        {
            var item=new ToolStripMenuItem(title){ShortcutKeyDisplayString=shortcut}; item.Click+=(_,_)=>action();parent.DropDownItems.Add(item);
            commands[id]=[item];
            if(onBar)
            {
                var icon=(id is "delete"?SystemIcons.Warning:id is "undo"?SystemIcons.Question:SystemIcons.Information).ToBitmap();commandIcons.Add(icon);
                var button=new ToolStripButton(title,icon){DisplayStyle=ToolStripItemDisplayStyle.ImageAndText,ToolTipText=title+(shortcut.Length>0?" ("+shortcut+")":"")};
                button.Click+=(_,_)=>action();bar.Items.Add(button);commands[id].Add(button);
            }
        }
        Add("edit","番号編集",edit,EditSelected,true,"F2");
        Add("add","番号追加",edit,BeginAdd,true);
        Add("delete","誤検出削除",edit,list.RequestDelete,true,"Delete");
        Add("undo","元に戻す",edit,Undo,true,"Ctrl+Z");
        Add("inventory","棚卸開始",inventory,()=>SetInventoryMode(!InventoryMode),true);
        Add("review","1件確認",inventory,()=>ReviewSelection(false),true,"Space");
        Add("reviewAll","選択全件確認",inventory,()=>ReviewSelection(true),true,"Ctrl+Enter");
        preferences.DropDownItems.Add(deleteConfirmation);
        deleteConfirmation.CheckedChanged+=(_,_)=>
        {
            confirmDelete=deleteConfirmation.Checked;
            if(!Visible)return;
            try{settings.SaveDeleteConfirmation(confirmDelete);}catch(Exception ex) when(SettingsError(ex)){NotifySettingsError(ex);}
        };
        Add("export","設定・データをエクスポート…",preferences,ExportData,false);
        Add("import","設定・データをインポート…",preferences,ImportData,false);
        Add("folders","画像検索フォルダ…",preferences,ManageSearchFolders,false);
        Add("pending","未紐付けデータ・再照合…",preferences,ShowPendingImports,false);
        bar.Items.Add(new ToolStripSeparator());bar.Items.Add(inventoryStatus);
        Controls.Add(bar);Controls.SetChildIndex(menu,Controls.Count-1);
        Disposed+=(_,_)=>{foreach(var icon in commandIcons)icon.Dispose();};
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        bool ready=catalog!=null&&!IsAnalyzing&&!adding&&!transferBusy;
        bool selected=ready&&list.SelectedItems.Count>0;
        void Enable(string id,bool value){if(commands.TryGetValue(id,out var items))foreach(var item in items)item.Enabled=value;}
        Enable("edit",selected);Enable("delete",selected);Enable("add",identity!=null&&!IsAnalyzing&&!adding&&!transferBusy);
        Enable("undo",ready&&undoState!=null);Enable("inventory",ready);
        Enable("review",selected&&InventoryMode);Enable("reviewAll",selected&&InventoryMode);
        foreach(var id in new[]{"import","export","folders","pending"})Enable(id,!IsAnalyzing&&!adding&&!transferBusy);
        addNumber.Enabled=identity!=null&&!IsAnalyzing&&!adding&&!transferBusy;
        reanalyze.Enabled=identity!=null&&!IsAnalyzing&&!transferBusy;
        if(commands.TryGetValue("inventory",out var entries))foreach(var item in entries)item.Text=InventoryMode?"棚卸終了":"棚卸開始";
        var remaining=catalog?.ActiveDetections.Count(d=>!d.IsReviewed)??0;
        inventoryStatus.Text=!InventoryMode?"通常モード":remaining==0?"棚卸完了":$"棚卸：未確認 {remaining}件（表示 {list.Items.Count}件）";
    }
    private void EditSelected()
    {
        if(catalog==null||IsAnalyzing||transferBusy||adding||list.SelectedItems.Count==0)return;
        showList.Checked=true;ApplyListVisibility();list.EditSelected();
    }

    internal void SetInventoryMode(bool enabled)
    {
        if(catalog==null||IsAnalyzing||transferBusy)return;
        list.InventoryMode=enabled;
        list.SetDetections(catalog.Detections,preserveSelection:!enabled);
        if(enabled)list.SelectNext(0,true);
        UpdateCommands();
    }
    private void BeginMutation()
    {
        if(catalog==null)return;
        mutationGeneration++;
        undoState=new(catalog,catalog.CaptureUndo(),list.CaptureView());
    }
    internal void Undo()
    {
        if(IsAnalyzing||transferBusy||catalog==null||undoState is not {} previous||previous.Catalog!=catalog)return;
        mutationGeneration++;undoState=null;
        list.SuppressPendingNavigation(); previous.Restore();
        list.SetDetections(catalog.Detections);list.RestoreView(previous.View);list.Focus();
        Changed();
    }
    internal void ReviewSelection(bool all)
    {
        if(!InventoryMode||catalog==null||IsAnalyzing||transferBusy)return;
        var selected=list.SelectedDetections.Take(all?int.MaxValue:1).ToArray();
        if(selected.Length==0)return;
        int index=list.FirstSelectedIndex;BeginMutation();
        foreach(var d in selected)d.IsReviewed=true;
        list.SetDetections(catalog.Detections);list.SelectNext(index,true);Changed();
    }
    private bool CommitLabel(string id,string value,bool deferUi)
    {
        if(catalog==null||IsAnalyzing||transferBusy||!Detection.ValidLabel(value))return false;
        var record=catalog.Detections.FirstOrDefault(d=>d.Id==id&&!d.IsDeleted);
        if(record==null)return false;
        int index=list.IndexOf(id);BeginMutation();int generation=mutationGeneration;var owner=catalog;
        record.Number=value;record.IsUnrecognized=false;record.IsModified=true;
        if(InventoryMode)record.IsReviewed=true;
        void Refresh()
        {
            if(IsDisposed||catalog!=owner||generation!=mutationGeneration)return;
            if(!InventoryMode&&unknownOnly.Checked)unknownOnly.Checked=false;
            list.SetDetections(owner.Detections,preserveSelection:true);
            if(InventoryMode)list.SelectNext(index,true);
            else {list.SelectDetection(id,true);list.Focus();}
            UpdateCommands();
        }
        // Native LabelEdit must finish releasing its editor before rebuilding the rows.
        if(deferUi)BeginInvoke(Refresh);else Refresh();
        Changed();return true;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(transferBusy && (keyData==Keys.Delete||keyData==Keys.F2||keyData==(Keys.Control|Keys.Z)))return true;
        if(!list.IsEditing && ActiveControl is not TextBoxBase)
        {
            if(InventoryMode && (keyData==Keys.Space||keyData==(Keys.Control|Keys.Enter)))
            {
                var key=keyData&Keys.KeyCode;
                if(heldReviewKeys.Add(key))ReviewSelection(key==Keys.Enter);
                return true;
            }
            if(keyData==(Keys.Control|Keys.Z)){Undo();return true;}
            if(keyData==Keys.Delete){list.RequestDelete();return true;}
            if(keyData==Keys.F2){EditSelected();return true;}
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override bool ProcessKeyPreview(ref Message m)
    {
        if(m.Msg is 0x101 or 0x105)heldReviewKeys.Remove((Keys)(int)m.WParam);
        return base.ProcessKeyPreview(ref m);
    }
    protected override void OnDeactivate(EventArgs e){heldReviewKeys.Clear();base.OnDeactivate(e);}
}
