using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PathAnalyzer.Models;
using PathAnalyzer.Services;

namespace PathAnalyzer;

/// <summary>Panneau d'édition d'une variable PATH (une portée) : liste des dossiers et volet de détails.</summary>
public partial class PathListPanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PathListPanel), new PropertyMetadata("PATH"));

    public static readonly DependencyProperty IsLockedProperty =
        DependencyProperty.Register(nameof(IsLocked), typeof(bool), typeof(PathListPanel),
            new PropertyMetadata(false, (d, _) => ((PathListPanel)d).IsEditable = !((PathListPanel)d).IsLocked));

    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(PathListPanel), new PropertyMetadata(true));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Vrai si la portée ne peut pas être écrite (pas de droits administrateur).</summary>
    public bool IsLocked { get => (bool)GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }

    public bool IsEditable { get => (bool)GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }

    public PathScope Scope { get; set; }

    /// <summary>Titre utilisé par les boîtes de dialogue de ce panneau.</summary>
    private string DialogTitle => Loc.T(Scope == PathScope.User ? "Nav.User" : "Nav.System");

    public ObservableCollection<PathEntry> Entries { get; } = new();

    /// <summary>Déclenché après toute modification (ajout, suppression, déplacement, édition).</summary>
    public event EventHandler? EntriesChanged;

    public event EventHandler<PathEntry?>? SelectedEntryChanged;

    private readonly ICollectionView _view;
    private int _lastRawLength;
    private int _lastExpandedLength;

    public PathListPanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(Entries);
        _view.Filter = FilterEntry;
        EntryList.ItemsSource = _view;
    }

    // --- Chargement / affichage -------------------------------------------------------------------

    public void Load(IEnumerable<PathEntry> entries)
    {
        Entries.Clear();
        foreach (var e in entries) Entries.Add(e);
        _view.Refresh();
    }

    public void RefreshView() => _view.Refresh();

    /// <summary>Repose les textes qui ne passent pas par un DynamicResource après un changement de langue.</summary>
    public void ApplyLanguage()
    {
        IndexColumn.Header = Loc.T("Col.Index");
        PathColumn.Header = Loc.T("Col.Path");
        DiagnosticColumn.Header = Loc.T("Col.Diagnostic");
        LengthColumn.Header = Loc.T("Col.Length");
        foreach (var entry in Entries) entry.NotifyLocalizedTexts();
        UpdateSummary(_lastRawLength, _lastExpandedLength);
    }

    public void UpdateSummary(int rawLength, int expandedLength)
    {
        var errors = Entries.Count(e => e.Status == EntryStatus.Error);
        var warnings = Entries.Count(e => e.Status == EntryStatus.Warning);
        var dirty = Entries.Any(e => e.IsDirty);
        _lastRawLength = rawLength;
        _lastExpandedLength = expandedLength;
        SummaryText.Text = Loc.F("List.Summary", Entries.Count, rawLength, expandedLength)
                           + (errors > 0 ? Loc.F("List.Summary.Errors", errors) : "")
                           + (warnings > 0 ? Loc.F("List.Summary.Warnings", warnings) : "")
                           + (dirty ? Loc.T("List.Summary.Modified") : "");
    }

    public PathEntry? SelectedEntry => EntryList.SelectedItem as PathEntry;

    private List<PathEntry> SelectedEntries() => EntryList.SelectedItems.Cast<PathEntry>().OrderBy(Entries.IndexOf).ToList();

    private void RaiseChanged() => EntriesChanged?.Invoke(this, EventArgs.Empty);

    private bool FilterEntry(object o)
    {
        var text = FilterBox.Text?.Trim();
        if (string.IsNullOrEmpty(text) || o is not PathEntry e) return true;
        return e.RawValue.Contains(text, StringComparison.OrdinalIgnoreCase)
               || e.ExpandedValue.Contains(text, StringComparison.OrdinalIgnoreCase)
               || e.StatusText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    /// <summary>Donne le focus au champ de recherche (Ctrl+F).</summary>
    public void FocusFilter() => FilterBox.Focus();

    // --- Actions d'édition -------------------------------------------------------------------------

    private void InsertEntry(string value)
    {
        var sel = SelectedEntry;
        var idx = sel != null ? Entries.IndexOf(sel) + 1 : Entries.Count;
        var entry = new PathEntry(Scope) { RawValue = value, IsDirty = true };
        Entries.Insert(idx, entry);
        RaiseChanged();
        Dispatcher.BeginInvoke(() =>
        {
            EntryList.SelectedItem = entry;
            EntryList.ScrollIntoView(entry);
            if (value.Length == 0 && !IsLocked)
            {
                EntryList.CurrentCell = new DataGridCellInfo(entry, PathColumn);
                EntryList.BeginEdit();
            }
        }, DispatcherPriority.Background);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => InsertEntry("");

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Dialog.Browse.Title"), Multiselect = true };
        var start = SelectedEntry?.ExpandedValue;
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog() != true) return;
        foreach (var folder in dlg.FolderNames) InsertEntry(folder.TrimEnd('\\'));
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (IsLocked) return;
        var sel = SelectedEntries();
        if (sel.Count == 0) return;
        var anchor = Entries.IndexOf(sel[0]);
        foreach (var entry in sel) Entries.Remove(entry);
        RaiseChanged();
        if (Entries.Count > 0) EntryList.SelectedItem = Entries[Math.Min(anchor, Entries.Count - 1)];
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (IsLocked) return;
        var sel = SelectedEntries();
        if (sel.Count == 0) return;
        if (delta < 0 && Entries.IndexOf(sel[0]) == 0) return;
        if (delta > 0 && Entries.IndexOf(sel[^1]) == Entries.Count - 1) return;

        var ordered = delta < 0 ? sel : Enumerable.Reverse(sel).ToList();
        foreach (var entry in ordered)
        {
            var i = Entries.IndexOf(entry);
            Entries.Move(i, i + delta);
            entry.IsDirty = true;
        }
        RaiseChanged();
        EntryList.SelectedItems.Clear();
        foreach (var entry in sel) EntryList.SelectedItems.Add(entry);
        EntryList.ScrollIntoView(delta < 0 ? sel[0] : sel[^1]);
    }

    private void RemoveDuplicates_Click(object sender, RoutedEventArgs e) => RemoveDuplicates();
    private void RemoveMissing_Click(object sender, RoutedEventArgs e) => RemoveMissing();
    private void Clean_Click(object sender, RoutedEventArgs e) => CleanEntries();

    /// <summary>Supprime les doublons de cette portée, la première occurrence est conservée.</summary>
    public void RemoveDuplicates()
    {
        if (IsLocked) { LockedWarning(); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var toRemove = new List<PathEntry>();
        foreach (var entry in Entries)
        {
            if (entry.IsBlank) continue;
            var key = PathAnalysisService.NormalizeForComparison(entry.ExpandedValue);
            if (!seen.Add(key)) toRemove.Add(entry);
        }
        if (toRemove.Count == 0)
        {
            Inform(Loc.T("Dialog.NoDuplicates"));
            return;
        }
        if (!Confirm(Loc.F("Dialog.ConfirmDuplicates", toRemove.Count), Preview(toRemove))) return;
        foreach (var entry in toRemove) Entries.Remove(entry);
        RaiseChanged();
    }

    /// <summary>Supprime les entrées dont le dossier n'existe pas.</summary>
    public void RemoveMissing()
    {
        if (IsLocked) { LockedWarning(); return; }
        var toRemove = Entries.Where(x => !x.IsBlank && !x.Exists && !x.HasUnresolvedVariable).ToList();
        if (toRemove.Count == 0)
        {
            Inform(Loc.T("Dialog.AllFoldersExist"));
            return;
        }
        if (!Confirm(Loc.F("Dialog.ConfirmMissing", toRemove.Count), Preview(toRemove))) return;
        foreach (var entry in toRemove) Entries.Remove(entry);
        RaiseChanged();
    }

    /// <summary>Retire les lignes vides, les guillemets et les espaces superflus.</summary>
    public void CleanEntries()
    {
        if (IsLocked) { LockedWarning(); return; }
        var blanks = Entries.Where(x => x.IsBlank).ToList();
        var changed = 0;
        foreach (var entry in Entries.Where(x => !x.IsBlank))
        {
            var cleaned = entry.RawValue.Trim().Replace("\"", "").Trim();
            if (cleaned != entry.RawValue) { entry.RawValue = cleaned; changed++; }
        }
        foreach (var entry in blanks) Entries.Remove(entry);
        if (blanks.Count == 0 && changed == 0)
        {
            Inform(Loc.T("Dialog.NothingToTidy"));
            return;
        }
        RaiseChanged();
        Inform(Loc.F("Dialog.TidyDone", blanks.Count, changed));
    }

    /// <summary>Nombre d'entrées concernées par chaque nettoyage, pour les compteurs de la page « Nettoyage ».</summary>
    public int DuplicateCount
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return Entries.Count(x => !x.IsBlank && !seen.Add(PathAnalysisService.NormalizeForComparison(x.ExpandedValue)));
        }
    }

    public int MissingCount => Entries.Count(x => !x.IsBlank && !x.Exists && !x.HasUnresolvedVariable);

    public int UntidyCount => Entries.Count(x => x.IsBlank || x.RawValue.Trim().Replace("\"", "").Trim() != x.RawValue);

    private Window? OwnerWindow => Window.GetWindow(this);

    private void LockedWarning() => MessageWindow.Warn(OwnerWindow, Loc.T("Dialog.ReadOnly"), null, DialogTitle);

    private void Inform(string message) => MessageWindow.Info(OwnerWindow, message, null, DialogTitle);

    private bool Confirm(string message, string? details = null) =>
        MessageWindow.Confirm(OwnerWindow, message, details, DialogTitle);

    private static string Preview(IEnumerable<PathEntry> entries)
    {
        var list = entries.Select(x => x.RawValue).ToList();
        return string.Join("\n", list.Take(40)) + (list.Count > 40 ? "\n" + Loc.F("Dialog.More", list.Count - 40) : "");
    }

    // --- Événements de la liste --------------------------------------------------------------------

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectedEntryChanged?.Invoke(this, SelectedEntry);

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        // La valeur est poussée dans le modèle après cet événement : on ré-analyse au prochain tour de boucle.
        Dispatcher.BeginInvoke(RaiseChanged, DispatcherPriority.Background);
    }

    // La colonne du chemin est un modèle personnalisé : c'est à nous de placer le curseur dans le champ.
    private void Grid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (FindTextBox(e.EditingElement) is not { } box) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    private static TextBox? FindTextBox(DependencyObject? root)
    {
        if (root is TextBox box) return box;
        if (root == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindTextBox(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var editing = EntryList.CurrentCell.IsValid && Keyboard.FocusedElement is TextBox;
        if (editing) return;

        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt) { Move(-1); e.Handled = true; }
        else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) { Move(+1); e.Handled = true; }
        else if (e.Key == Key.Insert) { Add_Click(sender, e); e.Handled = true; }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsLocked) OpenInExplorer_Click(sender, e);
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null) return;
        var path = entry.ExpandedValue.Trim().Trim('"');
        if (!Directory.Exists(path))
        {
            MessageWindow.Warn(OwnerWindow, Loc.F("Dialog.FolderMissing", path), null, DialogTitle);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry != null) Clipboard.SetText(SelectedEntry.RawValue);
    }

    private void CopyExpanded_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry != null) Clipboard.SetText(SelectedEntry.ExpandedValue);
    }
}
