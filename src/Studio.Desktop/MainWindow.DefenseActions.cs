using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
namespace Studio.Desktop;
public partial class MainWindow
{
    ComboBox? conditionActionChoice;Button? conditionActionUse;Expander? scalingPanel,conditionActionPanel;
    void AddDefenseControls(BehaviorNode node)
    {
        conditionActionChoice=null;conditionActionUse=null;conditionActionPanel=null;scalingPanel=null;var revision=document.Revision;
        if(adapter.IsConditionAction(document,node))
        {
            var body=new StackPanel();conditionActionChoice=new(){ItemsSource=Enum.GetValues<ConditionEffectIndex>(),SelectedItem=adapter.ReadConditionAction(document,node),MaxDropDownHeight=300,Margin=new(0,8,0,6)};body.Children.Add(conditionActionChoice);conditionActionUse=new(){Content="Use condition",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node)&&conditionActionChoice.SelectedItem!=null};body.Children.Add(conditionActionUse);conditionActionChoice.SelectionChanged+=(_,_)=>UpdateDefenseControls();conditionActionUse.Click+=(_,_)=>Try(()=>{if(conditionActionChoice.SelectedItem is ConditionEffectIndex effect){adapter.ApplyConditionAction(document,selectedEntity!.Id,node.Id,revision,effect);Changed(node.Id);}});
            body.Children.Add(new TextBlock{Text=node.Kind=="RemoveConditionalEffect"?"Removes the host condition on entry. It has no ongoing refresh or exit restoration. A death wrapper dispatches this entry action at death.":"Applies the host condition on entry. Duration and perm retain native expiry/exit behavior. Removing a condition does not restore an earlier effect stack.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,10,0,6)});conditionActionPanel=new(){Header="Host condition",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(conditionActionPanel);
        }
        if(adapter.IsScalingAction(document,node))
        {
            var body=new StackPanel();var summary=node.Kind switch
            {
                "ScaleHP"=>"Counts unique player names. Positive distance is strict; zero or negative distance counts world player entries. No visibility/death filter. Names persist for this entry. New names change current and maximum HP with the original fraction rounding and cap order.",
                "ScaleHP2"=>"Counts player names, including Hidden and Invisible, in a strict native range. New names add an integer percentage of XML max HP plus the game's Legendary/Mythical gear term. Current and maximum HP increase together. Entry resets counting state, not HP.",
                _=>"Counts player names, including Hidden and Invisible, in a strict native range. Defense is overwritten with XML defense plus the ceiling of counted names × perPerson. Names persist for this entry, and exit does not restore defense."
            };
            body.Children.Add(new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});
            body.Children.Add(new TextBlock{Text="Checks at entry's first tick, then a 1000 ms countdown and a later comparison tick. On ordinary 100 ms ticks, checks are 1100 ms apart. State belongs to each actor, including shared behavior instances.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,0,0,8)});
            if(node.Kind=="ScaleHP2")
            {
                var weapon=RapleAdapter.ScalingWeaponInfo(selectedWeaponId);body.Children.Add(new TextBlock{Text=weapon==null?"Choose valid weapon XML to inspect its native gear flags.":"Test weapon: "+weapon.Id+"\nLegendary: "+weapon.Legendary+" · Mythical: "+weapon.Mythical+"\nNative weapon contribution: "+weapon.NativeGearPoints+" gear points",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A5B2AC"),Margin=new(0,0,0,8)});
                body.Children.Add(new TextBlock{Text="Prototype equipment supplies the selected XML weapon; ability, armor and ring slots are empty. This does not certify a complete game loadout. The native method keeps its batch-local item count and accumulated item-count behavior, including later signed differences.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,0,0,8)});
            }
            scalingPanel=new(){Header="Native scaling",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(scalingPanel);
        }
        if(node.Kind=="TransferDamageOnDeath"&&adapter.EntityParameterFor(document,node)!=null)Inspector.Children.Add(new Expander{Header="Damage record transfer",Margin=new(0,10,0,6),Content=new TextBlock{Text="Death copies this host's LastHitter and recorded total to the nearest matching Enemy, then adds each contributor's damage. The recipient's recorded total is replaced. This transfers records rather than health damage and leaves parent ownership unchanged. A matching host may select itself; use a distinct recipient XML type when appropriate.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)}});
    }
    void UpdateDefenseControls(){if(selectedNode!=null&&conditionActionChoice!=null){conditionActionChoice.IsEnabled=RegionEditable(selectedNode);conditionActionUse!.IsEnabled=RegionEditable(selectedNode)&&conditionActionChoice.SelectedItem!=null;}}
}
