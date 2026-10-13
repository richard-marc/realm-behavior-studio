using System.IO;
using System.Windows;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ViewportEditorsSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/viewport-editors-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool passed,string label){if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);}
        var source=File.ReadAllText(selectedEntity!.SourceFile);var imported=new BehaviorDocument(source,adapter);var root=imported.Entities.Single(e=>e.EntityId==selectedEntity.Id).Root;
        await RunAttackUiSmoke(source,root,"",output,Check);await RunMovementUiSmoke(source,root,"",output,Check);await RunPathUiSmoke(source,root,"",output,Check);
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
}
