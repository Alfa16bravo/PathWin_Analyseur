using System.Globalization;
using System.IO;
using System.Windows;
using Path = System.IO.Path;

namespace PathAnalyzer;

/// <summary>Langues proposées par l'application.</summary>
public enum AppLanguage
{
    English,
    French
}

/// <summary>
/// Textes de l'interface. Chaque langue est un dictionnaire de ressources fusionné dans
/// <c>Application.Resources</c> : les <c>DynamicResource</c> du XAML se mettent donc à jour
/// tout seuls quand on change de langue, et le code lit les mêmes clés via <see cref="T"/>.
/// </summary>
public static class Loc
{
    private const string SentinelKey = "Lang.Code";

    private static readonly string SettingFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PathAnalyzer", "language.txt");

    /// <summary>Langue en cours. L'anglais est la langue par défaut de l'application.</summary>
    public static AppLanguage Language { get; private set; } = AppLanguage.English;

    /// <summary>Levé après chaque changement de langue : les textes construits en code doivent être refaits.</summary>
    public static event EventHandler? Changed;

    public static void LoadAndApply() => Apply(ReadSetting(), save: false);

    public static void Apply(AppLanguage language, bool save = true)
    {
        Language = language;

        var app = Application.Current;
        if (app != null)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var current = dictionaries.FirstOrDefault(d => d.Contains(SentinelKey));
            var replacement = new ResourceDictionary { Source = SourceOf(language) };

            if (current != null)
            {
                var index = dictionaries.IndexOf(current);
                dictionaries[index] = replacement;
            }
            else
            {
                dictionaries.Add(replacement);
            }
        }

        // Les nombres et les dates suivent la langue choisie (séparateur décimal, noms de mois…).
        var culture = new CultureInfo(language == AppLanguage.French ? "fr-FR" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (save) WriteSetting(language);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Uri SourceOf(AppLanguage language) => new(
        language == AppLanguage.French
            ? "pack://application:,,,/Localization/Strings.fr.xaml"
            : "pack://application:,,,/Localization/Strings.en.xaml",
        UriKind.Absolute);

    /// <summary>
    /// Texte associé à une clé. Renvoie la clé elle-même si elle manque, pour repérer les oublis.
    /// Dans les fichiers de ressources, un saut de ligne s'écrit « \n » : il est traduit ici.
    /// </summary>
    public static string T(string key) =>
        Application.Current?.TryFindResource(key) is string s ? s.Replace("\\n", "\n") : key;

    /// <summary>Texte à trous : <c>F("Home.Health.Errors", 3)</c>.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Choisit entre la forme au singulier et la forme au pluriel selon le nombre.</summary>
    public static string Plural(int count, string singularKey, string pluralKey) =>
        F(count == 1 ? singularKey : pluralKey, count);

    private static AppLanguage ReadSetting()
    {
        try
        {
            if (!File.Exists(SettingFile)) return AppLanguage.English;
            return File.ReadAllText(SettingFile).Trim().ToLowerInvariant() switch
            {
                "french" or "fr" => AppLanguage.French,
                _ => AppLanguage.English
            };
        }
        catch { return AppLanguage.English; }
    }

    private static void WriteSetting(AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingFile)!);
            File.WriteAllText(SettingFile, language.ToString().ToLowerInvariant());
        }
        catch { /* préférence non enregistrable : sans conséquence */ }
    }
}
