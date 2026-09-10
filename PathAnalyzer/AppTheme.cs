using System.IO;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Path = System.IO.Path;

namespace PathAnalyzer;

/// <summary>Préférence de thème choisie dans les réglages.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Thème de l'application : palette Fluent (clair / sombre) et accent indigo unique,
/// appliqués partout et mémorisés entre deux lancements.
/// </summary>
public static class AppTheme
{
    /// <summary>Indigo : seule couleur d'accent de l'application, lisible en clair comme en sombre.</summary>
    public static readonly Color Accent = Color.FromRgb(0x63, 0x66, 0xF1);

    private static readonly string SettingFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PathAnalyzer", "theme.txt");

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>Thème effectivement affiché (le mode « Système » est résolu en clair ou sombre).</summary>
    public static ApplicationTheme Effective =>
        ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    /// <summary>Prévient les fenêtres ouvertes qu'elles doivent rafraîchir ce qui dépend du thème.</summary>
    public static event EventHandler? Changed;

    public static void LoadAndApply() => Apply(ReadSetting(), save: false);

    public static void Apply(ThemeMode mode, bool save = true)
    {
        Mode = mode;
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => SystemPrefersDark() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, true);
        // ApplicationThemeManager.Apply repose l'accent du système : on réapplique le nôtre juste après.
        ApplicationAccentColorManager.Apply(Accent, theme, false, false);

        if (save) WriteSetting(mode);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static ThemeMode ReadSetting()
    {
        try
        {
            if (!File.Exists(SettingFile)) return ThemeMode.System;
            return File.ReadAllText(SettingFile).Trim().ToLowerInvariant() switch
            {
                "dark" => ThemeMode.Dark,
                "light" => ThemeMode.Light,
                _ => ThemeMode.System
            };
        }
        catch { return ThemeMode.System; }
    }

    private static void WriteSetting(ThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingFile)!);
            File.WriteAllText(SettingFile, mode.ToString().ToLowerInvariant());
        }
        catch { /* préférence non enregistrable : sans conséquence */ }
    }
}
