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
            "\nSpawned by behavior: " + (actor.SpawnedByBehavior ? "yes" : "no")+"\nDefense: "+actor.Defense+
            "\nSize: "+actor.Size+" · texture stat: "+actor.AltTexture+" · visible texture: "+actor.TextureIndex+"\nGlow: "+(actor.GlowColor is {} glow?glow.ToString("X6",CultureInfo.InvariantCulture):"unavailable")+" · projectile size base: "+actor.ProjectileSizeBase+(actor.Damage is {} damage?"\nRecorded damage: "+damage.RecordedDamage+"\nDamage credited to present players: "+damage.PresentPlayerDamage+
                (damage.ParentActorId is {} counterParent?"\nCounter parent: #"+counterParent:"")+string.Concat(damage.Players.Select(p=>"\nPlayer #"+p.PlayerId+": "+p.Damage)):""))).ToArray() ?? [];
        rows=rows.Concat(session?.WorldObjects.Select(actor=>new RuntimeActorChoice(actor.Id,actor.Name+" · #"+actor.Id,actor.Name,string.Create(CultureInfo.InvariantCulture,$"{actor.Name} · #{actor.Id}\nNative class: {actor.Kind}\nPosition: {actor.X:0.###}, {actor.Y:0.###}\nSize: {actor.Size} · texture stat: {actor.AltTexture} · visible texture: {actor.TextureIndex}\nPhase: {actor.Phase}\nThis object has no enemy health counter.")))??[]).OrderBy(actor=>actor.Id).ToArray();
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
