using System.Windows;
using ReliosExport.Arayuz;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>NVENC H.264 (ekran kartı) ayarları. Pencere kopya üzerinde çalışır: "Vazgeç" bir şey değiştirmez.</summary>
public partial class NvencPenceresi : Window
{
    private readonly ProfilAyarlari _onizleme;
    private readonly double _fps;

    public NvencPenceresi(ProfilAyarlari profil)
    {
        InitializeComponent();

        _onizleme = profil.Kopya();
        _fps = PencereOrtak.OnizlemeKareHizi(profil);
        KomutNotu.Text = PencereOrtak.KomutNotu(profil).Replace("(keyint, min-keyint, scenecut)", "(-g, sahne değişimi)");
        Sonuc = AyarDosyasi.Kopyala(profil.Video.Nvenc);
        Sonuc.Degisti += (_, _) => KomutuYaz();
        _onizleme.Video.Nvenc = Sonuc;
        DataContext = Sonuc;
        KomutuYaz();
    }

    /// <summary>"Tamam" ile kapanınca kullanılacak ayarlar.</summary>
    public NvencAyarlari Sonuc { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private void KomutuYaz() => KomutKutusu.Text = ArgsBuilder.NvencKomutMetni(_onizleme, _fps);

    private void OnTamam(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        DialogResult = true;
    }
}
