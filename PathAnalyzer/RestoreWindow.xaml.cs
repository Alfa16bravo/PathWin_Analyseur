using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PathAnalyzer.Models;
using PathAnalyzer.Services;

namespace PathAnalyzer;

public partial class RestoreWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly BackupService _backup;

    public PathSnapshot? Snapshot { get; private set; }
    public bool RestoreUser => UserCheck.IsChecked == true;
    public bool RestoreSystem => SystemCheck.IsChecked == true;

    public RestoreWindow(BackupService backup)
    {
        InitializeComponent();
        _backup = backup;
        BackupList.ItemsSource = _backup.ListBackups();
        if (BackupList.Items.Count == 0)
            PreviewText.Text = Loc.F("Restore.Empty", _backup.BackupDirectory);
    }

    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupInfo info) return;
        TryLoad(info.FilePath);
    }

    private void BackupList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Snapshot != null) Ok_Click(sender, e);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Restore.Open.Title"),
            Filter = Loc.T("Restore.Open.Filter"),
            InitialDirectory = _backup.BackupDirectory
        };
        if (dlg.ShowDialog() != true) return;
        BackupList.SelectedItem = null;
        TryLoad(dlg.FileName);
    }

    private void TryLoad(string file)
    {
        try
        {
            Snapshot = _backup.Load(file);
            var sys = PathAnalysisService.Split(Snapshot.SystemPath, PathScope.System);
            var usr = PathAnalysisService.Split(Snapshot.UserPath, PathScope.User);
            PreviewText.Text =
                Loc.F("Restore.Preview.Head", Path.GetFileName(file), Snapshot.CreatedAt, Snapshot.MachineName, Snapshot.UserName) +
                (string.IsNullOrWhiteSpace(Snapshot.Comment) ? "" : Loc.F("Restore.Preview.Comment", Snapshot.Comment)) +
                Loc.F("Restore.Preview.System", sys.Count) + string.Join("\n  ", sys.Select(x => x.RawValue)) +
                Loc.F("Restore.Preview.User", usr.Count) + string.Join("\n  ", usr.Select(x => x.RawValue));
            OkButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Snapshot = null;
            OkButton.IsEnabled = false;
            PreviewText.Text = Loc.F("Restore.Unreadable", ex.Message);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Snapshot == null) return;
        if (!RestoreUser && !RestoreSystem)
        {
            MessageWindow.Info(this, Loc.T("Restore.PickOne"), null, Loc.T("Restore.Title"));
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
