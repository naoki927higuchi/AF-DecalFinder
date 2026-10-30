using AF.DecalFinder;
using System.Text.Json;
using System.Numerics;

namespace AF.DecalFinder.Checks;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label); checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var root = Path.GetFullPath(args[0]);
        var catalog = OcrCatalog.Load(Path.Combine(root, "results", "600dpi.json"));
        Check(catalog.ImageSize == new Size(7783, 6579) && catalog.Detections.Count == 181, "Phase 1 JSON: dimensions and 181 records");
        Check(catalog.Find("102").Count == 1 && catalog.Find("102")[0].Bounds == new Rectangle(4249, 3354, 49, 44), "102 exact match and source pixel bbox");
        Check(catalog.Find("49").Count == 2 && catalog.Find("118").Count == 2, "All separately located duplicate numbers retained");
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "results", "600dpi.json"))))
            Check(catalog.Detections.Select(d => d.Id).SequenceEqual(json.RootElement.GetProperty("detections").EnumerateArray().Select(d => d.GetProperty("id").GetString())), "Detection IDs preserved from JSON in source order");
        Check(catalog.Find("10").All(d => d.Number == "10") && catalog.Find("999999").Count == 0 && catalog.Find("0102").Count == 0 && catalog.Find("").Count == 0, "Exact string matching and empty query");

        var transform = new ViewTransform();
        transform.Fit(catalog.ImageSize, new Size(1000, 800));
        var point = new Point(570, 360);
        var imageX = (point.X - transform.OffsetX) / transform.Scale;
        var imageY = (point.Y - transform.OffsetY) / transform.Scale;
        transform.ZoomAt(point, 5.2, .001, 16);
        Check(Math.Abs(transform.OffsetX + imageX * transform.Scale - point.X) < 1e-8 &&
            Math.Abs(transform.OffsetY + imageY * transform.Scale - point.Y) < 1e-8, "Wheel zoom preserves image point under cursor");
        var before = transform.ToScreen(catalog.Find("102")[0].Bounds);
        transform.Pan(137, -83);
        var after = transform.ToScreen(catalog.Find("102")[0].Bounds);
        Check(Math.Abs(after.X - before.X - 137) < .001 && Math.Abs(after.Y - before.Y + 83) < .001 && after.Size == before.Size, "Pan translates bbox without resizing");

        using var input = new DigitsTextBox();
        input.Text = "a10+2 日本３";
        Check(input.Text == "102", "Paste filters ASCII non-digits including full-width digits");
        input.Text = ""; Check(input.Text.Length == 0, "Empty input allowed");
        using (var highDpi = new Bitmap(100, 80))
        {
            highDpi.SetResolution(600, 600);
            using (var g = Graphics.FromImage(highDpi)) g.Clear(Color.Magenta);
            using var display = DisplayBitmap.Create(highDpi);
            Check(display.Size == highDpi.Size && display.GetPixel(99, 79).ToArgb() == Color.Magenta.ToArgb(), "600dpi display conversion copies original pixels, ignores physical print size");
        }
        input.Text = "102";
        typeof(Control).GetMethod("OnEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(input, new object[] { EventArgs.Empty });
        Check(input.SelectionLength == 3, "Search focus selects existing text");
        input.DeselectAll();
        typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(input, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0) });
        Check(input.SelectionLength == 3, "Search click reselects existing text");

        var drop = new DataObject(); drop.SetData(DataFormats.FileDrop, new[] { "test.png" });
        Check(MainForm.GetDroppedImage(drop) == "test.png", "Single PNG file drop accepted");
        drop.SetData(DataFormats.FileDrop, new[] { "test.jpg", "second.png" });
        Check(MainForm.GetDroppedImage(drop) == null, "Multiple file drop rejected");
        drop.SetData(DataFormats.FileDrop, new[] { "test.json" });
        Check(MainForm.GetDroppedImage(drop) == null, "Non-image drop rejected");

        using (var form = new MainForm(null, new EditStore(Path.Combine(root, "viewer-checks", "isolated-" + Guid.NewGuid().ToString("N")))))
        {
            var originalPath = Path.Combine(root, "inputs", "600dpi.jpg");
            var originalDrop = new DataObject(); originalDrop.SetData(DataFormats.FileDrop, new[] { originalPath });
            typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(form, new object[] { new DragEventArgs(originalDrop, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
            var search = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<DigitsTextBox>().Single();
            Check(form.Text.EndsWith("600dpi.jpg") && search.Enabled, "Actual form Drop handler loads original and enables search");
            search.Text = "49";
            Check(form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Label>().Any(l => l.Text == "2件"), "Drop-to-search integration finds two matches");
            using var exclusive = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            Check(exclusive.Length > 0, "Displayed image does not lock source file");
            exclusive.Dispose();
            form.Show();
            Application.DoEvents();
            var toolbar = form.Controls.OfType<FlowLayoutPanel>().Single();
            var toggle = toolbar.Controls.OfType<CheckBox>().Single();
            var panes = form.Controls.OfType<SplitContainer>().Single();
            var images = panes.Panel2.Controls.OfType<SplitContainer>().Single();
            var list = panes.Panel1.Controls.OfType<OcrResultsList>().Single();
            var overview = images.Panel1.Controls.OfType<ImageViewControl>().Single();
            var detail = images.Panel2.Controls.OfType<ImageViewControl>().Single();
            Check(panes.SplitterDistance == 150 && Math.Abs(images.Panel1.Width - images.Panel2.Width) <= 1, "Initial 150px list and equal image panes");
            panes.SplitterDistance = 210;
            toggle.Checked = false;
            Check(panes.Panel1Collapsed && images.Width == panes.Panel2.ClientSize.Width, "List toggle hides list and splitter");
            toggle.Checked = true;
            Check(panes.SplitterDistance == 210 && !panes.Panel1Collapsed, "List toggle restores previous width");
            images.SplitterDistance += 30;
            Check(images.Panel1.Width != images.Panel2.Width, "Image splitter independently adjustable");
            Check(list.Items.Count == 181 && list.View == View.Details && list.FullRowSelect && list.MultiSelect && list.LabelEdit && list.CheckBoxes,
                "181 editable Details rows with multi selection and checkboxes");
            Check(list.Columns.Count == 1 && list.Columns[0].Text == "番号", "Number-only list column");
            var rows = list.Items.Cast<ListViewItem>().Select(i => (Detection)i.Tag!).ToArray();
            var expected = catalog.Detections.OrderBy(d => BigInteger.Parse(d.Number)).ThenBy(d => d.Bounds.Y).ThenBy(d => d.Bounds.X).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id);
            Check(rows.Select(d => d.Id).SequenceEqual(expected) && list.Items.Cast<ListViewItem>().All(i => i.Text == ((Detection)i.Tag!).Number), "Numeric Y/X sort preserves text and IDs");
            var duplicates = list.Items.Cast<ListViewItem>().Where(i => ((Detection)i.Tag!).Number == "49").ToArray();
            IReadOnlyList<Detection> notified = Array.Empty<Detection>();
            list.SelectionChanged += d => notified = d;
            var priorCenter = detail.ImageCenter;
            duplicates[0].Selected = true;
            duplicates[1].Selected = true;
            Check(notified.Count == 2 && ReferenceEquals(notified[0], duplicates[0].Tag) && ReferenceEquals(notified[1], duplicates[1].Tag), "Multiple selected rows retain distinct record references");
            Check(detail.ImageCenter == priorCenter && detail.Zoom == 1, "Selection and search do not move detail view");
            var navigationCount = 0;
            Detection? navigationTarget = null;
            list.NavigateRequested += d => { navigationCount++; navigationTarget = d; };
            duplicates[1].Focused = true;
            detail.SetZoom(2);
            Key(list, Keys.Enter);
            var target = (Detection)duplicates[1].Tag!;
            Check(ReferenceEquals(navigationTarget, target) && detail.ImageCenter == new PointF(target.Bounds.X + target.Bounds.Width / 2f, target.Bounds.Y + target.Bounds.Height / 2f) && detail.Zoom == 2,
                "Enter navigates focused selected duplicate by record, retaining zoom");
            var navigationCenter = detail.ImageCenter;
            Check(navigationCount == 1 && detail.ImageCenter == navigationCenter, "Selection retains navigation target");
            list.Items[0].Focused = true; // Deliberately focused but not selected.
            Key(list, Keys.Enter);
            Check(navigationCount == 1, "Ambiguous multiple selection without selected focus does not navigate");
            duplicates[1].Focused = true;
            var undock = toolbar.Controls.OfType<Button>().Single(b => b.Text == "一覧を切り離す");
            var widthBeforeFloat = panes.SplitterDistance;
            undock.PerformClick();
            var floating = form.OwnedForms.Single();
            Check(ReferenceEquals(list.Parent, floating) && panes.Panel1Collapsed && floating.Visible && !floating.Modal && list.SelectedItems.Count == 2,
                "Float reparents same selected ListView, collapses pane, remains modeless");
            Check(ReferenceEquals(list.FocusedItem, duplicates[1]) && ReferenceEquals(list.SelectedItems[1].Tag, target) && search.Text == "49", "Float retains focused record, selection references and search");
            floating.ClientSize = new Size(350, 500);
            Check(list.Width == floating.ClientSize.Width && list.Height == floating.ClientSize.Height - 30, "Floating list and add toolbar follow host resize");
            toggle.Checked = false;
            Check(!floating.Visible && panes.Panel1Collapsed && !undock.Enabled && list.SelectedItems.Count == 2, "Floating OFF hides host, retains selection and float mode");
            toggle.Checked = true;
            Check(floating.Visible && panes.Panel1Collapsed && undock.Enabled, "Floating ON restores same host");
            floating.Close();
            Check(ReferenceEquals(list.Parent, panes.Panel1) && !list.IsDisposed && !panes.Panel1Collapsed && panes.SplitterDistance == widthBeforeFloat && list.SelectedItems.Count == 2,
                "Closing floating host redocks same list and restores width");
            undock.PerformClick();
            undock.PerformClick();
            Check(form.OwnedForms.Length == 0 && ReferenceEquals(list.Parent, panes.Panel1) && list.SelectedItems.Count == 2, "Toggle button redocks without duplicate host or selection loss");
            detail.SetZoom(1);
            var beforeSelectAll = detail.ImageCenter;
            typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(list, new object[] { new KeyEventArgs(Keys.Control | Keys.A) });
            Check(notified.Count == 181 && list.SelectedItems.Count == 181, "Ctrl+A selects all 181 records");
            list.SelectDetection(((Detection)duplicates[0].Tag!).Id, false);
            Check(list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItems[0], duplicates[0]), "Reverse lookup clears all-selection by fixed ID");
            list.SelectDetection(((Detection)duplicates[1].Tag!).Id, true);
            Check(list.SelectedItems.Count == 2 && ReferenceEquals(list.FocusedItem, duplicates[1]), "Ctrl reverse lookup adds distinct duplicate number without toggling");
            list.SelectDetection("missing-id", false);
            Check(list.SelectedItems.Count == 2, "Stale detection ID leaves selection unchanged");
            toggle.Checked = false;
            list.SelectDetection(rows.Last().Id, false);
            Check(list.SelectedItems.Count == 1 && ((Detection)list.SelectedItems[0].Tag!).Id == rows.Last().Id, "Reverse lookup updates hidden list");
            toggle.Checked = true;
            Check(list.SelectedItems[0].Bounds.IntersectsWith(list.ClientRectangle), "Reverse lookup scrolls distant row into view");
            undock.PerformClick();
            list.SelectDetection(((Detection)duplicates[1].Tag!).Id, false);
            Check(list.Parent is Form && ReferenceEquals(list.SelectedItems[0], duplicates[1]) && detail.ImageCenter == beforeSelectAll, "Floating reverse lookup retains image center and uses same record");
            undock.PerformClick();
            Check(detail.ImageCenter == beforeSelectAll && navigationCount == 1, "Ctrl+A never triggers navigation");
            typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(list, new object[] { new KeyEventArgs(Keys.Escape) });
            Check(notified.Count == 0, "Escape clears all selected records");
            var clickedRecord = (Detection)duplicates[0].Tag!;
            var clickedCenter = new PointF(clickedRecord.Bounds.X + clickedRecord.Bounds.Width / 2f, clickedRecord.Bounds.Y + clickedRecord.Bounds.Height / 2f);
            detail.CenterOn(clickedCenter);
            var screenBox = detail.ScreenBounds(clickedRecord.Bounds);
            var clickPoint = Point.Round(new PointF(screenBox.X + screenBox.Width / 2, screenBox.Y + screenBox.Height / 2));
            foreach (var handler in new[] { "OnMouseDown", "OnMouseUp" })
                typeof(ImageViewControl).GetMethod(handler, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(detail, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPoint.X, clickPoint.Y, 0) });
            Check(list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItems[0].Tag, clickedRecord)
                && detail.ImageCenter == clickedCenter && search.Text == "49", "Real form image-to-list wiring selects fixed ID and retains search and center");
            detail.CenterOn(new PointF(4000, 3000));
            var zoom = images.Panel2.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<ComboBox>().Single();
            zoom.SelectedIndex = 5;
            Check(detail.Zoom == 2 && detail.ImageCenter == new PointF(4000, 3000), "Dropdown zoom preserves image center");
            form.ClientSize = new Size(1000, 700);
            Application.DoEvents();
            Check(detail.ImageCenter == new PointF(4000, 3000) && detail.Zoom == 2, "Resize preserves detail center and zoom");
            var fitted = overview.ScreenBounds(new Rectangle(Point.Empty, catalog.ImageSize));
            Check(fitted.Left >= -.01 && fitted.Top >= -.01 && fitted.Right <= overview.Width + .01 && fitted.Bottom <= overview.Height + .01, "Overview auto fits after splitter and window resize");
            duplicates[0].Selected = true;
            typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(form, new object[] { new DragEventArgs(originalDrop, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
            Check(list.Items.Count == 181 && list.SelectedItems.Count == 0 && notified.Count == 0 && search.Text == "" && detail.Zoom == 1,
                "Image reload resets search, selection and zoom");
            Check(!ReferenceEquals(list.Items.Cast<ListViewItem>().First(i => ((Detection)i.Tag!).Id == rows[0].Id).Tag, rows[0]), "Image reload replaces catalog references");
            var copiedJson = Path.Combine(AppContext.BaseDirectory, "results", "600dpi.json");
            var savedJson = File.ReadAllBytes(copiedJson);
            try
            {
                File.Delete(copiedJson);
                typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { new DragEventArgs(originalDrop, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
                Check(list.Items.Count == 0 && !search.Enabled, "Missing JSON empties list and disables search");
            }
            finally { File.WriteAllBytes(copiedJson, savedJson); }
            typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(form, new object[] { new DragEventArgs(originalDrop, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
            Check(list.Items.Count == 181 && search.Enabled, "Restored JSON repopulates list on next load");
        }
        var temp = Path.Combine(root, "viewer-checks"); Directory.CreateDirectory(temp);
        var cases = new[] {
            "{", "{}", "null",
            "{\"schema_version\":2}",
            "{\"schema_version\":1,\"image\":{\"width\":10,\"height\":10},\"detections\":[{\"id\":\"a\",\"number\":\"1\",\"bbox\":[9,0,2,1],\"confidence\":90}]}",
            "{\"schema_version\":1,\"image\":{\"width\":10,\"height\":10},\"detections\":[{\"id\":\"a\",\"number\":\"abc\",\"bbox\":[0,0,2,1],\"confidence\":90}]}"
        };
        for (int i = 0; i < cases.Length; i++)
        {
            var file = Path.Combine(temp, $"invalid-{i}.json"); File.WriteAllText(file, cases[i]);
            var rejected = false;
            try { OcrCatalog.Load(file); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            Check(rejected, $"Malformed/schema-invalid JSON case {i} rejected");
        }
        var missing = false;
        try { OcrCatalog.Load(Path.Combine(temp, "missing.json")); } catch (FileNotFoundException) { missing = true; }
        Check(missing, "Missing JSON reported distinctly");

        // Exercise actual paint/mouse handlers and inspect rendered screen pixels.
        using var canvas = new TestCanvas { Size = new Size(400, 300) };
        var bitmap = new Bitmap(400, 300);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.White);
        canvas.SetImage(bitmap);
        var highlights = new[] { new Detection("49", new Rectangle(100, 100, 30, 20), 90), new Detection("49", new Rectangle(200, 100, 30, 20), 90) };
        canvas.SetHighlights(highlights);
        using var original = canvas.Render();
        Check(RedNear(original, 100, 100) && RedNear(original, 200, 100), "Both hits drawn red");
        Check(original.GetPixel(115, 110).ToArgb() == Color.White.ToArgb(), "Highlight interior is not filled");
        canvas.Wheel(200, 150, 120); // scale 1.2, offset -40,-30
        canvas.PanBy(30, 20); // offset -10,-10
        using var zoomed = canvas.Render();
        Check(RedNear(zoomed, 110, 110) && RedNear(zoomed, 230, 110), "Painted boxes follow real wheel+pan handlers");
        var redWidth = Enumerable.Range(105, 11).Count(x => IsRed(zoomed.GetPixel(x, 120)));
        Check(redWidth is >= 1 and <= 3, "Screen pen stays approximately 2px after zoom");
        canvas.SetHighlights(Array.Empty<Detection>());
        using var cleared = canvas.Render();
        Check(!RedNear(cleared, 110, 110), "Clearing query clears highlights without refitting");
        canvas.SetHighlights(highlights);
        using var restored = canvas.Render();
        Check(RedNear(restored, 110, 110), "Changing matches does not zoom or move image");
        canvas.SetSelection(highlights[0]);
        using var blue = canvas.Render();
        Check(BlueNear(blue, 110, 110) && !RedNear(blue, 110, 110) && RedNear(blue, 230, 110), "Blue overrides red for selected hit only, other red hit remains");
        canvas.PanBy(20, 30);
        canvas.Wheel(0, 0, 120);
        using var movedBlue = canvas.Render();
        Check(BlueNear(movedBlue, 156, 168) && RedNear(movedBlue, 300, 168), "Blue and red follow the same pan and zoom mapping");
        canvas.SetSelection(highlights[1]);
        using var switchedBlue = canvas.Render();
        Check(RedNear(switchedBlue, 156, 168) && BlueNear(switchedBlue, 300, 168), "Switching selection restores red on previous record without moving view");
        canvas.SetSelection(null);
        using var unselected = canvas.Render();
        Check(RedNear(unselected, 156, 168) && RedNear(unselected, 300, 168), "Clearing blue preserves red matches");
        canvas.SetSelection(highlights[0]);
        canvas.SetHighlights(Array.Empty<Detection>());
        using var onlyBlue = canvas.Render();
        Check(BlueNear(onlyBlue, 156, 168) && !RedNear(onlyBlue, 300, 168), "Empty search leaves independent blue selection");
        canvas.SetImage(new Bitmap(400, 300));
        using var replacedImage = canvas.Render();
        Check(!BlueNear(replacedImage, 100, 100), "Replacing image clears blue selection");
        using var detailCanvas = new TestCanvas { Size = new Size(400, 300), Mode = ImageViewMode.Detail };
        using var overviewCanvas = new TestCanvas { Size = new Size(400, 300), Mode = ImageViewMode.Overview };
        var shared = new Bitmap(800, 600);
        using (var g = Graphics.FromImage(shared)) g.Clear(Color.White);
        overviewCanvas.SetImage(shared, false);
        detailCanvas.SetImage(shared);
        detailCanvas.ViewportChanged += () => overviewCanvas.SetNavigationBounds(detailCanvas.VisibleImageBounds);
        overviewCanvas.NavigateRequested += detailCanvas.CenterOn;
        detailCanvas.CenterOn(new PointF(400, 300));
        Check(detailCanvas.Zoom == 1 && detailCanvas.ScreenBounds(new Rectangle(300, 250, 30, 20)).Size == new SizeF(30, 20), "100 percent maps original pixels one to one");
        overviewCanvas.ClickAt(150, 125);
        Check(detailCanvas.ImageCenter == new PointF(300, 250), "Overview click centers detail on image coordinate");
        // Frame spans x50..250/y50..200; grab away from its center and preserve the grab offset.
        overviewCanvas.DragFrom(100, 100, 40, 20);
        Check(detailCanvas.ImageCenter == new PointF(380, 290), "Overview frame drag follows without grab offset jump");
        detailCanvas.PanBy(20, 10);
        Check(detailCanvas.ImageCenter == new PointF(360, 280) && detailCanvas.VisibleImageBounds == new RectangleF(160, 130, 400, 300), "Detail pan updates source viewport bounds");
        detailCanvas.CenterOn(new PointF(-1000, -1000));
        Check(detailCanvas.VisibleImageBounds == new RectangleF(0, 0, 400, 300), "Top and left pan limits prevent empty margins");
        detailCanvas.CenterOn(new PointF(9000, 9000));
        Check(detailCanvas.VisibleImageBounds == new RectangleF(400, 300, 400, 300), "Bottom and right pan limits prevent empty margins");
        detailCanvas.SetZoom(.25);
        var smallImage = detailCanvas.ScreenBounds(new Rectangle(0, 0, 800, 600));
        Check(smallImage == new RectangleF(100, 75, 200, 150) && detailCanvas.ImageCenter == new PointF(400, 300), "Small image centered when viewport exceeds image size");
        detailCanvas.SetZoom(1);
        detailCanvas.CenterOn(new PointF(400, 300));
        var marks = new[] { new Detection("04", new Rectangle(300, 250, 30, 20), 90, "a"), new Detection("4", new Rectangle(450, 250, 30, 20), 90, "b") };
        foreach (var c in new[] { overviewCanvas, detailCanvas }) { c.SetHighlights(marks); c.SetSelections(new[] { marks[0] }); }
        using var detailPaint = detailCanvas.Render();
        using var overviewPaint = overviewCanvas.Render();
        Check(BlueNear(detailPaint, 100, 100) && RedNear(detailPaint, 250, 100) && BlueNear(overviewPaint, 150, 125) && RedNear(overviewPaint, 225, 125), "Both panes paint identical records with blue priority and red remainder");
        detailCanvas.SetSelections(marks);
        using var multiPaint = detailCanvas.Render();
        Check(BlueNear(multiPaint, 100, 100) && BlueNear(multiPaint, 250, 100), "Multiple selected records are simultaneously blue");
        detailCanvas.SetZoom(2);
        Check(detailCanvas.ImageCenter == new PointF(400, 300) && detailCanvas.VisibleImageBounds == new RectangleF(300, 225, 200, 150), "Zoom updates navigation bounds and retains center");
        overviewCanvas.Size = new Size(600, 300);
        Check(overviewCanvas.ScreenBounds(new Rectangle(0, 0, 800, 600)) == new RectangleF(100, 0, 400, 300), "Overview letterboxes and fits on resize");
        using var png = new Bitmap(120, 80); png.Save(Path.Combine(temp, "mismatch.png"));
        ReverseLookupChecks();
        Phase5Checks.Run(root, Check);
        File.WriteAllText(Path.Combine(temp, "broken.jpg"), "not an image");
        Console.WriteLine($"PASS: {checks} checks");
    }

    private static bool IsRed(Color c) => c.R > 200 && c.G < 80 && c.B < 80;
    private static void ReverseLookupChecks()
    {
        using var detail = new TestCanvas { Size = new Size(400, 300), Mode = ImageViewMode.Detail };
        detail.SetImage(new Bitmap(800, 600));
        var records = new[] { new Detection("wrong", new Rectangle(300, 250, 30, 20), 90, "a"), new Detection("wrong", new Rectangle(450, 250, 30, 20), 90, "b") };
        detail.SetDetections(records);
        Detection? clicked = null; bool added = false; int hits = 0;
        detail.DetectionClicked += (d, add) => { clicked = d; added = add; hits++; };
        var center = detail.ImageCenter;
        detail.ClickAt(110, 110);
        Check(ReferenceEquals(clicked, records[0]) && detail.ImageCenter == center, "Detail click finds record regardless of OCR text, without pan");
        detail.ControlHeld = true;
        detail.ClickAt(250, 110);
        Check(ReferenceEquals(clicked, records[1]) && added, "Ctrl image click signals additive selection for duplicate text");
        detail.ClickAt(30, 30);
        Check(hits == 2, "Empty detail click does not signal selection");
        detail.ControlHeld = false;
        detail.DragFrom(110, 110, 3, 4);
        Check(hits == 3 && detail.ImageCenter == center, "Exactly five pixels remains a click and does not pan");
        detail.Down(110, 110); detail.MovePointerTo(130, 110); detail.MovePointerTo(110, 110); detail.Up(110, 110);
        Check(hits == 3 && detail.ImageCenter == center, "Move out then back remains drag, no reverse lookup");
        detail.Down(110, 110); detail.Up(120, 110);
        Check(hits == 3, "MouseUp displacement also rejects a click without MouseMove");
        detail.Down(110, 110); detail.LoseCapture(); detail.Up(110, 110);
        Check(hits == 3, "Capture loss cancels pending click");
        Check(ReferenceEquals(detail.HitTestDetection(new Point(96, 110)), records[0]) && detail.HitTestDetection(new Point(94, 110)) == null, "Detail hit halo uses screen-space five pixels");
        detail.SetZoom(2);
        var box = detail.ScreenBounds(records[0].Bounds);
        Check(ReferenceEquals(detail.HitTestDetection(new Point((int)box.X + 5, (int)box.Y + 5)), records[0]), "Hit testing follows zoom and pan transform");
        detail.SetImage(new Bitmap(800, 600));
        Check(detail.HitTestDetection(new Point(110, 110)) == null, "Image replacement clears stale hit records");

        using var overview = new TestCanvas { Size = new Size(400, 300), Mode = ImageViewMode.Overview };
        overview.SetImage(new Bitmap(800, 600)); overview.SetDetections(records);
        PointF destination = PointF.Empty; int overviewHits = 0;
        overview.DetectionClicked += (_, _) => overviewHits++;
        overview.NavigateRequested += p => destination = p;
        overview.ClickAt(153, 128);
        Check(overviewHits == 1 && destination == new PointF(315, 260), "Overview OCR click navigates to bbox center rather than click point");
        overview.ClickAt(40, 40);
        Check(overviewHits == 1 && destination == new PointF(80, 80), "Overview miss keeps ordinary point navigation");
        overview.SetNavigationBounds(new RectangleF(200, 150, 400, 300));
        overview.DragFrom(153, 128, 40, 20);
        Check(overviewHits == 1, "Dragging from OCR box inside navigation frame never selects");
        var dense = new[] { new Detection("1", new Rectangle(300, 250, 10, 10), 90, "x"), new Detection("2", new Rectangle(304, 250, 10, 10), 90, "y") };
        overview.SetDetections(dense);
        Check(overview.HitTestDetection(new Point(154, 127)) == null, "Ambiguous overlapping Fit labels are not selected");
        Check(overview.HitTestDetection(new Point(146, 127)) == null, "Fit hit expansion stays small for tiny labels");
        detail.SetDetections(new[] { new Detection("1", new Rectangle(300, 250, 30, 20), 90, "inside"), new Detection("2", new Rectangle(328, 250, 2, 20), 90, "near") });
        Check(detail.HitTestDetection(new Point(126,110))?.Id == "inside", "Actual bbox containment beats a nearer halo candidate");
    }
    private static void Key(Control control, Keys key) => typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, new object[] { new KeyEventArgs(key) });
    private static bool RedNear(Bitmap image, int x, int y) => Enumerable.Range(-2, 5).Any(dx => Enumerable.Range(-2, 5).Any(dy => IsRed(image.GetPixel(x + dx, y + dy))));
    private static bool BlueNear(Bitmap image, int x, int y) => Enumerable.Range(-2, 5).Any(dx => Enumerable.Range(-2, 5).Any(dy => { var c = image.GetPixel(x + dx, y + dy); return c.B > 200 && c.R < 80 && c.G < 80; }));

    private sealed class TestCanvas : ImageViewControl
    {
        public bool ControlHeld;
        protected override bool AddToSelection => ControlHeld;
        public void Down(int x,int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));
        public void MovePointerTo(int x,int y) => OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));
        public void Up(int x,int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));
        public void LoseCapture() => Capture = false;
        public Bitmap Render() { var b = new Bitmap(Width, Height); using var g = Graphics.FromImage(b); g.Clear(BackColor); OnPaint(new PaintEventArgs(g, ClientRectangle)); return b; }
        public void Wheel(int x, int y, int delta) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, x, y, delta));
        public void PanBy(int dx, int dy)
            => DragFrom(100, 100, dx, dy);
        public void ClickAt(int x, int y)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        }
        public void DragFrom(int x, int y, int dx, int dy)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x + dx, y + dy, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x + dx, y + dy, 0));
        }
    }
}
