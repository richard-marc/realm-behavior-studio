using System.Globalization;
using System.Windows.Controls;

namespace Studio.Desktop;

public partial class MainWindow
{
    sealed record RuntimeActorChoice(int Id, string Title, string SpriteId, string Summary);
    RuntimeActorChoice[] runtimeActorRows = [];
    void RefreshRuntimeActors()
    {
        var rows = session?.Entities.OrderBy(actor => actor.Id).Select(actor => new RuntimeActorChoice(actor.Id,
            actor.Name + " · #" + actor.Id, actor.Name,
            string.Create(CultureInfo.InvariantCulture, $"{actor.Name} · #{actor.Id}\nPhase: {actor.Phase}\nHP {actor.Hp} / {actor.MaxHp} · position {actor.X:0.###}, {actor.Y:0.###}\n") +
            (actor.ParentId is { } parent ? "Native parent: #" + parent : "Native parent: unassigned") +
            "\nSpawned by behavior: " + (actor.SpawnedByBehavior ? "yes" : "no"))).ToArray() ?? [];
        if (rows.SequenceEqual(runtimeActorRows)) return;
        var selected = (RuntimeActors.SelectedItem as RuntimeActorChoice)?.Id;
        runtimeActorRows = rows; RuntimeActors.ItemsSource = rows;
        RuntimeActors.SelectedItem = rows.FirstOrDefault(row => row.Id == selected) ?? rows.FirstOrDefault();
        if (rows.Length == 0) RuntimeActorInfo.Text = session == null ? "Play an encounter to inspect its actors." : "No live native actors.";
    }
    void RuntimeActorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RuntimeActorInfo != null) RuntimeActorInfo.Text = RuntimeActors.SelectedItem is RuntimeActorChoice choice ? choice.Summary : "Select a native actor.";
    }
}
