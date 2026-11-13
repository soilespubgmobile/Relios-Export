# Relios Export

TikTok ve YouTube için video dışa aktarma programı (FFmpeg + FDK AAC). Tamamen çevrimdışı.

- `app/ReliosExport/` : Program (.NET 11 WPF, tek exe; FFmpeg içinde). Arayüz Relios Vision / Relios Color /
  Relios Audio ile aynı tasarım dilinde.
  - Her profilin (TikTok, YouTube) kendi ayarları vardır; sağdaki ayar panelinde üç bölüm:
    Video (çözünürlük, kare hızı, x264 / NVENC), Ses (kaynak, gecikme, seviye, örnekleme, kanal,
    FDK AAC / WAV) ve Anahtar kare. x264, NVENC ve FDK AAC'nin bütün ayarları XviD4PSP'deki gibi
    sekmeli pencerelerde. Ayarlar adıyla ön ayar olarak kaydedilebilir.
  - TikTok varsayılanı: XviD4PSP "Relios 9x16" ile birebir aynı
    (1216×2160 9:16, 29,97 fps, H.264 Main@L5.0 CRF 18, FDK AAC 128 kb/s 48 kHz).
  - YouTube varsayılanı: kaynakla aynı çözünürlük ve kare hızı, H.264 High CRF 16,
    FDK AAC 384 kb/s 48 kHz.
  - Videonun yanında aynı adlı ses dosyası (ör. `edit1.mp4` için `edit1.wav`) varsa videonun kendi
    sesi yerine o kullanılır.
  - Ayarlar program klasöründeki `ayarlar.json` dosyasında durur (kurulum yok).
- `ffmpeg-build/` : FFmpeg'in (libfdk_aac dahil, ağ desteği kapalı) Windows 64-bit derlemesi.
  GitHub Actions'ta en son kararlı sürümlerle derlenir.
- `tools/` : GitHub'daki doğrulama araçları (deneme videoları, çıktı kontrolleri, 0.3.0 komut karşılaştırması).

Not: libfdk_aac içeren FFmpeg derlemesi "nonfree"dir; yalnızca kişisel kullanım içindir,
yeniden dağıtılamaz.
