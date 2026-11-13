using System.Windows;
using System.Windows.Input;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>
/// Adıyla kaydedilen ayarlar: şu anki ayarları kaydet, kayıtlı olanı yükle ya da sil.
/// Yüklenen ön ayar, hangi profilde kaydedildiğine bakılmadan şu anki profile uygulanır.
/// </summary>
public partial class OnAyarPenceresi : Window
{
    private readonly AppSettings _settings;
    private readonly ExportProfile _profil;
    private readonly ProfilAyarlari _simdiki;

    public OnAyarPenceresi(AppSettings settings, ExportProfile profil, ProfilAyarlari simdiki)
    {
        InitializeComponent();
        _settings = settings;
        _profil = profil;
        _simdiki = simdiki;
        Aciklama.Text = $"Yüklenen ön ayar şu anki profile ({profil.DisplayName}) uygulanır. "
                        + "Ön ayarlar program klasöründeki ayarlar.json dosyasında durur.";
        Yenile();
        Loaded += (_, _) => AdKutusu.Focus();
    }

    /// <summary>"Yükle" ile kapanınca uygulanacak ayarlar (kopya).</summary>
    public ProfilAyarlari? Yuklenen { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private void Yenile()
    {
        Liste.ItemsSource = null;
        Liste.ItemsSource = _settings.OnAyarlar
            .OrderBy(o => o.Profil != _profil.Id)
            .ThenBy(o => o.Ad, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        BosListe.Visibility = _settings.OnAyarlar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAdKutusuKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnKaydet(sender, e);
            e.Handled = true;
        }
    }

    private void OnKaydet(object sender, RoutedEventArgs e)
    {
        var ad = AdKutusu.Text.Trim();
        if (ad.Length == 0)
        {
            Bilgi.Text = "Önce bir ad yazın.";
            AdKutusu.Focus();
            return;
        }

        var mevcut = _settings.OnAyarlar.FirstOrDefault(o => string.Equals(o.Ad, ad, StringComparison.CurrentCultureIgnoreCase));
        if (mevcut is not null)
        {
            if (!ConfirmDialog.Ask(this, $"\"{mevcut.Ad}\" zaten var", "Şu anki ayarlar bu ön ayarın üzerine yazılsın mı?", "Üzerine yaz", "Vazgeç"))
            {
                return;
            }

            mevcut.Profil = _profil.Id;
            mevcut.Ayarlar = _simdiki.Kopya();
        }
        else
        {
            _settings.OnAyarlar.Add(new KayitliOnAyar { Ad = ad, Profil = _profil.Id, Ayarlar = _simdiki.Kopya() });
        }

        _settings.Save();
        AdKutusu.Clear();
        Bilgi.Text = $"\"{ad}\" kaydedildi.";
        Yenile();
    }

    private void OnYukle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is KayitliOnAyar o)
        {
            Yuklenen = o.Ayarlar.Kopya();
            DialogResult = true;
        }
    }

    private void OnSil(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not KayitliOnAyar o)
        {
            return;
        }

        if (!ConfirmDialog.Ask(this, $"\"{o.Ad}\" silinsin mi?", "Bu ön ayar kalıcı olarak silinir.", "Sil", "Vazgeç"))
        {
            return;
        }

        _settings.OnAyarlar.Remove(o);
        _settings.Save();
        Bilgi.Text = $"\"{o.Ad}\" silindi.";
        Yenile();
    }
}
