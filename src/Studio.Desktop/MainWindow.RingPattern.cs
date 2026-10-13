using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? ringPatternPanel;
    void AddRingPatternControls(BehaviorNode node)
    {
        ringPatternPanel=null;
        if(node.Kind=="GordoRotation")
        {Inspector.Children.Add(new TextBlock{Text="Chooses a seeded rotation sign on entry. GordoSpiral and GapRing read it only from the actor's current leaf behaviors; a parent-only rotation falls back to +1.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7"),Margin=new(0,10,0,6)});return;}
        if(!adapter.IsRingPattern(document,node))return;var pattern=adapter.ReadRingPattern(document,node);var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=pattern==null?"This ring uses source inputs. Edit its expressions below; Play runs the original controller.":$"{pattern.Count} projectiles · {pattern.Spacing:0.###}° spacing\n{pattern.Delay} ms initial countdown · {pattern.Interval} ms interval"+(pattern.Slots is {} slots?$"\n{slots} divisions · {pattern.Missing} omitted":"")+(pattern.WarnFirstVolley?"\nNative first-volley warning enabled":""),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        panel.Children.Add(new TextBlock{Text="Rotation and cooldown belong to each enemy. Native ticks attempt one volley at a time and advance rotation only after a successful shot. Play applies Dazed/Weak/Stunned, seeded offsets, alternating sides and the current leaf's GordoRotation. Warning packets use the original marked ray count; continuous drift may shift the later first volley.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7")});
        ringPatternPanel=new(){Header="Ring pattern",Content=panel,Margin=new(0,10,0,6)};Inspector.Children.Add(ringPatternPanel);
    }
}
