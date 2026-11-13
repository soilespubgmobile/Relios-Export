# Relios Export

TikTok ve YouTube için video dışa aktarma programı (FFmpeg + FDK AAC). Tamamen çevrimdışı.

- `app/ReliosExport/` : Program (.NET 11 WPF, tek exe; FFmpeg içinde). Arayüz Relios Vision / Relios Color /
  Relios Audio ile aynı tasarım dilinde.
  - Her profilin (TikTok, YouTube) kendi ayarları vardır; sağdaki ayar panelinde üç bölüm:
    Video (çözünürlük, kare hızı, x264 / NVENC), Ses (kaynak, gecikme, seviye, örnekleme, kanal,
    FDK AAC / WAV) ve Anahtar kare. x264, NVENC ve FDK AAC'nin bütün ayarları XviD4PSP'deki gibi
    sekmeli pencerelerde. Ayarlar adıyla ön ayar olarak kaydedilebilir.
  - TikTok varsayılanı: ekran kartıyla (NVENC H.264) CBR 34375 kb/s; çözünürlük, kare hızı, H.264 profili
    ve seviyesi kaynakla aynı; B-kare yok, 3 referans kare, 30 karede bir anahtar kare; FDK AAC 128 kb/s 48 kHz.
  - Dikey 9:16 hazır ön ayarı: 1216×2160 9:16, 29,97 fps, x264 Main@L5.0 CRF 18, FDK AAC 128 kb/s 48 kHz
    (önceki TikTok varsayılanı; hedef videoyla doğrulanmış).
  - YouTube varsayılanı: dikey 9:16 ayarıyla aynı x264 / FDK AAC ayarları; çözünürlük, en-boy oranı, kare hızı,
    H.264 profili ve seviyesi kaynakla aynı.
  - Videonun yanında aynı adlı ses dosyası (ör. `edit1.mp4` için `edit1.wav`) varsa videonun kendi
    sesi yerine o kullanılır.
  - Ayarlar program klasöründeki `ayarlar.json` dosyasında durur (kurulum yok).
- `ffmpeg-build/` : FFmpeg'in (libfdk_aac dahil, ağ desteği kapalı) Windows 64-bit derlemesi.
  GitHub Actions'ta en son kararlı sürümlerle derlenir.
- `tools/` : GitHub'daki doğrulama araçları (deneme videoları, çıktı kontrolleri, 0.3.0 komut karşılaştırması).

Not: libfdk_aac içeren FFmpeg derlemesi "nonfree"dir; yalnızca kişisel kullanım içindir,
yeniden dağıtılamaz.
