using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ReliosExport.Core.Ayarlar;

/// <summary>
/// Arayüzün bağlandığı ayar nesnelerinin tabanı. Bir özellik değişince hem kendisi hem de
/// <see cref="Ozet"/> bildirilir; <see cref="Degisti"/> üst nesneye (kaydetmek için) iletilir.
/// </summary>
public abstract class Gozlenebilir : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bu nesnede ya da alt nesnelerinde bir ayar değişti.</summary>
    public event EventHandler? Degisti;

    /// <summary>Panel satırında görünen kısa özet.</summary>
    [JsonIgnore]
    public virtual string Ozet => string.Empty;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Bildir(name);
        return true;
    }

    /// <summary>Alt ayar nesnesini bağlar: onun değişiklikleri bu nesnenin değişikliği sayılır.</summary>
    protected T Bagla<T>(ref T field, T value, [CallerMemberName] string? name = null)
        where T : Gozlenebilir
    {
        if (ReferenceEquals(field, value))
        {
            return value;
        }

        if (field is not null)
        {
            field.Degisti -= AltDegisti;
        }

        field = value;
        field.Degisti += AltDegisti;
        Bildir(name);
        return value;
    }

    /// <summary>Kurucuda oluşturulan alt ayar nesnesini izlemeye başlar.</summary>
    protected void Izle(Gozlenebilir alt) => alt.Degisti += AltDegisti;

    protected void Bildir(string? name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name != nameof(Ozet))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ozet)));
        }

        foreach (var ek in EkBildirimler(name))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ek));
        }

        Degisti?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bir özellik değişince ayrıca bildirilecek hesaplanan özellikler.</summary>
    protected virtual IEnumerable<string> EkBildirimler(string? name) => [];

    private void AltDegisti(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ozet)));
        foreach (var ek in EkBildirimler(null))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ek));
        }

        Degisti?.Invoke(this, EventArgs.Empty);
    }
}
