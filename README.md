# Relios Export

TikTok ve YouTube için video dışa aktarma programı (FFmpeg + FDK AAC). Tamamen çevrimdışı.

- `app/ReliosExport/` : Program (.NET 11 WPF, tek exe; FFmpeg içinde). Arayüz Relios Vision / Relios Color /
  Relios Audio ile aynı tasarım dilinde.
  - Her profilin (TikTok, YouTube) kendi ayarları vardır; sağdaki ayar panelinde üç bölüm:
    Video (çözünürlük, kare hızı, x264 / NVENC), Ses (kaynak, gecikme, seviye, örnekleme, kanal,
    FDK AAC / WAV) ve Anahtar kare. x264, NVENC ve FDK AAC'nin bütün ayarları XviD4PSP'deki gibi
    sekmeli pencerelerde. Ayarlar adıyla ön ayar olarak kaydedilebilir.
  - TikTok varsayılanı (0.11.0): x264 veryslow, CRF 24, High profil, 16 referans kare, önden bakış 250 (diğer x264
    ayarları hız ayarının: 8 B-kare, B-piramit, umh, subme 10, trellis 2, 8x8 DCT); bit hızı üst sınırı yok; anahtar kare
    en çok 10 saniyede bir (en az ve sahne eşiği x264'ün); çözünürlük, kare hızı (60 fps korunur) ve seviye kaynakla aynı
    (16 referans kare yetmezse yükselir: 1080p'de 5.1); ses FDK AAC-LC 128 kb/s 44,1 kHz stereo; MOV. TikTok yüklenen
    videoyu kendisi yeniden kodlar: TikTok'un kendi sayfalarındaki video bilgisine göre 1080p sürümü H.265, dikey
    1080×1920 60 fps PUBG videolarında 2,2–2,4 Mb/s (bütün 1080p 60 fps örneklerde 1,6–3,4 Mb/s); 60 fps yalnızca
    1080p sürümde kalıyor, 720p ve 540p 30 fps. Bu dönüşümün benzetiminde (aynı codec, bit hızı ve kare hızı) CRF 24'e
    kadar yüklemenin TikTok'taki sonucu kaynağın kendisini yüklemekle aynı (fark ≤ 0,06 dB), CRF 26'da kayıp başlıyor;
    önceki varsayılana (NVENC 34375 kb/s) göre dosya ~%70 küçük. Kullanıcının bilgisayarında programla denendi.
    Son kullanılan ayar 0.10.0'ın değiştirilmemiş TikTok ayarıysa (video, anahtar kare, ses ayrı ayrı) kendiliğinden
    yenisine geçer; LUFS eşitleme seçimi ve ön ayarlar olduğu gibi kalır.
  - TikTok'ta NVENC seçilirse 0.10.0'ın varsayılanı kullanılır: ekran kartıyla (NVENC H.264) CBR 34375 kb/s, tampon 1
    saniye; H.264 profili ve seviyesi kaynakla aynı; B-kare yok, 3 referans kare. NVENC seviyeyi katı denetler: kaynağın seviyesi çıktının boyutuna, kare hızına,
    bit hızına ya da tamponuna yetmiyorsa (ör. 34375 kb/s, 4.0 ve altının sınırını aşar) seviye yeten en düşük
    seviyeye çıkar; yoksa kart kodlamayı hiç başlatmaz. Referans kare tamponu (DPB) da referans kare sayısı kadar
    verilir (`-dpb_size`): ffmpeg'de `-refs` yalnızca kare başına referanstır, DPB boş kalınca kart 6 kare seçer
    ve 1080p'de 4.x seviyelerini (en fazla 4 kare) aşar.
  - Ön ayar listesine program kendiliğinden bir şey koymaz; yalnızca kullanıcının kaydettikleri durur.
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
    sesi yerine o kullanılır. Videoda birden fazla ses izi varsa WAV (PCM) ya da başka kayıpsız iz tercih edilir
    (aynı videoda WAV ve AAC varsa FDK AAC'ye WAV'dan çevrilir), yoksa ilk iz.
    Ses (videonun içindeki PCM / WAV dahil) 48 kHz'e soxr ile çevrilir;
    seviye değişmez. Mono ses stereoya iki kanala tam seviyede açılır (ffmpeg'in `-ac 2` açılışı -3 dB kısardı).
  - Ayarlar program klasöründeki `ayarlar.json` dosyasında, hata günlükleri `Günlük` klasöründe durur
    (kurulum yok). Program bir arşivin içinden (WinRAR, 7-Zip, zip) açılırsa arşiv programı exe'yi geçici bir
    klasöre açar ve kapanınca siler; o durumda ayarlar ve günlükler `%LOCALAPPDATA%\Relios Export` klasörüne yazılır.
- `ffmpeg-build/` : FFmpeg'in (libfdk_aac dahil, ağ desteği kapalı) Windows 64-bit derlemesi.
  GitHub Actions'ta en son kararlı sürümlerle derlenir.
- `tools/` : GitHub'daki doğrulama araçları (deneme videoları, çıktı kontrolleri, 0.3.0 komut karşılaştırması).

Not: libfdk_aac içeren FFmpeg derlemesi "nonfree"dir; yalnızca kişisel kullanım içindir,
yeniden dağıtılamaz.
