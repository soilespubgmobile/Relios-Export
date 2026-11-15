# Relios Export — yeni oturum için devir notu

Bu dosya önceki Claude sohbetinin özetidir. Yeni oturum buradan devam eder.

## Kullanıcıyla çalışma
- Her zaman **Türkçe** yanıt ver. Kısa ve doğrudan ol; yalnızca sorulana cevap ver ("sadece dediğime cevap ver").
- "Hiç bir hata yapma": her değişikliği GitHub doğrulamasıyla kanıtla, sonra bildir.
- Kullanıcı kendi testlerini yapıyor; test sonucunu söylediğinde ona güven, tartışma.
- Şifre yazma; kullanıcının e-postası özeldir.

## Program
- Tek exe WPF (.NET 11) video dışa aktarıcı: FFmpeg 9.0.2 + FDK AAC (nonfree derleme — yalnızca kişisel kullanım, dağıtılmaz).
- Profiller: **TikTok** ve **YouTube** (Instagram yok). Kaynak her formatta okunur, çıktı yalnızca H.264 (x264 core 165 r3223 0480cb0 veya NVENC).
- Sürüm: **0.8.9** (`app/ReliosExport/ReliosExport.csproj`). Ayar sürümü `ProfilAyarlari.GuncelSurum = 10`.

## Kalıcı kurallar (asla bozma)
- XviD4PSP.exe'ye ve ayarlarına/ön ayarlarına dokunma (imza değişirse lisans iptal).
- Program tamamen çevrimdışı; ağ kodu yok.
- Kararsız / en son kararlı olmayan bileşen kullanma; bileşenler resmi kaynaktan, orijinal.
- Programın içinde (arayüz, açıklamalar) başka hiçbir program adı geçmez: XviD4PSP de, kullanıcının diğer programları (Relios Audio/Mirror/Color/Vision…) de.
- Video ve ses dili her zaman `eng`.
- Ön ayar listesine program kendiliğinden bir şey koymaz; kullanıcının kaydettiği ön ayarlar kaydedildiği gibi yüklenir.
- ReliosAudio, Relios-Mirror, dlss5-visual-enhancer depolarına yalnızca okuma; asla push yok.
- Sınıflandırıcının reddettiği bir işlemin etrafından dolaşma.

## Varsayılanlar (0.8.9)
- **TikTok:** NVENC CBR 34375 kb/s, tampon boş (= bit hızı); çözünürlük/fps/profil/seviye kaynakla aynı; B-kare 0, ref 3, GOP 30 kare, scenecut 0; MOV.
- **YouTube:** x264 CRF 18, VBV 34375 / 55000; çözünürlük/fps/profil/seviye kaynakla aynı (seviye gerekirse yükseltilir); anahtar kare 30 fps'e göre 50/26 (60 fps'e uyarlanır); MOV.
- **Ses (ikisinde de):** WAV (PCM) 16 bit, 48 kHz, stereo; dönüşüm soxr 33 bit (En yüksek), titreşim Üçgen (triangular). 24/32 bit/float kaynak 16 bite titreşimle iner; 16 bit 48 kHz kaynak bit bit aynı kalır (eşitleme açıkken: ses zaten -14 ya da daha yüksekse; kısıksa -14'e yükseltilir).
- Encoded date / Tagged date = dışa aktarma anı (`-metadata creation_time=now`, UTC).
- Varsayılan çıktı klasörü: İndirilenler.
- x264 threads 8.
- **Ses yüksekliği (ikisinde de):** tepe sınırlayıcı açık, -1 dB (alimiter; yalnızca ses yükseltilince — artı dB ya da LUFS eşitleme — devreye girer, yoksa sese dokunmaz). Sınırlayıcı **192 kHz'te** çalışır (gerçek tepe, örnekler arası dahil): elle yükseltmede ses önce soxr ile 192 kHz'e çıkar, sonra hedef hıza döner; eşitlemede loudnorm zaten 192 kHz verir (16 Kasım). LUFS eşitleme **açık** (16 Kasım, kullanıcı isteği; sürüm 10 geçişi kayıtlı profil ayarlarını da açar, ön ayarlar kaydedildiği gibi kalır, Dikey 9:16 hedef ayarı eşitlemesiz), hedef -14, yön "yalnızca kısık sesi yükselt". **16 Kasım'dan beri dinamik kip yok:** ölçüm (loudnorm) sonrası kısık ses her zaman bütünüyle **sabit kazançla** (`volume=hedef-ölçülen`) -14'e getirilir, ses aralığı (LRA) karara girmez; ±0,5 LU hedefteyse ya da yüksekse dokunulmaz. Kazanç tepeyi -1'in üstüne çıkaracaksa 192 kHz sınırlayıcı tutar (kutusu kapalı olsa da) ve kıstığı pay yalnızca ses okuyan deneme ölçümleriyle telafi edilir (en çok 3 deneme, en çok 6 dB; `Esitleme.TelafiAdimi`, `ArgsBuilder.DenemeOlcumKomutu`). Bitince çıktı ölçülüp satıra yazılır. TikTok -14 kullanıcının kararı (TikTok resmi değer yayınlamıyor).
- Tekrar işle: bütün videolar bitince Başlat düğmesi "Tekrar işle" olur, hepsini o anki ayarlarla yeniden işler. Bekleyen satırda tahmini dosya boyutu, biten satırda gerçek boyut.
- Ayar geçişleri `app/ReliosExport/Core/AppSettings.cs` içinde (`Yukselt`); ses geçişi ön ayarlara uygulanmaz.

## Doğrulama (her değişiklikte)
1. Push → `app-build.yml` çalışır (ekran görüntüleri log'da base64, `EKRAN|ad|...` satırları).
2. `gh api -X POST repos/soilespubgmobile/Relios-Export/actions/workflows/ffmpeg-dogrula.yml/dispatches -f ref=main -f inputs[run_id]=43369861243`
3. Bakılacaklar: birim kontrol (`--birim-kontrol`, son: 245 kontrol 0 fark), 19 deneme videosu, Diğer ayarlar, Ses dönüşümü, x264 seviye tabloları, Ses yüksekliği (16 deneme; karar adları Dogrusal / Dogrusal/Sinirli / Hedefte / ZatenYuksek), Tahmini boyut (4 deneme) — hepsi 0 fark.
- `app-build.yml` ayrıca **uçtan uca arayüz denemesi** çalıştırır (`--uctan-uca`, `Arayuz/UctanUca.cs`): gerçek pencereyle ekle / Başlat / yalnızca hatalıyı yeniden / çıkar / Tekrar işle + eşitleme / iptal / TikTok-NVENC hatası + günlük / Türkçe dosya adı / temizle / ayar dosyası — son: 101 kontrol 0 fark. GitHub'da ekran kartı yok: NVENC kodlaması yalnızca kullanıcının bilgisayarında denenebilir.
- Exe bu oturumdan indirilemiyor (artifact deposu engelli): kullanıcıya Actions sayfasındaki Artifacts bağlantısı verilir.
- Yardımcılar: `tools/hedef_kontrol.py` (`--ses pcm16`, `--vbv`, `--kap`), `tools/ayar_kontrol.py`, `tools/saglamlik_kontrol.py`, `tools/ses_donusum_kontrol.py`.
- Commit mesajlarında Co-Authored-By satırı kullanılıyor.

## Son oturum (15 Kasım 2026) — A'dan Z'ye

### Yapılanlar (hepsi GitHub'da doğrulandı; son yeşil: app-build 43763139516, ffmpeg-dogrula 43763152254, commit 0a501ee)
1. **Tekrar işle:** Önce satıra küçük bir düğme eklendi; kullanıcı beğenmedi, kaldırıldı. Şimdi bütün videolar bitince **Başlat** düğmesinin yazısı **"Tekrar işle"** oluyor ve hepsini o anki ayarlarla yeniden işliyor. Önceki dosya silinmiyor, yenisi `_2` adıyla kaydediliyor. Bekleyen ya da hatalı video varsa düğme "Başlat" kalıyor ve yalnızca onları işliyor.
2. **Tek ilerleme çizgisi:** Alttaki genel ilerleme şeridi kaldırıldı; yalnızca videonun satırındaki çizgi kaldı. Görev çubuğundaki ilerleme duruyor.
3. **Tahmini dosya boyutu:** Bekleyen satırda "tahmini 257 MB" ya da "en çok … MB" yazıyor (`Core/BoyutTahmini.cs`); "en çok" x264 tampon payını da sayıyor (tampon × vbv-init, varsayılan 0,9). Biten satırda gerçek boyut yazıyor. İpucunda qpmax notu var.
4. **Tepe sınırlayıcı:** alimiter, -1 dB, varsayılan açık. Yalnızca ses yükseltilince devreye giriyor (artı dB ya da LUFS eşitleme); yoksa sese dokunmuyor.
5. **LUFS eşitleme** (`Core/Esitleme.cs`): loudnorm iki geçiş. Önce varsayılan kapalıydı; **16 Kasım'da kullanıcının isteğiyle TikTok ve YouTube'da varsayılan açık yapıldı** (ayar sürümü 10). Hedef -14. Yön: "Yalnızca kısık sesi yükselt" (varsayılan) ya da "Kısık sesi yükselt, yüksek sesi kıs". Hedefe ±0,5 LU yakınsa dokunmuyor. Doğrusal/dinamik kararı loudnorm'un kendi kuralıyla veriliyor (af_loudnorm.c): tepe + kazanç ≤ tavan ve 0 < LRA ≤ 11 ise doğrusal (sabit kazanç), değilse dinamik. Bitince çıktı ölçülüp satıra yazılıyor ("-14,0 LUFS", "(hedefe ulaşılamadı)", "(dokunulmadı)"); ayrıntı ipucunda. Mono çıktıda önce mono'ya iniliyor, sonra ölçülüyor.
   - **TikTok -14 kullanıcının kararı.** Kullanıcı kendi "Relios Master" programında TikTok için -10 kullanmıştı; bu program GitHub'da yok, bilgisayarında İndirilenler'de "Relios Master 1.8.1" var. Araştırma sonucu: TikTok resmi bir değer yayınlamıyor; kaynakların çoğu -14 diyor, bazıları -10 ile -12 arası öneriyor.
6. **Kapsamlı test:** Uçtan uca arayüz denemesi (99 kontrol, app-build'de), ses yüksekliği (14 deneme), tahmini boyut (4 deneme), birim kontrol (218). Bulunan 3 hata düzeltildi (aşağıdaki "Son konuşulanlar"da).

### Önerilip yapılmayanlar (kullanıcı "önerilere sonra bakarım" dedi; sorulmadan yapma)
- HDR ton eşleme: zimg gerekir, FFmpeg yeniden derlenmeli. Kullanıcı sordu; cevap: SDR için gerekmez, yalnızca HDR telefon videoları için.
- x264 yolunda da ekran kartıyla çözme (NVDEC).
- Aynı anda iki video işleme.
- x264 threads için "otomatik" seçeneği (varsayılan 8 kalır).
- Tamamen ekran kartında işleme (scale_cuda; bit bit aynı olmaz).
- NVENC için qmin/qmax, gpu ve surfaces kutuları.
- Başlangıç/bitiş kesme; bitince bildirim sesi ya da bilgisayarı kapatma; "kaynağın yanına kaydet" seçeneği.
- Sürüm yükseltme (0.8.9 → 0.9.0): kullanıcıya soruldu, cevap gelmedi; sürüm hâlâ 0.8.9.

### Yapılmayanlar / sıradaki iş
- **Bilgisayar testleri yapılmadı.** Kullanıcının bilgisayarına bağlanıldı:
  - Windows, cihaz `desktop-jdnkoph`, kullanıcı klasörü `C:\Users\relio`.
  - İndirilenler klasörüne erişim verildi.
  - Bilgisayar kullanımı: File Explorer yalnızca "click" düzeyinde verilebiliyor. Çalışırken Chrome ve Discord pencereleri gizleniyor.
  - `Relios Export` kurulu bir uygulama olmadığı (taşınabilir exe) için `computer_resolve_access` onu bulamadı. Program açıkken bulup bulamadığı denenmedi; bulamazsa kullanıcıdan programı açmasını ya da çözüm üretmesini iste.
- **Yeni exe kullanıcının bilgisayarında değil.** İndirilenler'deki `Relios Export.exe` 14 Kasım tarihli (eski). En yeni exe: Actions run **43774713565** → Artifacts → `relios-export-win64` (~87 MB zip; 16 Kasım, sabit kazançlı eşitleme). (Önceki: 43769903001, 43763139516.) Kullanıcı 16 Kasım'da 43769903001'i kendisi indirip `İndirilenler\Yeni klasör (3)`'e çıkardı ve programı kullanıyor.
  - Bulut oturumundan artifact indirilemiyor (blob deposu engelli).
  - Uygulama içi tarayıcı GitHub'a girişli değil.
  - Kullanıcıya iki yol sunuldu, cevap gelmedi: A) tarayıcı panelinde (Ctrl+Shift+B) GitHub'a kendisi giriş yapar, Claude indirir; B) Chrome'dan zip'i kendisi indirir.
  - Exe, eski exe'nin ve ayarların üzerine yazılmadan İndirilenler'de **"Relios Export test 15-11"** klasörüne konacak. İndirme için kullanıcının açık izni gerekir (dosya adı, kaynak, boyut söylenerek).
- **Öncelikli bilgisayar testleri (sırayla):**
  1. TikTok + NVENC kodlama (GitHub'da ekran kartı yok; en önemlisi).
  2. Kodlarken pencereyi kapatma: onay sorusu, "Durdur ve kapat", `.part` kalmamalı.
  3. Programı kapatıp açma: ayarlar korunmalı.
  4. Sürükle-bırak.
  - **TikTok'a yüklemeyi kullanıcı en son kendisi yapacak.**
- **Diğer bilgisayar testleri:** satırdaki klasör ve günlük düğmeleri, kayıt yeri seçme, ön ayar kaydet/yükle/sil, YouTube'da "content loudness" (eşitleme açık), HDR telefon videosu (renklerin soluk çıkması beklenir).

## 16 Kasım oturumu
- **Yapıldı (doğrulandı; app-build 43769903001, ffmpeg-dogrula 43769910163, commit 864dc37):** tepe sınırlayıcı 192 kHz'te (öneri 6). Eskiden elle yükseltmede yalnızca örnekleri sınırlıyordu: yerel ölçümde kırpılmış gürültü +4,8 dBTP, kare dalga +1,3 dBTP; 44,1 kHz kaynak 48 kHz'e çevrilirken 0,00 dBFS'te kırpılıyordu. Şimdi: -1,00 dBTP (aşırı tam bant gürültüde en kötü ~-0,6). Yeni CI denemesi: 44,1 kHz tiz ton +12 dB. Arayüz ipucu güncellendi.
- **Yapıldı (doğrulandı; ffmpeg-dogrula 43773824293 commit 30cf4a9, app-build 43774713565 commit 1333b0f):** LUFS eşitlemede dinamik kip kaldırıldı; kullanıcının isteği: "kısıksa her zaman bütün ses stabil -14'e". Neden: kullanıcının videosu (`2026-11-14 07-24-10.mkv`, OBS kaydı; ilk 5 sn sessiz, 6–8 sn -42 LUFS, sonra ~-22; ölçüm -20,8 LUFS, tepe -11,2, LRA 21) LRA > 11 yüzünden dinamik kipe giriyordu: yükseltme video boyunca değişiyordu (+2 → +8 dB), aralık 21 → 5,7 LU sıkışıyordu, sonuç -15,8 "hedefe ulaşılamadı". Şimdi aynı video +6,8 dB sabit → -14,00 (tepe -4,4). Ani tepeli seste sınırlayıcı + telafi → -14,0 (tepe -1,00). Yeni CI denemeleri: geniş aralık (LRA 17,9) ve sabit ton sabit kazançla, tepeli ses -14 ±0,5.
- Kullanıcının bilgisayarında `C:\Users\relio\Downloads\Yeni klasör (3)` klasörüne erişim verildi (içinde exe, zip, `ayarlar.json`, kaynak MKV). İndirilenler ve Videolar köküne erişim isteği sınıflandırıcı tarafından reddedildi: dosya gerekiyorsa kullanıcıdan bu klasöre koymasını iste.
- **Kullanıcı HDR kaydetmiyor** → zimg / ton eşleme gerekmez (yapılmayacak). zimg için tek eksik kütüphane oydu; derleme ~1–1,5 sa, hepsi 3–4 sa.
- **Önemli, yapılmadı:** onaylı FFmpeg derlemesi (run 43369861243) GitHub'da **13 Aralık 2026'da siliniyor** (30 gün). Ondan önce FFmpeg yeniden derlenip doğrulanmalı (saklama 90 güne çıkarılabilir; yeniden derleme bütün bileşenleri o günkü en son kararlı sürüme çeker → hepsi yeniden doğrulanır). Kullanıcıya söylendi, karar bekleniyor.
- **NVENC için sürücü:** FFmpeg nv-codec-headers **n13.1.15.0** ile derlenmiş → Windows'ta NVIDIA sürücüsü **610.0 veya üstü** gerekir (RTX 5060 Ti). Eskiyse program "sürücü çok eski" Türkçe hatasını verir. NVENC testinden önce sürücü sürümüne bakılacak.
- **Bilgi olarak söylendi (yapılmadı):** YouTube resmi önerisi 2 ardışık B-kare + GOP kare hızının yarısı; bizde B-kare 0 ve GOP 50/26 (XviD4PSP hedefinden). NVENC'de ağırlıklı tahmin (isteğe bağlı, B-kare 0 olduğu için açılabilir). Diğer öneriler: uyku önleme, taramalı kaynak (bwdif), disk alanı kontrolü — kullanıcı yalnızca 6'yı seçti.

## Son konuşulanlar (bilgi)
- YouTube'a PCM 16 bit yükleme, FDK 128'e göre 15–17 kHz'te biraz daha iyi sonuç verdi (kullanıcı testi). YouTube sesi zaten yeniden kodluyor.
- TikTok'a WAV sesli MOV'u kullanıcı henüz denemedi — ilk yüklemede sesi kontrol etmesi önerildi.
- TikTok indirmesi (1024×576, ~2,2 Mb/s, High@3.1, kodlayıcı bvc0ot) incelendi: muhtemelen tavanlı VBR; kesinlik için dosyanın kendisi gerekiyor.
- Bir kayıt dosyası (Record_…mp4) incelendi: NVENC benzeri, Main@4.2, 1080p60, HRD'de VBR 13 Mb/s, 3 B-kare, 120 karede IDR, tam aralık; görüntü siyahtı. Kayıt cihazının saat dilimi UTC+4 görünüyordu (Türkiye UTC+3).
- Kapsamlı testte bulunan ve düzeltilen: eşitleme + mono çıktı -14 yerine ~-20 LUFS veriyordu (artık önce mono'ya inilir); ölçüm hatasında günlük yazılmıyordu; "en çok" tahmini kısa videoda tamponu saymıyordu.
- Bilgi: x264 qpmax 33 (YouTube varsayılanı, hedef videodan) saf parazit gibi aşırı karmaşık görüntüde VBV üst sınırını aşabiliyor; gerçek videolarda görülmedi, varsayılana dokunulmadı.
- Önceden önerilen ama istenmeyen fikirler: 1440p yükleme seçeneği, H.264 olmayan YouTube kaynaklarında High profil.
