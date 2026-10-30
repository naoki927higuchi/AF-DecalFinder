using AF.DecalFinder;
using System.Reflection;
using System.Text.Json;

namespace AF.DecalFinder.Checks;

internal static class Phase5Checks
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Invoke(Control c, string name, object arg) => c.GetType().GetMethod(name, Hidden)!.Invoke(c, [arg]);
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Hidden)!.GetValue(obj)!;
    internal static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "viewer-checks", "phase5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "sample.png");
        using (var bitmap = new Bitmap(800, 600)) { using var g = Graphics.FromImage(bitmap); g.Clear(Color.White); bitmap.Save(path); }
        var identity = ImageIdentity.Read(path, new Size(800, 600));
        var store = new EditStore(Path.Combine(folder, "cache"));
        var a = new Detection("49", new Rectangle(100, 100, 40, 30), 91, "a");
        var b = new Detection("49", new Rectangle(300, 100, 40, 30), 92, "b");
        var catalog = new OcrCatalog(new Size(800, 600), [a, b]);
        check(store.Load(identity) == null, "Absent edit cache returns no overrides");
        a.Number = "004"; a.IsModified = true; a.IsDone = true;
        var manual = catalog.AddManual("120", new PointF(799, 599));
        check(manual.IsManual && manual.OriginalNumber == null && manual.Bounds.Right == 800 && manual.Bounds.Bottom == 600,
            "Manual record has distinct origin, median size and clamped image-edge bounds");
        store.Save(identity, catalog);
        var restored = new EditStore(store.DirectoryPath).Load(identity)!;
        var ra = restored.Detections.Single(d => d.Id == "a");
        check(ra.Number == "004" && ra.IsModified && ra.IsDone && ra.OriginalNumber == "49" && ra.OriginalBounds == a.OriginalBounds,
            "Restart restores corrected text, done state, ID and original OCR evidence");
        check(restored.Detections.Single(d => d.Id == manual.Id).IsManual && restored.Find("120").Count == 1,
            "Restart restores searchable manual record with same ID");
        a.IsDone = false; store.Save(identity, catalog);
        check(!store.Load(identity)!.Detections[0].IsDone && Directory.GetFiles(store.DirectoryPath, "*.bak").Length == 1
            && Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Atomic replacement restores unchecked state, retains backup and removes temp");
        var saved = File.ReadAllBytes(store.PathFor(path));
        using (var locked = new FileStream(store.PathFor(path), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; try { store.Save(identity, catalog); } catch (IOException) { failed = true; }
            check(failed, "Locked destination reports save failure");
        }
        check(saved.SequenceEqual(File.ReadAllBytes(store.PathFor(path))) && Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0,
            "Failed replacement preserves previous committed cache");
        bool stale = false; try { store.Load(identity with { LastWriteUtc = identity.LastWriteUtc.AddSeconds(1) }); } catch (InvalidDataException) { stale = true; }
        check(stale, "Changed image metadata rejects stale overrides");
        File.SetLastWriteTimeUtc(path, identity.LastWriteUtc.AddMinutes(1));
        bool changedSave = false; try { store.Save(identity, catalog); } catch (IOException) { changedSave = true; }
        check(changedSave && saved.SequenceEqual(File.ReadAllBytes(store.PathFor(path))), "Image changed during editing prevents overwrite");
        File.SetLastWriteTimeUtc(path, identity.LastWriteUtc);
        File.WriteAllText(store.PathFor(path), "{}");
        bool bad = false; try { store.Load(identity); } catch (InvalidDataException) { bad = true; }
        check(bad, "Invalid persisted schema is rejected");
        File.WriteAllBytes(store.PathFor(path), saved);
        check(store.PathFor(path) == store.PathFor(path.ToUpperInvariant()) && store.PathFor(path) != store.PathFor(path + ".png"),
            "Absolute image path keys are case insensitive and image-specific");

        using (var host = new Form { ClientSize = new Size(300, 200) })
        using (var editList = new OcrResultsList())
        {
            host.Controls.Add(editList); host.Show();
            var first = new Detection("10", new Rectangle(10, 10, 20, 20), 80, "first");
            var second = new Detection("20", new Rectangle(40, 10, 20, 20), 80, "second");
            editList.SetDetections([first, second]);
            editList.SelectDetection(first.Id, false); editList.SelectDetection(second.Id, true);
            int editingIndex = -1;
            editList.BeforeLabelEdit += (_, e) => editingIndex = e.Item;
            Invoke(editList, "OnKeyDown", new KeyEventArgs(Keys.F2));
            check(editingIndex == 0, "F2 edits SelectedItems[0], not focused selected item");
            var native = Field<NativeWindow>(editList, "labelWindow");
            check(native != null, "Native label accessibility owner is retained during edit");
            typeof(OcrResultsList).GetMethod("SendMessage", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [editList.Handle, (uint)0x10B3, IntPtr.Zero, IntPtr.Zero]); // LVM_CANCELEDITLABEL
            Application.DoEvents(); GC.Collect(); GC.WaitForPendingFinalizers(); Application.DoEvents();
            var pending = typeof(OcrResultsList).GetField("pendingLabelWindows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            check((int)pending.GetType().GetProperty("Count")!.GetValue(pending)! == 0,
                "Native label cleanup releases retained callbacks before subsequent GC");
            host.Close();
        }
        using (var host = new Form { ClientSize = new Size(300, 200) })
        using (var editList = new OcrResultsList())
        {
            host.Controls.Add(editList); host.Show();
            var first = new Detection("10", new Rectangle(10, 10, 20, 20), 80, "first");
            var second = new Detection("20", new Rectangle(40, 10, 20, 20), 80, "second");
            editList.SetDetections([first, second]);
            editList.SelectDetection(first.Id, false);
            int editingIndex = -1;
            editList.BeforeLabelEdit += (_, e) => editingIndex = e.Item;
            var p = editList.Items[1].GetBounds(ItemBoundsPortion.Label).Location; p.Offset(3, 3);
            var message = Message.Create(editList.Handle, 0x203, IntPtr.Zero, (IntPtr)((p.Y << 16) | p.X));
            typeof(OcrResultsList).GetMethod("WndProc", Hidden)!.Invoke(editList, [message]);
            check(editingIndex == 1, "Double click edits clicked row independently of previous selection");
            host.Close();
        }
        using (var host = new Form { ClientSize = new Size(300, 200) })
        using (var editList = new OcrResultsList())
        {
            host.Controls.Add(editList); host.Show();
            var record = new Detection("10", new Rectangle(10, 10, 20, 20), 80, "edit");
            editList.SetDetections([record]);
            int commits = 0; editList.DataChanged += () => commits++;
            Invoke(editList, "OnAfterLabelEdit", new LabelEditEventArgs(0, null));
            check(record.Number == "10" && commits == 0, "Esc/null label leaves record and persistence untouched");
            Invoke(editList, "OnAfterLabelEdit", new LabelEditEventArgs(0, "007"));
            check(record.Number == "007" && commits == 1 && record.IsModified, "Label commit updates and saves model before queued sort");
            Application.DoEvents();
            check(editList.Items[0].Text == "007" && editList.SelectedDetections.Single() == record, "Deferred native label refresh preserves record selection");
            host.Close();
        }

        using (var form = new MainForm(path, store, new AppSettingsStore(Path.Combine(folder, "settings.json"))))
        {
            form.Show(); Application.DoEvents();
            var list = Field<OcrResultsList>(form, "list");
            var view = Field<ImageViewControl>(form, "view");
            var overview = Field<ImageViewControl>(form, "overview");
            var number = Field<DigitsTextBox>(form, "number");
            check(list.Items.Count == 3 && list.Items[0].Text == "004", "Main form loads saved records before incompatible Phase 1 JSON");
            foreach (ListViewItem row in list.Items) row.Selected = true;
            Application.DoEvents();
            var target = list.Items[1]; var targetRecord = (Detection)target.Tag!;
            // Send the production WndProc a state-icon down/up on a real native ListView.
            var hitPoint = Enumerable.Range(0, 32).Select(x => new Point(x, target.Bounds.Top + target.Bounds.Height / 2))
                .First(p => list.HitTest(p).Location.HasFlag(ListViewHitTestLocations.StateImage));
            foreach (int msg in new[] { 0x201, 0x202 })
            {
                var message = Message.Create(list.Handle, msg, IntPtr.Zero, (IntPtr)((hitPoint.Y << 16) | hitPoint.X));
                typeof(OcrResultsList).GetMethod("WndProc", Hidden)!.Invoke(list, [message]);
            }
            check(list.CheckedItems.Count == 1 && targetRecord.IsDone && list.SelectedItems.Count == 3,
                "Native checkbox click changes exactly one of multiple selected rows");
            var key = new KeyEventArgs(Keys.Space); Invoke(list, "OnKeyDown", key);
            check(key.SuppressKeyPress && list.CheckedItems.Count == 1, "Space suppresses native bulk checking");
            check(store.Load(identity)!.Detections.Single(d => d.Id == targetRecord.Id).IsDone, "Checkbox autosaves without changing other records");
            target.Checked = false;
            check(!store.Load(identity)!.Detections.Single(d => d.Id == targetRecord.Id).IsDone, "Uncheck autosaves immediately");
            number.Text = "49"; var center = view.ImageCenter; var scale = view.Zoom;
            using (var isolatedList = new OcrResultsList())
            {
                isolatedList.SetDetections([new Detection("1", new Rectangle(1, 1, 2, 2), 90, "test")]);
                check(isolatedList.Rename("test", "12a") && !isolatedList.Rename("test", ""), "Alphanumeric labels accepted; empty edits rejected");
            }
            check(list.Rename(targetRecord.Id, "002") && list.Items[0].Tag == targetRecord && target.Selected && targetRecord.Id == "b",
                "Edit resorts numerically and retains selected fixed-ID record");
            check(view.ImageCenter == center && view.Zoom == scale && store.Load(identity)!.Find("002").Count == 1
                && store.Load(identity)!.Find("49").Count == 0, "Edit autosaves and refreshes exact search without navigation");
            form.BeginAdd(); check(Field<SplitContainer>(form, "panes").Panel1Collapsed && view.AddingPoint, "Add mode temporarily hides list and arms detail click");
            form.EndAdd(); check(list.Items.Count == 3 && !view.AddingPoint && !Field<SplitContainer>(form, "panes").Panel1Collapsed,
                "Cancel addition restores docking and leaves records unchanged");
            form.BeginAdd(); form.AddAt("999", new PointF(400, 300));
            var added = list.SelectedDetections.Single();
            check(added.IsManual && added.Number == "999" && list.Items.Count == 4 && !view.AddingPoint, "Add selects new manual record and exits mode");
            var box = view.ScreenBounds(added.Bounds);
            check(view.HitTestDetection(Point.Round(new PointF(box.X + box.Width / 2, box.Y + box.Height / 2))) == added,
                "Manual record participates in image reverse lookup");
            number.Text = "999";
            check(Field<Label>(form, "count").Text == "1件", "Manual record participates in immediate search");
            Field<Button>(form, "floatList").PerformClick();
            var host = form.OwnedForms.Single();
            form.BeginAdd(); check(!host.Visible && view.AddingPoint, "Floating add mode hides same modeless host");
            form.EndAdd(); check(host.Visible && list.SelectedDetections.Single() == added, "Cancel restores floating host and same selection");
            host.Close();
            check(list.SelectedDetections.Single() == added && list.Items.Cast<ListViewItem>().All(r => r.Checked == ((Detection)r.Tag!).IsDone),
                "Redocking preserves model, checks and fixed-ID selection");
            var selectedIds = list.SelectedDetections.Select(d => d.Id).ToArray();
            list.SetDetections(Field<OcrCatalog>(form, "catalog").Detections, true);
            check(list.SelectedDetections.Select(d => d.Id).SequenceEqual(selectedIds), "List rebuild restores selection by IDs");
            form.Close();
        }
        using (var restarted = new MainForm(path, new EditStore(store.DirectoryPath), new AppSettingsStore(Path.Combine(folder, "settings.json"))))
        {
            restarted.Show(); Application.DoEvents();
            var list = Field<OcrResultsList>(restarted, "list");
            check(list.Items.Count == 4 && list.Items.Cast<ListViewItem>().Any(r => r.Text == "002") && list.Items.Cast<ListViewItem>().Any(r => r.Text == "999"),
                "New application form restores corrected and manually added records");
            restarted.Close();
        }
        foreach (var mode in new[] { ImageViewMode.Overview, ImageViewMode.Detail })
        {
            using var canvas = new ImageViewControl { Size = new Size(400, 300), Mode = mode };
            var image = new Bitmap(400, 300); using (var g = Graphics.FromImage(image)) g.Clear(Color.White);
            canvas.SetImage(image);
            var done = new Detection("1", new Rectangle(100, 100, 40, 30), 90, "done") { IsDone = true };
            canvas.SetDetections([done]); canvas.SetHighlights([done]); canvas.SetSelection(done);
            Bitmap Render() { var result = new Bitmap(400, 300); using var g = Graphics.FromImage(result); Invoke(canvas, "OnPaint", new PaintEventArgs(g, canvas.ClientRectangle)); return result; }
            using (var result = Render())
            {
                var pixel = result.GetPixel(120, 115);
                check(pixel.R is >= 126 and <= 128 && pixel.G == pixel.R && pixel.B == pixel.R && result.GetPixel(100, 110).B > 200,
                    $"{mode}: 50% black under blue/red borders, source pixel unaffected");
            }
            done.IsDone = false; using var cleared = Render();
            check(cleared.GetPixel(120,115).R == 255 && image.GetPixel(120,115).R == 255, $"{mode}: uncheck clears shade and source stays unchanged");
            if (mode == ImageViewMode.Detail)
            {
                canvas.AddingPoint = true; int clicks = 0; PointF chosen = default;
                canvas.AddPointClicked += point => { clicks++; chosen = point; };
                Invoke(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 120, 115, 0));
                Invoke(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 120, 115, 0));
                check(clicks == 1 && chosen == new PointF(120,115), "Add click emits original pixel coordinates even over existing detection");
                Invoke(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 120, 115, 0));
                Invoke(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 1, 150, 115, 0));
                Invoke(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 150, 115, 0));
                check(clicks == 1, "Detail drag in add mode never opens number entry");
            }
        }
    }
}
