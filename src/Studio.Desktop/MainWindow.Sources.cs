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

    List<StudioSourceDocument> SourceDependencies()=>sourceDependencies.Values.Where(s=>!s.SourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase)).ToList();
    void LoadSourceDependencies(StudioProject project,RapleAdapter scoped)
    {
        sourceDependencies.Clear();sourceUndo.Clear();sourceRedo.Clear();sourceDrafts.Clear();displayedSourceFile=null;
        foreach(var source in project.AdditionalSources)sourceDependencies.Add(source.SourceFile,source);
        adapter=scoped;
    }
    void RebindSourceWorkspace()
    {
        if(sourceDependencies.Count==0&&!adapter.HasAdditionalSources)return;
        adapter=adapter.ForProject(Snapshot());
        document.RebindSchema(adapter);
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
        displayedSource=primary?document.Source:sourceDependencies[choice.Path].Source;
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
            document.ReplaceSource(SourceEditor.Text,d=>adapter.Validate(d,selectedEntity!.Id));
            sourceDrafts.Remove(selectedEntity!.SourceFile);displayedSource=SourceEditor.Text;Changed();return;
        }
        var previous=sourceDependencies[displayedSourceFile];
        if(previous.Source==SourceEditor.Text)return;
        // Cross-file caller ownership is not complete yet. Do not bypass existing caller locks.
        if(document.LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter before changing a shared source file; cross-file lock ownership is still pending.");
        var candidate=previous with{Source=SourceEditor.Text};var scoped=ValidateSourceWorkspace(WithDependency(candidate));
        var dependency=new BehaviorDocument(previous.Source,scoped,previous.Identities,previous.SourceFile);
        dependency.LockedNodes.UnionWith(previous.LockedNodes);
        dependency.ReplaceSource(candidate.Source);
        candidate=candidate with{Identities=dependency.Identities};
        if(!sourceUndo.TryGetValue(previous.SourceFile,out var history))sourceUndo[previous.SourceFile]=history=new();
        history.Push(previous);sourceRedo.Remove(previous.SourceFile);
        sourceDependencies[previous.SourceFile]=candidate;sourceDrafts.Remove(previous.SourceFile);displayedSource=candidate.Source;scenarioRevision++;Changed();
    }
    bool UndoSourceBuffer(bool redo)
    {
        if(displayedSourceFile==null||displayedSourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))return false;
        var from=redo?sourceRedo:sourceUndo;var to=redo?sourceUndo:sourceRedo;
        if(!from.TryGetValue(displayedSourceFile,out var history)||history.Count==0)return true;
        var candidate=history.Peek();ValidateSourceWorkspace(WithDependency(candidate));
        if(document.LockedNodes.Count!=0||sourceDependencies[displayedSourceFile].LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter and shared source before restoring this edit.");
        if(!to.TryGetValue(displayedSourceFile,out var other))to[displayedSourceFile]=other=new();
        other.Push(sourceDependencies[displayedSourceFile]);history.Pop();sourceDependencies[displayedSourceFile]=candidate;
        sourceDrafts.Remove(displayedSourceFile);displayedSource=candidate.Source;SourceEditor.Text=candidate.Source;scenarioRevision++;Changed();return true;
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
