using AF.DecalFinder;
using System.Reflection;
namespace AF.DecalFinder.Checks;
internal static class WindowSelectionChecks
{
    static void Key(Control c, Keys key) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(c,[new KeyEventArgs(key)]);
    internal static void Run(string root, Action<bool,string> check)
    {
        using(var host=new Form())
        using(var list=new OcrResultsList())
        {
            host.Controls.Add(list); host.Show();
            var records=Enumerable.Range(1,60).Select(i=>new Detection(i.ToString(),new Rectangle(i*20,100,10,10),90,"id"+i)).ToArray();
            list.SetDetections(records);
            int moves=0; Detection? target=null;
            list.NavigateRequested+=d=>{moves++;target=d;};
            list.Items[5].Selected=true; list.Items[2].Selected=true; list.Items[5].Selected=false;
            check(moves==0,"Selection pan is deferred until final selection");
            Application.DoEvents();
            check(moves==1 && target==records[2],"Selection burst pans once to first final selected record");
            Key(list,Keys.Control|Keys.A); Application.DoEvents();
            check(moves==2 && target==records[0] && list.SelectedItems.Count==60,"Ctrl+A pans once to first selected record");
            list.Items[59].EnsureVisible(); Application.DoEvents();
            check(moves==2,"Scrolling alone never pans");
            Key(list,Keys.Escape); Application.DoEvents();
            check(moves==2,"Empty selection never pans");
            list.Items[3].Selected=true; list.SelectDetection("id40",false); Application.DoEvents();
            check(moves==2 && list.SelectedDetections.Single().Id=="id40","Reverse lookup cancels queued user pan");
            list.Items[4].Selected=true; list.SetDetections(records,true); Application.DoEvents();
            check(moves==2,"Rebuild cancels pending selection pan");
            list.Items[6].Selected=true; list.Rename("id40","001"); Application.DoEvents();
            check(moves==2,"Edit and numeric sort do not pan");
            using var floating=new Form(); floating.Show();
            list.Items[8].Selected=true; list.MoveTo(floating); Application.DoEvents();
            check(moves==2,"Reparenting cancels pending selection pan");
            list.MoveTo(host); floating.Close();host.Close();
        }
        var areas=new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1280,0,1280,984)};
        var min=new Size(850,450);
        check(new WindowPlacement(-1200,80,900,600,false).Fit(areas,min)==new Rectangle(-1200,80,900,600),"Visible negative-coordinate monitor placement preserved");
        var corrected=new WindowPlacement(9000,-9000,4000,3000,true).Fit(areas,min);
        check(areas.Any(a=>a.Contains(corrected)),"Disconnected monitor and oversized window corrected into work area");
        check(new WindowPlacement(0,0,1200,900,false).Fit([new Rectangle(0,0,640,400)],min)==new Rectangle(0,0,640,400),"Work area smaller than minimum still keeps window on screen");
        var path=Path.Combine(root,"viewer-checks","window-"+Guid.NewGuid()+".json");
        var settings=new AppSettingsStore(path);
        check(settings.LoadWindow()==null,"Existing settings without placement retain defaults");
        settings.RecordOpened(Path.Combine(root,"inputs","600dpi.jpg"));
        settings.SaveWindow(new(70,80,900,650,true));
        settings.RecordOpened(Path.Combine(root,"inputs","300dpi.jpg"));
        check(settings.LoadWindow()==new WindowPlacement(70,80,900,650,true) && settings.Load().RecentImages.Count==2,"Window and history saves preserve each other's fields");
        // Use empty image history for real form placement round trips.
        var formSettings=new AppSettingsStore(Path.Combine(root,"viewer-checks","placement-"+Guid.NewGuid()+".json"));
        Rectangle normal;
        using(var form=new MainForm(null,settingsStore:formSettings))
        {
            form.Show();Application.DoEvents();
            var area=Screen.FromControl(form).WorkingArea;
            form.Bounds=new Rectangle(area.X+30,area.Y+40,900,650);Application.DoEvents();
            normal=form.Bounds;form.Close();
        }
        using(var form=new MainForm(null,settingsStore:formSettings))
        {
            form.Show();Application.DoEvents();
            check(form.Bounds==normal && form.WindowState==FormWindowState.Normal,"Normal bounds restored by real MainForm");
            form.WindowState=FormWindowState.Maximized;Application.DoEvents();
            form.WindowState=FormWindowState.Minimized;Application.DoEvents();form.Close();
        }
        check(formSettings.LoadWindow()!.Maximized,"Minimized-from-maximized saves maximized state");
        using(var form=new MainForm(null,settingsStore:formSettings))
        {
            form.Show();Application.DoEvents();
            check(form.WindowState==FormWindowState.Maximized,"Maximized MainForm restored on startup");
            form.WindowState=FormWindowState.Normal;Application.DoEvents();
            check(form.Bounds==normal,"Maximized restart retains normal RestoreBounds");
            form.WindowState=FormWindowState.Minimized;Application.DoEvents();form.Close();
        }
        check(!formSettings.LoadWindow()!.Maximized,"Minimized-from-normal saves normal state");
    }
}
