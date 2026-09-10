using System.IO;

namespace PathAnalyzer.Models;

/// <summary>Portée d'une variable PATH.</summary>
public enum PathScope
{
    /// <summary>HKEY_CURRENT_USER\Environment</summary>
    User,
    /// <summary>HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment</summary>
    System
}

/// <summary>Gravité de l'état d'une entrée.</summary>
public enum EntryStatus
{
    Ok,
    Info,
    Warning,
    Error
}

/// <summary>Photographie complète des deux PATH, utilisée pour les sauvegardes.</summary>
public sealed class PathSnapshot
{
    public string Version { get; set; } = "1";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string Comment { get; set; } = "";
    public string UserPath { get; set; } = "";
    public string UserPathKind { get; set; } = "ExpandString";
    public string SystemPath { get; set; } = "";
    public string SystemPathKind { get; set; } = "ExpandString";
}

/// <summary>Informations sur un fichier de sauvegarde présent sur le disque.</summary>
public sealed class BackupInfo
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public DateTime CreatedAt { get; init; }
    public string Comment { get; init; } = "";
    public int UserEntries { get; init; }
    public int SystemEntries { get; init; }
    public string Display => Loc.F("Restore.Display", CreatedAt, UserEntries, SystemEntries)
                            + (string.IsNullOrWhiteSpace(Comment) ? "" : "  —  " + Comment);
}
