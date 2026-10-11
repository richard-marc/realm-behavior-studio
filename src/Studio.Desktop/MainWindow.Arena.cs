using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    ArenaPreset arenaPreset=new();
    long scenarioRevision;
    record ArenaEditState(ArenaPreset Arena,double PlayerX,double PlayerY,int Size);
    record ArenaChoice(string Id,string Summary,string? SpriteId=null,ushort? Ground=null)
    {
        public BitmapSource? Image=>Ground is {} type?ClientSprites.Current?.GetGround(type):ClientSprites.Current?.Get(SpriteId!);
    }
    readonly Stack<ArenaEditState> arenaUndo=[],arenaRedo=[];
    ComboBox arenaMode=null!,arenaChoices=null!,arenaShape=null!,arenaSizeChoice=null!;
    TextBox arenaSearch=null!;
    TextBlock arenaHint=null!;
    Button arenaFill=null!;
    bool syncingArena;
    ArenaEditState ArenaState()=>new(arenaPreset,double.Parse(PlayerStartX.Text,CultureInfo.InvariantCulture),double.Parse(PlayerStartY.Text,CultureInfo.InvariantCulture),int.Parse(ArenaSize.Text));
    void InitializeArenaControls()
    {
        var row=new WrapPanel();ArenaControls.Children.Add(row);
        arenaMode=new(){Width=112,ItemsSource=new[]{"Ground","Obstacle","Erase","Enemy start","Player start","Add enemy"},SelectedIndex=0,ToolTip="Paint or place spawns"};row.Children.Add(arenaMode);
        arenaSearch=new(){Width=120,Tag="Find resource",Margin=new(6,0,6,0),ToolTip="Search native XML resources"};row.Children.Add(arenaSearch);
        arenaChoices=new(){Width=205,MaxDropDownHeight=320,ToolTip="Actual game XML and client sprites"};row.Children.Add(arenaChoices);
        arenaChoices.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));VirtualizingPanel.SetIsVirtualizing(arenaChoices,true);ScrollViewer.SetCanContentScroll(arenaChoices,true);
        var template=new DataTemplate(typeof(ArenaChoice));
        var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);panel.SetBinding(ToolTipProperty,new System.Windows.Data.Binding("Summary"));
        var image=new FrameworkElementFactory(typeof(Image));image.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Image"));image.SetValue(WidthProperty,20d);image.SetValue(HeightProperty,20d);image.SetValue(MarginProperty,new Thickness(0,0,8,0));image.SetValue(System.Windows.Media.RenderOptions.BitmapScalingModeProperty,System.Windows.Media.BitmapScalingMode.NearestNeighbor);panel.AppendChild(image);
        var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Id"));label.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);panel.AppendChild(label);template.VisualTree=panel;arenaChoices.ItemTemplate=template;
        arenaShape=new(){Width=90,ItemsSource=new[]{"Square","Circle"},SelectedIndex=0,Margin=new(6,0,0,0),ToolTip="Native prototype arena bounds"};row.Children.Add(arenaShape);
        arenaSizeChoice=new(){Width=68,ItemsSource=new[]{32,48,64,96,128},SelectedItem=48,Margin=new(6,0,0,0),ToolTip="Arena size in tiles"};row.Children.Add(arenaSizeChoice);
        Button Action(string text,Action execute){var b=new Button{Content=text,Style=(Style)FindResource("QuietButton"),Margin=new(6,0,0,0)};b.Click+=(_,_)=>Try(execute);row.Children.Add(b);return b;}
        Action("Undo",UndoArena);Action("Redo",RedoArena);arenaFill=Action("Fill floor",()=>{if(arenaChoices.SelectedItem is ArenaChoice choice&&arenaMode.SelectedIndex==0)CommitArena(ArenaState() with {Arena=arenaPreset with {GroundId=choice.Id,Tiles=arenaPreset.Tiles.Select(t=>t with {GroundId=choice.Id}).ToArray()}});});
        arenaHint=new(){FontSize=11,Foreground=(System.Windows.Media.Brush)FindResource("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new(2,8,2,0)};ArenaControls.Children.Add(arenaHint);
        arenaMode.SelectionChanged+=(_,_)=>RefreshArenaChoices();arenaSearch.TextChanged+=(_,_)=>RefreshArenaChoices();arenaChoices.SelectionChanged+=(_,_)=>DescribeArenaChoice();
        arenaShape.SelectionChanged+=(_,_)=>{if(!syncingArena&&Viewport.ArenaEditing)Try(()=>CommitArena(ArenaState() with {Arena=arenaPreset with {Shape=(string)arenaShape.SelectedItem}}));};
        arenaSizeChoice.SelectionChanged+=(_,_)=>{if(!syncingArena&&Viewport.ArenaEditing&&arenaSizeChoice.SelectedItem is int size)Try(()=>CommitArena(ArenaState() with {Size=size}));};
        Viewport.ArenaStroke+=points=>Try(()=>PaintArena(points));
    }
    async void ToggleArenaEditor(object sender,RoutedEventArgs e)=>await TryRuntime(async()=>
    {
        if(Viewport.ArenaEditing){ClearArenaEditor();return;}
        await AwaitCombatCancellation();ClearViewportEditors();Stop(this,new());if(pathPreviewTask!=null)await pathPreviewTask;
        Viewport.ArenaEditing=true;ArenaControls.Visibility=Visibility.Visible;ArenaEditButton.Content="Done editing";ArenaSize.IsReadOnly=true;PlayerStartX.IsReadOnly=true;PlayerStartY.IsReadOnly=true;WorkspaceTabs.SelectedIndex=0;SyncArenaDraft();RefreshArenaChoices();Viewport.Focus();
    });
    void ClearArenaEditor()
    {Viewport.ArenaEditing=false;Viewport.CancelArenaPainting();ArenaSize.IsReadOnly=false;PlayerStartX.IsReadOnly=false;PlayerStartY.IsReadOnly=false;ArenaControls.Visibility=Visibility.Collapsed;ArenaEditButton.Content="Edit arena";Viewport.InvalidateVisual();}
    void SyncArenaDraft()
    {
        var state=ArenaState();Viewport.ArenaDraft=state.Arena;Viewport.ArenaDraftSize=state.Size;Viewport.ArenaPlayerStart=new(state.PlayerX,state.PlayerY);
        syncingArena=true;arenaShape.SelectedItem=state.Arena.Shape;arenaSizeChoice.SelectedItem=state.Size;syncingArena=false;Viewport.InvalidateVisual();DescribeArenaChoice();
    }
    void RefreshArenaChoices()
    {
        if(adapter==null||!Viewport.ArenaEditing)return;
        var selected=(arenaChoices.SelectedItem as ArenaChoice)?.Id;var search=arenaSearch.Text;
        IEnumerable<ArenaChoice> choices=arenaMode.SelectedIndex switch
        {
            0=>NativeArena.Grounds().Select(g=>new ArenaChoice(g.Id,g.Summary,Ground:g.Type)),
            1=>NativeArena.Obstacles().Select(o=>new ArenaChoice(o.Id,o.Summary,SpriteId:o.SpriteId)),
            5=>EntityResources.Catalog(EntityResourceScope.Enemy).Select(a=>new ArenaChoice(a.Id,a.Summary,SpriteId:a.SpriteId)),
            _=>[]
        };
        // Resolve sprites only for the filtered palette, never for the whole resource catalog.
        var filtered=choices.Where(c=>c.Id.Contains(search,StringComparison.OrdinalIgnoreCase)).Take(200).ToArray();
        arenaChoices.ItemsSource=filtered;arenaChoices.SelectedItem=filtered.FirstOrDefault(c=>c.Id==selected)??filtered.FirstOrDefault();
        arenaChoices.IsEnabled=arenaMode.SelectedIndex is 0 or 1 or 5;arenaSearch.IsEnabled=arenaChoices.IsEnabled;DescribeArenaChoice();
    }
    void DescribeArenaChoice()
    {
        if(arenaHint==null)return;
        if(arenaFill!=null)arenaFill.IsEnabled=arenaMode.SelectedIndex==0&&arenaChoices.SelectedItem!=null;
        arenaHint.Text=(arenaChoices.SelectedItem as ArenaChoice)?.Summary??"Click a tile to place a spawn. Erase clears its obstacle and additional enemies.";
        arenaHint.Text+=$" · {arenaPreset.Tiles.Count} edited tiles · {arenaPreset.Actors.Count} extra enemies";
        if(arenaChoices.Items.Count==200)arenaHint.Text+=" · first 200 matches; search to narrow";
        if(NativeArena.Limitations(arenaPreset) is {} limitation)arenaHint.Text+="\n"+limitation;
    }
    void PaintArena(Point[] points)
    {
        if(!Viewport.ArenaEditing)return;
        var before=ArenaState();var next=before;var choice=(arenaChoices.SelectedItem as ArenaChoice)?.Id;
        var tiles=arenaPreset.Tiles.ToDictionary(t=>(t.X,t.Y));var actors=arenaPreset.Actors.ToList();
        if(arenaMode.SelectedIndex is 3 or 4 or 5)points=[points[^1]];
        foreach(var p in points)
        {
            var x=(int)p.X;var y=(int)p.Y;var key=(x,y);var tile=tiles.GetValueOrDefault(key)??new ArenaTile(x,y,arenaPreset.GroundId);
            switch(arenaMode.SelectedIndex)
            {
                case 0 when choice!=null: tiles[key]=tile with {GroundId=choice};break;
                case 1 when choice!=null: tiles[key]=tile with {ObjectId=choice,ObjectConfig=""};break;
                case 2: tiles[key]=tile with {ObjectId=null,ObjectConfig=""};actors.RemoveAll(a=>(int)a.X==x&&(int)a.Y==y);break;
                case 3: next=next with {Arena=next.Arena with {BossX=x+.5,BossY=y+.5}};break;
                case 4: next=next with {PlayerX=x+.5,PlayerY=y+.5};break;
                case 5 when choice!=null: actors.Add(new(choice,x+.5,y+.5));break;
            }
        }
        next=next with {Arena=next.Arena with {Tiles=tiles.Values.Where(t=>t.ObjectId!=null||t.GroundId!=arenaPreset.GroundId||t.Elevation!=0||t.Terrain!=0||t.Region!=0||t.ObjectConfig!="").OrderBy(t=>t.Y).ThenBy(t=>t.X).ToArray(),Actors=actors.ToArray()}};
        CommitArena(next);
    }
    void CommitArena(ArenaEditState state)
    {
        NativeArena.Validate(state.Arena);if(state.Size is <32 or >128)throw new ArgumentException("Arena size must be 32–128 tiles.");
        var before=ArenaState();if(System.Text.Json.JsonSerializer.Serialize(before)==System.Text.Json.JsonSerializer.Serialize(state))return;
        arenaUndo.Push(before);arenaRedo.Clear();ApplyArenaState(state);Status.Text="Arena updated · next Play loads the native map";
    }
    void ApplyArenaState(ArenaEditState state)
    {
        Stop(this,new());ClearPathEditor();ClearCombatPreview();aiCancellation?.Cancel();pendingProposal=null;scenarioRevision++;
        arenaPreset=state.Arena;PlayerStartX.Text=state.PlayerX.ToString("R",CultureInfo.InvariantCulture);PlayerStartY.Text=state.PlayerY.ToString("R",CultureInfo.InvariantCulture);ArenaSize.Text=state.Size.ToString();SyncArenaDraft();
    }
    void UndoArena(){if(arenaUndo.TryPop(out var state)){arenaRedo.Push(ArenaState());ApplyArenaState(state);}}
    void RedoArena(){if(arenaRedo.TryPop(out var state)){arenaUndo.Push(ArenaState());ApplyArenaState(state);}}
}
