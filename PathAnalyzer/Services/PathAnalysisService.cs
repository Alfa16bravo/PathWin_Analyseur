using System.IO;
using PathAnalyzer.Models;

namespace PathAnalyzer.Services;

/// <summary>Résultat global de l'analyse des tailles.</summary>
public sealed class SizeReport
{
    /// <summary>Limite absolue d'une variable d'environnement sous Windows (caractères).</summary>
    public const int AbsoluteLimit = 32767;
    /// <summary>Limite de la commande setx et de l'ancienne boîte de dialogue Windows.</summary>
    public const int SetxLimit = 2047;
    /// <summary>Limite de longueur d'une ligne de commande cmd.exe.</summary>
    public const int CmdLineLimit = 8191;

    public int UserLength { get; init; }
    public int SystemLength { get; init; }
    public int UserExpandedLength { get; init; }
    public int SystemExpandedLength { get; init; }

    /// <summary>Longueur du PATH effectif (système + ";" + utilisateur, résolu), tel que vu par les processus.</summary>
    public int CombinedExpandedLength { get; init; }
    public int Remaining => Math.Max(0, AbsoluteLimit - CombinedExpandedLength);
    public double UsagePercent => Math.Min(100.0, CombinedExpandedLength * 100.0 / AbsoluteLimit);

    public bool ExceedsSetxLimit => CombinedExpandedLength > SetxLimit;
    public bool ExceedsCmdLimit => CombinedExpandedLength > CmdLineLimit;
    public bool ExceedsAbsoluteLimit => CombinedExpandedLength > AbsoluteLimit;
}

/// <summary>Découpe, analyse et vérifie les entrées des variables PATH.</summary>
public sealed class PathAnalysisService
{
    private static readonly string[] ExecutableExtensions =
        { ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".msc", ".dll" };

    private static readonly char[] InvalidChars = Path.GetInvalidPathChars().Concat(new[] { '<', '>', '|', '"', '*', '?' }).ToArray();

    /// <summary>Découpe une valeur PATH brute en entrées, en conservant les entrées vides (pour les signaler).</summary>
    public static List<PathEntry> Split(string raw, PathScope scope)
    {
        var list = new List<PathEntry>();
        if (string.IsNullOrEmpty(raw)) return list;

        var parts = raw.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            // Une valeur qui se termine par ';' produit une dernière entrée vide : on l'ignore silencieusement
            // (c'est très courant et sans conséquence), mais on garde les entrées vides au milieu.
            if (i == parts.Length - 1 && parts[i].Length == 0) continue;
            list.Add(new PathEntry(scope) { RawValue = parts[i], Index = list.Count + 1, IsDirty = false });
        }
        return list;
    }

    /// <summary>Recompose la valeur brute à partir des entrées.</summary>
    public static string Join(IEnumerable<PathEntry> entries) => string.Join(";", entries.Select(e => e.RawValue));

    /// <summary>Clé de comparaison pour la détection des doublons (insensible à la casse, sans « \ » final, sans guillemets).</summary>
    public static string NormalizeForComparison(string expanded)
    {
        var s = expanded.Trim().Trim('"').Trim();
        if (s.Length == 0) return "";
        try
        {
            if (!s.Contains('%')) s = Path.GetFullPath(s);
        }
        catch
        {
            // chemin invalide : on garde la chaîne telle quelle
        }
        s = s.Replace('/', '\\');
        while (s.Length > 3 && s.EndsWith('\\')) s = s[..^1];
        return s.ToUpperInvariant();
    }

    /// <summary>Analyse complète : résolution, existence, contenu, doublons dans et entre les portées.</summary>
    public void Analyze(IList<PathEntry> userEntries, IList<PathEntry> systemEntries, bool inspectContent = true)
    {
        AnalyzeScope(systemEntries, PathScope.System, inspectContent);
        AnalyzeScope(userEntries, PathScope.User, inspectContent);

        // Doublons entre les deux portées : les entrées système sont prioritaires dans l'ordre de recherche,
        // on marque donc surtout l'entrée utilisateur comme redondante, mais les deux sont signalées.
        var systemKeys = systemEntries.Where(e => !e.IsBlank)
            .Select(e => NormalizeForComparison(e.ExpandedValue))
            .ToHashSet(StringComparer.Ordinal);
        var userKeys = userEntries.Where(e => !e.IsBlank)
            .Select(e => NormalizeForComparison(e.ExpandedValue))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var e in userEntries)
            e.IsDuplicateAcrossScopes = !e.IsBlank && systemKeys.Contains(NormalizeForComparison(e.ExpandedValue));
        foreach (var e in systemEntries)
            e.IsDuplicateAcrossScopes = !e.IsBlank && userKeys.Contains(NormalizeForComparison(e.ExpandedValue));

        foreach (var e in userEntries.Concat(systemEntries)) ComputeStatus(e);
    }

    private void AnalyzeScope(IList<PathEntry> entries, PathScope scope, bool inspectContent)
    {
        var seen = new Dictionary<string, PathEntry>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            e.Index = i + 1;
            AnalyzeEntry(e, scope, inspectContent);

            if (e.IsBlank) { e.IsDuplicateInScope = false; continue; }

            var key = NormalizeForComparison(e.ExpandedValue);
            if (seen.TryGetValue(key, out var first))
            {
                e.IsDuplicateInScope = true;
                first.IsDuplicateInScope = true; // la première occurrence est aussi signalée (info)
            }
            else
            {
                e.IsDuplicateInScope = false;
                seen[key] = e;
            }
        }
    }

    private static void AnalyzeEntry(PathEntry e, PathScope scope, bool inspectContent)
    {
        var raw = e.RawValue;
        e.IsBlank = string.IsNullOrWhiteSpace(raw);
        e.HasTrailingWhitespace = raw.Length > 0 && (raw != raw.Trim());
        e.HasQuotes = raw.Contains('"');

        var expanded = RegistryPathService.Expand(raw, scope);
        e.ExpandedValue = expanded;
        e.HasUnresolvedVariable = expanded.Contains('%');

        var cleaned = expanded.Trim().Trim('"');
        e.HasInvalidCharacters = cleaned.IndexOfAny(InvalidChars) >= 0 && !e.HasUnresolvedVariable;
        e.IsRelative = cleaned.Length > 0 && !e.HasUnresolvedVariable && !Path.IsPathRooted(cleaned);

        e.Exists = false;
        e.IsEmptyDirectory = false;
        e.FileCount = 0;
        e.ExecutableCount = 0;
        e.SubDirectoryCount = 0;
        e.SizeBytes = 0;
        e.ExecutablesPreview = "";
        e.Note = "";

        if (e.IsBlank || e.HasUnresolvedVariable || e.HasInvalidCharacters) return;

        try
        {
            if (Directory.Exists(cleaned))
            {
                e.Exists = true;
                if (inspectContent) InspectDirectory(e, cleaned);
            }
            else if (File.Exists(cleaned))
            {
                // Un fichier dans le PATH n'a aucun effet : on le signale comme inexistant (en tant que dossier).
                e.Exists = false;
                e.Note = Loc.T("Analysis.IsFile");
            }
        }
        catch
        {
            e.Exists = false;
        }
    }

    private static void InspectDirectory(PathEntry e, string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            var files = info.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }).ToList();
            var dirs = info.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }).Count();

            e.FileCount = files.Count;
            e.SubDirectoryCount = dirs;
            e.SizeBytes = files.Sum(f => { try { return f.Length; } catch { return 0L; } });

            var exes = files.Where(f => ExecutableExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                            .Select(f => f.Name)
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();
            e.ExecutableCount = exes.Count;
            e.ExecutablesPreview = exes.Count == 0
                ? Loc.T("Analysis.NoExecutableList")
                : string.Join(Environment.NewLine, exes.Take(200))
                  + (exes.Count > 200 ? Environment.NewLine + Loc.F("Analysis.MoreExecutables", exes.Count - 200) : "");
            e.IsEmptyDirectory = files.Count == 0 && dirs == 0;
        }
        catch (Exception ex)
        {
            e.Note = Loc.F("Analysis.ContentUnreadable", ex.Message);
        }
    }

    /// <summary>Détermine l'état affiché de l'entrée à partir des indicateurs.</summary>
    private static void ComputeStatus(PathEntry e)
    {
        var problems = new List<string>();
        var status = EntryStatus.Ok;

        void Add(EntryStatus s, string text)
        {
            problems.Add(text);
            if (s > status) status = s;
        }

        if (e.IsBlank) Add(EntryStatus.Warning, Loc.T("Analysis.Blank"));
        else
        {
            if (e.HasInvalidCharacters) Add(EntryStatus.Error, Loc.T("Analysis.InvalidChars"));
            if (e.HasUnresolvedVariable) Add(EntryStatus.Error, Loc.T("Analysis.Unresolved"));
            else if (!e.Exists) Add(EntryStatus.Error, Loc.T("Analysis.Missing"));
            else if (e.IsEmptyDirectory) Add(EntryStatus.Warning, Loc.T("Analysis.EmptyDir"));
            else if (e.ExecutableCount == 0) Add(EntryStatus.Info, Loc.T("Analysis.NoExecutable"));

            if (e.IsRelative) Add(EntryStatus.Warning, Loc.T("Analysis.Relative"));
            if (e.IsDuplicateInScope) Add(EntryStatus.Warning, Loc.T("Analysis.DuplicateInScope"));
            if (e.IsDuplicateAcrossScopes) Add(EntryStatus.Warning, Loc.T(e.Scope == PathScope.User ? "Analysis.AlsoInSystem" : "Analysis.AlsoInUser"));
            if (e.HasQuotes) Add(EntryStatus.Warning, Loc.T("Analysis.Quotes"));
            if (e.HasTrailingWhitespace) Add(EntryStatus.Info, Loc.T("Analysis.TrailingSpace"));
        }

        e.Status = status;
        e.StatusText = problems.Count == 0 ? Loc.T("Analysis.Ok") : string.Join(" · ", problems);

        var details = new List<string>
        {
            Loc.F("Analysis.Detail.Position", e.Index),
            Loc.F("Analysis.Detail.Raw", e.RawValue)
        };
        if (!string.Equals(e.RawValue, e.ExpandedValue, StringComparison.Ordinal))
            details.Add(Loc.F("Analysis.Detail.Resolved", e.ExpandedValue));
        details.Add(Loc.F("Analysis.Detail.Length", e.Length));
        if (e.Exists)
            details.Add(Loc.F("Analysis.Detail.Content", e.FileCount, e.SubDirectoryCount, e.ExecutableCount, FormatSize(e.SizeBytes)));
        if (problems.Count > 0) details.Add(Loc.F("Analysis.Detail.Problems", string.Join(", ", problems)));
        if (!string.IsNullOrEmpty(e.Note)) details.Add(e.Note);
        e.StatusDetails = string.Join(Environment.NewLine, details);
    }

    public static string FormatSize(long bytes)
    {
        var units = Loc.T("Size.Units").Split('|');
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {units[0]}" : $"{v:0.#} {units[u]}";
    }

    /// <summary>Calcule les longueurs et l'espace restant par rapport aux limites Windows.</summary>
    public static SizeReport ComputeSizes(IEnumerable<PathEntry> userEntries, IEnumerable<PathEntry> systemEntries)
    {
        var userRaw = Join(userEntries);
        var systemRaw = Join(systemEntries);
        var userExp = RegistryPathService.Expand(userRaw, PathScope.User);
        var systemExp = RegistryPathService.Expand(systemRaw, PathScope.System);

        // Windows construit le PATH d'un processus comme : PATH système + ";" + PATH utilisateur.
        var combined = systemExp.Length == 0 ? userExp
                     : userExp.Length == 0 ? systemExp
                     : systemExp + ";" + userExp;

        return new SizeReport
        {
            UserLength = userRaw.Length,
            SystemLength = systemRaw.Length,
            UserExpandedLength = userExp.Length,
            SystemExpandedLength = systemExp.Length,
            CombinedExpandedLength = combined.Length
        };
    }
}
