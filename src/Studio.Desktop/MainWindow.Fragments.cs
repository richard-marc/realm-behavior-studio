using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Core;

namespace Studio.Desktop;

public partial class MainWindow
{
    string fragmentDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "Fragments");
    public sealed record FragmentEntry(string Path, string Name);
    List<FragmentEntry> fragmentEntries = [];
    BehaviorFragment? fragmentEditing;
    bool fragmentCapturing;
    readonly List<(FragmentParameter Parameter, CheckBox Expose, TextBox Input)> fragmentFields = [];
    readonly Dictionary<string, ComboBox> fragmentPhaseTargets = new(StringComparer.Ordinal);
    BehaviorDocument? fragmentPreviewDocument;
    long fragmentPreviewRevision;
    Guid fragmentPreviewTarget;
    void RefreshFragments()
    {
        fragmentEntries = [];
        if (Directory.Exists(fragmentDirectory)) foreach (var file in Directory.EnumerateFiles(fragmentDirectory, "*.rbsfrag"))
        {
            try { fragmentEntries.Add(new(file, BehaviorFragment.Load(file).Name)); }
            catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException) { fragmentEntries.Add(new(file, Path.GetFileNameWithoutExtension(file) + " · unavailable")); }
        }
        FragmentSearchChanged(this, null!);
    }
    void FragmentSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (FragmentLibrary != null) FragmentLibrary.ItemsSource = fragmentEntries.Where(f => f.Name.Contains(FragmentSearch.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name).ToArray();
    }
    void ClearFragmentPreview()
    {
        fragmentPreviewDocument = null; if (FragmentInsertButton != null) FragmentInsertButton.IsEnabled = false;
    }
    void RefreshFragmentDestinations()
    {
        if (fragmentEditing == null || fragmentCapturing || selectedEntity == null) return;
        var choices = EncounterRoot.Descendants().Where(n => n.Kind == "State").Select(n => document.StateName(n.Id)).Where(n => n != null).Distinct().Order().ToArray();
        foreach (var (phase, input) in fragmentPhaseTargets)
        {
            var previous = input.SelectedItem as string; input.ItemsSource = choices;
            input.SelectedItem = choices.Contains(previous) ? previous : choices.Contains(phase) ? phase : null;
        }
    }
    void FragmentOptionsChanged(object sender, RoutedEventArgs e) => ClearFragmentPreview();
    void CaptureFragmentSelection(object sender, RoutedEventArgs e) => Try(() =>
    {
        var fields = document.FragmentParameterCandidates(nodeSelection);
        var bindings = fields.Select((f, i) => new FragmentBinding(f.NodeId, f.Parameter, f.Parameter + "-" + (i + 1))).ToArray();
        var fragment = document.CaptureFragment(nodeSelection, selectedNode?.Label ?? "Encounter fragment", bindings: bindings);
        ShowFragment(fragment, true);
    });
    void ShowFragment(BehaviorFragment fragment, bool capturing)
    {
        ClearFragmentPreview(); fragmentEditing = fragment; fragmentCapturing = capturing;
        FragmentHeading.Text = capturing ? "Save selected source" : fragment.Name;
        FragmentInfo.Text = "From " + fragment.OriginEntity + " · source " + fragment.SourceSha256[..8] + (fragment.RequiresSourceContext ? " · source helpers and shared values must exist in the destination; their definitions are not copied." : " · native constructor expressions.");
        FragmentNameFields.Visibility = capturing ? Visibility.Visible : Visibility.Collapsed;
        FragmentName.Text = fragment.Name; FragmentDescription.Text = fragment.Description;
        FragmentSaveButton.Visibility = capturing ? Visibility.Visible : Visibility.Collapsed;
        FragmentPreviewButton.IsEnabled = !capturing; FragmentShareButton.IsEnabled = !capturing;
        FragmentSelfOrders.Visibility = !capturing && fragment.References.Any(r => r.Kind == "self-entity") ? Visibility.Visible : Visibility.Collapsed;
        FragmentSelfOrders.IsChecked = true; FragmentFields.Children.Clear(); FragmentPhases.Children.Clear(); fragmentFields.Clear(); fragmentPhaseTargets.Clear();
        foreach (var parameter in fragment.Parameters)
        {
            var label = new CheckBox { Content = capturing ? "Expose " + parameter.Label : parameter.Name + " · " + parameter.Label, IsChecked = !capturing, IsHitTestVisible = capturing, Focusable = capturing, Margin = new(0, 10, 0, 4) };
            var input = new TextBox { Text = capturing ? parameter.Name : parameter.Value.DefaultExpression, ToolTip = capturing ? "Name of this template input · saved default: " + parameter.Value.DefaultExpression : "C# value passed to the native constructor/helper" };
            input.TextChanged += (_, _) => ClearFragmentPreview(); label.Checked += (_, _) => ClearFragmentPreview(); label.Unchecked += (_, _) => ClearFragmentPreview();
            if (capturing) FragmentFields.Children.Add(label);
            else FragmentFields.Children.Add(new TextBlock { Text = parameter.Name + " · " + parameter.Label, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 4) });
            FragmentFields.Children.Add(input); fragmentFields.Add((parameter, label, input));
        }
        if (!capturing) foreach (var phase in fragment.References.Where(r => r.Kind == "phase-external").Select(r => r.Target).Distinct())
        {
            var choices = EncounterRoot.Descendants().Where(n => n.Kind == "State").Select(n => document.StateName(n.Id)).Where(n => n != null).Distinct().Order().ToArray();
            var input = new ComboBox { ItemsSource = choices, SelectedItem = choices.Contains(phase) ? phase : null, Margin = new(0, 4, 0, 8), ToolTip = "Map this external reference to one named phase in the destination" };
            input.SelectionChanged += (_, _) => ClearFragmentPreview(); FragmentPhases.Children.Add(new TextBlock { Text = "External phase · " + phase }); FragmentPhases.Children.Add(input); fragmentPhaseTargets.Add(phase, input);
        }
        FragmentPreview.Text = string.Join("\n\n", fragment.Contents.Expressions); WorkspaceTabs.SelectedIndex = 4;
    }
    void FragmentSelected(object sender, SelectionChangedEventArgs e) => Try(() =>
    { if (FragmentLibrary.SelectedItem is FragmentEntry entry) ShowFragment(BehaviorFragment.Load(entry.Path), false); });
    BehaviorFragment SaveFragmentToLibrary()
    {
        if (!fragmentCapturing || fragmentEditing == null) throw new ArgumentException("Choose Save selection first.");
        var fragment = fragmentEditing with { Name = FragmentName.Text, Description = FragmentDescription.Text, Parameters = fragmentFields.Where(f => f.Expose.IsChecked == true).Select(f => f.Parameter with { Name = f.Input.Text }).ToArray() };
        fragment.Save(Path.Combine(fragmentDirectory, fragment.Id + ".rbsfrag")); RefreshFragments(); ShowFragment(fragment, false); Status.Text = "Saved local fragment · " + fragment.Name; return fragment;
    }
    void SaveFragmentTemplate(object sender, RoutedEventArgs e) => Try(() => SaveFragmentToLibrary());
    void OpenFragmentFile(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Realm Studio fragment|*.rbsfrag" };
        if (dialog.ShowDialog() != true) return;
        var fragment = BehaviorFragment.Load(dialog.FileName) with { Id = Guid.NewGuid() };
        fragment.Save(Path.Combine(fragmentDirectory, fragment.Id + ".rbsfrag")); RefreshFragments(); ShowFragment(fragment, false);
    });
    void ShareFragmentFile(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (fragmentEditing == null || fragmentCapturing) return;
        var dialog = new SaveFileDialog { Filter = "Realm Studio fragment|*.rbsfrag", FileName = "encounter-fragment.rbsfrag" };
        if (dialog.ShowDialog() == true) { fragmentEditing.Save(dialog.FileName); Status.Text = "Fragment file exported"; }
    });
    BehaviorNode FragmentDestination()
    {
        if (selectedNode == null) throw new ArgumentException("Select a destination phase or typed child list in Behavior.");
        return document.CanEditChildren(selectedNode.Id) ? document.ChildContainer(selectedNode.Id) : document.Parent(selectedNode.Id) ?? throw new ArgumentException("Choose a destination child list.");
    }
    IReadOnlyList<Guid> ImportCurrentFragment(BehaviorDocument destination, Guid target)
    {
        if (fragmentEditing == null || fragmentCapturing) throw new ArgumentException("Choose a saved fragment.");
        var values = fragmentFields.ToDictionary(f => f.Parameter.Name, f => f.Input.Text, StringComparer.Ordinal);
        var phases = fragmentPhaseTargets.ToDictionary(f => f.Key, f => f.Value.SelectedItem as string ?? throw new ArgumentException("Choose a destination for external phase '" + f.Key + "'."), StringComparer.Ordinal);
        return destination.ImportFragment(destination.Revision, target, destination.Find(target).Children.Count, fragmentEditing, values, phases, FragmentSelfOrders.IsChecked == true, d => adapter.Validate(d, selectedEntity!.Id));
    }
    void PreviewFragmentInsertion(object sender, RoutedEventArgs e) => Try(PrepareFragmentPreview);
    void PrepareFragmentPreview()
    {
        ClearFragmentPreview(); var target = FragmentDestination();
        var probe = new BehaviorDocument(document.Source, adapter, document.Identities, document.SourceFile); probe.LockedNodes.UnionWith(document.LockedNodes);
        var ids = ImportCurrentFragment(probe, target.Id);
        FragmentPreview.Text = string.Join("\n\n", ids.Select(id => probe.Expression(probe.Find(id))));
        fragmentPreviewDocument = document; fragmentPreviewRevision = document.Revision; fragmentPreviewTarget = target.Id; FragmentInsertButton.IsEnabled = true;
        Status.Text = "Fragment preview validated · destination " + target.Label + " · Insert makes one undo";
    }
    void InsertFragment(object sender, RoutedEventArgs e) => Try(CommitFragmentInsertion);
    void CommitFragmentInsertion()
    {
        if (fragmentPreviewDocument != document || fragmentPreviewRevision != document.Revision || fragmentPreviewTarget != FragmentDestination().Id || !FragmentInsertButton.IsEnabled) throw new InvalidOperationException("Preview the fragment against the current selection and revision before inserting.");
        var ids = ImportCurrentFragment(document, fragmentPreviewTarget); ChangedSelection(ids); Status.Text = "Inserted fragment · " + fragmentEditing!.Name;
    }
}
