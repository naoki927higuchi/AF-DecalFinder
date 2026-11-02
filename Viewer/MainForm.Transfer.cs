namespace AF.DecalFinder;

internal sealed partial class MainForm
{
    private bool transferBusy;
    private DataTransfer Transfers=>new(store,settings);
    private ImportConflict AskImportConflict(ImageIdentity image) => MessageBox.Show(this,
        $"{image.AbsolutePath}\n有効なローカルデータがあります。\nインポートデータで置換しますか？\n「はい」：バックアップして置換\n「いいえ」：既存データを維持してスキップ",
        "データの衝突",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes?ImportConflict.Replace:ImportConflict.Skip;
    private void TransferError(Exception ex)=>MessageBox.Show(this,ex.Message,"データ移行",MessageBoxButtons.OK,MessageBoxIcon.Error);
    private void ExportData()
    {
        if(dirty&&!SaveChanges())return;
        try
        {
            var transfer=Transfers;
            using var selection=new ExportSelectionDialog(transfer.CachedItems().Concat(transfer.PendingItems()).ToArray());
            if(selection.ShowDialog(this)!=DialogResult.OK)return;
            using var save=new SaveFileDialog{Filter="デカール移行ZIP|*.zip",FileName="AF-DecalFinder-data.zip"};
            if(save.ShowDialog(this)!=DialogResult.OK)return;
            transfer.Export(save.FileName,selection.Selected);
            MessageBox.Show(this,$"{selection.Selected.Length}画像分をエクスポートしました。画像原本は含みません。","エクスポート");
        }
        catch(Exception ex){TransferError(ex);}
    }
    private async void ImportData()
    {
        if(dirty&&!SaveChanges())return;
        using var open=new OpenFileDialog{Filter="デカール移行ZIP|*.zip"};if(open.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            int imported=Transfers.Import(open.FileName);
            ApplyImportedPreferences();
            var result=await MatchPendingAsync();
            MessageBox.Show(this,$"読込 {imported}件\n紐付け {result.Bound}件・既存維持 {result.Skipped}件\n未紐付け {Transfers.PendingItems().Length}件\n複数一致は「未紐付けデータ・再照合」から手動で選べます。","インポート結果");
        }
        catch(Exception ex){TransferError(ex);}
    }
    private void ApplyImportedPreferences()
    {
        restoringFrameWidth=true;try{frameWidth.SelectedIndex=settings.LoadFrameWidth()-1;highlightFill.Checked=settings.LoadHighlightFill();}finally{restoringFrameWidth=false;}
        deleteConfirmation.Checked=settings.LoadDeleteConfirmation();
    }
    private void ManageSearchFolders()
    {
        try
        {
            using var dialog=new FolderDialog(settings.LoadSearchFolders());if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            settings.SaveSearchFolders(dialog.Folders);
            ShowPendingImports();
        }
        catch(Exception ex){TransferError(ex);}
    }
    private async Task<(int Bound,int Skipped)> MatchPendingAsync()
    {
        var transfer=Transfers;var pending=transfer.PendingItems();var folders=settings.LoadSearchFolders();
        transferBusy=true;UseWaitCursor=true;list.Enabled=false;UpdateCommands();
        IReadOnlyList<ImageIdentity> images;
        try{images=await Task.Run(()=>DataTransfer.Scan(folders,pending));}
        catch { transferBusy=false;UseWaitCursor=IsAnalyzing;list.Enabled=!IsAnalyzing;UpdateCommands();throw; }
        if(IsDisposed)return (0,0);
        int bound=0,skipped=0;
        try
        {
        foreach(var item in pending)
        {
            var matches=images.Where(image=>DataTransfer.Matches(item,image)).ToArray();
            if(matches.Length!=1)continue;
            if(transfer.Bind(item,matches[0],AskImportConflict))bound++;else skipped++;
        }
        RefreshImportedCurrent();return(bound,skipped);
        }
        finally{transferBusy=false;UseWaitCursor=IsAnalyzing;list.Enabled=!IsAnalyzing;UpdateCommands();}
    }
    private void ResolveOpenedImage(ImageIdentity image)
    {
        var transfer=Transfers;var matches=transfer.Matching(image);
        TransferItem? chosen=matches.Length==1?matches[0]:null;
        if(matches.Length>1)
        {
            using var dialog=new ItemChoiceDialog<TransferItem>("同名・同寸法の未紐付けデータを選択",matches);
            if(dialog.ShowDialog(this)==DialogResult.OK)chosen=dialog.Selected;
        }
        if(chosen!=null)transfer.Bind(chosen,image,AskImportConflict);
    }
    private void RefreshImportedCurrent()
    {
        if(identity==null||IsAnalyzing||dirty)return;
        var loaded=store.Load(identity);if(loaded==null)return;
        catalog=loaded;undoState=null;mutationGeneration++;
        list.SetDetections(loaded.Detections);view.SetDetections(loaded.Detections);overview.SetDetections(loaded.Detections);Search();UpdateCommands();
    }
    private void ShowPendingImports()
    {
        if(dirty&&!SaveChanges())return;
        try
        {
            using var dialog=new Form{Text="未紐付けデータ",Width=760,Height=430,StartPosition=FormStartPosition.CenterParent};
            var listBox=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
            var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42};
            var scan=new Button{Text="再照合"};var bind=new Button{Text="画像を指定…",AutoSize=true,Enabled=false};var close=new Button{Text="閉じる",DialogResult=DialogResult.Cancel};
            void Reload(){listBox.Items.Clear();listBox.Items.AddRange(Transfers.PendingItems());bind.Enabled=false;}
            listBox.SelectedIndexChanged+=(_,_)=>bind.Enabled=listBox.SelectedItem!=null;
            scan.Click+=async(_,_)=>
            {
                scan.Enabled=bind.Enabled=false;
                try{var result=await MatchPendingAsync();if(!dialog.IsDisposed){Reload();MessageBox.Show(dialog,$"紐付け {result.Bound}件・既存維持 {result.Skipped}件\n未紐付け {listBox.Items.Count}件","再照合結果");}}
                catch(Exception ex){TransferError(ex);}finally{if(!dialog.IsDisposed)scan.Enabled=true;}
            };
            bind.Click+=(_,_)=>
            {
                if(listBox.SelectedItem is not TransferItem item)return;
                using var file=new OpenFileDialog{Filter="画像|*.jpg;*.jpeg;*.png",Title=$"{item.FileName}（{item.Data.Image.Width}×{item.Data.Image.Height}）に紐付け"};
                if(file.ShowDialog(dialog)!=DialogResult.OK)return;
                try{using var source=Image.FromFile(file.FileName);Transfers.Bind(item,ImageIdentity.Read(file.FileName,source.Size),AskImportConflict);RefreshImportedCurrent();Reload();}
                catch(Exception ex){TransferError(ex);}
            };
            bar.Controls.AddRange([scan,bind,close]);dialog.Controls.Add(listBox);dialog.Controls.Add(bar);dialog.CancelButton=close;
            Reload();dialog.ShowDialog(this);
        }
        catch(Exception ex){TransferError(ex);}
    }
}
