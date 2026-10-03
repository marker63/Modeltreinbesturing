using System.Windows;

namespace Modeltreinbesturing;

public partial class OudereBackupDialog : Window
{
    public string? GekozenPad { get; private set; }

    public OudereBackupDialog()
    {
        InitializeComponent();
        var backups = BackupBeheerder.AlleBackups();
        if (backups.Count == 0)
        {
            BackupsLijst.Items.Add("(nog geen backups aanwezig)");
            BackupsLijst.IsEnabled = false;
            return;
        }
        foreach (var (pad, tijdstip) in backups)
            BackupsLijst.Items.Add(new BackupItem(pad, tijdstip));
        BackupsLijst.SelectedIndex = 0;
    }

    private record BackupItem(string Pad, DateTime Tijdstip)
    {
        public override string ToString() => Tijdstip.ToString("dddd d MMMM yyyy, HH:mm:ss");
    }

    private void Openen_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsLijst.SelectedItem is not BackupItem item)
        {
            DialogResult = false;
            return;
        }
        GekozenPad = item.Pad;
        DialogResult = true;
    }
}
