using System.Windows;
using System.Windows.Threading;

namespace PathAnalyzer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        // Langue, thème et accent posés avant l'ouverture de la fenêtre : pas de scintillement au démarrage.
        Loc.LoadAndApply();
        AppTheme.LoadAndApply();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageWindow.Error(Current?.MainWindow, Loc.F("Dialog.Unexpected", e.Exception.Message));
        e.Handled = true;
    }
}
