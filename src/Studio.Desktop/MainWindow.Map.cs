using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    NativeMapImage? mapImage;
    NativeMapInspection? mapInspection;
    Task<NativeMapInspection>? mapInspectionTask;
    string? mapPath;bool mapDirty;
    long mapRevision;
    readonly List<NativeMapImage> mapUndo=[],mapRedo=[];
    ComboBox mapMode=null!,mapPalette=null!;
    TextBox mapSearch=null!,mapNumber=null!;
    bool mapBusy;
    Task? mapSaveTask;
    Task? mapRecoveryTask;
    long recoveredMapRevision=-1;
    bool mapClosePending,mapCloseReady;
    string MapRecoveryPath()=>arguments.Contains("--smoke")&&arguments.Contains("--verify-map-close")
        ?Path.GetFullPath(".artifacts/desktop/map-close-recovery.wmap"):Path.Combine(recoveryDir,"Maps","draft.wmap");
    void InitializeMapControls()
    {
        MapCanvas.Selected+=SelectMapTile;MapCanvas.Stroke+=points=>Try(()=>PaintMap(points));
        mapMode=new(){Width=120,ItemsSource=new[]{"Inspect","Paint ground","Paint object","Erase object","Paint elevation","Paint region","Paint terrain"},SelectedIndex=0};MapTools.Children.Add(mapMode);
        mapSearch=new(){Width=145,Tag="Find XML resource",Margin=new(6,0,6,0)};MapTools.Children.Add(mapSearch);
        mapPalette=new(){Width=230,MaxDropDownHeight=320};MapTools.Children.Add(mapPalette);
        mapPalette.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));VirtualizingPanel.SetIsVirtualizing(mapPalette,true);ScrollViewer.SetCanContentScroll(mapPalette,true);
        var template=new DataTemplate(typeof(ArenaChoice));var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetBinding(ToolTipProperty,new Binding("Summary"));
        var image=new FrameworkElementFactory(typeof(Image));image.SetBinding(Image.SourceProperty,new Binding("Image"));image.SetValue(WidthProperty,20d);image.SetValue(HeightProperty,20d);image.SetValue(MarginProperty,new Thickness(0,0,8,0));image.SetValue(System.Windows.Media.RenderOptions.BitmapScalingModeProperty,System.Windows.Media.BitmapScalingMode.NearestNeighbor);row.AppendChild(image);
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("Id"));text.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(text);template.VisualTree=row;mapPalette.ItemTemplate=template;
        mapNumber=new(){Width=62,Text="0",ToolTip="Native byte value · 0–255",Margin=new(6,0,0,0)};MapTools.Children.Add(mapNumber);
        mapMode.SelectionChanged+=(_,_)=>{RefreshMapPalette();RefreshMapInspector();};mapSearch.TextChanged+=(_,_)=>RefreshMapPalette();
        WorkspaceTabs.SelectionChanged+=(_,e)=>{if(!ReferenceEquals(e.Source,WorkspaceTabs)||!initialized)return;if(MapWorkspace.IsSelected)RefreshMapInspector();else if(ParametersTab.Header?.ToString()=="Map tile"){ParametersTab.Header="Parameters";if(selectedNode!=null)SelectNode(selectedNode.Id);}};
        Closing+=async(_,e)=>
        {
            if(arguments.Contains("--smoke")&&!arguments.Contains("--verify-map-close")||!mapDirty||mapCloseReady)return;e.Cancel=true;if(mapClosePending)return;mapClosePending=true;
            IsEnabled=false;
            try{await FlushMapRecovery();mapCloseReady=true;Dispatcher.BeginInvoke(new Action(Close));}catch(Exception error){mapClosePending=false;IsEnabled=true;Status.Text="Map recovery could not be saved: "+error.GetBaseException().Message;}
        };
    }
    void BeginMapRecovery()
    {if(!arguments.Contains("--smoke")&&mapDirty&&mapImage!=null&&recoveredMapRevision!=mapRevision&&mapRecoveryTask?.IsCompleted!=false)_=RecoverMapSafely();}
    async Task RecoverMapSafely(){try{await FlushMapRecovery();}catch(Exception error){Status.Text="Map recovery: "+error.GetBaseException().Message;}}
    async Task FlushMapRecovery(string? destination=null)
    {
        if(mapRecoveryTask!=null){try{await mapRecoveryTask;}catch{mapRecoveryTask=null;throw;}}
        if(!mapDirty||mapImage==null||recoveredMapRevision==mapRevision)return;var image=mapImage;var revision=mapRevision;
        mapRecoveryTask=Task.Run(()=>NativeMapFile.Save(destination??MapRecoveryPath(),image));await mapRecoveryTask;if(mapRevision==revision)recoveredMapRevision=revision;
    }
    void RefreshMapPalette()
    {
        if(adapter==null||sprites==null)return;MapCanvas.Sprites=sprites;MapCanvas.Painting=mapMode.SelectedIndex>0;
        IEnumerable<ArenaChoice> choices=mapMode.SelectedIndex switch
        {1=>NativeArena.Grounds().Select(g=>new ArenaChoice(g.Id,g.Summary,Ground:g.Type)),2=>EntityResources.Catalog(EntityResourceScope.Resolvable).Select(e=>new ArenaChoice(e.Id,e.Summary,SpriteId:e.SpriteId)),_=>[]};
        var previous=(mapPalette.SelectedItem as ArenaChoice)?.Id;var filtered=choices.Where(c=>c.Id.Contains(mapSearch.Text,StringComparison.OrdinalIgnoreCase)).Take(200).ToArray();
        mapPalette.ItemsSource=filtered;mapPalette.SelectedItem=filtered.FirstOrDefault(c=>c.Id==previous)??filtered.FirstOrDefault();mapPalette.IsEnabled=mapMode.SelectedIndex is 1 or 2;mapSearch.IsEnabled=mapPalette.IsEnabled;
        mapPalette.Visibility=mapSearch.Visibility=mapPalette.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        mapNumber.Visibility=mapMode.SelectedIndex>=4?Visibility.Visible:Visibility.Collapsed;
    }
    void SetMapImage(NativeMapImage image,string? path=null)
    {
        image.Validate();mapImage=image;mapInspection=null;mapInspectionTask=null;mapPath=path;mapDirty=path==null;mapRevision++;mapUndo.Clear();mapRedo.Clear();MapCanvas.Model=image;MapCanvas.Sprites=sprites;MapCanvas.Selection=null;
        MapHeading.Text=path==null?"Native map":Path.GetFileName(path);MapInfo.Text=$"{image.Width} × {image.Height} · source format {image.SourceVersion} · native export format 2";
        WorkspaceTabs.SelectedItem=MapWorkspace;RefreshMapPalette();RefreshMapInspector();Dispatcher.BeginInvoke(new Action(MapCanvas.Fit));
    }
    bool PrivateMapTarget(string path)
    {var full=Path.GetFullPath(path);return new[]{adapter.ServerRoot,sprites.ClientRoot}.Any(root=>full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));}
    async void OpenMapFile(object sender,RoutedEventArgs e)=>await TryRuntime(async()=>
    {
        var dialog=new OpenFileDialog{Filter="Native game map|*.wmap",InitialDirectory=File.Exists(MapRecoveryPath())?Path.GetDirectoryName(MapRecoveryPath()):null};if(dialog.ShowDialog()!=true)return;
        var revision=mapRevision;var loaded=await Task.Run(()=>NativeMapFile.Load(dialog.FileName));if(revision!=mapRevision)throw new InvalidOperationException("The map changed while loading. Open the file again.");
        await FlushMapRecovery();if(revision!=mapRevision)throw new InvalidOperationException("The map changed while recovering. Open the file again.");
        SetMapImage(loaded,PrivateMapTarget(dialog.FileName)?null:dialog.FileName);Status.Text="Opened native map · full dimensions and object configuration retained";
    });
    async void SaveMapFile(object sender,RoutedEventArgs e)=>await TryRuntime(async()=>
    {
        if(mapImage==null||mapBusy)return;var image=mapImage;var revision=mapRevision;var path=mapPath;
        if(path==null){var dialog=new SaveFileDialog{Filter="Native game map|*.wmap",FileName="encounter.wmap"};if(dialog.ShowDialog()!=true)return;path=dialog.FileName;}
        if(PrivateMapTarget(path))throw new ArgumentException("Save a map copy outside the supplied game checkouts.");
        mapBusy=true;MapSave.IsEnabled=false;Status.Text="Saving native map…";
        try{mapSaveTask=Task.Run(()=>NativeMapFile.Save(path,image));await mapSaveTask;if(revision==mapRevision){mapPath=path;mapDirty=false;MapHeading.Text=Path.GetFileName(path);}Status.Text=revision==mapRevision?"Saved native map":"Saved the previous map revision; current edits still need saving";}
        finally{mapBusy=false;MapSave.IsEnabled=true;}
    });
    async void ArenaToMap(object sender,RoutedEventArgs e)=>await TryRuntime(async()=>
    {
        var image=NativeMapFile.FromArena(arenaPreset,selectedEntity!.Id);var revision=mapRevision;
        await FlushMapRecovery();if(revision!=mapRevision)throw new InvalidOperationException("The map changed while recovering. Bring in the arena again.");SetMapImage(image);
    });
    void MapToArena(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(mapImage==null||MapCanvas.Selection is not {} point)throw new ArgumentException("Select the current encounter enemy's tile in the map first.");
        var arena=NativeMapFile.ToArena(mapImage,selectedEntity!.Id,(int)point.X,(int)point.Y);
        CommitArena(ArenaState() with {Arena=arena,Size=128});ClearViewportEditors();WorkspaceTabs.SelectedIndex=0;Status.Text="Map transferred · native configurations will apply on Play";
    });
    void FitMap(object sender,RoutedEventArgs e)=>MapCanvas.Fit();
    void FocusMapTile(object sender,RoutedEventArgs e)=>MapCanvas.FocusSelection();
    void CommitMap(NativeMapImage image)
    {
        if(mapImage==null||ReferenceEquals(image,mapImage))return;image.Validate();mapUndo.Add(mapImage);if(mapUndo.Count>16)mapUndo.RemoveAt(0);mapRedo.Clear();ApplyMapImage(image);
    }
    void ApplyMapImage(NativeMapImage image)
    {mapImage=image;mapInspection=null;mapInspectionTask=null;mapRevision++;mapDirty=true;MapCanvas.Model=image;MapHeading.Text=(mapPath==null?"Native map":Path.GetFileName(mapPath))+" · edited";RefreshMapInspector();}
    void UndoMap(){if(mapImage!=null&&mapUndo.Count>0){mapRedo.Add(mapImage);var last=mapUndo[^1];mapUndo.RemoveAt(mapUndo.Count-1);ApplyMapImage(last);}}
    void RedoMap(){if(mapImage!=null&&mapRedo.Count>0){mapUndo.Add(mapImage);var last=mapRedo[^1];mapRedo.RemoveAt(mapRedo.Count-1);ApplyMapImage(last);}}
    void PaintMap(Point[] points)
    {
        if(mapImage==null)return;var mode=mapMode.SelectedIndex;var choice=(mapPalette.SelectedItem as ArenaChoice)?.Id;var value=mode>=4?byte.Parse(mapNumber.Text,CultureInfo.InvariantCulture):(byte)0;
        CommitMap(mapImage.Edit(points.Select(point=>
        {
            var x=(int)point.X;var y=(int)point.Y;var cell=mapImage[x,y];cell=mode switch{1 when choice!=null=>cell with {Ground=NativeArena.GroundType(choice)},2 when choice!=null=>cell with {ObjectId=choice,Config=""},3=>cell with {ObjectId="",Config=""},4=>cell with {Elevation=value},5=>cell with {Region=value},6=>cell with {Terrain=value},_=>cell};return(x,y,cell);
        })));Status.Text="Native map stroke applied · one Undo";
    }
    void SelectMapTile(int x,int y){MapCanvas.Selection=new(x,y);RefreshMapInspector();}
    async void RefreshMapInspector()
    {
        if(!MapWorkspace.IsSelected)return;ParametersTab.Header="Map tile";Inspector.Children.Clear();
        if(mapImage==null||MapCanvas.Selection is not {} point){Inspector.Children.Add(new TextBlock{Text="Select a map tile",FontSize=21,Margin=new(0,0,0,12)});Inspector.Children.Add(new TextBlock{Text="Open a .wmap or bring in the arena. Inspect and paint preserve native map dimensions and object data.",TextWrapping=TextWrapping.Wrap,Foreground=(System.Windows.Media.Brush)FindResource("Muted")});return;}
        var image=mapImage;var revision=mapRevision;var x=(int)point.X;var y=(int)point.Y;var cell=image[x,y];
        Inspector.Children.Add(new TextBlock{Text=$"Tile {x}, {y}",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        TextBox Field(string name,string value,bool multiline=false){Inspector.Children.Add(new TextBlock{Text=name,Margin=new(0,10,0,6)});var input=new TextBox{Text=value,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,AcceptsReturn=multiline,MinHeight=multiline?72:0};Inspector.Children.Add(input);return input;}
        var ground=Field("Ground XML / native type",NativeMapFile.GroundId(cell.Ground));var objectId=Field("Object XML",cell.ObjectId);var config=Field("Native configuration",cell.Config,true);
        config.ToolTip="Passed unchanged to native Wmap. Examples: hp:80;name:Captain;size:150; Native entity/world prerequisites still apply.";
        var elevation=Field("Elevation · 0–255",cell.Elevation.ToString());var terrain=Field("Terrain · native byte",cell.Terrain.ToString());terrain.ToolTip=string.Join("\n",NativeMapFile.Terrains().Select(v=>$"{v.Value}: {v.Name}"));
        var region=Field("Region · native byte",cell.Region.ToString());region.ToolTip=string.Join("\n",NativeMapFile.Regions().Select(v=>$"{v.Value}: {v.Name}"));
        var apply=new Button{Content="Apply tile",Margin=new(0,14,0,12)};Inspector.Children.Add(apply);
        apply.Click+=(_,_)=>Try(()=>{if(mapImage!=image||mapRevision!=revision)throw new InvalidOperationException("The map changed; select its tile again.");CommitMap(image.Edit([(x,y,new(NativeArena.GroundType(ground.Text),objectId.Text,config.Text,byte.Parse(terrain.Text),byte.Parse(region.Text),byte.Parse(elevation.Text)))]));});
        var nativeInfo=new TextBlock{Text="Reading native tile rules…",TextWrapping=TextWrapping.Wrap,Foreground=(System.Windows.Media.Brush)FindResource("Muted"),FontSize=11};Inspector.Children.Add(nativeInfo);
        if(mapMode.SelectedIndex>0){nativeInfo.Text="Choose Inspect to read native walking and spawning rules.";return;}
        try
        {
            var inspection=mapInspection??await (mapInspectionTask??=Task.Run(()=>new NativeMapInspection(image)));
            if(mapImage!=image||revision!=mapRevision||MapCanvas.Selection!=point||!MapWorkspace.IsSelected)return;mapInspection=inspection;var probe=inspection.Probe(x,y);
            nativeInfo.Text=$"{probe.ObjectKind} · ground 0x{probe.EffectiveGround:X4}\nNative tile walking: {(probe.Passable?"passable":"blocked")} · spawning: {(probe.Spawnable?"passable":"blocked")}"+(probe.NativeConfig!=cell.Config?"\nNative connection data: "+probe.NativeConfig:"");
        }
        catch(Exception error){if(mapImage==image&&mapRevision==revision)nativeInfo.Text=error.GetBaseException().Message;}
    }
}
