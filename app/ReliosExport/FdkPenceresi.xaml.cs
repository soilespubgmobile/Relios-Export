using System.Windows;
using ReliosExport.Arayuz;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>FDK AAC ayarları. Pencere kopya üzerinde çalışır.</summary>
public partial class FdkPenceresi : Window
{
    private readonly SesAyarlari _onizleme;

    public FdkPenceresi(ProfilAyarlari profil)
    {
        InitializeComponent();

        _onizleme = AyarDosyasi.Kopyala(profil.Ses);
        _onizleme.Kodlayici = SesKodlayici.FdkAac;
        Sonuc = AyarDosyasi.Kopyala(profil.Ses.Fdk);
        Sonuc.Degisti += (_, _) => KomutuYaz();
        _onizleme.Fdk = Sonuc;
        DataContext = Sonuc;
        KomutuYaz();
    }

    /// <summary>"Tamam" ile kapanınca kullanılacak ayarlar.</summary>
    public FdkAyarlari Sonuc { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private void KomutuYaz() => KomutKutusu.Text = ArgsBuilder.SesKomutMetni(_onizleme);

    private void OnTamam(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        DialogResult = true;
    }
}
