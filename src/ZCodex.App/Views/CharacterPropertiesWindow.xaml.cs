using ZCodex.App.ViewModels;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using System.Windows;

namespace ZCodex.App.Views;

public partial class CharacterPropertiesWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    private EquipmentBuild? _equipment;

    public string OutputName       { get; private set; }
    public string OutputNotes      { get; private set; }
    public string OutputAssignment { get; private set; }
    public EquipmentBuild? OutputEquipment { get; private set; }

    public CharacterPropertiesWindow(CharacterSlotViewModel charVm)
    {
        InitializeComponent();

        OutputName       = charVm.Name;
        OutputNotes      = charVm.Notes;
        OutputAssignment = charVm.Assignment;
        _equipment       = charVm.Equipment;

        Loaded += (_, _) =>
        {
            NameBox.Text       = OutputName;
            NotesBox.Text      = OutputNotes;
            AssignmentBox.Text = OutputAssignment == "(unassigned)" ? "" : OutputAssignment;
            RefreshEquipDisplay();
            NameBox.SelectAll();
            NameBox.Focus();
        };
    }

    private void RefreshEquipDisplay()
    {
        if (_equipment is { IsEmpty: false } eq)
        {
            EquipCodeBox.Text  = GwEquipmentCodec.EncodeAllSets(eq);
            CopyEquipBtn.IsEnabled = true;
        }
        else
        {
            EquipCodeBox.Text  = T("S.Misc.NoEquipment");
            CopyEquipBtn.IsEnabled = false;
        }
    }

    private void ImportEquip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportTemplateWindow { Owner = this };
        dialog.Title = T("S.Dlg.ImportEquip");
        if (dialog.ShowDialog() != true) return;

        // Code brut OU code de chat « [nom;code] » : c'est ce que produisent le jeu et les sites
        // de builds, et l'utilisateur ne fait pas la différence.
        var build = GwTemplateCodec.ExtractCodes(dialog.TemplateCode)
            .Select(GwEquipmentCodec.Decode)
            .FirstOrDefault(b => b != null);
        if (build == null)
        {
            MessageBox.Show(T("S.Msg.InvalidEquipCode"),
                T("S.Msg.ImportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _equipment = build;
        RefreshEquipDisplay();
    }

    private void CopyEquip_Click(object sender, RoutedEventArgs e)
    {
        if (_equipment is not { IsEmpty: false } eq) return;
        SafeClipboard.SetText(GwEquipmentCodec.EncodeAllSets(eq));
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        OutputName       = name;
        OutputNotes      = NotesBox.Text.Trim();
        var assignment   = AssignmentBox.Text.Trim();
        OutputAssignment = string.IsNullOrEmpty(assignment) ? "(unassigned)" : assignment;
        OutputEquipment  = _equipment;
        DialogResult     = true;
    }
}
