# Relios Export

TikTok ve YouTube için video dışa aktarma programı (FFmpeg + FDK AAC). Tamamen çevrimdışı.

- `app/ReliosExport/` : Program (.NET 11 WPF, tek exe). Arayüz Relios Vision / Relios Color /
  Relios Audio ile aynı tasarım dilinde.
  - TikTok profili: XviD4PSP "Relios 9x16" mantığının birebir aynısı
    (1216×2160 9:16, 29,97 fps, H.264 Main@L5.0 CRF 18, FDK AAC 128 kb/s 48 kHz).
  - YouTube profili: ayarları belirlenecek.
  - `ffmpeg.exe` ve `ffprobe.exe`, exe'nin yanındaki `ffmpeg` klasöründe olmalı.
- `ffmpeg-build/` : FFmpeg'in (libfdk_aac dahil, ağ desteği kapalı) Windows 64-bit derlemesi.
  GitHub Actions'ta en son kararlı sürümlerle derlenir.

Not: libfdk_aac içeren FFmpeg derlemesi "nonfree"dir; yalnızca kişisel kullanım içindir,
yeniden dağıtılamaz.
