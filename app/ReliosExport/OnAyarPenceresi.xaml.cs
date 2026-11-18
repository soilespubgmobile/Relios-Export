using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>
/// Önerilen ayarlar (programla gelir, yalnızca yüklenir; bkz. <see cref="Varsayilanlar.Onerilenler"/>) ve adıyla kaydedilen
/// ayarlar: şu anki ayarları kaydet, kayıtlı olanı yükle ya da sil. Yüklenen ayar, hangi profilde kaydedildiğine
/// bakılmadan şu anki profile uygulanır. İki seçenek (0.15.0, kullanıcının isteği: "açınca ön ayarlar seçeneği olsun
/// tıklayıp görelim"): "Önerilen" (gruplu kartlar) ve "Kayıtlı ayarlarım"; tıklanan liste görünür. Açılınca kayıtlı ön
/// ayar varsa onlar, yoksa önerilenler; kaydedince kayıtlılar.
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
        Aciklama.Text = $"Yüklenen ayar şu anki profile ({profil.DisplayName}) uygulanır. Önerilen ayarlar programla gelir; "
                        + "kayıtlı ayarlar listesinde yalnızca buradan kaydedilenler durur.";
        // Programın kendi listesi: ayar dosyasına yazılmaz, kullanıcının ön ayarlarına karışmaz. Gruplu (her platform /
        // örnek dosyalardan), sırası listedeki gibi.
        var onerilenler = Varsayilanlar.Onerilenler();
        var gorunum = new ListCollectionView(onerilenler.ToList());
        gorunum.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OnerilenAyar.Grup)));
        OnerilenListe.ItemsSource = gorunum;
        SekmeOnerilen.Content = $"Önerilen ({onerilenler.Count})";
        Yenile();
        (_settings.OnAyarlar.Count > 0 ? SekmeKayitli : SekmeOnerilen).IsChecked = true;
        Loaded += (_, _) => AdKutusu.Focus();
    }

    /// <summary>Hangi liste görünüyor: önerilenler mi (yoksa kayıtlılar).</summary>
    internal bool OnerilenGorunuyor => OnerilenPanel.Visibility == Visibility.Visible;

    private void OnSekme(object sender, RoutedEventArgs e)
    {
        // XAML'de hiçbiri seçili değil: ilk seçim kurucuda, paneller kurulduktan sonra.
        var onerilen = SekmeOnerilen.IsChecked == true;
        OnerilenPanel.Visibility = onerilen ? Visibility.Visible : Visibility.Collapsed;
        KayitliPanel.Visibility = onerilen ? Visibility.Collapsed : Visibility.Visible;
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
        // Listede yalnızca kullanıcının kaydettikleri durur; program kendiliğinden ön ayar eklemez.
        var liste = _settings.OnAyarlar
            .OrderBy(o => o.Profil != _profil.Id)
            .ThenBy(o => o.Ad, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Liste.ItemsSource = liste;
        BosListe.Visibility = liste.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SekmeKayitli.Content = $"Kayıtlı ayarlarım ({liste.Count})";
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
        SekmeKayitli.IsChecked = true;
    }

    private void OnYukle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is KayitliOnAyar o)
        {
            Yuklenen = o.Ayarlar.Kopya();
            DialogResult = true;
        }
    }

    private void OnOneriliYukle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OnerilenAyar o)
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
