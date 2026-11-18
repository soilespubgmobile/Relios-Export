# Relios Export — geliştirme kılavuzu

Programı ileride değiştirmek, yeniden derlemek ya da başka birine (insan ya da yapay zekâ) devretmek için gerekenler.
Ayrıntılı geçmiş, ölçümler ve kararlar `CLAUDE.md` dosyasındadır; bu dosya kısa yol haritasıdır.

## 1. Saklanması gerekenler

| Ne | Nerede | Neden |
|---|---|---|
| `Relios Export.exe` (son sürüm) | Kendi bilgisayarın + bir yedek | Tek başına çalışır: .NET ve FFmpeg içindedir; kurulum, internet, süre sınırı yok. GitHub'daki exe çıktısı 30 günde silinir. |
| Kaynak kodu | GitHub deposu + `relios-export-<sürüm>-kaynak.zip` + `relios-export-<sürüm>.bundle` (bütün geçmiş) | Depo silinse de devam edilebilir. |
| Onaylı FFmpeg (`ffmpeg.exe`, `ffprobe.exe`) | Her exe'nin içinde; ayrıca `relios-ffmpeg-n9.0.2-onayli.zip` | Program yalnızca bu dosyalarla doğrulandı. GitHub'daki arşivin süresi doluyor (5. bölüm). |
| `CLAUDE.md`, `GELISTIRME.md` | Deponun kökü | Kurallar, varsayılanların nedenleri, doğrulama adımları. |

FFmpeg dosyaları exe'den her zaman çıkarılabilir: `"Relios Export.exe" --ffmpeg-kontrol C:\bir\klasor\sonuc.txt`
komutu pencere açmadan çalışır ve dosyaya `klasor=…` satırını yazar (exe'nin FFmpeg'i açtığı geçici klasör). Oradaki
`ffmpeg.exe` ve `ffprobe.exe`'nin SHA-256'sı `ffmpeg-build/ONAYLI_SHA256` ile aynı olmalıdır
(PowerShell: `Get-FileHash ffmpeg.exe -Algorithm SHA256`).

## 2. Programı derlemek

**GitHub'da (her zamanki yol).** `app/` altında bir dosya değişip `main`'e gönderilince `app-build.yml` kendiliğinden
çalışır: onaylı FFmpeg'i indirir (SHA-256'sını denetler), tek exe'yi derler, pencereleri, çift açılışı, uçtan uca
arayüz denemesini ve arayüz taramasını çalıştırır, exe'yi çalışmanın "Artifacts" bölümüne koyar. Elle: Actions →
"Relios Export (uygulama)" → Run workflow.

**Kendi bilgisayarında (GitHub olmadan).**
1. .NET 11 SDK'yı kur (dotnet.microsoft.com; yalnızca derlemek için).
2. `ffmpeg.exe` ve `ffprobe.exe`'yi `app\ReliosExport\ffmpeg\` klasörüne koy.
3. Deponun kökünde:
   ```
   dotnet publish app/ReliosExport/ReliosExport.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o out
   ```
4. Sonuç: `out\Relios Export.exe` (yaklaşık 93 MB). İnternet yalnızca SDK'yı ve ilk derlemede .NET paketlerini
   indirmek için gerekir; programın kendisinde ağ kodu yoktur.

Yerel derleme GitHub'daki denemelerden geçmemiş olur; kalıcı kullanılacak exe GitHub'da derlenip doğrulanmalıdır.

## 3. Değişiklik yapmanın kuralları

- **Sürüm:** kullanıcıya verilen her yeni exe'de sürüm artar (`app/ReliosExport/ReliosExport.csproj`): hata
  düzeltmesi → son hane (0.16.1 → 0.16.2), yeni özellik ya da varsayılan değişikliği → orta hane (0.17.0). Commit
  mesajı sürümle başlar (`0.16.2: …`).
- **Ayar değişirse:** `Core/Ayarlar/Varsayilanlar.cs` + `Core/AppSettings.cs` (`Yukselt`: kayıtlı ayarların geçişi,
  `ProfilAyarlari.GuncelSurum`) + `tools/ReliosExport.Smoke/BirimKontrol.cs` (beklenen komutlar).
- **Kalıcı kurallar** (`CLAUDE.md` → "Kalıcı kurallar"): program tamamen çevrimdışı; arayüzde başka program adı
  geçmez (önerilen ayarların "CapCut" ve "After Effects" adları dışında); video ve ses dili `eng`; bileşenler resmi
  kaynaktan ve en son kararlı sürüm; ön ayar listesine program kendiliğinden bir şey koymaz.
- **Kanıt:** her değişiklik GitHub'daki denemelerle doğrulanır, sonra bildirilir (4. bölüm).

## 4. Doğrulama (her değişiklikte)

`<N>` = `ffmpeg-build/ONAYLI_DERLEME` içindeki numara.

1. `main`'e gönder → `app-build.yml` (derleme, çift açılış, uçtan uca arayüz denemesi, arayüz taraması, ekran
   görüntüleri).
2. `gh api -X POST repos/soilespubgmobile/Relios-Export/actions/workflows/ffmpeg-dogrula.yml/dispatches -f ref=main -f inputs[run_id]=<N>`
   (birim kontrol, deneme videoları, ses dönüşümü, ses yüksekliği, ses zaman çizgisi, iki kopya, tahmini boyut,
   önerilen ayarlar, x264 seviye).
3. `gh api -X POST repos/soilespubgmobile/Relios-Export/actions/workflows/kombinasyon.yml/dispatches -f ref=main -f inputs[run_id]=<N>`
   (her ayarın her değeri gerçek kodlamayla; 8 parça).
4. Hepsi "0 fark" olmalı; sayılar günlükten okunur (`gh run view <çalışma> --log`). Sonuçlar `CLAUDE.md`'ye yazılır.
5. Ekran kartı (NVENC) GitHub'da yok: NVENC kodlaması yalnızca kullanıcının bilgisayarında denenir
   (`tools/onerilen_nvenc_kontrol.py`).

## 5. Süresi dolan şeyler

| Ne | Ne zaman | Ne yapılır |
|---|---|---|
| Exe çıktısı (Actions → Artifacts) | Derlemeden 30 gün sonra | Exe indirilip saklanır. Gerekirse `app-build.yml` elle yeniden çalıştırılır. |
| Onaylı FFmpeg arşivi (run 44274951659) | **16 Şubat 2027** | Öncesinde Actions → "Onayli FFmpeg'i sakla" → Run workflow. Aynı arşivi (SHA-256 denetimiyle, derlemeden) 90 gün daha saklar. Çıkan çalışma numarası `ffmpeg-build/ONAYLI_DERLEME`'ye ve `kombinasyon.yml` / `teshis.yml` içindeki varsayılan `run_id`'ye yazılır. |
| GitHub'ın derleme ortamı (Windows imajı, .NET 11 SDK, `actions/…@v7` sürümleri) | Zamanla değişir | Derleme kırılırsa iş akışlarındaki sürümler güncellenir. Elindeki exe bundan etkilenmez. |

**Arşivin süresi kaçırılırsa** program GitHub'da derlenemez. İki yol:
- Elindeki `ffmpeg.exe` / `ffprobe.exe` ile kendi bilgisayarında derle (2. bölüm); ya da
- FFmpeg'i yeniden derle: Actions → "Relios FFmpeg (win64, nonfree, …)" → Run workflow. Bütün bileşenler o günün en
  son kararlı sürümüne çekilir (x264 sabit: core 165 r3223 0480cb0). Derleme bitince `ffmpeg-dogrula.yml`
  kendiliğinden çalışır; hepsi 0 fark ise yeni çalışma numarası `ONAYLI_DERLEME`'ye, yeni dosyaların SHA-256'sı
  `ONAYLI_SHA256`'ya yazılır, kombinasyon denemesi ve bilgisayardaki NVENC denemeleri yinelenir. Bu, programın
  FFmpeg'ini değiştirir: çıktılar artık eski exe'ninkilerle bit bit aynı olmayabilir.

**Depo herkese açık.** FFmpeg derlemesi "nonfree" (FDK AAC) olduğundan dağıtılamaz: dosyaları depoya ya da bir
"Release"e koyma. Depo gizli yapılırsa konabilir; o zaman arşivin süresi sorun olmaktan çıkar.

## 6. Dosya haritası

- `app/ReliosExport/` — program.
  - `App.xaml.cs`: açılış, tek kopya denetimi, derleme denetimi kipleri (`--ffmpeg-kontrol`, `--pencere-kontrol`,
    `--uctan-uca`, `--arayuz-taramasi`).
  - `MainWindow.xaml(.cs)`: ana pencere, liste, ayar paneli; `X264Penceresi`, `NvencPenceresi`, `FdkPenceresi`,
    `OnAyarPenceresi`.
  - `Core/ArgsBuilder.cs`: FFmpeg komutunun tamamı (video, ses zinciri, seviye, anahtar kare).
  - `Core/FfmpegRunner.cs`: `Exporter` (ölçüm, denemeler, kodlama, yarım dosya, çıktının adı), `FfmpegRunner`.
  - `Core/Esitleme.cs`: LUFS eşitleme ve tepe koruması kararları. `Core/BoyutTahmini.cs`, `Core/H264Seviye.cs`,
    `Core/FdkUyumu.cs`, `Core/MediaProbe.cs`, `Core/HataAciklamasi.cs`.
  - `Core/Ayarlar/`: ayar sınıfları, `Varsayilanlar.cs` (TikTok / YouTube / önerilen ayarlar).
  - `Core/AppSettings.cs`: `ayarlar.json` (okuma, yazma, sürüm geçişleri). `Core/AppPaths.cs`: klasörler.
  - `Arayuz/UctanUca.cs`, `Arayuz/ArayuzTaramasi.cs`: programın içindeki arayüz denemeleri (yalnızca GitHub'da).
- `tools/` — doğrulama araçları (programın parçası değil): `ReliosExport.Smoke` (kodlama kodunu arayüzsüz
  çalıştırır; `BirimKontrol.cs`), `hedef_kontrol.py`, `ayar_kontrol.py`, `saglamlik_kontrol.py`,
  `ses_donusum_kontrol.py`, `ses_yukseklik_kontrol.py`, `ses_zaman_kontrol.py`, `onerilen_kontrol.py`,
  `kombinasyon.py`, `onerilen_nvenc_kontrol.py`, `h264_basliklar.py`.
- `.github/workflows/` — `app-build.yml`, `ffmpeg-dogrula.yml`, `kombinasyon.yml`, `ffmpeg-build.yml`,
  `ffmpeg-sakla.yml`, `teshis.yml`, `tiktok-calisma.yml`, `surum-bilgisi.yml`.
- `ffmpeg-build/` — FFmpeg derlemesi (`README-RELIOS.md`), `ONAYLI_DERLEME`, `ONAYLI_SHA256`.

## 7. Başka biriyle devam etmek

Depoyu (ya da kaynak zip'ini) ver ve önce `CLAUDE.md`'yi okut: çalışma biçimi (Türkçe, kısa, kanıtla), kalıcı
kurallar, bütün varsayılanların nedenleri, her sürümün doğrulama sonuçları ve bilgisayar testlerinin notları oradadır.
