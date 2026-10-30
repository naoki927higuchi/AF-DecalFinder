using AF.DecalFinder;
using System.Reflection;
using System.Text.Json.Nodes;

namespace AF.DecalFinder.Checks;
internal static class HistoryChecks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Hidden)!.GetValue(obj)!;
    static KeyEventArgs Enter(Control c)
    {
        var e = new KeyEventArgs(Keys.Enter);
        typeof(Control).GetMethod("OnKeyDown", Hidden)!.Invoke(c, [e]);
        return e;
    }
    internal static void Run(string root, Action<bool,string> check)
    {
        var folder = Path.Combine(root, "viewer-checks", "history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new AppSettingsStore(Path.Combine(folder, "Settings.json"));
        check(settings.Load().RecentImages.Count == 0, "Absent settings starts with empty history");
        File.WriteAllText(settings.FilePath, "{\"OtherSetting\":{\"value\":42}}");
        for (int i=0;i<11;i++) settings.RecordOpened(Path.Combine(folder, i + ".png"));
        var history = settings.Load();
        check(history.RecentImages.Count == 10 && history.RecentImages[0].EndsWith("10.png") && history.RecentImages[9].EndsWith("1.png"), "History retains newest ten in order");
        settings.RecordOpened(Path.Combine(folder,"5.png").ToUpperInvariant());
        check(settings.Load().RecentImages.Count == 10 && settings.Load().RecentImages[0].EndsWith("5.PNG"), "Reopening case-insensitive path moves one entry to front");
        check(JsonNode.Parse(File.ReadAllText(settings.FilePath))!["OtherSetting"]!["value"]!.GetValue<int>() == 42, "Unknown settings fields survive safe replacement");
        var before = File.ReadAllText(settings.FilePath);
        using (var locked = new FileStream(settings.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            bool failed=false; try { settings.RecordOpened(Path.Combine(folder,"fail.png")); } catch(IOException) { failed=true; }
            check(failed, "Settings replacement failure is reported");
        }
        check(File.ReadAllText(settings.FilePath)==before && Directory.GetFiles(folder,"*.tmp").Length==0, "Failed settings save keeps previous bytes and removes temp");
        File.WriteAllText(settings.FilePath,"broken");
        bool invalid=false; try { settings.RecordOpened(Path.Combine(folder,"fail.png")); } catch(System.Text.Json.JsonException) { invalid=true; }
        check(invalid && File.ReadAllText(settings.FilePath)=="broken", "Malformed settings cannot be silently overwritten");
        File.WriteAllText(settings.FilePath,before);
        var image = Path.Combine(folder,"sample.png");
        using(var b = new Bitmap(1600,1200)) b.Save(image);
        var store = new EditStore(Path.Combine(folder,"cache"));
        var identity = ImageIdentity.Read(image,new Size(1600,1200));
        var records = new List<Detection> {
            new("49",new Rectangle(900,700,40,40),90,"later"),
            new("49",new Rectangle(700,500,40,40),90,"first") {IsDone=true,IsModified=true},
            new("49",new Rectangle(900,500,40,40),90,"same-y"),
            new("999",new Rectangle(1100,800,40,40),0,"manual") {IsManual=true} };
        store.Save(identity,new OcrCatalog(new Size(1600,1200),records));
        settings.RecordOpened(image);
        using(var form = new MainForm(null,store,settings))
        {
            form.Show(); Application.DoEvents();
            var list=Field<OcrResultsList>(form,"list");
            var view=Field<ImageViewControl>(form,"view");
            var overview=Field<ImageViewControl>(form,"overview");
            var number=Field<DigitsTextBox>(form,"number");
            check(list.Items.Count==4 && list.Items.Cast<ListViewItem>().Any(i=>i.Checked), "Startup without arguments restores image and done cache");
            check(list.Items.Cast<ListViewItem>().Any(i=>((Detection)i.Tag!).IsManual) && list.Items.Cast<ListViewItem>().Any(i=>((Detection)i.Tag!).IsModified), "Startup restores modified and manual records");
            list.SelectDetection("manual",false);
            int events=0; list.SelectionChanged+=_=>events++;
            var center=view.ImageCenter; var zoom=view.Zoom;
            number.Text="4"; number.Text="49";
            check(view.ImageCenter==center && events==0, "Typing updates search without pan or selection events");
            check(Field<Label>(form,"count").Text=="3件", "Exact matches update count immediately");
            var key=Enter(number);
            check(key.Handled && key.SuppressKeyPress, "Search Enter suppresses keypress and beep");
            check(view.ImageCenter==new PointF(720,520) && view.Zoom==zoom, "Search Enter chooses first Y then X match and keeps zoom");
            check(list.SelectedDetections.Single().Id=="manual" && events==0, "Search navigation never changes list selection");
            check(Field<Label>(form,"count").Text=="3件", "Enter keeps all duplicate search hits");
            center=view.ImageCenter;
            number.Text="888"; Enter(number); number.Clear(); Enter(number);
            check(view.ImageCenter==center && events==0, "No-match and empty Enter are no-ops");
            var menu=Field<ToolStripMenuItem>(form,"recentImages");
            check(menu.Enabled && menu.DropDownItems.Count==10 && menu.DropDownItems[0].ToolTipText==image, "History menu displays names with absolute-path tooltips");
            menu.DropDownItems[0].PerformClick(); Application.DoEvents();
            check(list.Items.Count==4 && settings.Load().LastImagePath==image, "History menu uses normal cached image load");
            form.Close();
        }
        var missing = new AppSettingsStore(Path.Combine(folder,"missing.json"));
        missing.RecordOpened(Path.Combine(folder,"absent.png"));
        using(var form=new MainForm(null,store,missing))
        {
            form.Show(); Application.DoEvents();
            check(Field<OcrResultsList>(form,"list").Items.Count==0 && !Field<DigitsTextBox>(form,"number").Enabled, "Missing last image starts normally without restore loop");
            check(missing.Load().RecentImages.Count==1, "Missing history remains stored");
            form.Close();
        }
        using(var form=new MainForm(null,store,new AppSettingsStore(Path.Combine(folder,"empty.json"))))
        {
            form.Show(); Application.DoEvents();
            check(!Field<ToolStripMenuItem>(form,"recentImages").Enabled,"Empty recent-images menu is disabled");
            form.Close();
        }
    }
}
