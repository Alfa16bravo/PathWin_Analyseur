using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using PathAnalyzer.Models;

namespace PathAnalyzer.Services;

/// <summary>Lecture et écriture des variables PATH directement dans le registre (valeurs non résolues).</summary>
public sealed class RegistryPathService
{
    public const string UserKeyPath = @"Environment";
    public const string SystemKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    public const string ValueName = "Path";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string RegistryLocation(PathScope scope) => scope == PathScope.User
        ? @"HKEY_CURRENT_USER\" + UserKeyPath
        : @"HKEY_LOCAL_MACHINE\" + SystemKeyPath;

    private static RegistryKey OpenKey(PathScope scope, bool writable)
    {
        var (root, sub) = scope == PathScope.User
            ? (Registry.CurrentUser, UserKeyPath)
            : (Registry.LocalMachine, SystemKeyPath);

        return root.OpenSubKey(sub, writable)
               ?? throw new InvalidOperationException(Loc.F("Registry.KeyMissing", RegistryLocation(scope)));
    }

    /// <summary>Lit la valeur brute (sans résolution des %VARIABLES%).</summary>
    public string ReadRaw(PathScope scope)
    {
        using var key = OpenKey(scope, writable: false);
        var value = key.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value as string ?? "";
    }

    public RegistryValueKind ReadKind(PathScope scope)
    {
        using var key = OpenKey(scope, writable: false);
        try { return key.GetValueKind(ValueName); }
        catch (IOException) { return RegistryValueKind.ExpandString; }
    }

    /// <summary>Vérifie si l'écriture est possible sans réellement modifier la valeur.</summary>
    public bool CanWrite(PathScope scope)
    {
        try
        {
            using var key = OpenKey(scope, writable: true);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Écrit la valeur brute en REG_EXPAND_SZ puis notifie le système.</summary>
    public void Write(PathScope scope, string rawValue, RegistryValueKind kind = RegistryValueKind.ExpandString)
    {
        using (var key = OpenKey(scope, writable: true))
        {
            key.SetValue(ValueName, rawValue, kind);
        }
        BroadcastEnvironmentChange();
    }

    /// <summary>Résout les %VARIABLES% avec l'environnement courant, en tenant compte de la portée.</summary>
    public static string Expand(string raw, PathScope scope)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // Environment.ExpandEnvironmentVariables utilise l'environnement du processus, ce qui convient
        // dans la grande majorité des cas (SystemRoot, ProgramFiles, USERPROFILE, LOCALAPPDATA...).
        var expanded = Environment.ExpandEnvironmentVariables(raw);

        // Pour la portée système, les variables utilisateur ne sont normalement pas disponibles :
        // on tente une résolution avec les variables machine uniquement si le résultat contient encore un %.
        if (scope == PathScope.System && expanded.Contains('%'))
        {
            expanded = ExpandWith(raw, EnvironmentVariableTarget.Machine);
        }
        return expanded;
    }

    private static string ExpandWith(string raw, EnvironmentVariableTarget target)
    {
        var vars = Environment.GetEnvironmentVariables(target);
        var result = raw;
        foreach (System.Collections.DictionaryEntry kv in vars)
        {
            var name = "%" + kv.Key + "%";
            var idx = result.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                result = result[..idx] + kv.Value + result[(idx + name.Length)..];
                idx = result.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            }
        }
        // Les variables système de base ne sont pas toujours dans la liste ci-dessus.
        result = Environment.ExpandEnvironmentVariables(result);
        return result;
    }

    // --- Notification WM_SETTINGCHANGE -------------------------------------------------------------

    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    /// <summary>Informe les applications ouvertes (Explorateur, etc.) que l'environnement a changé.</summary>
    public static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
                SMTO_ABORTIFHUNG, 5000, out _);
        }
        catch
        {
            // Non bloquant : la valeur est déjà écrite dans le registre.
        }
    }
}
