# Relios FFmpeg derlemesi

BtbN/FFmpeg-Builds tabanli, kisisel kullanim icin sadelestirilmis derleme.

- Hedef: Windows 64-bit, statik, `nonfree` (libfdk_aac dahil)
- Tum bilesenler derleme aninda `relios/pin_stable.sh` ile en son KARARLI
  surum etiketine sabitlenir (rc/beta/gelistirme dallari kullanilmaz).
  x264: XviD4PSP 8.2.14'teki surumun aynisi (core 165 r3223 0480cb0), sabit commit;
  videoya XviD4PSP gibi "x264 core 165 r3223 0480cb0" yazar (sig klonda bos kalan surum satiri sabitlenir).
- Icerik (yalnizca kullanilanlar): x264 (core 165 r3223) + NVENC ile H.264 kodlama,
  FDK AAC ve PCM/WAV (16/24/32 bit) ses, soxr ile 44.1/48 kHz ornekleme,
  dav1d ile AV1 kaynak cozme, zlib. FFmpeg'in kendi cozuculeri (H.264, HEVC,
  VP9, ProRes, AAC, Opus, MP3, FLAC, PCM...) her kaynak icin icinde.

Lisans notu: libfdk_aac iceren bu derleme yeniden DAGITILAMAZ; yalnizca kisisel kullanim icindir.
