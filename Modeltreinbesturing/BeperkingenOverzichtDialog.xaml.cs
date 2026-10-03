using System.Windows;
using Modeltreinbesturing.Model;

namespace Modeltreinbesturing;

public partial class BeperkingenOverzichtDialog : Window
{
    public BeperkingenOverzichtDialog(
        RichtingsverbodBeheerder richtingsverbodBeheerder,
        StopverbodBeheerder stopverbodBeheerder,
        StopverbodStilstandBeheerder stopverbodStilstandBeheerder,
        TreinrouteBeheerder routeBeheerder)
    {
        InitializeComponent();

        foreach (var verbod in richtingsverbodBeheerder.Richtingsverboden)
            RichtingsverbodenLijst.Items.Add(verbod.ToString());
        if (RichtingsverbodenLijst.Items.Count == 0) RichtingsverbodenLijst.Items.Add("(geen)");

        foreach (var item in stopverbodBeheerder.Verboden)
            StopverbodenLijst.Items.Add(item.ToString());
        if (StopverbodenLijst.Items.Count == 0) StopverbodenLijst.Items.Add("(geen)");

        foreach (var verbod in stopverbodStilstandBeheerder.Verboden)
            StopverbodStilstandLijst.Items.Add(verbod.ToString());
        if (StopverbodStilstandLijst.Items.Count == 0) StopverbodStilstandLijst.Items.Add("(geen)");

        bool heeftUitgeslotenWissels = false;
        foreach (var route in routeBeheerder.Treinroutes)
        {
            if (route.UitgeslotenWissels.Count == 0) continue;
            heeftUitgeslotenWissels = true;
            UitgeslotenWisselsLijst.Items.Add($"Route '{route.Omschrijving}': {string.Join(", ", route.UitgeslotenWissels.Select(w => $"wissel {w.Adres}"))}");
        }
        if (!heeftUitgeslotenWissels) UitgeslotenWisselsLijst.Items.Add("(geen)");
    }

    private void Sluiten_Click(object sender, RoutedEventArgs e) => Close();
}
