using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PathAnalyzer.Models;
using Wpf.Ui.Controls;

namespace PathAnalyzer;

/// <summary>Palette des états : quatre couleurs, lisibles sur fond clair comme sur fond sombre.</summary>
public static class StatusPalette
{
    public static readonly Color OkColor = Color.FromRgb(0x1F, 0xA0, 0x63);
    public static readonly Color InfoColor = Color.FromRgb(0x63, 0x66, 0xF1);
    public static readonly Color WarningColor = Color.FromRgb(0xD9, 0x88, 0x06);
    public static readonly Color ErrorColor = Color.FromRgb(0xE0, 0x44, 0x4B);

    public static readonly Brush Ok = Frozen(OkColor);
    public static readonly Brush Info = Frozen(InfoColor);
    public static readonly Brush Warning = Frozen(WarningColor);
    public static readonly Brush Error = Frozen(ErrorColor);

    /// <summary>Même teinte très diluée : sert de fond aux pastilles et aux lignes signalées.</summary>
    public static readonly Brush OkTint = Frozen(OkColor, 0x24);
    public static readonly Brush InfoTint = Frozen(InfoColor, 0x24);
    public static readonly Brush WarningTint = Frozen(WarningColor, 0x24);
    public static readonly Brush ErrorTint = Frozen(ErrorColor, 0x24);

    private static Brush Frozen(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static Brush Of(EntryStatus status) => status switch
    {
        EntryStatus.Ok => Ok,
        EntryStatus.Info => Info,
        EntryStatus.Warning => Warning,
        EntryStatus.Error => Error,
        _ => Info
    };

    public static Brush TintOf(EntryStatus status) => status switch
    {
        EntryStatus.Ok => OkTint,
        EntryStatus.Info => InfoTint,
        EntryStatus.Warning => WarningTint,
        EntryStatus.Error => ErrorTint,
        _ => InfoTint
    };
}

/// <summary>EntryStatus → icône Fluent de la pastille d'état.</summary>
public sealed class StatusToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        EntryStatus.Ok => SymbolRegular.Checkmark24,
        EntryStatus.Info => SymbolRegular.Info24,
        EntryStatus.Warning => SymbolRegular.Warning24,
        EntryStatus.Error => SymbolRegular.Dismiss24,
        _ => SymbolRegular.Info24
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>EntryStatus → couleur pleine (icône, texte du badge).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is EntryStatus s ? StatusPalette.Of(s) : StatusPalette.Info;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>EntryStatus → couleur diluée (fond de la pastille).</summary>
public sealed class StatusToTintConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is EntryStatus s ? StatusPalette.TintOf(s) : StatusPalette.InfoTint;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>bool → Visibility (paramètre "invert" pour inverser).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Chaîne vide → élément masqué (utilisé par la deuxième ligne des entrées).</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Longueur du PATH effectif → couleur de la jauge d'occupation.</summary>
public sealed class UsageToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var length = value is int i ? i : 0;
        if (length > Services.SizeReport.CmdLineLimit) return StatusPalette.Error;
        if (length > Services.SizeReport.SetxLimit) return StatusPalette.Warning;
        return StatusPalette.Ok;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
