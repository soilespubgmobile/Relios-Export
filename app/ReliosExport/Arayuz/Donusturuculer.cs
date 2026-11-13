using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ReliosExport.Arayuz;

/// <summary>Değer, parametredeki seçeneklerden birine eşitse görünür ("A|B" birden fazla).</summary>
public sealed class EsitseGorunur : IValueConverter
{
    public bool Ters { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var esit = parameter is string p && value is not null
                   && p.Split('|').Any(x => string.Equals(x, value.ToString(), StringComparison.Ordinal));
        return esit != Ters ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Seçim listesinde "ön ayar" (null) satırı için yer tutucu nesne.</summary>
public sealed class OnAyarDegeri
{
    public static readonly OnAyarDegeri Ornek = new();

    private OnAyarDegeri()
    {
    }

    public override string ToString() => "Ön ayar";
}

/// <summary>null ⇄ "ön ayar" yer tutucusu (ComboBox.SelectedValue için).</summary>
public sealed class BosSecenek : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value ?? OnAyarDegeri.Ornek;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OnAyarDegeri ? null : value;
}

/// <summary>Seçim listesindeki bir satır: kaydedilen değer ve ekranda görünen ad.</summary>
public sealed record Secenek(object? Deger, string Ad)
{
    public override string ToString() => Ad;
}

/// <summary>
/// Değer listede yoksa "ozel" satırını seçtirir (ör. kutulara elle yazılan 1000×2000 boyutu).
/// Parametre: seçenek listesi ({x:Static a:Secenekler.Boyutlar}).
/// </summary>
public sealed class ListeDisi : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is IEnumerable<Secenek> liste && value is not null && !liste.Any(s => Equals(s.Deger, value))
            ? "ozel"
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}

/// <summary>bool → görünürlük (Ters: true iken gizli).</summary>
public sealed class DogruysaGorunur : IValueConverter
{
    public bool Ters { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) != Ters ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Sayı kutuları: virgül de nokta da ondalık ayırıcı sayılır (binlik ayırıcı yok), ekranda Türkçe virgül görünür.
/// Boş kutu: değer yok (ön ayar / otomatik). Hatalı yazım kaynağa yazılmaz; kutudan çıkınca eski değer geri gelir.
/// </summary>
public sealed class Sayi : IValueConverter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => string.Empty,
        double d => d.ToString("0.###", Tr),
        float f => ((double)f).ToString("0.###", Tr),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? string.Empty).Trim().Replace(',', '.');
        var nullable = Nullable.GetUnderlyingType(targetType);
        var tip = nullable ?? targetType;
        if (text.Length == 0)
        {
            return nullable is not null ? null : Binding.DoNothing;
        }

        if (tip == typeof(int))
        {
            return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i)
                ? i
                : Binding.DoNothing;
        }

        if (tip == typeof(double))
        {
            return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
                   && double.IsFinite(d)
                ? d
                : Binding.DoNothing;
        }

        return value;
    }
}
