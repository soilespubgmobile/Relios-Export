# Relios FFmpeg derlemesi

BtbN/FFmpeg-Builds tabanli, kisisel kullanim icin sadelestirilmis derleme.

- Hedef: Windows 64-bit, statik, `nonfree` (libfdk_aac dahil)
- Tum bilesenler derleme aninda `relios/pin_stable.sh` ile en son KARARLI
  surum etiketine sabitlenir (rc/beta/gelistirme dallari kullanilmaz).
  x264: XviD4PSP 8.2.14'teki surumun aynisi (core 165 r3223 0480cb0), sabit commit;
  videoya XviD4PSP gibi "x264 core 165 r3223 0480cb0" yazar: kaynak resmi depodan tam gecmisiyle
  indirilir ve bu etiketi x264'un kendi version.sh'i hesaplar (elle yazilan bir deger yok).
- Icerik (yalnizca kullanilanlar): x264 (core 165 r3223) + NVENC ile H.264 kodlama
  (HEVC ve AV1 NVENC kodlayicilari derlenmez; NVDEC ile ekran karti cozme tum bicimlerde var),
  FDK AAC ve PCM/WAV (16/24/32 bit) ses, soxr ile 44.1/48 kHz ornekleme,
  dav1d ile AV1 kaynak cozme, zlib. FFmpeg'in kendi cozuculeri (H.264, HEVC,
  VP9, ProRes, AAC, Opus, MP3, FLAC, PCM...) her kaynak icin icinde.

Lisans notu: libfdk_aac iceren bu derleme yeniden DAGITILAMAZ; yalnizca kisisel kullanim icindir.

## Programa gomulen FFmpeg (ONAYLI_DERLEME)

`ONAYLI_DERLEME` dosyasinda, 13 videoluk Windows testinden ("FFmpeg dogrulama (Windows)")
gecmis FFmpeg derlemesinin run numarasi yazar. "Relios Export (uygulama)" is akisi bu
derlemenin ffmpeg.exe ve ffprobe.exe'sini tek exe'nin icine gomer; dosya degisince program
yeniden derlenir. Gomulen dosyalarin SHA-256'si `ONAYLI_SHA256` dosyasinda kayitlidir: is akisi
indirdigi dosyalari bununla karsilastirir, farkliysa derleme durur.

GitHub derleme ciktilarini sinirli sure saklar (FFmpeg derlemesi 30 gun; en cok 90 gun). Onayli
derleme silinmeden once "Onayli FFmpeg'i sakla" is akisi (`ffmpeg-sakla.yml`) elle calistirilir:
AYNI arsivi (dosyalar SHA-256 ile dogrulanir, hicbir sey derlenmez) 90 gun saklanacak yeni bir
cikti olarak yukler; sonra `ONAYLI_DERLEME`'ye o calismanin numarasi yazilir. Onayli derleme:
13 Kasim 2026 (run 43369861243); 18 Kasim 2026'da yeniden yuklendi (run 44274951659, 16 Subat
2027'ye kadar). Arsiv silinirse FFmpeg yeniden derlenip (butun bilesenler o gunun en son kararli
surumune cekilir) bastan dogrulanmali, `ONAYLI_DERLEME` ve `ONAYLI_SHA256` guncellenmelidir.
Ayni dosyalar verilen her "Relios Export.exe"nin icindedir (ilk acilista gecici klasore acilir;
`"Relios Export.exe" --ffmpeg-kontrol sonuc.txt` klasoru yazar): GELISTIRME.md.
