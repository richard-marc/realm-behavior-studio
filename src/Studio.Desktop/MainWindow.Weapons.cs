using System.Windows.Controls;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    IReadOnlyList<WeaponEntry> weaponCatalog = [];
    string selectedWeaponId = "Staff of Diabolic Secrets";
    void WeaponSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Weapons == null || weaponCatalog.Count == 0) return;
        Weapons.ItemsSource = weaponCatalog.Where(w => w.Name.Contains(WeaponSearch.Text, StringComparison.OrdinalIgnoreCase) || w.Id.Contains(WeaponSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        Weapons.SelectedItem = Weapons.Items.Cast<WeaponEntry>().FirstOrDefault(w => w.Id == selectedWeaponId);
    }
    void WeaponSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Weapons.SelectedItem is not WeaponEntry weapon) return;
        selectedWeaponId = weapon.Id;
        WeaponInfo.Text = weapon.Summary + "\n" + (weapon.Limitation ?? weapon.ClassId + " · apply & restart to equip");
    }
}
