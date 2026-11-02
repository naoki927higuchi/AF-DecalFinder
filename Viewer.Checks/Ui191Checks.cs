using AF.DecalFinder;
using System.Reflection;
using System.Text.Json.Nodes;

namespace AF.DecalFinder.Checks;
internal static class Ui191Checks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Hidden)!.GetValue(o)!;
    static int count;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS: "+text);}
    static void Key(OcrResultsList list,Keys key,bool up=false)=>typeof(OcrResultsList).GetMethod(up?"OnKeyUp":"OnKeyDown",Hidden)!.Invoke(list,[new KeyEventArgs(key)]);
    static bool Command(OcrResultsList list,Keys key)=>(bool)typeof(OcrResultsList).GetMethod("ProcessCmdKey",Hidden)!.Invoke(list,[Message.Create(list.Handle,0x100,(IntPtr)(int)key,IntPtr.Zero),key])!;
    static void Cancel(OcrResultsList list){typeof(OcrResultsList).GetMethod("SendMessage",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[list.Handle,0x10B3u,IntPtr.Zero,IntPtr.Zero]);Application.DoEvents();}
    internal static void Run(string root)
    {
        var dir=Path.Combine(root,"viewer-checks","ui191-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"test.png");using(var b=new Bitmap(1600,1200))b.Save(path);
        var store=new EditStore(Path.Combine(dir,"cache"));var settings=new AppSettingsStore(Path.Combine(dir,"Settings.json"));
        var identity=ImageIdentity.Read(path,new(1600,1200));store.Save(identity,new(new(1600,1200),[new("1",new(400,400,100,100),90,"a"),new("2",new(800,600,100,100),90,"b")]));
        Check(!settings.LoadHighlightFill(),"Missing fill setting defaults OFF");
        File.WriteAllText(settings.FilePath,"{\"HighlightFill\":\"true\",\"Other\":42}");
        Check(!settings.LoadHighlightFill(),"Invalid fill setting defaults OFF");
        using(var form=new MainForm(path,store,settings))
        {
            form.Show();Application.DoEvents();var list=Field<OcrResultsList>(form,"list");var view=Field<ImageViewControl>(form,"view");var overview=Field<ImageViewControl>(form,"overview");
            list.SelectDetection("a",false);list.SelectDetection("b",true);list.Focus();Application.DoEvents();
            var center=view.ImageCenter;var zoom=view.Zoom;int changes=0;list.DataChanged+=()=>changes++;
            Check(Command(list,Keys.Space)&&list.CheckedItems.Count==1&&list.Items[0].Checked&&list.SelectedItems.Count==2,"Preprocessed Space changes first selected only");
            Command(list,Keys.Space);Check(changes==1&&list.CheckedItems.Count==1,"Held Space cannot toggle twice");
            Check(center==view.ImageCenter&&zoom==view.Zoom,"Space preserves viewport and selection");
            Check(store.Load(identity)!.Detections.Single(d=>d.Id=="a").IsDone,"Space autosaves done state");
            form.Undo();Check(list.CheckedItems.Count==0&&list.SelectedItems.Count==2,"Space is one undo transaction");
            Key(list,Keys.Space,true);Command(list,Keys.Space);Key(list,Keys.Space,true);Command(list,Keys.Space);Key(list,Keys.Space,true);
            Check(list.CheckedItems.Count==0&&changes==3,"New key presses each toggle once in both directions");
            list.Items[1].Focused=true;Command(list,Keys.Control|Keys.Space);Key(list,Keys.Space,true);
            Check(list.SelectedItems.Count==1&&!list.Items[1].Selected&&list.CheckedItems.Count==0,"Ctrl+Space toggles focused selection without checks");
            list.SelectDetection("b",true);int starts=0,index=-1;list.BeforeLabelEdit+=(_,e)=>{starts++;index=e.Item;};
            Check(Command(list,Keys.Enter)&&list.IsEditing&&starts==1&&index==0,"LV command preprocessing opens one editor for SelectedItems[0]");
            Cancel(list);Check(!list.IsEditing&&list.Focused,"Cancel returns focus to LV");
            list.SelectDetection("a",false);list.SelectDetection("a",true,true);Command(list,Keys.Enter);
            Check(starts==1&&!list.IsEditing,"Enter with no selection is consumed without editing");
            form.SetInventoryMode(true);list.SelectDetection("a",false);Command(list,Keys.Space);Command(list,Keys.Space);
            Check(list.Items.Count==1&&list.SelectedDetections.Single().Id=="b","Inventory Space reviews once and advances");Key(list,Keys.Space,true);
            Command(list,Keys.Control|Keys.Enter);Key(list,Keys.Enter,true);Check(list.Items.Count==0,"Inventory Ctrl+Enter remains bulk review");form.Undo();
            Command(list,Keys.Enter);Check(list.IsEditing&&starts==2,"Inventory Enter still edits");Cancel(list);
            var fill=Field<CheckBox>(form,"highlightFill");int invalidations=0;view.Invalidated+=(_,_)=>invalidations++;overview.Invalidated+=(_,_)=>invalidations++;
            fill.Checked=true;Check(view.HighlightFill&&overview.HighlightFill&&invalidations>=2&&settings.LoadHighlightFill(),"Fill setting instantly updates both views and saves");
            Check(JsonNode.Parse(File.ReadAllText(settings.FilePath))!["Other"]!.GetValue<int>()==42,"Fill save preserves unknown settings");form.Close();
        }
        using(var form=new MainForm(path,store,settings)){form.Show();Application.DoEvents();Check(Field<CheckBox>(form,"highlightFill").Checked&&Field<ImageViewControl>(form,"overview").HighlightFill,"Restart restores fill");form.Close();}
        foreach(var mode in new[]{ImageViewMode.Detail,ImageViewMode.Overview})
        foreach(var scale in new[]{.25,1,2})
        {
            using var canvas=new ImageViewControl{Mode=mode,Size=new((int)(400*scale),(int)(400*scale))};
            using var source=new Bitmap(400,400);using(var g=Graphics.FromImage(source))g.Clear(Color.White);
            canvas.SetImage(source,false);canvas.CreateControl();if(mode==ImageViewMode.Detail)canvas.SetZoom(scale);
            var red=new Detection("1",new(80,80,160,160),90,"red"){IsDone=true};var blue=new Detection("2",new(160,80,160,160),90,"blue");
            canvas.SetDetections([red,blue]);canvas.SetHighlights([red,red]);canvas.SetSelections([blue,blue]);
            Color Pixel(int x,int y){using var output=new Bitmap(canvas.Width,canvas.Height);canvas.DrawToBitmap(output,canvas.ClientRectangle);var box=canvas.ScreenBounds(new Rectangle(x,y,1,1));return output.GetPixel((int)box.X,(int)box.Y);}
            var original=red.Bounds;var ui=red.UiBounds;
            Check(Pixel(120,160).R is >=126 and <=128,$"{mode} {scale}: OFF keeps black done fill");
            canvas.HighlightFill=true;
            var r=Pixel(120,160);var overlap=Pixel(200,160);var b=Pixel(280,160);
            Check(Math.Abs(r.R-172)<=2&&Math.Abs(r.G-82)<=2&&r.G==r.B,$"{mode} {scale}: red alpha90 over black128 exactly once");
            Check(Math.Abs(overlap.B-172)<=2&&Math.Abs(overlap.R-82)<=2&&overlap.R==overlap.G,$"{mode} {scale}: blue excludes red even at partial overlap");
            Check(b.B==255&&Math.Abs(b.R-165)<=1&&b.R==b.G,$"{mode} {scale}: blue alpha90 once without done");
            Check(red.Bounds==original&&red.UiBounds==ui&&canvas.HitTestDetection(Point.Round(canvas.ScreenBounds(ui).Location+new SizeF(2,2)))==red,$"{mode} {scale}: geometry and hit test unchanged");
            foreach(int width in new[]{1,2,3,4})
            {
                canvas.FrameWidthPx=width;
                using var output=new Bitmap(canvas.Width,canvas.Height);canvas.DrawToBitmap(output,canvas.ClientRectangle);
                var box=canvas.ScreenBounds(red.UiBounds);int x=(int)box.Left,y=(int)(box.Top+box.Height/2);
                int measured=Enumerable.Range(x-6,13).Count(px=>{var c=output.GetPixel(px,y);return c.R>240&&c.G<30&&c.B<30;});
                Check(measured==width,$"{mode} {scale}: fill ON keeps {width}px red outline");
            }
        }
        Console.WriteLine($"PASS: {count} final UI checks");
    }
}
