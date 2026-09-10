using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PathAnalyzer.Models;

namespace PathAnalyzer.Services;

/// <summary>Sauvegarde et restauration des deux PATH (fichier JSON + fichier .reg importable).</summary>
public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string BackupDirectory { get; }

    public BackupService()
    {
        BackupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PathAnalyzer", "backups");
        Directory.CreateDirectory(BackupDirectory);
    }

    /// <summary>Crée une sauvegarde horodatée (JSON + .reg) et retourne le chemin du JSON.</summary>
    public string CreateBackup(PathSnapshot snapshot, string? directory = null)
    {
        directory ??= BackupDirectory;
        Directory.CreateDirectory(directory);

        var stamp = snapshot.CreatedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        var jsonPath = Path.Combine(directory, $"path-backup_{stamp}.json");
        var regPath = Path.Combine(directory, $"path-backup_{stamp}.reg");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(snapshot, JsonOptions), Encoding.UTF8);
        File.WriteAllText(regPath, BuildRegFile(snapshot), new UnicodeEncoding(false, true)); // .reg = UTF-16 LE avec BOM
        return jsonPath;
    }

    public PathSnapshot Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<PathSnapshot>(json)
               ?? throw new InvalidDataException(Loc.T("Backup.Unreadable"));
    }

    public IReadOnlyList<BackupInfo> ListBackups(string? directory = null)
    {
        directory ??= BackupDirectory;
        if (!Directory.Exists(directory)) return Array.Empty<BackupInfo>();

        var list = new List<BackupInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "path-backup_*.json"))
        {
            try
            {
                var snap = Load(file);
                list.Add(new BackupInfo
                {
                    FilePath = file,
                    CreatedAt = snap.CreatedAt,
                    Comment = snap.Comment,
                    UserEntries = PathAnalysisService.Split(snap.UserPath, PathScope.User).Count,
                    SystemEntries = PathAnalysisService.Split(snap.SystemPath, PathScope.System).Count
                });
            }
            catch
            {
                // fichier corrompu : ignoré
            }
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>Génère un fichier .reg (format Regedit 5.00) avec les valeurs en REG_EXPAND_SZ (hex(2)).</summary>
    public static string BuildRegFile(PathSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine(Loc.F("Backup.Reg.Comment", snapshot.CreatedAt));
        sb.AppendLine(Loc.F("Backup.Reg.Machine", snapshot.MachineName, snapshot.UserName));
        if (!string.IsNullOrWhiteSpace(snapshot.Comment)) sb.AppendLine("; " + snapshot.Comment.Replace("\r", "").Replace("\n", " "));
        sb.AppendLine();
        sb.AppendLine("[HKEY_CURRENT_USER\\Environment]");
        sb.AppendLine(FormatValue(snapshot.UserPath, ParseKind(snapshot.UserPathKind)));
        sb.AppendLine();
        sb.AppendLine("[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment]");
        sb.AppendLine(FormatValue(snapshot.SystemPath, ParseKind(snapshot.SystemPathKind)));
        return sb.ToString();
    }

    private static RegistryValueKind ParseKind(string kind) =>
        Enum.TryParse<RegistryValueKind>(kind, out var k) ? k : RegistryValueKind.ExpandString;

    private static string FormatValue(string value, RegistryValueKind kind)
    {
        if (kind == RegistryValueKind.String)
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"Path\"=\"{escaped}\"";
        }

        // REG_EXPAND_SZ : hex(2) en UTF-16 LE terminé par un double zéro, lignes coupées avec "\".
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        var hex = string.Join(",", bytes.Select(b => b.ToString("x2")));
        var sb = new StringBuilder("\"Path\"=hex(2):");
        var lineLen = sb.Length;
        var parts = hex.Split(',');
        for (var i = 0; i < parts.Length; i++)
        {
            var piece = parts[i] + (i < parts.Length - 1 ? "," : "");
            if (lineLen + piece.Length > 76)
            {
                sb.AppendLine("\\");
                sb.Append("  ");
                lineLen = 2;
            }
            sb.Append(piece);
            lineLen += piece.Length;
        }
        return sb.ToString();
    }
}
