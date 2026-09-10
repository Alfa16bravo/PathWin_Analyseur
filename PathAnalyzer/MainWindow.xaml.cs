using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PathAnalyzer.Models;
using PathAnalyzer.Services;
using Path = System.IO.Path;

namespace PathAnalyzer;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly RegistryPathService _registry = new();
    private readonly PathAnalysisService _analyzer = new();
    private readonly BackupService _backup = new();

    private string _loadedUserRaw = "";
    private string _loadedSystemRaw = "";
    private RegistryValueKind _userKind = RegistryValueKind.ExpandString;
    private RegistryValueKind _systemKind = RegistryValueKind.ExpandString;
    private SizeReport _sizes = new();
    private bool _loading;

    private readonly bool _elevated = RegistryPathService.IsElevated;

    public MainWindow()
    {
        InitializeComponent();

        SystemPanel.Scope = PathScope.System;
        UserPanel.Scope = PathScope.User;
        SystemPanel.EntriesChanged += (_, _) => OnEntriesChanged();
        UserPanel.EntriesChanged += (_, _) => OnEntriesChanged();

        AdminButton.Visibility = _elevated ? Visibility.Collapsed : Visibility.Visible;
        AdminSettingsButton.Visibility = _elevated ? Visibility.Collapsed : Visibility.Visible;

        SystemPanel.IsLocked = !_registry.CanWrite(PathScope.System);
        UserPanel.IsLocked = !_registry.CanWrite(PathScope.User);

        ApplyLanguage();
        Loc.Changed += (_, _) => OnLanguageChanged();
        AppTheme.Changed += (_, _) => UpdateThemeControls();

        InputBindings.Add(new KeyBinding(new RelayCommand(() => Refresh_Click(this, new RoutedEventArgs())), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Backup_Click(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Apply_Click(this, new RoutedEventArgs())), Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(FocusCurrentFilter), Key.F, ModifierKeys.Control));

        Loaded += (_, _) => LoadFromRegistry();
        UsageBar.SizeChanged += (_, _) => DrawLimitMarkers();
    }

    // --- Langue ------------------------------------------------------------------------------------

    // Les boutons radio réagissent sur « Checked » et non sur « Click » : cela couvre aussi
    // le clavier et les lecteurs d'écran. En contrepartie il faut ignorer les cochages qui
    // viennent du code (drapeau) ou de la mise en place de la fenêtre (IsLoaded).
    private bool _updatingChoices;

    private bool ChoiceIsFromUser => IsLoaded && !_updatingChoices;

    private void LanguageChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (!ChoiceIsFromUser || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;
        Loc.Apply(Enum.TryParse<AppLanguage>(tag, out var language) ? language : AppLanguage.English);
    }

    private void OnLanguageChanged()
    {
        ApplyLanguage();
        // Le dernier message de la barre d'état appartient à l'ancienne langue.
        SetStatus(Loc.T("Status.Ready"));
        // Les états des entrées sont des phrases : il faut les reconstruire dans la nouvelle langue.
        if (!_loading) Analyze();
    }

    /// <summary>Repose tous les textes construits en code (le reste suit les DynamicResource du XAML).</summary>
    private void ApplyLanguage()
    {
        _updatingChoices = true;
        LangEnglishRadio.IsChecked = Loc.Language == AppLanguage.English;
        LangFrenchRadio.IsChecked = Loc.Language == AppLanguage.French;
        _updatingChoices = false;

        Title = _elevated ? AppInfo.Name + "  —  " + Loc.T("Status.Elevated") : AppInfo.Name;
        Bar.Title = Title;
        ElevationText.Text = Loc.T(_elevated ? "Status.Elevated" : "Status.NotElevated");
        AdminStateText.Text = Loc.T(_elevated ? "Settings.Admin.Yes" : "Settings.Admin.No");

        UserPageSubtitle.Text = Loc.F("Page.User.Subtitle", RegistryPathService.RegistryLocation(PathScope.User));
        SystemPageSubtitle.Text = Loc.F("Page.System.Subtitle", RegistryPathService.RegistryLocation(PathScope.System));
        BackupFolderText.Text = _backup.BackupDirectory;
        AboutText.Text = Loc.F("Settings.About.Detail", AppInfo.Version, Environment.MachineName, Environment.UserName);
        RegistryLocationsText.Text =
            Loc.F("Settings.Registry.User", RegistryPathService.RegistryLocation(PathScope.User))
            + Environment.NewLine
            + Loc.F("Settings.Registry.System", RegistryPathService.RegistryLocation(PathScope.System));

        UserPanel.ApplyLanguage();
        SystemPanel.ApplyLanguage();
        UpdateThemeControls();
    }

    // --- Navigation --------------------------------------------------------------------------------

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Le premier élément est sélectionné pendant la construction : les pages n'existent pas encore.
        if (PageHome == null) return;
        ShowPage((Nav.SelectedItem as ListBoxItem)?.Tag as string ?? "Home");
    }

    private void ShowPage(string tag)
    {
        PageHome.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        PageUser.Visibility = tag == "User" ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;
        PageBackup.Visibility = tag == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PageClean.Visibility = tag == "Clean" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectPage(string tag)
    {
        foreach (var item in Nav.Items.OfType<ListBoxItem>())
        {
            if ((item.Tag as string) == tag) { Nav.SelectedItem = item; return; }
        }
    }

    private void GoUser_Click(object sender, RoutedEventArgs e) => SelectPage("User");
    private void GoSystem_Click(object sender, RoutedEventArgs e) => SelectPage("System");

    private void FocusCurrentFilter()
    {
        if (PageUser.Visibility == Visibility.Visible) UserPanel.FocusFilter();
        else if (PageSystem.Visibility == Visibility.Visible) SystemPanel.FocusFilter();
    }

    // --- Chargement / analyse ----------------------------------------------------------------------

    private void LoadFromRegistry()
    {
        try
        {
            _loading = true;
            Mouse.OverrideCursor = Cursors.Wait;

            _loadedSystemRaw = _registry.ReadRaw(PathScope.System);
            _loadedUserRaw = _registry.ReadRaw(PathScope.User);
            _systemKind = _registry.ReadKind(PathScope.System);
            _userKind = _registry.ReadKind(PathScope.User);

            SystemPanel.Load(PathAnalysisService.Split(_loadedSystemRaw, PathScope.System));
            UserPanel.Load(PathAnalysisService.Split(_loadedUserRaw, PathScope.User));

            Analyze();
            SetStatus(Loc.F("Status.Loaded", DateTime.Now.ToString("HH:mm:ss"), UserPanel.Entries.Count, SystemPanel.Entries.Count));
        }
        catch (Exception ex)
        {
            MessageWindow.Error(this, Loc.F("Dialog.RegistryReadFailed", ex.Message));
        }
        finally
        {
            _loading = false;
            Mouse.OverrideCursor = null;
        }
    }

    private void Analyze()
    {
        var inspect = InspectContentToggle.IsChecked == true;
        _analyzer.Analyze(UserPanel.Entries, SystemPanel.Entries, inspect);
        _sizes = PathAnalysisService.ComputeSizes(UserPanel.Entries, SystemPanel.Entries);

        SystemPanel.UpdateSummary(_sizes.SystemLength, _sizes.SystemExpandedLength);
        UserPanel.UpdateSummary(_sizes.UserLength, _sizes.UserExpandedLength);
        SystemPanel.RefreshView();
        UserPanel.RefreshView();

        UpdateHome();
        UpdateCleanCounts();
        UpdateDirtyState();
    }

    private void OnEntriesChanged()
    {
        if (_loading) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try { Analyze(); }
        finally { Mouse.OverrideCursor = null; }
    }

    // Comparaison après normalisation : un « ; » final dans le registre ne compte pas comme une modification.
    private bool IsSystemDirty => PathAnalysisService.Join(SystemPanel.Entries) != Normalize(_loadedSystemRaw, PathScope.System);
    private bool IsUserDirty => PathAnalysisService.Join(UserPanel.Entries) != Normalize(_loadedUserRaw, PathScope.User);

    private static string Normalize(string raw, PathScope scope) => PathAnalysisService.Join(PathAnalysisService.Split(raw, scope));

    private void UpdateDirtyState()
    {
        var sysDirty = IsSystemDirty;
        var userDirty = IsUserDirty;
        var dirty = sysDirty || userDirty;

        DirtyBar.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        if (!dirty) return;

        var changed = UserPanel.Entries.Count(x => x.IsDirty) + SystemPanel.Entries.Count(x => x.IsDirty);
        var scope = Loc.T(sysDirty && userDirty ? "Dirty.Scope.Both" : userDirty ? "Dirty.Scope.User" : "Dirty.Scope.System");
        DirtyTitle.Text = changed switch
        {
            0 => Loc.F("Dirty.Title.Plain", scope),
            1 => Loc.F("Dirty.Title.One", scope),
            _ => Loc.F("Dirty.Title.Many", changed, scope)
        };
        DirtyDetail.Text = Loc.T("Dirty.Detail");
    }

    // --- Page d'accueil ----------------------------------------------------------------------------

    private void UpdateHome()
    {
        UserCountText.Text = UserPanel.Entries.Count.ToString();
        SystemCountText.Text = SystemPanel.Entries.Count.ToString();
        UserStateText.Text = IssuesSummary(UserPanel.Entries);
        SystemStateText.Text = IssuesSummary(SystemPanel.Entries);

        var all = UserPanel.Entries.Concat(SystemPanel.Entries).ToList();
        var errors = all.Count(x => x.Status == EntryStatus.Error);
        var warnings = all.Count(x => x.Status == EntryStatus.Warning);

        EntryStatus health;
        if (errors > 0)
        {
            health = EntryStatus.Error;
            HealthTitle.Text = Loc.Plural(errors, "Home.Health.Error.One", "Home.Health.Error.Many");
            HealthDetail.Text = Loc.T("Home.Health.Error.Detail");
        }
        else if (warnings > 0)
        {
            health = EntryStatus.Warning;
            HealthTitle.Text = Loc.Plural(warnings, "Home.Health.Warning.One", "Home.Health.Warning.Many");
            HealthDetail.Text = Loc.T("Home.Health.Warning.Detail");
        }
        else
        {
            health = EntryStatus.Ok;
            HealthTitle.Text = Loc.T("Home.Health.Ok");
            HealthDetail.Text = Loc.F("Home.Health.Ok.Detail", all.Count);
        }

        HealthPill.Background = StatusPalette.TintOf(health);
        HealthIcon.Foreground = StatusPalette.Of(health);
        HealthIcon.Symbol = health switch
        {
            EntryStatus.Error => Wpf.Ui.Controls.SymbolRegular.Dismiss24,
            EntryStatus.Warning => Wpf.Ui.Controls.SymbolRegular.Warning24,
            _ => Wpf.Ui.Controls.SymbolRegular.Checkmark24
        };

        CombinedSizeText.Text = Loc.F("Home.Usage.Value", _sizes.CombinedExpandedLength, SizeReport.AbsoluteLimit);
        UsageBar.Value = Math.Min(_sizes.CombinedExpandedLength, SizeReport.AbsoluteLimit);
        UsageBar.Foreground = (Brush)new UsageToBrushConverter().Convert(_sizes.CombinedExpandedLength, typeof(Brush), null!, null!);
        RemainingText.Text = Loc.F("Home.Usage.Remaining", _sizes.Remaining, _sizes.UsagePercent, SizeReport.SetxLimit, SizeReport.CmdLineLimit);

        if (_sizes.ExceedsAbsoluteLimit)
            ShowLimitWarning(Wpf.Ui.Controls.InfoBarSeverity.Error, "Home.Limit.Absolute.Title", "Home.Limit.Absolute.Message");
        else if (_sizes.ExceedsCmdLimit)
            ShowLimitWarning(Wpf.Ui.Controls.InfoBarSeverity.Warning, "Home.Limit.Cmd.Title", "Home.Limit.Cmd.Message");
        else if (_sizes.ExceedsSetxLimit)
            ShowLimitWarning(Wpf.Ui.Controls.InfoBarSeverity.Warning, "Home.Limit.Setx.Title", "Home.Limit.Setx.Message");
        else
            LimitInfoBar.IsOpen = false;

        DrawLimitMarkers();
    }

    private void ShowLimitWarning(Wpf.Ui.Controls.InfoBarSeverity severity, string titleKey, string messageKey)
    {
        LimitInfoBar.Severity = severity;
        LimitInfoBar.Title = Loc.T(titleKey);
        LimitInfoBar.Message = Loc.T(messageKey);
        LimitInfoBar.IsOpen = true;
    }

    private static string IssuesSummary(IEnumerable<PathEntry> entries)
    {
        var list = entries.ToList();
        var parts = new List<string>();
        int n;
        if ((n = list.Count(e => !e.IsBlank && !e.Exists && !e.HasUnresolvedVariable)) > 0) parts.Add(Loc.F("Issues.Missing", n));
        if ((n = list.Count(e => e.HasUnresolvedVariable)) > 0) parts.Add(Loc.F("Issues.Unresolved", n));
        if ((n = list.Count(e => e.IsEmptyDirectory)) > 0) parts.Add(Loc.F("Issues.EmptyDir", n));
        if ((n = list.Count(e => e.IsDuplicateInScope)) > 0) parts.Add(Loc.F("Issues.Duplicates", n));
        if ((n = list.Count(e => e.IsDuplicateAcrossScopes)) > 0) parts.Add(Loc.F("Issues.Shared", n));
        if ((n = list.Count(e => e.IsBlank)) > 0) parts.Add(Loc.F("Issues.Blank", n));
        return parts.Count == 0 ? Loc.T("Issues.Ok") : string.Join(" · ", parts);
    }

    private void DrawLimitMarkers()
    {
        LimitCanvas.Children.Clear();
        var width = UsageBar.ActualWidth;
        if (width <= 0) return;
        foreach (var limit in new[] { SizeReport.SetxLimit, SizeReport.CmdLineLimit })
        {
            var x = width * limit / SizeReport.AbsoluteLimit;
            LimitCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = UsageBar.ActualHeight,
                Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 2 },
                ToolTip = limit.ToString()
            });
        }
    }

    // --- Page de nettoyage -------------------------------------------------------------------------

    private void UpdateCleanCounts()
    {
        UserDupText.Text = Count(UserPanel.DuplicateCount, "Clean.Count.Dup");
        UserMissingText.Text = Count(UserPanel.MissingCount, "Clean.Count.Missing");
        UserTidyText.Text = Count(UserPanel.UntidyCount, "Clean.Count.Tidy");

        SystemDupText.Text = Count(SystemPanel.DuplicateCount, "Clean.Count.Dup");
        SystemMissingText.Text = Count(SystemPanel.MissingCount, "Clean.Count.Missing");
        SystemTidyText.Text = Count(SystemPanel.UntidyCount, "Clean.Count.Tidy");

        CrossDupText.Text = Count(UserPanel.Entries.Count(x => x.IsDuplicateAcrossScopes), "Clean.Count.Cross");
    }

    private static string Count(int n, string prefix) =>
        n == 0 ? Loc.T(prefix + ".None") : Loc.Plural(n, prefix + ".One", prefix + ".Many");

    private void UserDuplicates_Click(object sender, RoutedEventArgs e) => UserPanel.RemoveDuplicates();
    private void UserMissing_Click(object sender, RoutedEventArgs e) => UserPanel.RemoveMissing();
    private void UserTidy_Click(object sender, RoutedEventArgs e) => UserPanel.CleanEntries();
    private void SystemDuplicates_Click(object sender, RoutedEventArgs e) => SystemPanel.RemoveDuplicates();
    private void SystemMissing_Click(object sender, RoutedEventArgs e) => SystemPanel.RemoveMissing();
    private void SystemTidy_Click(object sender, RoutedEventArgs e) => SystemPanel.CleanEntries();

    // --- Thème -------------------------------------------------------------------------------------

    private void ThemeChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (!ChoiceIsFromUser || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;
        AppTheme.Apply(Enum.TryParse<ThemeMode>(tag, out var mode) ? mode : ThemeMode.System);
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e) =>
        AppTheme.Apply(AppTheme.Effective == Wpf.Ui.Appearance.ApplicationTheme.Dark ? ThemeMode.Light : ThemeMode.Dark);

    private void UpdateThemeControls()
    {
        _updatingChoices = true;
        ThemeSystemRadio.IsChecked = AppTheme.Mode == ThemeMode.System;
        ThemeLightRadio.IsChecked = AppTheme.Mode == ThemeMode.Light;
        ThemeDarkRadio.IsChecked = AppTheme.Mode == ThemeMode.Dark;
        _updatingChoices = false;

        var dark = AppTheme.Effective == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        ThemeButton.Icon = new Wpf.Ui.Controls.SymbolIcon(dark
            ? Wpf.Ui.Controls.SymbolRegular.WeatherSunny24
            : Wpf.Ui.Controls.SymbolRegular.WeatherMoon24);
        ThemeButton.Content = Loc.T(dark ? "Nav.Theme.Light" : "Nav.Theme.Dark");
    }

    private void InspectContent_Click(object sender, RoutedEventArgs e) => OnEntriesChanged();

    // --- Actions -----------------------------------------------------------------------------------

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (IsSystemDirty || IsUserDirty)
        {
            if (!MessageWindow.Confirm(this, Loc.T("Dialog.RefreshConfirm"))) return;
        }
        LoadFromRegistry();
    }

    private PathSnapshot CurrentSnapshot(string comment, bool fromRegistry) => new()
    {
        Comment = comment,
        UserPath = fromRegistry ? _loadedUserRaw : PathAnalysisService.Join(UserPanel.Entries),
        SystemPath = fromRegistry ? _loadedSystemRaw : PathAnalysisService.Join(SystemPanel.Entries),
        UserPathKind = _userKind.ToString(),
        SystemPathKind = _systemKind.ToString()
    };

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new PromptWindow(Loc.T("Dialog.BackupPrompt.Title"), Loc.T("Dialog.BackupPrompt.Label"), "") { Owner = this };
        if (prompt.ShowDialog() != true) return;

        try
        {
            // On sauvegarde ce qui est réellement dans le registre, pas l'état modifié de l'éditeur.
            var file = _backup.CreateBackup(CurrentSnapshot(prompt.Value, fromRegistry: true));
            SetStatus(Loc.F("Status.BackupCreated", file));
            MessageWindow.Success(this, Loc.F("Dialog.BackupDone", file));
        }
        catch (Exception ex)
        {
            MessageWindow.Error(this, Loc.F("Dialog.BackupFailed", ex.Message));
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RestoreWindow(_backup) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Snapshot == null) return;

        _loading = true;
        try
        {
            if (dlg.RestoreSystem)
            {
                var entries = PathAnalysisService.Split(dlg.Snapshot.SystemPath, PathScope.System);
                foreach (var en in entries) en.IsDirty = true;
                SystemPanel.Load(entries);
            }
            if (dlg.RestoreUser)
            {
                var entries = PathAnalysisService.Split(dlg.Snapshot.UserPath, PathScope.User);
                foreach (var en in entries) en.IsDirty = true;
                UserPanel.Load(entries);
            }
        }
        finally { _loading = false; }

        Analyze();
        SetStatus(Loc.F("Status.Restored", dlg.Snapshot.CreatedAt));
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_backup.BackupDirectory}\"") { UseShellExecute = true });

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var sysDirty = IsSystemDirty;
        var userDirty = IsUserDirty;
        if (!sysDirty && !userDirty) return;

        if (sysDirty && SystemPanel.IsLocked)
        {
            MessageWindow.Warn(this, Loc.T("Dialog.SystemLocked"));
            return;
        }

        var errors = SystemPanel.Entries.Concat(UserPanel.Entries).Count(x => x.Status == EntryStatus.Error);
        var sb = new StringBuilder(Loc.T("Dialog.Apply.Intro"));
        if (sysDirty) sb.AppendLine(Loc.F("Dialog.Apply.System", SystemPanel.Entries.Count, _sizes.SystemLength));
        if (userDirty) sb.AppendLine(Loc.F("Dialog.Apply.User", UserPanel.Entries.Count, _sizes.UserLength));
        if (errors > 0) sb.AppendLine(Loc.F("Dialog.Apply.Errors", errors));
        if (_sizes.ExceedsAbsoluteLimit) sb.AppendLine(Loc.T("Dialog.Apply.TooLong"));
        sb.AppendLine(Loc.T("Dialog.Apply.Outro"));

        if (!MessageWindow.Confirm(this, Loc.T("Dialog.Apply.Intro") + Loc.T("Dialog.Apply.Outro"), sb.ToString(), Loc.T("Dialog.Apply.Title")))
            return;

        try
        {
            var backupFile = _backup.CreateBackup(CurrentSnapshot(Loc.T("Backup.Auto.Comment"), fromRegistry: true));

            if (sysDirty) _registry.Write(PathScope.System, PathAnalysisService.Join(SystemPanel.Entries), _systemKind);
            if (userDirty) _registry.Write(PathScope.User, PathAnalysisService.Join(UserPanel.Entries), _userKind);

            LoadFromRegistry();
            SetStatus(Loc.F("Status.Applied", DateTime.Now.ToString("HH:mm:ss"), Path.GetFileName(backupFile)));
            MessageWindow.Success(this, Loc.T("Dialog.Applied"));
        }
        catch (UnauthorizedAccessException)
        {
            MessageWindow.Error(this, Loc.T("Dialog.AccessDenied"));
        }
        catch (Exception ex)
        {
            MessageWindow.Error(this, Loc.F("Dialog.WriteFailed", ex.Message));
        }
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (MessageWindow.Confirm(this, Loc.T("Dialog.DiscardConfirm"))) LoadFromRegistry();
    }

    private void RemoveCrossDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (UserPanel.IsLocked) return;
        var dup = UserPanel.Entries.Where(x => x.IsDuplicateAcrossScopes).ToList();
        if (dup.Count == 0)
        {
            MessageWindow.Info(this, Loc.T("Dialog.NoCrossDuplicates"));
            return;
        }
        var preview = string.Join("\n", dup.Take(40).Select(x => x.RawValue))
                      + (dup.Count > 40 ? "\n" + Loc.F("Dialog.More", dup.Count - 40) : "");
        if (!MessageWindow.Confirm(this, Loc.F("Dialog.ConfirmCross", dup.Count), preview)) return;
        foreach (var x in dup) UserPanel.Entries.Remove(x);
        OnEntriesChanged();
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Report.Save.Title"),
            Filter = Loc.T("Report.Save.Filter"),
            FileName = Loc.F("Report.Save.FileName", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"))
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, BuildReport(), Encoding.UTF8);
        SetStatus(Loc.F("Status.ReportSaved", dlg.FileName));
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("Report.Title"));
        sb.AppendLine(Loc.F("Report.Generated", DateTime.Now, Environment.MachineName, Environment.UserName));
        sb.AppendLine(new string('=', 90));
        sb.AppendLine();
        sb.AppendLine(Loc.T("Report.Sizes"));
        sb.AppendLine(Loc.F("Report.Size.System", SystemPanel.Entries.Count, _sizes.SystemLength, _sizes.SystemExpandedLength));
        sb.AppendLine(Loc.F("Report.Size.User", UserPanel.Entries.Count, _sizes.UserLength, _sizes.UserExpandedLength));
        sb.AppendLine(Loc.F("Report.Size.Combined", _sizes.CombinedExpandedLength, SizeReport.AbsoluteLimit, _sizes.Remaining, _sizes.UsagePercent));
        if (_sizes.ExceedsSetxLimit) sb.AppendLine(Loc.F("Report.Over.Setx", SizeReport.SetxLimit));
        if (_sizes.ExceedsCmdLimit) sb.AppendLine(Loc.F("Report.Over.Cmd", SizeReport.CmdLineLimit));
        if (_sizes.ExceedsAbsoluteLimit) sb.AppendLine(Loc.T("Report.Over.Absolute"));
        sb.AppendLine();

        void Section(string title, IEnumerable<PathEntry> entries)
        {
            sb.AppendLine(title);
            sb.AppendLine(new string('-', 90));
            foreach (var x in entries)
            {
                var glyph = x.Status switch { EntryStatus.Ok => "OK  ", EntryStatus.Info => "INFO", EntryStatus.Warning => "WARN", EntryStatus.Error => "ERR ", _ => "?   " };
                sb.AppendLine($"{x.Index,3}. [{glyph}] {x.RawValue}");
                if (x.RawValue != x.ExpandedValue) sb.AppendLine($"           → {x.ExpandedValue}");
                if (x.Exists) sb.AppendLine($"           {x.ContentSummary}, {PathAnalysisService.FormatSize(x.SizeBytes)}");
                if (x.Status != EntryStatus.Ok) sb.AppendLine($"           {x.StatusText}");
            }
            sb.AppendLine();
        }

        Section(Loc.F("Report.Section.System", RegistryPathService.RegistryLocation(PathScope.System)), SystemPanel.Entries);
        Section(Loc.F("Report.Section.User", RegistryPathService.RegistryLocation(PathScope.User)), UserPanel.Entries);
        return sb.ToString();
    }

    private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if ((IsSystemDirty || IsUserDirty) && !MessageWindow.Confirm(this, Loc.T("Dialog.AdminConfirm"))) return;

        try
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exe == null) throw new InvalidOperationException("Executable path not found.");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // L'utilisateur a refusé l'élévation UAC : on reste ouvert.
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!(IsSystemDirty || IsUserDirty)) return;
        if (!MessageWindow.Confirm(this, Loc.T("Dialog.CloseConfirm"))) e.Cancel = true;
    }

    private void SetStatus(string text) => StatusText.Text = text;
}

/// <summary>Commande minimale pour les raccourcis clavier.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _action;
    public RelayCommand(Action action) => _action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
