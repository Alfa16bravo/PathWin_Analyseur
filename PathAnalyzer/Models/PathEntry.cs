using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PathAnalyzer.Models;

/// <summary>Une entrée (un dossier) d'une variable PATH, avec le résultat de son analyse.</summary>
public sealed class PathEntry : INotifyPropertyChanged
{
    private string _rawValue = "";
    private string _expandedValue = "";
    private int _index;
    private EntryStatus _status = EntryStatus.Ok;
    private string _statusText = "";
    private string _statusDetails = "";
    private bool _exists;
    private bool _isBlank;
    private bool _isEmptyDirectory;
    private bool _isDuplicateInScope;
    private bool _isDuplicateAcrossScopes;
    private bool _hasUnresolvedVariable;
    private bool _hasInvalidCharacters;
    private bool _hasQuotes;
    private bool _hasTrailingWhitespace;
    private bool _isRelative;
    private int _fileCount;
    private int _executableCount;
    private int _subDirectoryCount;
    private string _executablesPreview = "";
    private long _sizeBytes;
    private bool _isDirty;

    public PathEntry(PathScope scope) => Scope = scope;

    public PathScope Scope { get; }

    /// <summary>Position (1 = premier) dans le PATH.</summary>
    public int Index
    {
        get => _index;
        set { if (Set(ref _index, value)) OnPropertyChanged(nameof(PositionAndLength)); }
    }

    /// <summary>Valeur brute telle qu'écrite dans le registre (non résolue, ex. %SystemRoot%\system32).</summary>
    public string RawValue
    {
        get => _rawValue;
        set
        {
            if (Set(ref _rawValue, value ?? ""))
            {
                IsDirty = true;
                OnPropertyChanged(nameof(Length));
                OnPropertyChanged(nameof(PositionAndLength));
                OnPropertyChanged(nameof(FolderName));
                OnPropertyChanged(nameof(DetailLine));
            }
        }
    }

    /// <summary>Nombre de caractères de l'entrée (valeur brute).</summary>
    public int Length => _rawValue.Length;

    /// <summary>Valeur avec les variables d'environnement résolues.</summary>
    public string ExpandedValue
    {
        get => _expandedValue;
        set { if (Set(ref _expandedValue, value)) { OnPropertyChanged(nameof(FolderName)); OnPropertyChanged(nameof(DetailLine)); } }
    }

    public EntryStatus Status { get => _status; set => Set(ref _status, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string StatusDetails { get => _statusDetails; set => Set(ref _statusDetails, value); }

    /// <summary>Remarque produite par l'analyse (chemin vers un fichier, contenu illisible…).</summary>
    public string Note { get; set; } = "";

    public bool Exists
    {
        get => _exists;
        set { if (Set(ref _exists, value)) { OnPropertyChanged(nameof(ContentSummary)); OnPropertyChanged(nameof(DetailLine)); } }
    }
    public bool IsBlank { get => _isBlank; set => Set(ref _isBlank, value); }
    public bool IsEmptyDirectory { get => _isEmptyDirectory; set => Set(ref _isEmptyDirectory, value); }
    public bool IsDuplicateInScope { get => _isDuplicateInScope; set => Set(ref _isDuplicateInScope, value); }
    public bool IsDuplicateAcrossScopes { get => _isDuplicateAcrossScopes; set => Set(ref _isDuplicateAcrossScopes, value); }
    public bool HasUnresolvedVariable { get => _hasUnresolvedVariable; set => Set(ref _hasUnresolvedVariable, value); }
    public bool HasInvalidCharacters { get => _hasInvalidCharacters; set => Set(ref _hasInvalidCharacters, value); }
    public bool HasQuotes { get => _hasQuotes; set => Set(ref _hasQuotes, value); }
    public bool HasTrailingWhitespace { get => _hasTrailingWhitespace; set => Set(ref _hasTrailingWhitespace, value); }
    public bool IsRelative { get => _isRelative; set => Set(ref _isRelative, value); }

    public int FileCount { get => _fileCount; set { if (Set(ref _fileCount, value)) { OnPropertyChanged(nameof(ContentSummary)); OnPropertyChanged(nameof(DetailLine)); } } }
    public int ExecutableCount { get => _executableCount; set { if (Set(ref _executableCount, value)) { OnPropertyChanged(nameof(ContentSummary)); OnPropertyChanged(nameof(DetailLine)); } } }
    public int SubDirectoryCount { get => _subDirectoryCount; set { if (Set(ref _subDirectoryCount, value)) { OnPropertyChanged(nameof(ContentSummary)); OnPropertyChanged(nameof(DetailLine)); } } }
    public long SizeBytes { get => _sizeBytes; set => Set(ref _sizeBytes, value); }
    public string ExecutablesPreview { get => _executablesPreview; set => Set(ref _executablesPreview, value); }

    /// <summary>Résumé du contenu du dossier pour la colonne « Contenu ».</summary>
    public string ContentSummary
    {
        get
        {
            if (!Exists) return "";
            if (FileCount == 0 && SubDirectoryCount == 0) return Loc.T("Entry.Content.Empty");
            return Loc.F("Entry.Content.Summary", ExecutableCount, FileCount, SubDirectoryCount);
        }
    }

    /// <summary>Nom du dossier seul, mis en avant dans le volet de détails.</summary>
    public string FolderName
    {
        get
        {
            var value = (string.IsNullOrEmpty(_expandedValue) ? _rawValue : _expandedValue).Trim().Trim('"').TrimEnd('\\');
            if (value.Length == 0) return Loc.T("Entry.Empty");
            var cut = value.LastIndexOf('\\');
            var name = cut >= 0 && cut < value.Length - 1 ? value[(cut + 1)..] : value;
            return name.Length == 0 ? value : name;
        }
    }

    /// <summary>Deuxième ligne affichée sous le chemin : valeur résolue et contenu du dossier.</summary>
    public string DetailLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_expandedValue) && !string.Equals(_expandedValue, _rawValue, StringComparison.Ordinal))
                parts.Add("→ " + _expandedValue);
            var content = ContentSummary;
            if (content.Length > 0) parts.Add(content);
            return string.Join("   ·   ", parts);
        }
    }

    /// <summary>Position et longueur, affichées ensemble dans le volet de détails.</summary>
    public string PositionAndLength => Loc.F("Detail.Position", Index) + "   ·   " + Loc.F("Detail.Length", Length);

    /// <summary>Rafraîchit les textes calculés après un changement de langue.</summary>
    public void NotifyLocalizedTexts()
    {
        OnPropertyChanged(nameof(FolderName));
        OnPropertyChanged(nameof(ContentSummary));
        OnPropertyChanged(nameof(DetailLine));
        OnPropertyChanged(nameof(PositionAndLength));
    }

    /// <summary>Vrai si la valeur a été modifiée depuis le chargement.</summary>
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => RawValue;
}
