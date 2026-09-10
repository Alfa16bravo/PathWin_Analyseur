using System.Windows;

namespace PathAnalyzer;

/// <summary>Petite boîte de saisie d'une ligne de texte.</summary>
public partial class PromptWindow : Wpf.Ui.Controls.FluentWindow
{
    public string Value => InputBox.Text;

    public PromptWindow(string title, string label, string initialValue)
    {
        InitializeComponent();
        Title = title;
        Bar.Title = title;
        LabelText.Text = label;
        InputBox.Text = initialValue;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
