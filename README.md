# Relios Export

TikTok ve YouTube için video dışa aktarma programı (FFmpeg + FDK AAC). Tamamen çevrimdışı.

- `app/ReliosExport/` : Program (.NET 11 WPF, tek exe; FFmpeg içinde). Arayüz Relios Vision / Relios Color /
  Relios Audio ile aynı tasarım dilinde.
  - Her profilin (TikTok, YouTube) kendi ayarları vardır; sağdaki ayar panelinde üç bölüm:
    Video (çözünürlük, kare hızı, x264 / NVENC), Ses (kaynak, gecikme, seviye, örnekleme, kanal,
    FDK AAC / WAV) ve Anahtar kare. x264, NVENC ve FDK AAC'nin bütün ayarları XviD4PSP'deki gibi
    sekmeli pencerelerde. Ayarlar adıyla ön ayar olarak kaydedilebilir.
  - TikTok varsayılanı (0.16.0): x264 veryslow, CRF 24, en yüksek bit hızı 8000 kb/s (tampon 12000 kb = 1,5 saniye),
    High profil, 16 referans kare, önden bakış 250 (diğer x264 ayarları hız ayarının: 8 B-kare, B-piramit, umh, subme 10,
    trellis 2, 8x8 DCT); anahtar kare en çok 10 saniyede bir (en az ve sahne eşiği x264'ün); çözünürlük, kare hızı
    (60 fps korunur) ve seviye kaynakla aynı (16 referans kare yetmezse yükselir: 1080p'de 5.1); ses FDK AAC-LC 128 kb/s
    44,1 kHz stereo; MOV. 60 saniyelik video en çok ~59 MB (bekleyen satırda "en çok" yazar). TikTok yüklenen videoyu
    kendisi yeniden kodlar: TikTok'un kendi sayfalarındaki video bilgisine göre 1080p sürümü H.265, dikey 1080×1920
    60 fps PUBG videolarında 2,2–2,4 Mb/s (bütün 1080p 60 fps örneklerde 1,6–3,4 Mb/s); 60 fps yalnızca 1080p sürümde
    kalıyor, 720p ve 540p 30 fps.
    Neden üst sınır (0.14.0'da 6000, 0.16.0'da 8000; `tools/tiktok_calisma.py`, `tiktok-calisma.yml`): yedi 1080p60 oyun videosu (Xiph'in
    herkese açık oyun kayıtları) ve kullanıcının PUBG kaydı her ayarla kodlanıp bu dönüşümün benzetiminden (x265,
    1080p60 2,3 Mb/s; 720p30 1,2 Mb/s) geçirildi, kalite kaynağa göre VMAF (telefon modeli dahil), PSNR, SSIM ve
    bantlanma (CAMBI) ile ölçüldü. Zor sahnelerde CRF 24'ün 12–20 Mb/s'i boşa gidiyor: 6000 kb/s üst sınırla dosya
    %26–52'ye iner (kullanıcının 25 sn'lik kaydı 28,6 MB → 17,2 MB), TikTok sonrası kalite farkı telefon VMAF'ında
    ortalama −1,5 (görülebilir fark ≈ 6), bantlanma artmıyor; 8000 kb/s'te −0,7, 4000 kb/s'te −3,8. Üst sınırın
    altında kalan (kolay) sahneler CRF 24'le aynı. x264'ün 22 farklı ayarı da (psy, AQ, qcomp, deblock, tesa,
    subme 11, 16 B-kare, placebo) denendi: TikTok sonrası kazanç ±0,5 VMAF'ı geçmedi, ölçüm puanını artıranlar
    bantlanmayı artırıyor ya da 1,5–3,7 kat yavaş; hız ayarının değerleri kaldı.
    0.16.0'da üst sınır 8000 kb/s (kullanıcı seçti, kalıcılık için): yüklenen video kalıcı, TikTok ileride kalitesini
    artırırsa yüklenen dosyanın kalitesi önem kazanır; dosya boyutu yalnızca yükleme anında önemli. Kullanıcının
    kaydının tamamında (SSIM, dosya / TikTok benzetimi sonrası): sınırsız 28,6 MB 0,9626 / 0,8729; 8000 / 12000 22,1 MB
    0,9513 / 0,8719; 8000 / 16000 22,6 MB 0,9523 / 0,8713; 6000 / 12000 17,2 MB 0,9374 / 0,8692. 8000 / 12000 TikTok
    sonrası 8000 / 16000 ile aynı, dosya %2 küçük; tampon 0.14.0'daki gibi 12000.
    Son kullanılan ayar 0.10.0'ın, 0.11.0–0.13.1'in ya da 0.14.0–0.15.0'ın değiştirilmemiş TikTok ayarıysa kendiliğinden yenisine geçer;
    kullanıcının değiştirdiği kısım, LUFS eşitleme seçimi ve ön ayarlar olduğu gibi kalır.
  - TikTok'ta NVENC seçilirse 0.10.0'ın varsayılanı kullanılır: ekran kartıyla (NVENC H.264) CBR 34375 kb/s, tampon 1
    saniye; H.264 profili ve seviyesi kaynakla aynı; B-kare yok, 3 referans kare. NVENC seviyeyi katı denetler: kaynağın seviyesi çıktının boyutuna, kare hızına,
    bit hızına ya da tamponuna yetmiyorsa (ör. 34375 kb/s, 4.0 ve altının sınırını aşar) seviye yeten en düşük
    seviyeye çıkar; yoksa kart kodlamayı hiç başlatmaz. Referans kare tamponu (DPB) da referans kare sayısı kadar
    verilir (`-dpb_size`): ffmpeg'de `-refs` yalnızca kare başına referanstır, DPB boş kalınca kart 6 kare seçer
    ve 1080p'de 4.x seviyelerini (en fazla 4 kare) aşar.
  - Ön ayar listesine program kendiliğinden bir şey koymaz; yalnızca kullanıcının kaydettikleri durur.
  - Önerilen ayarlar (0.13.0; 0.13.1'den beri ekran kartıyla; 0.15.0'dan beri beş ayar, iki grup): ön ayar penceresi
    açılınca üstte iki seçenek var, "Önerilen" ve "Kayıtlı ayarlarım"; tıklanan liste görünür (kayıtlı ön ayar varsa
    pencere onunla, yoksa önerilenlerle açılır; kaydedince kayıtlılar). Önerilenler programla gelir, kart kart gösterilir
    (ad, etiketler, açıklama, "Yükle"); yalnızca yüklenir (silinmez, ayar dosyasına ve kayıtlı ön ayarlara yazılmaz).
    Hepsi ekran kartıyla (NVENC p7, çok geçiş, ekran kartıyla çözme); boyut, kare hızı ve seviye kaynakla aynı (seviye
    yetmezse yükselir); ses yüksekliği -14 LUFS, tepe -1 dB. Kodlayıcı x264'e çevrilirse aynı yapı x264'le.
    - HER PLATFORM: "Her platform · Yüksek kalite" (sabit kalite 19, en çok 25 Mb/s, AAC-LC 320 kb/s) ve "Her platform ·
      Dengeli" (sabit kalite 23, en çok 12 Mb/s, AAC-LC 192 kb/s): High, 2 B-kare (referans değil, uyarlamalı), 3
      referans kare, uzamsal AQ, en çok 2 saniyede bir anahtar kare (kapalı GOP), MP4 (hızlı başlatma), 48 kHz stereo.
      YouTube, Instagram, Facebook, X ve TikTok'un yükleme kurallarına uyar (H.264 High, 4:2:0, kapalı GOP, en çok 25
      Mb/s, AAC-LC 48 kHz). x264'te CRF 19 / 23 + aynı üst sınır (tampon 2 sn), 4 referans kare.
    - ÖRNEK DOSYALARDAN (örnek videoların kodlaması ölçüldü; adlar kullanıcının isteğiyle): "CapCut" (1017 (2) videosu:
      NVENC'le kodlanmış; sabit 34,4 Mb/s (CBR), tampon 62,5 Mb (Main 4.1 / 4.2'nin en büyüğü; örneğinki 68,75 ile 4.2'yi
      aşıyor), Main, 3 referans kare, B-kare yok, AQ yok, 30 karede bir anahtar kare, MOV, AAC-LC 192 kb/s 44,1 kHz),
      "After Effects" (eski adı "1717504691091 videosu gibi": VBR 52 Mb/s, en çok 62, tampon 62, Main, 3 referans kare,
      B-kare yok, AQ yok, 30 karede bir anahtar kare, MP4, AAC-LC 512 kb/s 48 kHz), "Mobil" (eski adı "38m videosu gibi":
      VBR 12 Mb/s, en çok 16, tampon 16, High, P-kare tek referans (DPB 2), 1 B-kare (referans değil, hep IPBPB),
      uzamsal AQ, her saniye anahtar kare, MOV, AAC-LC 128 kb/s 44,1 kHz). Üçünde sahne değişiminde ek anahtar kare yok.
      After Effects ve Mobil kullanıcının kartında denendi (0.13.1): H.264 başlıkları, kare düzeni, IDR aralığı ve ses
      örneklerinkiyle aynı.
  - NVENC penceresinde "Uyarlamalı B-kare" (0.13.1, `-b_adapt`): kapalıyken B-kareler hep aynı dizilir (1 B-karede
    IPBPB); açıkken (varsayılan) kart hızlı sahnelerde B-kare yerine P-kare koyabilir. "Önden bakış" kutusu boşsa kartın
    ön ayarınınki kullanılır: p7'de kart yine önden bakar (sahne değişiminde anahtar kare, uyarlamalı B-kare; kullanıcının
    kartında ölçüldü), kutu eskiden "kapalı" yazıyordu.
  - Varsayılan düğmesi (0.13.0) ayarlar zaten varsayılandayken kapalıdır, bir ayar değişince açılır.
  - İlerleme çubuğu (0.13.0) kodlanan kare sayısından ilerler: x264 kareleri önden bakış ve B-kareler yüzünden geriden
    kodlar (TikTok ayarında ~266 kare); FFmpeg'in zamanına göre çubuk %85–90'da durup birden bitiyordu.
  - x264'ün açılışta reddettiği ya da çöktüğü birleşimler (0.13.0) kendiliğinden düzeltilip yeniden kodlanır, satırın
    ipucunda yazar: bellek yetmezse iş parçacığı / önden bakış azaltılır; geçmeli kodlamada kısıtlı intra kapalı ve arama
    aralığı en az 16; 2 / 3 geçişte çok düşük bit hızı x264'ün en azına; nicemleme matrisiyle olanaksız QP aralığında
    düz matris. Bir boyut küçülüp öbürü büyürken Spline / Lanczos önce küçülen boyutta çalışır (FFmpeg açmıyordu).
  - FDK AAC (0.13.0): FDK'nin kodlayamadığı hız, kanal düzeni, profil ve bit hızı en yakın çalışan ayara çevrilir
    (FDK AAC 3.0.0 ile bütün birleşimler ölçüldü); AAC-LD / ELD yalnızca çalınabildiği 22,05–48 kHz'te. Ses gecikmesi
    dosyanın süresini değiştirmez.
  - Varsayılan kayıt yeri: İndirilenler klasörü.
  - Dikey 9:16 ayarı (1216×2160, 29,97 fps, x264 Main@L5.0 CRF 18, FDK AAC 128 kb/s 48 kHz; önceki TikTok
    varsayılanı) yalnızca kodda durur: YouTube varsayılanının temeli ve GitHub doğrulamasının hedef ayarı.
  - YouTube varsayılanı: dikey 9:16 ayarıyla aynı x264 (CRF 18, Main profil) ayarları; ses FDK AAC-LC 128 kb/s 48 kHz;
    çözünürlük, en-boy oranı, kare hızı ve H.264 seviyesi kaynakla aynı. VBV: en yüksek bit hızı 34375 kb/s, tampon 55000 kb
    (kalite yine CRF 18'le belirlenir; bit hızı en çok 34375 kb/s'e çıkar). Anahtar kare aralığı 30 fps'e göre
    50 / 26 ve kare hızıyla orantılı (aynı süre): 25 fps'te 42 / 22, 50 fps'te 83 / 42, 60 fps'te 100 / 51
    (en az, en fazlanın yarısı + 1'i geçemez).
  - x264'te de seviye "kaynakla aynı" iken çıktı kaynağın seviyesini aşıyorsa (boyut, kare hızı, VBV ya da
    referans kareler) yeten en düşük seviye yazılır: ör. 1080p30 4.0 seviyeli kaynak, 34375 kb/s ile 4.1 olur
    (Main 4.0'ın sınırı 20000 kb/s, High'ınki 25000 kb/s). x264 aşan seviyeyle de kodlar ama yalnızca uyarır; dosya kendi yazdığı seviyeye
    uymazdı. Referans kareler x264'ün kendi hesabıyla sayılır (B-kare varsa en az 2, B-piramitte en az 4; boş
    bırakılanlar hız ayarından).
  - TikTok ve YouTube varsayılan dosya biçimi MOV (dikey 9:16 ön ayarı MP4); ses WAV ise dosya her zaman MOV.
  - WAV (PCM) 16 bit sesi (0.10.0'a kadar TikTok sesi, 48 kHz; şimdi TikTok FDK AAC 128 kb/s 44,1 kHz, YouTube FDK AAC-LC 128 kb/s 48 kHz). Platform sesi zaten kendisi sıkıştırır; 16 bit PCM,
    FDK AAC'nin de girişte kullandığı 16 bitlik sesi hiç sıkıştırmadan verir. YouTube'a yükleyip indirerek
    denendi: 128 kb/s AAC yüklemeye göre 15–17 kHz'te 1,7–3,6 dB daha çok ayrıntı kalıyor, altı aynı.
    Dönüşüm en yüksek kalitede (soxr 33 bit); 24 / 32 bit ya da kayan noktalı kaynak 16 bite üçgen titreşimle
    iner (çok kısık yerlerde ve sönen seslerde yuvarlama bozulması olmaz). Kaynak zaten 16 bit 48 kHz ise ses
    bit bit aynı kalır. Son kullanılan ayardaki ses eski varsayılandaysa (FDK AAC 128 kb/s) kendiliğinden geçer;
    adını verip kaydettiğin ön ayarların sesi kaydedildiği gibi kalır.
  - Kaynağın etiketleri (başlık, tarih, kodlayıcı adları) çıktıya taşınmaz. Video ve ses izi İngilizce (eng)
    etiketlenir. Encoded date / Tagged date (dosyanın ve izlerin oluşturma / değiştirme zamanı) kodlamanın
    başladığı an olarak yazılır: bilgisayarın saatinden, MP4 / MOV kuralı gereği UTC (MediaInfo "UTC" diye
    gösterir; Türkiye saatinden 3 saat geri).
  - Videonun yanında aynı adlı ses dosyası (ör. `edit1.mp4` için `edit1.wav`) varsa videonun kendi
    sesi yerine o kullanılır; ses videonun süresine tam getirilir (uzunsa kesilir, kısaysa sonu sessizlikle
    doldurulur). LUFS eşitlemenin ölçümü ve AAC telafi denemesi de aynı uzunlukla yapılır (0.11.1; önceden `-shortest`
    sesi video bitince son AAC karesinde keserdi, sesin sonu kısıksa yükseklik 0,05 LU kadar kayardı). Videoda birden fazla ses izi varsa WAV (PCM) ya da başka kayıpsız iz tercih edilir
    (aynı videoda WAV ve AAC varsa FDK AAC'ye WAV'dan çevrilir), yoksa ilk iz.
    Ses (videonun içindeki PCM / WAV dahil) 48 kHz'e soxr ile çevrilir;
    seviye değişmez. Mono ses stereoya iki kanala tam seviyede açılır (ffmpeg'in `-ac 2` açılışı -3 dB kısardı).
  - Tepe sınırlayıcı (varsayılan açık, -1 dB; 0.12.0): çıktının gerçek tepesi (örneklerin arası dahil; AAC'de çözülen
    sesinki) tavanı aşmaz. AAC kodlaması ve örnekleme hızı dönüşümü tepeyi biraz yükseltir (kullanıcının videolarında
    AAC 128 kb/s'de 0,1–1 dB: -1'de tutulan tepe çözülünce -0,9'a, hazır yüksek müzik 0 dB'nin üstüne çıkıyordu). Ses
    önce yalnızca ses olarak kodlanıp (WAV'da kodlanmadan) ölçülür; tepe tavanı aşıyorsa sınırlayıcı görülen taşma +
    0,1 dB aşağıda çalışır ve yeniden denenir (en çok 10 deneme, LUFS eşitlemenin telafisiyle birlikte; yükseklik yine
    -14). Ses yükseltilmese de tepesi tavanı aşan seste yalnızca tepeler tutulur; tepesi tavanın altında kalan sese
    dokunulmaz (WAV'da bit bit aynı). Kutu kapalıysa dokunulmayan ses olduğu gibi kalır (LUFS eşitleme sesi
    yükseltiyorsa tepe yine tutulur). Platform (TikTok, YouTube) sesi yeniden kodlar: onun kodlayıcısına giren ses
    bizim çözülen sesimizdir, standart (AES TD1008) kodlayıcı girişinde en çok -1 dBTP ister.
  - Sınırlayıcının gecikmesi (0.16.1): sınırlayıcı sesi 5 ms (192 kHz'te 959 örnek) geciktirir; gecikme zincirde
    giderilir (sona 959 örnek sessizlik, baştan 959 örnek kırpma, zaman damgası 959 örnek geri). 0.16.0'a kadar bunu
    FFmpeg'in kendi seçeneği (`alimiter latency=1`) yapıyordu; o seçenek sesin zaman damgasında tek örneklik bir
    düzensizlik görünce (CapCut'ın dışa aktardığı MP4 / MOV dosyalarında var) telafiyi bırakıyor: çıktının ses izinde
    5 ms'lik bir boşluk kalıyor, sonrası 5 ms geç etiketleniyordu (ses örnekleri tam ve yerindeydi; MKV / WebM
    kaynaklarda ve sınırlayıcının girmediği seste sorun yoktu). Yeni zincir düzenli seste eskisiyle bit bit aynı sesi
    verir; kaynağın zaman çizgisi sınırlayıcısız yoldaki gibi aynen korunur (`tools/ses_zaman_kontrol.py`).
  - LUFS eşitleme (0.13.0): sonuç hedefe 0,02 LU içine getirilir; 0,1 LU'dan uzak kalırsa satırda "hedefe ulaşılamadı"
    ve ipucunda nedeni yazar (6 dB'lik telafi yetmedi, sınırlayıcı doydu, çok düşük bit hızında AAC yüksekliği
    sıçrayarak değiştiriyor). Denemeler sınırlayıcının ölçekten bağımsızlığıyla ve hedefin iki yanı daraltılarak yapılır
    (en çok 10). Seviye (elle kazanç) ölçüme girmez, ölçülene eklenir: FFmpeg'in ölçeri +10 LUFS'un üstünü ölçemez
    (çok yüksek seste ölçüm ses kısılarak yinelenir), -70'in altını sessiz sayar.
  - Tek kopya (0.16.1): aynı exe ikinci kez açılırsa yeni pencere açılmaz, açık olan öne gelir (simge durumundaysa
    geri açılır). İki pencere aynı `ayarlar.json`'ı birbirinin üstüne yazmaz, aynı video iki kez işlenmez. Yalnızca
    Windows'un oturuma özel adlandırılmış nesneleri kullanılır (ağ, dosya, kayıt defteri yok); açık kopya 5 saniyede
    yanıt vermezse ya da denetim yapılamazsa program her zamanki gibi açılır. Başka klasördeki kopya ya da başka sürüm
    ayrıca açılabilir; o durumda da çıktılar çakışmaz: kodlama sürerken yazılan yarım dosyanın adı kopyaya özeldir
    (`<çıktı>.<işlem numarası>.part<uzantı>`; 0.16.0'a kadar `<çıktı>.part<uzantı>` idi, iki kopya aynı adlı videoyu
    aynı klasöre aktarırsa aynı dosyaya yazıp çıktıyı bozabiliyordu), biten dosya ad bu sırada alındıysa sıradaki boş
    ada (`_2`, `_3` …) taşınır.
  - Ayarlar program klasöründeki `ayarlar.json` dosyasında, hata günlükleri `Günlük` klasöründe durur
    (kurulum yok). Program bir arşivin içinden (WinRAR, 7-Zip, zip) açılırsa arşiv programı exe'yi geçici bir
    klasöre açar ve kapanınca siler; o durumda ayarlar ve günlükler `%LOCALAPPDATA%\Relios Export` klasörüne yazılır.
- `ffmpeg-build/` : FFmpeg'in (libfdk_aac dahil, ağ desteği kapalı) Windows 64-bit derlemesi.
  GitHub Actions'ta en son kararlı sürümlerle derlenir. Programın içine gömülen onaylı derleme `ONAYLI_DERLEME`'de
  (çalışma numarası), dosyalarının SHA-256'sı `ONAYLI_SHA256`'da kayıtlıdır: `app-build.yml` indirdiği dosyaları
  bununla karşılaştırır (farklıysa derleme durur). GitHub derleme çıktılarını en çok 90 gün saklar; onaylı arşiv
  `ffmpeg-sakla.yml` ile (aynı dosyalar, yeniden derlemeden) yeniden yüklenir.
- `tools/` : GitHub'daki doğrulama araçları (deneme videoları, çıktı kontrolleri, 0.3.0 komut karşılaştırması,
  kombinasyon denemesi: her ayarın her değeri ve değer çiftleri 73 kaynakla gerçek kodlamayla, 4747 deneme; önerilen
  ayarların (beşi) beklenen kodlamalarla karşılaştırılması; ekran kartıyla kodlanmış önerilen ayar çıktılarını kullanıcının
  bilgisayarında denetleyen `onerilen_nvenc_kontrol.py` ve H.264 başlık özetçisi `h264_basliklar.py`; çıktının ses
  izinin zaman çizgisini denetleyen `ses_zaman_kontrol.py`).
- `GELISTIRME.md` : programı ileride geliştirmek için gerekenler (derleme, doğrulama, saklanması gereken dosyalar,
  süresi dolan şeyler).

Not: libfdk_aac içeren FFmpeg derlemesi "nonfree"dir; yalnızca kişisel kullanım içindir,
yeniden dağıtılamaz.
