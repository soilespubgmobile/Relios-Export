using System.Windows;
using ReliosExport.Arayuz;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>
/// x264'ün bütün ayarları (XviD4PSP'deki x264 penceresi gibi sekmeli).
/// Pencere ayarların bir kopyası üzerinde çalışır: "Vazgeç" hiçbir şeyi değiştirmez.
/// </summary>
public partial class X264Penceresi : Window
{
    private readonly ProfilAyarlari _onizleme;
    private readonly double _fps;

    public X264Penceresi(ProfilAyarlari profil)
    {
        InitializeComponent();

        // Komut önizlemesi anahtar kare ve kare hızı ayarlarını da kullanır: profilin kopyası üzerinde hesaplanır.
        _onizleme = profil.Kopya();
        _fps = PencereOrtak.OnizlemeKareHizi(profil);
        KomutNotu.Text = PencereOrtak.KomutNotu(profil);
        Bagla(AyarDosyasi.Kopyala(profil.Video.X264));
    }

    /// <summary>"Tamam" ile kapanınca kullanılacak ayarlar.</summary>
    public X264Ayarlari Sonuc { get; private set; } = null!;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private void Bagla(X264Ayarlari x)
    {
        if (Sonuc is not null)
        {
            Sonuc.Degisti -= OnDegisti;
        }

        Sonuc = x;
        Sonuc.Degisti += OnDegisti;
        _onizleme.Video.X264 = x;
        DataContext = x;
        KomutuYaz();
    }

    private void OnDegisti(object? sender, EventArgs e) => KomutuYaz();

    private void KomutuYaz() => KomutKutusu.Text = ArgsBuilder.X264KomutMetni(_onizleme, _fps);

    private void OnOnAyaraBirak(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        var x = Sonuc;
        Bagla(new X264Ayarlari
        {
            OnAyar = x.OnAyar,
            Ayar = x.Ayar,
            Profil = x.Profil,
            Seviye = x.Seviye,
            Mod = x.Mod,
            Crf = x.Crf,
            Qp = x.Qp,
            BitHizi = x.BitHizi,
            HizliIlkGecis = x.HizliIlkGecis,
            IsParcacigi = x.IsParcacigi,
            IleriBakisIsParcacigi = x.IleriBakisIsParcacigi,
        });
    }

    private void OnTamam(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        DialogResult = true;
    }
}
