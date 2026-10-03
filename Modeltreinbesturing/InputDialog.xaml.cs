using System.Windows;

namespace Modeltreinbesturing;

public partial class InputDialog : Window
{
    public string Resultaat { get; private set; } = "";

    public InputDialog(string titel, string label, string standaardWaarde)
    {
        InitializeComponent();
        Title = titel;
        LabelTekst.Text = label;
        InvoerBox.Text = standaardWaarde;
        InvoerBox.SelectAll();
        Loaded += (_, _) => InvoerBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Resultaat = InvoerBox.Text;
        DialogResult = true;
    }

    private void Annuleren_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>Toont het dialoogje modaal en geeft de ingevoerde tekst terug, of null als geannuleerd.</summary>
    public static string? Vraag(Window eigenaar, string titel, string label, string standaardWaarde = "")
    {
        var dialoog = new InputDialog(titel, label, standaardWaarde) { Owner = eigenaar };
        return dialoog.ShowDialog() == true ? dialoog.Resultaat : null;
    }
}
