using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    sealed record SourceFileChoice(string Path,string Title)
    {
        public override string ToString()=>Title;
    }
    readonly Dictionary<string,StudioSourceDocument> sourceDependencies=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Stack<StudioSourceDocument>> sourceUndo=new(StringComparer.OrdinalIgnoreCase),sourceRedo=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> sourceDrafts=new(StringComparer.OrdinalIgnoreCase);
    string? displayedSourceFile;
    string displayedSource="";
    bool refreshingSourceFiles;

    List<StudioSourceDocument> SourceDependencies()=>sourceDependencies.Values.Where(s=>!s.SourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))
        .Select(s=>dependencyDocuments.TryGetValue(s.SourceFile,out var d)?s with{Source=d.Source,Identities=d.Identities,LockedNodes=d.LockedNodes.ToList(),CollapsedNodes=collapsedByDocument.GetValueOrDefault(d,[]).ToList()}:s).ToList();
    void LoadSourceDependencies(StudioProject project,RapleAdapter scoped)
    {
        encounterDocument=null;dependencyDocuments.Clear();sourceDependencies.Clear();sourceUndo.Clear();sourceRedo.Clear();sourceDrafts.Clear();displayedSourceFile=null;
        foreach(var source in project.AdditionalSources)sourceDependencies.Add(source.SourceFile,source);
        adapter=scoped;
    }
    void RebindSourceWorkspace()
    {
        if(sourceDependencies.Count==0&&!adapter.HasAdditionalSources)return;
        adapter=adapter.ForProject(Snapshot());
        PrimaryDocument.RebindSchema(adapter);
        if(encounterDocument!=null)document.RebindSchema(adapter);
    }
    void RememberSourceDraft()
    {
        if(displayedSourceFile!=null&&SourceEditor.Text!=displayedSource)sourceDrafts[displayedSourceFile]=SourceEditor.Text;
    }
    void RefreshSourceFiles()
    {
        RememberSourceDraft();refreshingSourceFiles=true;
        var path=displayedSourceFile??selectedEntity!.SourceFile;
        var choices=new[]{selectedEntity!.SourceFile}.Concat(SourceDependencies().Select(s=>s.SourceFile))
            .Select(p=>new SourceFileChoice(p,Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),p))).ToArray();
        SourceFiles.ItemsSource=choices;SourceFiles.SelectedItem=choices.FirstOrDefault(c=>c.Path.Equals(path,StringComparison.OrdinalIgnoreCase))??choices[0];
        refreshingSourceFiles=false;DisplaySourceFile();
    }
    void SourceFileSelected(object sender,SelectionChangedEventArgs e)
    {
        if(refreshingSourceFiles||SourceFiles.SelectedItem==null)return;
        RememberSourceDraft();DisplaySourceFile();
    }
    void ShowEncounterSource()
    {
        WorkspaceTabs.SelectedItem=SourceWorkspace;
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(c=>c.Path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase));
    }
    void DisplaySourceFile()
    {
        if(SourceFiles.SelectedItem is not SourceFileChoice choice)return;
        displayedSourceFile=choice.Path;
        var primary=choice.Path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase);
        displayedSource=primary?PrimaryDocument.Source:SourceDependencies().Single(s=>s.SourceFile.Equals(choice.Path,StringComparison.OrdinalIgnoreCase)).Source;
        SourceEditor.Text=sourceDrafts.GetValueOrDefault(choice.Path,displayedSource);
        SourceFileNotice.Text=primary?"Encounter source · Apply checks all project files.":"Shared file · saved and played with this encounter. Edit shared phases here.";
    }
    void AddSourceFile(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var dialog=new OpenFileDialog{Filter="Server C# source|*.cs",InitialDirectory=Path.Combine(adapter.ServerRoot,"TKR.WorldServer/logic")};
        if(dialog.ShowDialog()==true)AttachSourceFile(dialog.FileName);
    });
    void AttachSourceFile(string path)
    {
        path=Path.GetFullPath(path);
        if(!path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase)&&!sourceDependencies.ContainsKey(path))
        {
            var project=Snapshot();var placeholder=new StudioSourceDocument(path,"");
            SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[..project.AdditionalSources,placeholder]});
            var source=placeholder with{Source=File.ReadAllText(path)};
            ValidateSourceWorkspace(project with{AdditionalSources=[..project.AdditionalSources,source]});
            sourceDependencies.Add(path,source);scenarioRevision++;Changed();
        }
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
        WorkspaceTabs.SelectedItem=SourceWorkspace;
    }
    RapleAdapter ValidateSourceWorkspace(StudioProject candidate)
    {
        var scoped=adapter.ForProject(candidate);
        var primary=new BehaviorDocument(candidate.Source,scoped,candidate.Identities,candidate.SourceFile);
        var errors=adapter.Validate(primary,candidate.EntityId).Where(d=>d.IsError).Select(d=>d.Message).Distinct().ToArray();
        if(errors.Length!=0)throw new ArgumentException(string.Join("\n",errors));
        return scoped;
    }
    StudioProject WithDependency(StudioSourceDocument replacement)=>Snapshot() with{AdditionalSources=SourceDependencies().Select(s=>s.SourceFile.Equals(replacement.SourceFile,StringComparison.OrdinalIgnoreCase)?replacement:s).ToList()};
    void ApplySourceBuffer()
    {
        if(displayedSourceFile==null||displayedSourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))
        {
            PrimaryDocument.ReplaceSource(SourceEditor.Text,d=>adapter.Validate(d,selectedEntity!.Id));
            sourceDrafts.Remove(selectedEntity!.SourceFile);displayedSource=SourceEditor.Text;Changed();return;
        }
        var previous=SourceDependencies().Single(s=>s.SourceFile.Equals(displayedSourceFile,StringComparison.OrdinalIgnoreCase));
        if(previous.Source==SourceEditor.Text)return;
        // Cross-file caller ownership is not complete yet. Do not bypass existing caller locks.
        if(PrimaryDocument.LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter before changing a shared source file; cross-file lock ownership is still pending.");
        var candidate=previous with{Source=SourceEditor.Text};var scoped=ValidateSourceWorkspace(WithDependency(candidate));
        var dependency=new BehaviorDocument(previous.Source,scoped,previous.Identities,previous.SourceFile);
        dependency.LockedNodes.UnionWith(previous.LockedNodes);
        dependency.ReplaceSource(candidate.Source);
        candidate=candidate with{Identities=dependency.Identities};
        if(!sourceUndo.TryGetValue(previous.SourceFile,out var history))sourceUndo[previous.SourceFile]=history=new();
        history.Push(previous);sourceRedo.Remove(previous.SourceFile);
        InstallDependencySource(candidate);sourceDrafts.Remove(previous.SourceFile);displayedSource=candidate.Source;scenarioRevision++;Changed();
    }
    bool UndoSourceBuffer(bool redo)
    {
        var file=SourceWorkspace.IsSelected?displayedSourceFile:encounterDocument!=null?document.SourceFile:displayedSourceFile;
        if(file==null||file.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))return false;
        var from=redo?sourceRedo:sourceUndo;var to=redo?sourceUndo:sourceRedo;
        if(!from.TryGetValue(file,out var history)||history.Count==0)return true;
        var candidate=history.Peek();ValidateSourceWorkspace(WithDependency(candidate));
        if(PrimaryDocument.LockedNodes.Count!=0||SourceDependencies().Single(s=>s.SourceFile.Equals(file,StringComparison.OrdinalIgnoreCase)).LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter and shared source before restoring this edit.");
        if(!to.TryGetValue(file,out var other))to[file]=other=new();
        other.Push(SourceDependencies().Single(s=>s.SourceFile.Equals(file,StringComparison.OrdinalIgnoreCase)));history.Pop();InstallDependencySource(candidate);
        sourceDrafts.Remove(file);if(displayedSourceFile==file){displayedSource=candidate.Source;SourceEditor.Text=candidate.Source;}scenarioRevision++;Changed();return true;
    }
    void InstallDependencySource(StudioSourceDocument source)
    {
        sourceDependencies[source.SourceFile]=source;
        if(!dependencyDocuments.Remove(source.SourceFile,out var previous))return;
        var replacement=new BehaviorDocument(source.Source,adapter.ForProject(WithDependency(source)),source.Identities,source.SourceFile);
        replacement.LockedNodes.UnionWith(source.LockedNodes);collapsedByDocument[replacement]=source.CollapsedNodes.ToHashSet();dependencyDocuments[source.SourceFile]=replacement;
        if(ReferenceEquals(document,previous))document=replacement;
    }
    void ExportSourceWorkspace(string path,StudioProject project,NativeRuntimeStamp stamp)
    {
        if(project.AdditionalSources.Count==0)
        {
            File.WriteAllText(path,project.Source);
            File.WriteAllText(path+".verification.json",JsonSerializer.Serialize(stamp,BehaviorDocument.JsonOptions));return;
        }
        var sources=SourceVerification.SourceSet(adapter.ServerRoot,project);
        using var archive=new ZipArchive(File.Create(path),ZipArchiveMode.Create);
        foreach(var source in sources)
        {
            var entry=archive.CreateEntry(Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),source.Key).Replace('\\','/'));
            using var writer=new StreamWriter(entry.Open());writer.Write(source.Value);
        }
        using var manifest=new StreamWriter(archive.CreateEntry("studio-verification.json").Open());manifest.Write(JsonSerializer.Serialize(stamp,BehaviorDocument.JsonOptions));
    }
}
