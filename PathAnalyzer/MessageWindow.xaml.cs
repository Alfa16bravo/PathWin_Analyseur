using System.Windows;
using PathAnalyzer.Models;

namespace PathAnalyzer;

/// <summary>Nature du message : décide de la couleur et du symbole de la pastille.</summary>
public enum DialogKind
{
    Info,
    Success,
    Warning,
    Error,
    Question
}

/// <summary>
/// Boîte de dialogue de l'application, au même style que la fenêtre principale
/// (barre de titre intégrée, fond Mica, accent indigo) plutôt que la boîte grise de Windows.
/// </summary>
public partial class MessageWindow : Wpf.Ui.Controls.FluentWindow
{
    private bool _result;

    private MessageWindow(DialogKind kind, string title, string heading, string message, string? details,
                          string primaryText, string? secondaryText)
    {
        InitializeComponent();

        Title = title;
        Bar.Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        MessageText.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsText.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
        }

        PrimaryButton.Content = primaryText;
        if (secondaryText == null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }

        var status = kind switch
        {
            DialogKind.Success => EntryStatus.Ok,
            DialogKind.Warning => EntryStatus.Warning,
            DialogKind.Error => EntryStatus.Error,
            _ => EntryStatus.Info
        };
        Pill.Background = StatusPalette.TintOf(status);
        Glyph.Foreground = StatusPalette.Of(status);
        Glyph.Symbol = kind switch
        {
            DialogKind.Success => Wpf.Ui.Controls.SymbolRegular.Checkmark24,
            DialogKind.Warning => Wpf.Ui.Controls.SymbolRegular.Warning24,
            DialogKind.Error => Wpf.Ui.Controls.SymbolRegular.Dismiss24,
            DialogKind.Question => Wpf.Ui.Controls.SymbolRegular.QuestionCircle24,
            _ => Wpf.Ui.Controls.SymbolRegular.Info24
        };

        Loaded += (_, _) =>
        {
            PrimaryButton.Focus();
        };
    }

    private void Primary_Click(object sender, RoutedEventArgs e) { _result = true; Close(); }

    private void Secondary_Click(object sender, RoutedEventArgs e) { _result = false; Close(); }

    // --- API utilisée par le reste de l'application -------------------------------------------------

    /// <summary>Message d'information : un seul bouton.</summary>
    public static void Info(Window? owner, string message, string? details = null, string? title = null) =>
        Show(owner, DialogKind.Info, title, message, details);

    public static void Success(Window? owner, string message, string? details = null, string? title = null) =>
        Show(owner, DialogKind.Success, title, message, details);

    public static void Warn(Window? owner, string message, string? details = null, string? title = null) =>
        Show(owner, DialogKind.Warning, title, message, details);

    public static void Error(Window? owner, string message, string? details = null, string? title = null) =>
        Show(owner, DialogKind.Error, title, message, details);

    /// <summary>Question fermée : vrai si l'utilisateur confirme.</summary>
    public static bool Confirm(Window? owner, string message, string? details = null, string? title = null)
    {
        var dialog = Build(owner, DialogKind.Question, title, message, details,
            Loc.T("Dialog.Yes"), Loc.T("Dialog.No"));
        dialog.ShowDialog();
        return dialog._result;
    }

    private static void Show(Window? owner, DialogKind kind, string? title, string message, string? details) =>
        Build(owner, kind, title, message, details, Loc.T("Dialog.Ok"), null).ShowDialog();

    private static MessageWindow Build(Window? owner, DialogKind kind, string? title, string message, string? details,
                                       string primaryText, string? secondaryText)
    {
        // La première ligne du message sert de titre visuel, le reste devient le corps :
        // les textes existants sont déjà écrits ainsi (« Phrase courte.\n\nExplication »).
        var parts = message.Replace("\r\n", "\n").Split("\n\n", 2);
        var heading = parts[0].Trim();
        var body = parts.Length > 1 ? parts[1].Trim() : "";

        var dialog = new MessageWindow(kind, title ?? AppInfo.Name, heading, body, details, primaryText, secondaryText);
        if (owner != null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dialog;
    }
}
