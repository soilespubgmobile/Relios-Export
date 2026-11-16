using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Arayuz;

/// <summary>
/// Yalnızca GitHub'daki arayüz taraması (--arayuz-taramasi). Ana penceredeki ayar panelinin ve x264 / NVENC /
/// FDK AAC / ön ayar pencerelerinin bütün kutularını, açılır listelerini, onay kutularını ve düğmelerini arayüzden
/// (kullanıcının değiştirdiği yoldan: kutuya yazıp Enter, listeden seçim, onay kutusu, düğmeye basma) tek tek dener:
/// <list type="bullet">
/// <item>ayar gerçekten değişiyor mu, ekranda ayarın geçerli değeri görünüyor mu;</item>
/// <item>değişiklik bir şeye yansıyor mu (FFmpeg komutu ya da satır özeti);</item>
/// <item>değişen komut FFmpeg'de gerçekten çalışıyor mu (küçük deneme videosuyla; NVENC GitHub'da yok);</item>
/// <item>uç değerler (çok büyük / çok küçük sayı) FFmpeg'i bozuyor mu, hatalı yazım ayara yazılmıyor mu, boş kutu;</item>
/// <item>pencerelerde Vazgeç hiçbir şeyi değiştirmiyor, Tamam uyguluyor ve ayar dosyasına yazılıyor mu;</item>
/// <item>"Tümünü hız ayarına bırak", ön ayar kaydet / üzerine yaz / yükle / sil, Varsayılan (onay penceresiyle);</item>
/// <item>WPF bağlama (binding) hatası var mı.</item>
/// </list>
/// Sonunda denenmemiş kutu kalmadığı denetlenir. Ayarlar her denemeden sonra eski hâline döner (o da denetlenir).
/// </summary>
internal static class ArayuzTaramasi
{
    private const string GecisAdi = "tarama-gecis";

    /// <summary>Ön ayar adı: I / ı / İ / i yok (büyük-küçük harf eşleşmesi işletim sisteminin diline bağlı olmasın).</summary>
    private const string OnAyarAdi = "Deneme ön ses";

    /// <summary>WPF bağlama iletilerini toplar; pencereler açılmadan önce kurulur.</summary>
    internal sealed class BaglamaDinleyici : TraceListener
    {
        private readonly StringBuilder _satir = new();

        public List<string> Iletiler { get; } = [];

        public override void Write(string? message) => _satir.Append(message);

        public override void WriteLine(string? message)
        {
            _satir.Append(message);
            Iletiler.Add(_satir.ToString());
            _satir.Clear();
        }
    }

    public static BaglamaDinleyici BaglamaIletileriniDinle()
    {
        var d = new BaglamaDinleyici();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(d);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        return d;
    }

    /// <summary>Denenecek bir kutu / liste / onay kutusu: hangi pencerede, hangi ayara bağlı.</summary>
    private sealed record Denetim(FrameworkElement Oge, string Anahtar, TabItem? Sekme);

    /// <summary>Denetimin bulunduğu yer: ayarın kendisi, etkisi (komut + özet) ve FFmpeg denemesi.</summary>
    private sealed record Ortam(string Ad, Func<string> Model, Func<Task<string>> Durum, Func<Task<string?>>? Calistir);

    /// <summary>Kodlama denenmedi (NVENC: GitHub'da ekran kartı yok).</summary>
    private const string Atlandi = "\u0000atlandı";

    public static async Task<int> CalistirAsync(MainWindow w, string medya, string cikti, BaglamaDinleyici baglama, StringBuilder rapor)
    {
        var fark = 0;
        var sira = 0;
        var calisan = 0;
        var atlanan = 0;

        void Kontrol(string ad, object? beklenen, object? sonuc, bool ok)
        {
            sira++;
            rapor.AppendLine($"{(ok ? "OK  " : "FARK")} | {sira,4}. {ad} | beklenen: {beklenen} | sonuç: {sonuc}");
            if (!ok)
            {
                fark++;
            }
        }

        void Bilgi(string metin) => rapor.AppendLine("     | " + metin);

        void Durumlu(string ad, bool ok, string iyi, string kotu) => Kontrol(ad, iyi, ok ? iyi : kotu, ok);

        void Bolum(string ad) => rapor.AppendLine().AppendLine("--- " + ad);

        Directory.CreateDirectory(cikti);
        var kaynak = Path.Combine(medya, "minik.mp4");
        var gecis = Path.Combine(cikti, GecisAdi);
        rapor.AppendLine("Arayüz taraması · deneme videosu: " + kaynak);
        var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, kaynak, CancellationToken.None);
        rapor.AppendLine($"Kaynak: {media.Width}×{media.Height} · {media.FrameRate:0.###} fps · {media.DurationSeconds:0.##} sn · ses izi {media.SesAkislari.Count}");

        string CiktiYolu(ProfilAyarlari a) => Path.Combine(cikti, "tarama" + ArgsBuilder.Uzanti(a));

        async Task<string> Komut(ProfilAyarlari a)
        {
            var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, a.Ses, media, kaynak, CancellationToken.None);
            var plan = ArgsBuilder.Olustur(a, media, kaynak, ses, CiktiYolu(a), gecis);
            return string.Join("\n", plan.Gecisler.Select(g => ArgsBuilder.KomutMetni(g)));
        }

        // Komutu FFmpeg'de çalıştırır (x264 ve FDK AAC GitHub'da çalışır; NVENC için ekran kartı yok: atlanır).
        // Sonuç: null = çalıştı (ya da atlandı), değilse hata yazısı.
        async Task<string?> Kodla(ProfilAyarlari a)
        {
            if (a.Video.Kodlayici == VideoKodlayici.Nvenc)
            {
                atlanan++;
                return Atlandi;
            }

            var cikis = CiktiYolu(a);
            try
            {
                var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, a.Ses, media, kaynak, CancellationToken.None);
                var plan = ArgsBuilder.Olustur(a, media, kaynak, ses, cikis, gecis);
                foreach (var g in plan.Gecisler)
                {
                    await FfmpegRunner.RunAsync(g, media.DurationSeconds, null, CancellationToken.None);
                }

                calisan++;
                return File.Exists(cikis) && new FileInfo(cikis).Length > 0 ? null : "çıktı dosyası oluşmadı";
            }
            catch (FfmpegHatasi ex)
            {
                return Kisa(ex.Message, 900);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception or ArgumentException)
            {
                return ex.GetType().Name + ": " + Kisa(ex.Message);
            }
            finally
            {
                SessizSil(cikis);
            }
        }

        async Task<string> AnaDurum()
        {
            await Bosta(w);
            var sb = new StringBuilder();
            foreach (var exp in Mantiksal(w.AyarPaneli).OfType<Expander>())
            {
                sb.Append(exp.Header).Append(" = ").Append(exp.Tag).Append('\n');
            }

            foreach (var t in w.KontrolEtiketler)
            {
                sb.Append(t.Baslik).Append(" = ").Append(t.Deger).Append(" / ").Append(t.Ayrinti).Append('\n');
            }

            return sb + await Komut(w.KontrolAyarlari);
        }

        async Task Calistir(Ortam o, string ad)
        {
            if (o.Calistir is null)
            {
                atlanan++;
                return;
            }

            var hata = await o.Calistir();
            if (hata != Atlandi)
            {
                Kontrol(ad + ": FFmpeg'de çalışıyor", "çalıştı", hata ?? "çalıştı", hata is null);
            }
        }

        var anaOrtam = new Ortam("Ana pencere", () => AyarDosyasi.Yaz(w.KontrolAyarlari), AnaDurum, () => Kodla(w.KontrolAyarlari));

        // ------------------------------------------------------------------ tek denetim denemeleri

        async Task KutuDene(TextBox tb, Ortam o, string anahtar)
        {
            var (kaynakNesne, ozellik) = Kaynak(tb.GetBindingExpression(TextBox.TextProperty));
            var tip = ozellik?.PropertyType;
            var bosOlur = tip is not null && (Nullable.GetUnderlyingType(tip) is not null || tip == typeof(string));
            var ilkMetin = tb.Text;
            var ilkModel = o.Model();
            var ilkDurum = await o.Durum();

            // 1) Geçerli bir değer: ayar değişmeli, ekranda ayarın değeri görünmeli, komuta / özete yansımalı, çalışmalı.
            string? secilen = null;
            foreach (var aday in Adaylar(anahtar, tip, ozellik?.GetValue(kaynakNesne)))
            {
                await Yaz(w, tb, aday);
                if (o.Model() != ilkModel)
                {
                    secilen = aday;
                    break;
                }
            }

            Kontrol($"{anahtar}: yazılan değer ayara geçiyor", "değişti", secilen is null ? "değişmedi" : $"{secilen} → {tb.Text}", secilen is not null);
            if (secilen is not null)
            {
                var gercek = Bicimle(ozellik?.GetValue(kaynakNesne));
                Kontrol($"{anahtar}: kutuda ayarın değeri görünüyor", gercek, tb.Text, tb.Text == gercek);
                Durumlu($"{anahtar} = {tb.Text}: komuta / özete yansıyor", await o.Durum() != ilkDurum, "farklı", "aynı");
                await Calistir(o, $"{anahtar} = {tb.Text}");
            }

            // 2) Uç değerler: ayar sınırlanır ya da kabul edilir; her durumda komut çalışmalı.
            if (tip is not null && tip != typeof(string))
            {
                foreach (var uc in new[] { "99999999", "-99999999" })
                {
                    await Yaz(w, tb, uc);
                    if (o.Model() == ilkModel)
                    {
                        Bilgi($"{anahtar} = {uc}: ayar değişmedi (kutuda {tb.Text})");
                        continue;
                    }

                    if (YavasUc(anahtar, uc))
                    {
                        Bilgi($"{anahtar} = {uc} → {tb.Text}: çok büyük görüntü, FFmpeg denemesi atlandı");
                        continue;
                    }

                    await Calistir(o, $"{anahtar} = {uc} yazılınca ({tb.Text})");
                }
            }

            // 3) Hatalı yazım ayara yazılmaz, kutuda eski değer kalır.
            await Yaz(w, tb, ilkMetin);
            var oncesi = o.Model();
            if (tip is not null && tip != typeof(string))
            {
                await Yaz(w, tb, "abc");
                Durumlu($"{anahtar}: hatalı yazım (abc) ayara yazılmıyor", o.Model() == oncesi, "aynı", "değişti");
                Kontrol($"{anahtar}: hatalı yazımdan sonra kutuda eski değer", ilkMetin, tb.Text, tb.Text == ilkMetin);
            }

            // 4) Boş kutu: değeri olmayan ayar (otomatik / varsayılan) boş kalır; zorunlu değer değişmez.
            await Yaz(w, tb, string.Empty);
            if (bosOlur)
            {
                var deger = ozellik?.GetValue(kaynakNesne);
                Kontrol($"{anahtar}: boş kutu → değer yok (otomatik)", "boş", Bicimle(deger),
                    deger is null || (deger is string s && s.Length == 0));
                await Calistir(o, $"{anahtar} boşken");
            }
            else
            {
                Kontrol($"{anahtar}: boş kutu zorunlu ayarı değiştirmiyor", ilkMetin, tb.Text, o.Model() == oncesi && tb.Text == ilkMetin);
            }

            // 5) Eski değer.
            await Yaz(w, tb, ilkMetin);
            Durumlu($"{anahtar}: eski değere dönüldü", o.Model() == ilkModel, "aynı", "farklı");
        }

        TextBox AnaKutu(string yol) =>
            Mantiksal(w.AyarPaneli).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == yol);

        async Task ListeDene(ComboBox cb, Ortam o, string anahtar)
        {
            (int G, int Y)? ilkBoyut = anahtar.EndsWith(".Video.BoyutSecimi", StringComparison.Ordinal)
                ? (w.KontrolAyarlari.Video.Genislik, w.KontrolAyarlari.Video.Yukseklik)
                : null;
            var ilkIndeks = cb.SelectedIndex;
            var ilkModel = o.Model();
            var ilkDurum = await o.Durum();
            var etkili = 0;
            var ayni = new List<string>();
            for (var i = 0; i < cb.Items.Count; i++)
            {
                if (i == ilkIndeks)
                {
                    continue;
                }

                var ad = (cb.Items[i] as Secenek)?.Ad ?? cb.Items[i]?.ToString() ?? i.ToString(CultureInfo.InvariantCulture);
                cb.SelectedIndex = i;
                await Bosta(w);

                // Ekrandaki seçim ayarın kendisiyle aynı mı (ayardan yeniden okununca seçim değişmemeli).
                var gorunen = cb.SelectedIndex;
                cb.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
                await Bosta(w);
                Kontrol($"{anahtar} → {ad}: ekrandaki seçim ayarla aynı", gorunen, cb.SelectedIndex, cb.SelectedIndex == gorunen);
                Durumlu($"{anahtar} → {ad}: ayar değişiyor", o.Model() != ilkModel, "değişti", "değişmedi");
                if (await o.Durum() != ilkDurum)
                {
                    etkili++;
                }
                else
                {
                    ayni.Add(ad);
                }

                await Calistir(o, $"{anahtar} → {ad}");
            }

            cb.SelectedIndex = ilkIndeks;
            await Bosta(w);

            // "Özel boyut"a dönünce kutulardaki boyut korunur (son seçilen boyut kalır): kullanıcı gibi eski boyut yazılır.
            if (o.Model() != ilkModel && anahtar.EndsWith(".Video.BoyutSecimi", StringComparison.Ordinal) && ilkBoyut is { } eski)
            {
                Durumlu($"{anahtar}: Özel boyut'a dönünce liste orada kalıyor", cb.SelectedValue as string == "ozel", "Özel boyut", cb.SelectedValue?.ToString() ?? "?");
                await Yaz(w, AnaKutu("Video.Genislik"), eski.G.ToString(CultureInfo.InvariantCulture));
                await Yaz(w, AnaKutu("Video.Yukseklik"), eski.Y.ToString(CultureInfo.InvariantCulture));
            }

            Kontrol($"{anahtar}: seçenekler komuta / özete yansıyor", "en az biri", etkili + " seçenek", etkili > 0 || cb.Items.Count <= 1);
            if (ayni.Count > 0)
            {
                Bilgi($"{anahtar}: bu ortamda komutu / özeti değiştirmeyen seçenek: {string.Join(", ", ayni)}");
            }

            // İlk seçime dönünce ayar aynı olmalı; değilse en azından komut ve özet aynı (ör. "Kaynakla aynı" kare hızında
            // son seçilen sabit hız ayar dosyasında kalır ama kullanılmaz).
            var donusModel = o.Model() == ilkModel;
            var donusDurum = donusModel || await o.Durum() == ilkDurum;
            if (!donusModel && donusDurum)
            {
                Bilgi($"{anahtar}: ilk seçime dönünce kullanılmayan bir değer ayarda kaldı (komut ve özet aynı)");
            }

            Durumlu($"{anahtar}: ilk seçime dönüldü (komut ve özet eskisi gibi)", donusDurum, "aynı", "farklı");
        }

        async Task OnayDene(CheckBox ch, Ortam o, string anahtar)
        {
            var ilk = ch.IsChecked;
            var ilkModel = o.Model();
            var ilkDurum = await o.Durum();
            var durumlar = ch.IsThreeState ? new bool?[] { true, false, null } : [true, false];
            foreach (var d in durumlar.Where(x => x != ilk))
            {
                var ad = d switch { true => "açık", false => "kapalı", _ => "hız ayarına göre" };
                ch.IsChecked = d;
                await Bosta(w);
                Durumlu($"{anahtar} = {ad}: ayar değişiyor", o.Model() != ilkModel, "değişti", "değişmedi");
                Durumlu($"{anahtar} = {ad}: komuta / özete yansıyor", await o.Durum() != ilkDurum, "farklı", "aynı");
                await Calistir(o, $"{anahtar} = {ad}");
            }

            ch.IsChecked = ilk;
            await Bosta(w);
            Durumlu($"{anahtar}: ilk duruma dönüldü", o.Model() == ilkModel, "aynı", "farklı");
        }

        async Task Dene(Denetim d, Ortam o)
        {
            switch (d.Oge)
            {
                case TextBox tb:
                    await KutuDene(tb, o, d.Anahtar);
                    break;
                case ComboBox cb:
                    await ListeDene(cb, o, d.Anahtar);
                    break;
                case CheckBox ch:
                    await OnayDene(ch, o, d.Anahtar);
                    break;
            }
        }

        // Görünen ve etkin, henüz denenmemiş denetimleri dener (sekmesi varsa önce sekme seçilir).
        var denenen = new HashSet<string>(StringComparer.Ordinal);
        async Task Gecis(IReadOnlyList<Denetim> liste, Ortam o, FrameworkElement kok)
        {
            foreach (var d in liste)
            {
                if (denenen.Contains(d.Anahtar))
                {
                    continue;
                }

                if (d.Sekme is { IsSelected: false } sekme)
                {
                    sekme.IsSelected = true;
                }

                kok.UpdateLayout();
                await Bosta(w);
                if (!d.Oge.IsVisible || !d.Oge.IsEnabled)
                {
                    continue;
                }

                denenen.Add(d.Anahtar);
                await Dene(d, o);
            }
        }

        // ------------------------------------------------------------------ 1. Ana pencere: ayar paneli
        w.KontrolCiktiKlasoru(cikti);
        var anaDenetimler = Denetimler(w.AyarPaneli, "Ana");
        rapor.AppendLine($"Ana paneldeki denetim: {anaDenetimler.Count} (kutu {anaDenetimler.Count(d => d.Oge is TextBox)}, liste {anaDenetimler.Count(d => d.Oge is ComboBox)}, onay kutusu {anaDenetimler.Count(d => d.Oge is CheckBox)})");

        ProfilAyarlari Ozel(ProfilAyarlari a)
        {
            // Ölçekleme ve kırpma her zaman çalışsın (16:9 kaynak → 9:16 küçük hedef): ölçekleme ayarlarının etkisi görünür.
            a.Video.Cozunurluk = CozunurlukModu.Hedef;
            a.Video.Genislik = 180;
            a.Video.Yukseklik = 320;
            return a;
        }

        var baglamlar = new (string Ad, ExportProfileId Profil, Func<ProfilAyarlari> Hazirla)[]
        {
            ("YouTube · x264 CRF · FDK AAC CBR · 180×320 · eşitleme ve sınırlayıcı açık", ExportProfileId.YouTube, () => Ozel(Varsayilanlar.YouTube())),
            ("YouTube · x264 sabit QP", ExportProfileId.YouTube, () =>
            {
                var a = Ozel(Varsayilanlar.YouTube());
                a.Video.X264.Mod = X264Modu.SabitQp;
                return a;
            }),
            ("YouTube · x264 ortalama bit hızı", ExportProfileId.YouTube, () =>
            {
                var a = Ozel(Varsayilanlar.YouTube());
                a.Video.X264.Mod = X264Modu.OrtalamaBitHizi;
                return a;
            }),
            ("YouTube · ses WAV", ExportProfileId.YouTube, () =>
            {
                var a = Ozel(Varsayilanlar.YouTube());
                a.Ses.Kodlayici = SesKodlayici.Wav;
                return a;
            }),
            ("TikTok · NVENC CBR · WAV (GitHub'da ekran kartı yok: komut denetlenir)", ExportProfileId.TikTok, () => Ozel(Varsayilanlar.TikTok())),
            ("TikTok · NVENC sabit kalite", ExportProfileId.TikTok, () =>
            {
                var a = Ozel(Varsayilanlar.TikTok());
                a.Video.Nvenc.Mod = NvencModu.SabitKalite;
                return a;
            }),
            ("TikTok · NVENC sabit QP", ExportProfileId.TikTok, () =>
            {
                var a = Ozel(Varsayilanlar.TikTok());
                a.Video.Nvenc.Mod = NvencModu.SabitQp;
                return a;
            }),
            ("TikTok · NVENC VBR", ExportProfileId.TikTok, () =>
            {
                var a = Ozel(Varsayilanlar.TikTok());
                a.Video.Nvenc.Mod = NvencModu.DegiskenBitHizi;
                return a;
            }),
        };

        async Task BaglamaGec(ExportProfileId profil, ProfilAyarlari a)
        {
            if (profil == ExportProfileId.YouTube)
            {
                w.RbYouTube.IsChecked = true;
            }
            else
            {
                w.RbTikTok.IsChecked = true;
            }

            await Bosta(w);
            w.KontrolProfilYukle(a);
            w.KontrolIcinTumSatirlariAc();
            w.UpdateLayout();
            await Bosta(w);
        }

        foreach (var (ad, profil, hazirla) in baglamlar)
        {
            Bolum("Ana panel · " + ad);
            await BaglamaGec(profil, hazirla());
            await Gecis(anaDenetimler, anaOrtam, w);

            // Bu bağlamda görünen ayrıntılı ayar penceresi düğmeleri.
            if (ad.StartsWith("YouTube · x264 CRF", StringComparison.Ordinal))
            {
                await X264PenceresiniDene();
                await FdkPenceresiniDene();
            }
            else if (ad.StartsWith("TikTok · NVENC CBR", StringComparison.Ordinal))
            {
                await NvencPenceresiniDene();
            }
        }

        Bolum("Ana panel · denenmeyen denetim kalmadı mı");
        foreach (var d in anaDenetimler.Where(x => !denenen.Contains(x.Anahtar)))
        {
            Durumlu($"{d.Anahtar}: denendi", false, "denendi", "hiçbir bağlamda görünür / etkin olmadı");
        }

        Kontrol("Ana paneldeki bütün denetimler denendi", anaDenetimler.Count, anaDenetimler.Count(d => denenen.Contains(d.Anahtar)),
            anaDenetimler.All(d => denenen.Contains(d.Anahtar)));

        // ------------------------------------------------------------------ 1b. "Özel boyut" seçimi
        Bolum("Boyut listesi: \"Özel boyut\" seçilince liste orada kalır, genişlik / yükseklik kutuları açılır");
        var yt = Varsayilanlar.YouTube();
        yt.Video.Cozunurluk = CozunurlukModu.Kaynak;
        await BaglamaGec(ExportProfileId.YouTube, yt);
        var boyutListesi = anaDenetimler.Select(d => d.Oge).OfType<ComboBox>()
            .First(c => c.GetBindingExpression(Selector.SelectedValueProperty)?.ParentBinding.Path.Path == "Video.BoyutSecimi");
        var genislikKutusu = AnaKutu("Video.Genislik");
        Esit2("Kaynakla aynı: genişlik kutusu gizli", false, genislikKutusu.IsVisible);
        boyutListesi.SelectedValue = "ozel";
        await Bosta(w);
        Esit2("Kaynakla aynı → Özel boyut: listede Özel boyut kalıyor", "ozel", boyutListesi.SelectedValue);
        Esit2("Özel boyut: genişlik kutusu görünüyor", true, genislikKutusu.IsVisible);
        await Yaz(w, genislikKutusu, "1000");
        Esit2("Özel boyut: yazılan genişlik ayara geçti", 1000, w.KontrolAyarlari.Video.Genislik);
        Esit2("Özel boyut: liste yine Özel boyut", "ozel", boyutListesi.SelectedValue);
        boyutListesi.SelectedValue = "1080x1920";
        await Bosta(w);
        Esit2("1080×1920 seçildi: kutularda 1080 × 1920", "1080×1920", $"{genislikKutusu.Text}×{AnaKutu("Video.Yukseklik").Text}");
        boyutListesi.SelectedValue = "ozel";
        await Bosta(w);
        Esit2("1080×1920 → Özel boyut: listede Özel boyut kalıyor (1080×1920'ye atlamıyor)", "ozel", boyutListesi.SelectedValue);
        Esit2("1080×1920 → Özel boyut: kutularda 1080 × 1920 kaldı", "1080×1920", $"{genislikKutusu.Text}×{AnaKutu("Video.Yukseklik").Text}");
        var hataOzel = await Kodla(w.KontrolAyarlari);
        Kontrol("Özel boyut 1080×1920: FFmpeg'de çalışıyor", "çalıştı", hataOzel ?? "çalıştı", hataOzel is null);

        // ------------------------------------------------------------------ 2. Kutularda Enter / Esc / odaktan çıkma
        Bolum("Kutular: Enter uygular, Esc geri alır, odaktan çıkınca uygulanır");
        await BaglamaGec(ExportProfileId.YouTube, Ozel(Varsayilanlar.YouTube()));
        var gecikmeKutusu = anaDenetimler.Select(d => d.Oge).OfType<TextBox>()
            .First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Ses.Gecikme");
        var seviyeKutusu = anaDenetimler.Select(d => d.Oge).OfType<TextBox>()
            .First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Ses.Seviye");
        gecikmeKutusu.Text = "250";
        Durumlu("Enter tuşu olayı gönderildi", Tus(gecikmeKutusu, Key.Enter), "gönderildi", "pencere yok");
        await Bosta(w);
        Esit2("Enter: gecikme ayara geçti", 250, w.KontrolAyarlari.Ses.Gecikme);
        gecikmeKutusu.Text = "777";
        Tus(gecikmeKutusu, Key.Escape);
        await Bosta(w);
        Esit2("Esc: yazılan geri alındı (kutu)", "250", gecikmeKutusu.Text);
        Esit2("Esc: ayar değişmedi", 250, w.KontrolAyarlari.Ses.Gecikme);
        gecikmeKutusu.Focus();
        gecikmeKutusu.Text = "300";
        seviyeKutusu.Focus();
        await Bosta(w);
        Esit2("Odaktan çıkınca (başka kutuya geçince) uygulandı", 300, w.KontrolAyarlari.Ses.Gecikme);
        gecikmeKutusu.Focus();
        gecikmeKutusu.Text = "12,5";
        seviyeKutusu.Focus();
        await Bosta(w);
        Esit2("Tam sayı kutusuna ondalık yazılınca ayar değişmiyor", 300, w.KontrolAyarlari.Ses.Gecikme);
        Esit2("Tam sayı kutusunda eski değer görünüyor", "300", gecikmeKutusu.Text);
        seviyeKutusu.Text = "-2,5";
        Tus(seviyeKutusu, Key.Enter);
        await Bosta(w);
        Esit2("Ondalık kutusunda virgül ondalık ayırıcı", -2.5, w.KontrolAyarlari.Ses.Seviye);
        seviyeKutusu.Text = "-3.5";
        Tus(seviyeKutusu, Key.Enter);
        await Bosta(w);
        Esit2("Ondalık kutusunda nokta da ondalık ayırıcı", -3.5, w.KontrolAyarlari.Ses.Seviye);
        Esit2("Ondalık kutusunda Türkçe virgülle görünüyor", "-3,5", seviyeKutusu.Text);

        // ------------------------------------------------------------------ 3. Varsayılan (onay penceresi)
        Bolum("Varsayılan düğmesi: onay penceresi (Vazgeç / Varsayılana dön)");
        var degisik = AyarDosyasi.Yaz(w.KontrolAyarlari);
        var onay = await DiyalogAc<ConfirmDialog>(w, w.BtnVarsayilan);
        Durumlu("Varsayılan: onay penceresi açıldı", onay is not null, "açıldı", "açılmadı");
        if (onay is not null)
        {
            Esit2("Onay penceresinde varsayılan seçim \"Vazgeç\"", true, onay.BtnNo.IsDefault);
            Bas(onay.BtnNo);
            await Kapandi(w, onay);
            Esit2("Vazgeç: ayarlar değişmedi", degisik, AyarDosyasi.Yaz(w.KontrolAyarlari));
        }

        onay = await DiyalogAc<ConfirmDialog>(w, w.BtnVarsayilan);
        if (onay is not null)
        {
            Bas(onay.BtnYes);
            await Kapandi(w, onay);
            Esit2("Varsayılana dön: YouTube fabrika ayarları", AyarDosyasi.Yaz(Varsayilanlar.YouTube()), AyarDosyasi.Yaz(w.KontrolAyarlari));
            await Task.Delay(300);
            var kayitli = AyarDosyasiOku(ExportProfileId.YouTube);
            Esit2("Varsayılana dön: ayar dosyasına da yazıldı", AyarDosyasi.Yaz(Varsayilanlar.YouTube()), kayitli);
        }

        // ------------------------------------------------------------------ 4. Ön ayarlar
        await OnAyarlariDene();

        // ------------------------------------------------------------------ 5. Bağlama hataları
        Bolum("WPF bağlama (binding) iletileri");
        var sinama = new TextBlock { DataContext = new X264Ayarlari() };
        sinama.SetBinding(TextBlock.TextProperty, new Binding("BoyleBirOzellikYok"));
        w.AyarPaneli.Children.Add(sinama);
        w.UpdateLayout();
        await Bosta(w);
        w.AyarPaneli.Children.Remove(sinama);
        var yakalandi = baglama.Iletiler.Any(i => i.Contains("BoyleBirOzellikYok", StringComparison.Ordinal));
        Durumlu("Bağlama hatası dinleyicisi çalışıyor (bilerek bozuk bağlama yakalandı)", yakalandi, "yakalandı", "yakalanmadı");
        var bizim = baglama.Iletiler.Where(i => BizimHatamiz(i) && !i.Contains("BoyleBirOzellikYok", StringComparison.Ordinal)).Distinct().ToList();
        rapor.AppendLine($"     | toplanan bağlama iletisi: {baglama.Iletiler.Count}");
        foreach (var ileti in baglama.Iletiler.Where(i => !i.Contains("BoyleBirOzellikYok", StringComparison.Ordinal)).Distinct().Take(40))
        {
            Bilgi(Kisa(ileti, 400));
        }

        Kontrol("Programın kendi bağlamalarında hata yok", 0, bizim.Count + (bizim.Count > 0 ? ": " + Kisa(bizim[0], 300) : string.Empty), bizim.Count == 0);

        rapor.AppendLine().AppendLine($"FFmpeg'de çalıştırılan komut: {calisan} · NVENC (ekran kartı yok) atlanan: {atlanan}");
        rapor.AppendLine($"Arayüz taraması: {sira} kontrol, {fark} fark.");
        SessizSil(Path.Combine(cikti, GecisAdi + "-0.log"));
        return fark;

        // ================================================================== yardımcılar (yerel)

        void Esit2(string ad, object? beklenen, object? sonuc) =>
            Kontrol(ad, beklenen, sonuc, Equals(beklenen?.ToString(), sonuc?.ToString()));

        // ------------------------------------------------------------------ x264 penceresi
        async Task X264PenceresiniDene()
        {
            Bolum("x264 penceresi (Tüm x264 ayarları…)");
            var dugme = Dugme(w.AyarPaneli, "Tüm x264 ayarları…");
            var ilkX264 = Json(w.KontrolAyarlari.Video.X264);
            var p = await DiyalogAc<X264Penceresi>(w, dugme);
            Durumlu("x264 penceresi açıldı", p is not null, "açıldı", "açılmadı");
            if (p is null)
            {
                return;
            }

            var liste = Denetimler(p, "x264");
            rapor.AppendLine($"x264 penceresindeki denetim: {liste.Count}");
            var ortam = new Ortam(
                "x264",
                () => Json(p.Sonuc),
                async () =>
                {
                    await Bosta(w);
                    var a = w.KontrolAyarlari.Kopya();
                    a.Video.X264 = AyarDosyasi.Kopyala(p.Sonuc);
                    return p.KomutKutusu.Text + "\n" + await Komut(a);
                },
                () =>
                {
                    var a = w.KontrolAyarlari.Kopya();
                    a.Video.X264 = AyarDosyasi.Kopyala(p.Sonuc);
                    return Kodla(a);
                });

            Kontrol("x264 penceresi: Komut sekmesi dolu ve salt okunur", "dolu, salt okunur", $"{p.KomutKutusu.Text.Length} karakter, salt okunur {p.KomutKutusu.IsReadOnly}",
                p.KomutKutusu.Text.Length > 0 && p.KomutKutusu.IsReadOnly);

            // Koşullu satırlar: kodlama moduna göre CRF / QP / bit hızı / hızlı ilk geçiş; deblock ve psy açıkken alt kutular.
            foreach (var mod in new[] { X264Modu.Crf, X264Modu.SabitQp, X264Modu.OrtalamaBitHizi, X264Modu.IkiGecis })
            {
                p.Sonuc.Mod = mod;
                p.Sonuc.Deblock = true;
                p.Sonuc.Psy = true;
                await Bosta(w);
                await Gecis(liste, ortam, p);
            }

            foreach (var d in liste.Where(x => !denenen.Contains(x.Anahtar)))
            {
                Durumlu($"{d.Anahtar}: denendi", false, "denendi", "görünür / etkin olmadı");
            }

            // "Tümünü hız ayarına bırak": ayrıntılı ayarlar boşalır, ana ayarlar (mod, CRF, hız ayarı...) kalır.
            p.Sonuc.Mod = X264Modu.Crf;
            p.Sonuc.Crf = 21;
            p.Sonuc.Ref = 5;
            p.Sonuc.Me = "umh";
            p.Sonuc.Cabac = false;
            await Bosta(w);
            var birak = Dugme(p, "Tümünü hız ayarına bırak");
            Bas(birak);
            await Bosta(w);
            await Task.Delay(200);
            Esit2("Tümünü hız ayarına bırak: referans kare boşaldı", null, p.Sonuc.Ref);
            Esit2("Tümünü hız ayarına bırak: hareket arama boşaldı", null, p.Sonuc.Me);
            Esit2("Tümünü hız ayarına bırak: CABAC varsayılana döndü", new X264Ayarlari().Cabac, p.Sonuc.Cabac);
            Esit2("Tümünü hız ayarına bırak: mod ve CRF kaldı", "Crf 21", $"{p.Sonuc.Mod} {p.Sonuc.Crf}");
            var refKutusu = liste.Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Ref");
            Esit2("Tümünü hız ayarına bırak: kutular da boşaldı (yeni ayarlara bağlandı)", string.Empty, refKutusu.Text);

            // Vazgeç: bunca değişiklikten sonra bile ana ayarlar değişmez.
            Bas(Dugme(p, "Vazgeç"));
            await Kapandi(w, p);
            Esit2("x264 penceresi: Vazgeç hiçbir şeyi değiştirmedi", ilkX264, Json(w.KontrolAyarlari.Video.X264));

            // Tamam: değişiklik ana ayarlara geçer ve ayar dosyasına yazılır.
            p = await DiyalogAc<X264Penceresi>(w, dugme);
            if (p is null)
            {
                Durumlu("x264 penceresi yeniden açıldı", false, "açıldı", "açılmadı");
                return;
            }

            refKutusu = Denetimler(p, "x264").Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Ref");
            SekmesiniSec(refKutusu);
            await Yaz(w, refKutusu, "4");
            Bas(Dugme(p, "Tamam"));
            await Kapandi(w, p);
            Esit2("x264 penceresi: Tamam uyguladı (referans kare 4)", 4, w.KontrolAyarlari.Video.X264.Ref);
            await Task.Delay(1200);
            Durumlu("x264 penceresi: Tamam ayar dosyasına yazıldı", AyarDosyasiOku(ExportProfileId.YouTube)?.Contains("\"Ref\": 4", StringComparison.Ordinal) == true,
                "Ref: 4", "yok");

            // Yeniden açınca kutuda 4 görünür; odaktaki kutuya yazılıp Enter'a basılmadan Tamam: yazılan yine uygulanır.
            p = await DiyalogAc<X264Penceresi>(w, dugme);
            if (p is not null)
            {
                refKutusu = Denetimler(p, "x264").Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Ref");
                SekmesiniSec(refKutusu);
                await Bosta(w);
                Esit2("x264 penceresi yeniden açılınca referans kare kutusunda 4", "4", refKutusu.Text);
                refKutusu.Focus();
                FocusManager.SetFocusedElement(p, refKutusu);
                refKutusu.Text = "3";
                Bas(Dugme(p, "Tamam"));
                await Kapandi(w, p);
                Esit2("Odaktaki kutuya yazılan, Enter'sız Tamam'da da uygulandı", 3, w.KontrolAyarlari.Video.X264.Ref);
            }

            w.KontrolAyarlari.Video.X264 = AyarDosyasi.Kopyala(Varsayilanlar.YouTube().Video.X264);
            await Bosta(w);
        }

        // ------------------------------------------------------------------ NVENC penceresi
        async Task NvencPenceresiniDene()
        {
            Bolum("NVENC penceresi (Tüm NVENC ayarları…)");
            var dugme = Dugme(w.AyarPaneli, "Tüm NVENC ayarları…");
            var ilk = Json(w.KontrolAyarlari.Video.Nvenc);
            var p = await DiyalogAc<NvencPenceresi>(w, dugme);
            Durumlu("NVENC penceresi açıldı", p is not null, "açıldı", "açılmadı");
            if (p is null)
            {
                return;
            }

            var liste = Denetimler(p, "NVENC");
            rapor.AppendLine($"NVENC penceresindeki denetim: {liste.Count}");
            var ortam = new Ortam(
                "NVENC",
                () => Json(p.Sonuc),
                async () =>
                {
                    await Bosta(w);
                    var a = w.KontrolAyarlari.Kopya();
                    a.Video.Nvenc = AyarDosyasi.Kopyala(p.Sonuc);
                    return p.KomutKutusu.Text + "\n" + await Komut(a);
                },
                null);
            Kontrol("NVENC penceresi: Komut sekmesi dolu ve salt okunur", "dolu, salt okunur", $"{p.KomutKutusu.Text.Length} karakter",
                p.KomutKutusu.Text.Length > 0 && p.KomutKutusu.IsReadOnly);

            foreach (var mod in new[] { NvencModu.Cbr, NvencModu.SabitKalite, NvencModu.SabitQp, NvencModu.DegiskenBitHizi })
            {
                p.Sonuc.Mod = mod;
                p.Sonuc.UzamsalAq = true;
                p.Sonuc.AgirlikliTahmin = false;
                p.Sonuc.BKare = 2;
                await Bosta(w);
                await Gecis(liste, ortam, p);
            }

            foreach (var d in liste.Where(x => !denenen.Contains(x.Anahtar)))
            {
                Durumlu($"{d.Anahtar}: denendi", false, "denendi", "görünür / etkin olmadı");
            }

            // Ağırlıklı tahmin açılınca B-kare satırları kapanır (kart ikisini birlikte kabul etmiyor).
            p.Sonuc.AgirlikliTahmin = true;
            await Bosta(w);
            var bKutusu = liste.Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "BKare");
            Esit2("Ağırlıklı tahmin açıkken B-kare kutusu kapalı", false, bKutusu.IsEnabled);
            p.Sonuc.AgirlikliTahmin = false;
            await Bosta(w);

            Bas(Dugme(p, "Vazgeç"));
            await Kapandi(w, p);
            Esit2("NVENC penceresi: Vazgeç hiçbir şeyi değiştirmedi", ilk, Json(w.KontrolAyarlari.Video.Nvenc));

            p = await DiyalogAc<NvencPenceresi>(w, dugme);
            if (p is null)
            {
                Durumlu("NVENC penceresi yeniden açıldı", false, "açıldı", "açılmadı");
                return;
            }

            var gpu = Denetimler(p, "NVENC").Select(d => d.Oge).OfType<CheckBox>().First(c => c.GetBindingExpression(ToggleButton.IsCheckedProperty)?.ParentBinding.Path.Path == "GpuCozme");
            SekmesiniSec(gpu);
            var onceki = gpu.IsChecked == true;
            gpu.IsChecked = !onceki;
            await Bosta(w);
            Bas(Dugme(p, "Tamam"));
            await Kapandi(w, p);
            Esit2("NVENC penceresi: Tamam uyguladı (NVDEC)", !onceki, w.KontrolAyarlari.Video.Nvenc.GpuCozme);
            await Task.Delay(1200);
            Durumlu("NVENC penceresi: Tamam ayar dosyasına yazıldı",
                AyarDosyasiOku(ExportProfileId.TikTok)?.Contains($"\"GpuCozme\": {(!onceki).ToString().ToLowerInvariant()}", StringComparison.Ordinal) == true,
                $"GpuCozme: {(!onceki).ToString().ToLowerInvariant()}", "yok");
            w.KontrolAyarlari.Video.Nvenc = AyarDosyasi.Kopyala(Varsayilanlar.TikTok().Video.Nvenc);
            await Bosta(w);
        }

        // ------------------------------------------------------------------ FDK AAC penceresi
        async Task FdkPenceresiniDene()
        {
            Bolum("FDK AAC penceresi (Tüm FDK AAC ayarları…)");
            var dugme = Dugme(w.AyarPaneli, "Tüm FDK AAC ayarları…");
            var ilk = Json(w.KontrolAyarlari.Ses.Fdk);
            var p = await DiyalogAc<FdkPenceresi>(w, dugme);
            Durumlu("FDK AAC penceresi açıldı", p is not null, "açıldı", "açılmadı");
            if (p is null)
            {
                return;
            }

            var liste = Denetimler(p, "FDK");
            rapor.AppendLine($"FDK AAC penceresindeki denetim: {liste.Count}");
            var ortam = new Ortam(
                "FDK",
                () => Json(p.Sonuc),
                async () =>
                {
                    await Bosta(w);
                    var a = w.KontrolAyarlari.Kopya();
                    a.Ses.Fdk = AyarDosyasi.Kopyala(p.Sonuc);
                    return p.KomutKutusu.Text + "\n" + await Komut(a);
                },
                () =>
                {
                    var a = w.KontrolAyarlari.Kopya();
                    a.Ses.Fdk = AyarDosyasi.Kopyala(p.Sonuc);
                    return Kodla(a);
                });
            Kontrol("FDK AAC penceresi: Komut sekmesi dolu ve salt okunur", "dolu, salt okunur", $"{p.KomutKutusu.Text.Length} karakter",
                p.KomutKutusu.Text.Length > 0 && p.KomutKutusu.IsReadOnly);

            foreach (var mod in new[] { FdkModu.Cbr, FdkModu.Vbr3 })
            {
                p.Sonuc.Mod = mod;
                await Bosta(w);
                await Gecis(liste, ortam, p);
            }

            foreach (var d in liste.Where(x => !denenen.Contains(x.Anahtar)))
            {
                Durumlu($"{d.Anahtar}: denendi", false, "denendi", "görünür / etkin olmadı");
            }

            var kesimKutusu = liste.Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Kesim");
            SekmesiniSec(kesimKutusu);
            p.Sonuc.Mod = FdkModu.Cbr;
            await Bosta(w);
            foreach (var kesim in new[] { "100", "20000", "21000" })
            {
                await Yaz(w, kesimKutusu, kesim);
                await Calistir(ortam, $"FDK kesim frekansı {kesim} yazılınca ({kesimKutusu.Text})");
            }

            p.Sonuc.Mod = FdkModu.Vbr3;
            await Bosta(w);
            await Yaz(w, kesimKutusu, "20000");
            await Calistir(ortam, "FDK VBR 3 + kesim 20000");
            await Yaz(w, kesimKutusu, "0");

            Bas(Dugme(p, "Vazgeç"));
            await Kapandi(w, p);
            Esit2("FDK AAC penceresi: Vazgeç hiçbir şeyi değiştirmedi", ilk, Json(w.KontrolAyarlari.Ses.Fdk));

            p = await DiyalogAc<FdkPenceresi>(w, dugme);
            if (p is null)
            {
                Durumlu("FDK AAC penceresi yeniden açıldı", false, "açıldı", "açılmadı");
                return;
            }

            var bitKutusu = Denetimler(p, "FDK").Select(d => d.Oge).OfType<TextBox>().First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "BitHizi");
            SekmesiniSec(bitKutusu);
            p.Sonuc.Mod = FdkModu.Cbr;
            await Bosta(w);
            await Yaz(w, bitKutusu, "160");
            Bas(Dugme(p, "Tamam"));
            await Kapandi(w, p);
            Esit2("FDK AAC penceresi: Tamam uyguladı (160 kb/s)", 160, w.KontrolAyarlari.Ses.Fdk.BitHizi);
            var hata = await Kodla(w.KontrolAyarlari);
            Kontrol("FDK AAC 160 kb/s ile FFmpeg'de çalışıyor", "çalıştı", hata ?? "çalıştı", hata is null && w.KontrolAyarlari.Video.Kodlayici == VideoKodlayici.X264);
            w.KontrolAyarlari.Ses.Fdk = AyarDosyasi.Kopyala(Varsayilanlar.YouTube().Ses.Fdk);
            await Bosta(w);
        }

        // ------------------------------------------------------------------ ön ayarlar
        async Task OnAyarlariDene()
        {
            Bolum("Ön ayarlar: boş ad, kaydet, üzerine yaz (Vazgeç / Üzerine yaz), yükle, sil (Vazgeç / Sil), Kapat");
            await BaglamaGec(ExportProfileId.YouTube, Ozel(Varsayilanlar.YouTube()));
            var dosya = w.KontrolAyarDosyasi;
            dosya.OnAyarlar.Clear();
            w.KontrolAyarlari.Ses.Gecikme = 111;
            await Bosta(w);

            var p = await DiyalogAc<OnAyarPenceresi>(w, w.BtnOnAyarlar);
            Durumlu("Ön ayar penceresi açıldı", p is not null, "açıldı", "açılmadı");
            if (p is null)
            {
                return;
            }

            Esit2("Liste boşken \"Henüz kayıtlı ön ayar yok.\" görünüyor", Visibility.Visible, p.BosListe.Visibility);
            var kaydet = Dugme(p, "Kaydet");
            p.AdKutusu.Text = "   ";
            Bas(kaydet);
            await Bosta(w);
            Esit2("Boş ad: kaydedilmedi", 0, dosya.OnAyarlar.Count);
            Esit2("Boş ad: uyarı yazıldı", "Önce bir ad yazın.", p.Bilgi.Text);

            p.AdKutusu.Text = OnAyarAdi;
            Bas(kaydet);
            await Bosta(w);
            Esit2("Kaydet: listeye eklendi", OnAyarAdi, string.Join(", ", dosya.OnAyarlar.Select(o => o.Ad)));
            Esit2("Kaydet: kaydedilen ayar şu anki ayar (gecikme 111)", 111, dosya.OnAyarlar.FirstOrDefault()?.Ayarlar.Ses.Gecikme);
            Esit2("Kaydet: ad kutusu temizlendi", string.Empty, p.AdKutusu.Text);
            Esit2("Kaydet: listede bir satır", 1, Gorsel(p.Liste).OfType<Button>().Count(b => b.Content as string == "Yükle"));
            await Task.Delay(300);
            Durumlu("Kaydet: ayar dosyasına yazıldı", File.ReadAllText(Path.Combine(AppPaths.VeriKlasoru, "ayarlar.json")).Contains(OnAyarAdi, StringComparison.Ordinal),
                OnAyarAdi, "yok");

            // Aynı adla yeniden: üzerine yazma sorulur; Vazgeç değiştirmez, "Üzerine yaz" yazar.
            Bas(Dugme(p, "Kapat"));
            await Kapandi(w, p);
            w.KontrolAyarlari.Ses.Gecikme = 222;
            await Bosta(w);
            p = await DiyalogAc<OnAyarPenceresi>(w, w.BtnOnAyarlar);
            if (p is null)
            {
                Durumlu("Ön ayar penceresi yeniden açıldı", false, "açıldı", "açılmadı");
                return;
            }

            kaydet = Dugme(p, "Kaydet");
            p.AdKutusu.Text = OnAyarAdi.ToUpperInvariant();
            var soru = await DiyalogAc<ConfirmDialog>(w, kaydet);
            Durumlu("Aynı ad (büyük/küçük harf farklı): üzerine yazma soruldu", soru is not null, "soruldu", "sorulmadı");
            if (soru is not null)
            {
                Bas(soru.BtnNo);
                await Kapandi(w, soru);
                Esit2("Üzerine yaz → Vazgeç: eski ayar kaldı (gecikme 111)", 111, dosya.OnAyarlar.FirstOrDefault()?.Ayarlar.Ses.Gecikme);
            }

            soru = await DiyalogAc<ConfirmDialog>(w, kaydet);
            if (soru is not null)
            {
                Bas(soru.BtnYes);
                await Kapandi(w, soru);
                Esit2("Üzerine yaz: yeni ayar yazıldı (gecikme 222)", 222, dosya.OnAyarlar.FirstOrDefault()?.Ayarlar.Ses.Gecikme);
                Esit2("Üzerine yaz: yine tek ön ayar", 1, dosya.OnAyarlar.Count);
            }

            // Yükle: ayar değiştirilip ön ayar yüklenince ayarlar geri gelir, pencere kapanır.
            Bas(Dugme(p, "Kapat"));
            await Kapandi(w, p);
            w.KontrolAyarlari.Ses.Gecikme = 999;
            await Bosta(w);
            p = await DiyalogAc<OnAyarPenceresi>(w, w.BtnOnAyarlar);
            if (p is null)
            {
                return;
            }

            var yukle = Gorsel(p.Liste).OfType<Button>().FirstOrDefault(b => b.Content as string == "Yükle");
            Durumlu("Listede Yükle düğmesi var", yukle is not null, "var", "yok");
            if (yukle is not null)
            {
                Bas(yukle);
                await Kapandi(w, p);
                Esit2("Yükle: ön ayarın ayarları yüklendi (gecikme 222)", 222, w.KontrolAyarlari.Ses.Gecikme);
            }

            // Sil: önce Vazgeç (silinmez), sonra Sil.
            p = await DiyalogAc<OnAyarPenceresi>(w, w.BtnOnAyarlar);
            if (p is null)
            {
                return;
            }

            var sil = Gorsel(p.Liste).OfType<Button>().FirstOrDefault(b => b.Content as string == "Sil");
            Durumlu("Listede Sil düğmesi var", sil is not null, "var", "yok");
            if (sil is not null)
            {
                soru = await DiyalogAc<ConfirmDialog>(w, sil);
                if (soru is not null)
                {
                    Bas(soru.BtnNo);
                    await Kapandi(w, soru);
                    Esit2("Sil → Vazgeç: ön ayar duruyor", 1, dosya.OnAyarlar.Count);
                }

                sil = Gorsel(p.Liste).OfType<Button>().FirstOrDefault(b => b.Content as string == "Sil");
                soru = sil is null ? null : await DiyalogAc<ConfirmDialog>(w, sil);
                if (soru is not null)
                {
                    Bas(soru.BtnYes);
                    await Kapandi(w, soru);
                    Esit2("Sil: ön ayar silindi", 0, dosya.OnAyarlar.Count);
                    Esit2("Sil: liste boş yazısı yeniden görünüyor", Visibility.Visible, p.BosListe.Visibility);
                }
            }

            var kapat = Dugme(p, "Kapat");
            Bas(kapat);
            await Kapandi(w, p);
            Durumlu("Kapat: pencere kapandı", !p.IsVisible, "kapandı", "açık");
            await Task.Delay(300);
            Durumlu("Sil: ayar dosyasından da silindi",
                !File.ReadAllText(Path.Combine(AppPaths.VeriKlasoru, "ayarlar.json")).Contains(OnAyarAdi, StringComparison.OrdinalIgnoreCase), "yok", "var");
        }
    }

    // ====================================================================== statik yardımcılar

    /// <summary>Kökten başlayıp mantıksal ağaçtaki bütün öğeler (gizli satırlar ve seçili olmayan sekmeler dahil).</summary>
    private static IEnumerable<DependencyObject> Mantiksal(DependencyObject kok)
    {
        yield return kok;
        foreach (var c in LogicalTreeHelper.GetChildren(kok))
        {
            if (c is DependencyObject d)
            {
                foreach (var x in Mantiksal(d))
                {
                    yield return x;
                }
            }
        }
    }

    /// <summary>Görsel ağaçtaki öğeler (şablondan üretilen satırlar ve düğmeleri için).</summary>
    private static IEnumerable<DependencyObject> Gorsel(DependencyObject kok)
    {
        yield return kok;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(kok); i++)
        {
            foreach (var x in Gorsel(VisualTreeHelper.GetChild(kok, i)))
            {
                yield return x;
            }
        }
    }

    /// <summary>Ayara bağlı kutular, açılır listeler ve onay kutuları; anahtar: pencere / ayar sınıfı.özellik.</summary>
    private static List<Denetim> Denetimler(DependencyObject kok, string pencere)
    {
        var liste = new List<Denetim>();
        var sayac = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var o in Mantiksal(kok).OfType<FrameworkElement>())
        {
            var be = o switch
            {
                TextBox { IsReadOnly: false } t => t.GetBindingExpression(TextBox.TextProperty),
                ComboBox c => c.GetBindingExpression(Selector.SelectedValueProperty),
                CheckBox c => c.GetBindingExpression(ToggleButton.IsCheckedProperty),
                _ => null,
            };
            if (be is null)
            {
                continue;
            }

            var anahtar = $"{pencere} · {o.DataContext?.GetType().Name ?? "?"}.{be.ParentBinding.Path.Path}";
            if (sayac.TryGetValue(anahtar, out var n))
            {
                sayac[anahtar] = n + 1;
                anahtar += " #" + (n + 1);
            }
            else
            {
                sayac[anahtar] = 1;
            }

            liste.Add(new Denetim(o, anahtar, Sekmesi(o)));
        }

        return liste;
    }

    private static TabItem? Sekmesi(DependencyObject o)
    {
        for (var p = LogicalTreeHelper.GetParent(o); p is not null; p = LogicalTreeHelper.GetParent(p))
        {
            if (p is TabItem t)
            {
                return t;
            }
        }

        return null;
    }

    private static void SekmesiniSec(FrameworkElement o)
    {
        if (Sekmesi(o) is { } t)
        {
            t.IsSelected = true;
        }
    }

    private static Button Dugme(DependencyObject kok, string yazi) =>
        Mantiksal(kok).OfType<Button>().FirstOrDefault(b => b.Content as string == yazi)
        ?? Gorsel(kok).OfType<Button>().First(b => b.Content as string == yazi);

    /// <summary>Bağlamanın kaynağı ve özelliği (değerini okumak için).</summary>
    private static (object? Nesne, PropertyInfo? Ozellik) Kaynak(BindingExpression? be)
    {
        if (be?.ResolvedSource is not { } nesne || be.ResolvedSourcePropertyName is not { } ad)
        {
            return (null, null);
        }

        return (nesne, nesne.GetType().GetProperty(ad));
    }

    private static string Bicimle(object? v) => new Sayi().Convert(v, typeof(string), null, CultureInfo.InvariantCulture) as string ?? string.Empty;

    private static string Json<T>(T a) => JsonSerializer.Serialize(a, AyarDosyasi.Secenekler);

    /// <summary>Kutuya yazılacak geçerli değer adayları: bilinen ayarlar için gerçekçi değer, sonra türüne göre genel adaylar.</summary>
    private static IEnumerable<string> Adaylar(string anahtar, Type? tip, object? simdiki)
    {
        var ozellik = anahtar[(anahtar.LastIndexOf('.') + 1)..];
        var bilinen = ozellik switch
        {
            "Genislik" => "200",
            "Yukseklik" => "356",
            "Crf" => "20",
            "Qp" => "25",
            "Cq" => "25",
            "BitHizi" when anahtar.Contains("Fdk", StringComparison.Ordinal) || anahtar.Contains("FDK", StringComparison.Ordinal) => "192",
            "BitHizi" => "3000",
            "MaksBitHizi" => "40000",
            "Tampon" => "50000",
            "Gecikme" => "120",
            "Seviye" => "-3",
            "EsitlemeHedef" => "-16",
            "SinirlayiciTavan" => "-2",
            "Kesim" => "16000",
            "DrcHedef" or "SikistirmaHedef" or "ProgramSeviyesi" => "-20",
            "EnBuyuk" => "60",
            "EnKucuk" => "20",
            "SahneDegisimi" => "30",
            "EkParametreler" when anahtar.StartsWith("NVENC", StringComparison.Ordinal) => "-surfaces 32",
            "EkParametreler" => "aq-strength=0.9",
            "IpOrani" or "PbOrani" => "1.3",
            "Qcomp" => "0.7",
            "CplxBlur" => "15",
            "QBlur" => "0.4",
            "VbvBaslangic" => "0.8",
            "PsyRd" => "1.1",
            "PsyTrellis" => "0.1",
            "AqGucu" when anahtar.StartsWith("NVENC", StringComparison.Ordinal) => "10",
            "AqGucu" => "1.2",
            _ => null,
        };
        if (bilinen is not null)
        {
            yield return bilinen;
        }

        var t = tip is null ? null : Nullable.GetUnderlyingType(tip) ?? tip;
        if (t == typeof(int))
        {
            var s = simdiki is int i ? i : (int?)null;
            if (s is { } v)
            {
                yield return (v + 1).ToString(CultureInfo.InvariantCulture);
                yield return (v - 1).ToString(CultureInfo.InvariantCulture);
            }

            foreach (var x in new[] { "2", "1", "3", "4", "0", "10", "100" })
            {
                yield return x;
            }
        }
        else if (t == typeof(double))
        {
            var s = simdiki is double d ? d : (double?)null;
            if (s is { } v)
            {
                yield return (v + 0.5).ToString("0.###", CultureInfo.InvariantCulture);
                yield return (v - 0.5).ToString("0.###", CultureInfo.InvariantCulture);
            }

            foreach (var x in new[] { "0.5", "1.5", "1", "2", "0.3", "3", "10" })
            {
                yield return x;
            }
        }
        else
        {
            yield return "deneme";
        }
    }

    /// <summary>Çok büyük görüntü (8192 piksel) GitHub'da bir dakika sürer: o uç değerde FFmpeg denenmez.</summary>
    private static bool YavasUc(string anahtar, string uc) =>
        uc == "99999999" && (anahtar.EndsWith(".Video.Genislik", StringComparison.Ordinal) || anahtar.EndsWith(".Video.Yukseklik", StringComparison.Ordinal));

    /// <summary>Kutuya yazıp Enter'a basar (kullanıcının yolu: değer uygulanır, kutuda ayarın geçerli değeri görünür).</summary>
    private static async Task Yaz(Window w, TextBox tb, string metin)
    {
        tb.Text = metin;
        if (!Tus(tb, Key.Enter))
        {
            var be = tb.GetBindingExpression(TextBox.TextProperty);
            be?.UpdateSource();
            be?.UpdateTarget();
        }

        await Bosta(w);
    }

    /// <summary>Kutuya bir tuş olayı gönderir (ayar kutularının Enter / Esc davranışı bu olayla çalışır).</summary>
    private static bool Tus(UIElement e, Key k)
    {
        if (PresentationSource.FromVisual(e) is not { } src)
        {
            return false;
        }

        e.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        return true;
    }

    /// <summary>Düğmeye gerçek tıklama yolundan basar (Vazgeç / Tamam gibi pencere düğmeleri dahil); sıraya konur.</summary>
    private static void Bas(ButtonBase b)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(b)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider ip)
        {
            ip.Invoke();
        }
        else
        {
            MainWindow.KontrolTikla(b);
        }
    }

    /// <summary>Düğmeye basar ve açılan pencereyi bekler (en çok 10 sn).</summary>
    private static async Task<T?> DiyalogAc<T>(Window w, ButtonBase dugme)
        where T : Window
    {
        var once = Application.Current.Windows.OfType<T>().ToList();
        Bas(dugme);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 10)
        {
            await Task.Delay(50);
            if (Application.Current.Windows.OfType<T>().FirstOrDefault(x => x.IsVisible && !once.Contains(x)) is { } p)
            {
                p.UpdateLayout();
                await Bosta(w);
                return p;
            }
        }

        return null;
    }

    private static async Task Kapandi(Window w, Window p)
    {
        var sw = Stopwatch.StartNew();
        while (p.IsVisible && sw.Elapsed.TotalSeconds < 10)
        {
            await Task.Delay(50);
        }

        await Bosta(w);
    }

    private static async Task Bosta(Window w) => await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static string? AyarDosyasiOku(ExportProfileId id)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppPaths.VeriKlasoru, "ayarlar.json")));
            var p = doc.RootElement.GetProperty("profiller").GetProperty(id.ToString());
            return AyarDosyasi.Oku(p.GetRawText()) is { } a ? AyarDosyasi.Yaz(a) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Programın kendi bağlamalarındaki hata (şablonların bilinen WPF iletileri değil).</summary>
    private static bool BizimHatamiz(string ileti) =>
        ileti.Contains("Data Error", StringComparison.Ordinal)
        && (ileti.Contains("ReliosExport", StringComparison.Ordinal) || ileti.Contains("Ayarlari", StringComparison.Ordinal)
            || ileti.Contains("FileItem", StringComparison.Ordinal) || ileti.Contains("OnAyar", StringComparison.Ordinal)
            || ileti.Contains("Secenek", StringComparison.Ordinal) || ileti.Contains("OzetParcasi", StringComparison.Ordinal)
            || ileti.Contains("BindingExpression path error", StringComparison.Ordinal));

    private static string Kisa(string s, int en = 220)
    {
        var tek = s.Replace("\r", string.Empty).Replace('\n', ' ').Trim();
        return tek.Length > en ? tek[..en] + "…" : tek;
    }

    private static void SessizSil(string yol)
    {
        try
        {
            if (File.Exists(yol))
            {
                File.Delete(yol);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // önemli değil
        }
    }
}
