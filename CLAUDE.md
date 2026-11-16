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
- Sürüm: **0.8.9** (`app/ReliosExport/ReliosExport.csproj`). Ayar sürümü `ProfilAyarlari.GuncelSurum = 11`.

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
- **TikTok sesi:** WAV (PCM) 16 bit, 48 kHz, stereo; dönüşüm soxr 33 bit (En yüksek), titreşim Üçgen (triangular). 24/32 bit/float kaynak 16 bite titreşimle iner; 16 bit 48 kHz kaynak bit bit aynı kalır (eşitleme açıkken: ses zaten -14 ya da daha yüksekse; kısıksa -14'e yükseltilir).
- **YouTube sesi (16 Kasım, kullanıcı isteği, ayar sürümü 11):** FDK AAC-LC **128 kb/s CBR**, 48 kHz, stereo, afterburner açık; dönüşüm soxr 33 bit, titreşim Üçgen; WAV seçilirse 16 bit (`Varsayilanlar.YouTubeSesi`). Sürüm 11 geçişi: kayıtlı YouTube ayarında ses tam 0.8.9 varsayılanıysa (WAV 16 48k stereo En yüksek Üçgen) FDK 128'e geçer; eşitleme seçimi, kullanıcının değiştirdiği ses, TikTok ve ön ayarlar olduğu gibi kalır.
- Encoded date / Tagged date = dışa aktarma anı (`-metadata creation_time=now`, UTC).
- Varsayılan çıktı klasörü: İndirilenler.
- x264 threads 8.
- **Ses yüksekliği (ikisinde de):** tepe sınırlayıcı açık, -1 dB (alimiter; yalnızca ses yükseltilince — artı dB ya da LUFS eşitleme — devreye girer, yoksa sese dokunmaz). Sınırlayıcı **192 kHz'te** çalışır (gerçek tepe, örnekler arası dahil): ses önce soxr ile 192 kHz'e çıkar, sonra hedef hıza döner (16 Kasım). LUFS eşitleme **açık** (16 Kasım, kullanıcı isteği; sürüm 10 geçişi kayıtlı profil ayarlarını da açar, ön ayarlar kaydedildiği gibi kalır, Dikey 9:16 hedef ayarı eşitlemesiz), hedef -14, yön "yalnızca kısık sesi yükselt". **16 Kasım'dan beri dinamik kip yok:** ölçüm sonrası kısık ses her zaman bütünüyle **sabit kazançla** (`volume=hedef-ölçülen`) -14'e getirilir, ses aralığı (LRA) karara girmez; ±0,5 LU hedefteyse ya da yüksekse dokunulmaz. Kazanç tepeyi -1'in üstüne çıkaracaksa (tepe + 0,05 dB pay, `Esitleme.TepePayi`) 192 kHz sınırlayıcı tutar (kutusu kapalı olsa da) ve kıstığı pay yalnızca ses okuyan deneme ölçümleriyle telafi edilir (en çok 3 deneme, en çok 6 dB; `Esitleme.TelafiAdimi`, `ArgsBuilder.DenemeOlcumKomutu`). Bitince çıktı ölçülüp satıra yazılır.
- **Ölçer (16 Kasım, commit 66020e0):** ölçüm, deneme ve sonuç ölçümü **ebur128** (BS.1770-4; `Esitleme.OlcumFiltresi` = `ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I`): tümleşik yükseklik son `lavfi.r128.I=` satırından (0,001), gerçek tepe ve LRA özetten (0,1); -70 = sessiz/0,4 sn'den kısa → ölçülemedi (`ArgsBuilder.OlcumOku`, kayıt `SesOlcum`). **loudnorm ölçümü kullanılmaz:** af_loudnorm.c `flush_frame` sesin son ~2,9 sn'sini r128_in'e ikinci kez ekliyor; kullanıcının OBS kaydında -14,0 diye yazılan çıktı standarda göre -13,56 idi (simülasyonla kanıtlandı: son 2,9 sn iki kez sayılınca tam loudnorm'un değerleri çıkıyor). TikTok -14 kullanıcının kararı (TikTok resmi değer yayınlamıyor).
- Tekrar işle: bütün videolar bitince Başlat düğmesi "Tekrar işle" olur, hepsini o anki ayarlarla yeniden işler. Bekleyen satırda tahmini dosya boyutu, biten satırda gerçek boyut.
- Ayar geçişleri `app/ReliosExport/Core/AppSettings.cs` içinde (`Yukselt`); ses geçişi ön ayarlara uygulanmaz.

## Doğrulama (her değişiklikte)
1. Push → `app-build.yml` çalışır (ekran görüntüleri log'da base64, `EKRAN|ad|...` satırları).
2. `gh api -X POST repos/soilespubgmobile/Relios-Export/actions/workflows/ffmpeg-dogrula.yml/dispatches -f ref=main -f inputs[run_id]=43369861243`
3. Bakılacaklar: birim kontrol (`--birim-kontrol`, son: 292 kontrol 0 fark), 19 deneme videosu, Diğer ayarlar (18), Ses dönüşümü (4), x264 seviye tabloları, Ses yüksekliği (18 deneme; hedef -14 ± 0,1, tepeli ± 0,25, program sonucu ± 0,05; ses örnekleri bit bit denetlensin diye YouTube + `Ses.Kodlayici=Wav`; karar adları Dogrusal / Dogrusal/Sinirli / Hedefte / ZatenYuksek), Tahmini boyut (4 deneme) — hepsi 0 fark. Son yeşil: ffmpeg-dogrula 43836618516, app-build 43836568836 (commit 4be81bd).
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

### Bilgisayar testleri (16 Kasım; eski exe 43774713565 ile, `C:\Users\relio\Downloads\Relios Export`)
- Bağlantı: Windows `desktop-jdnkoph`, `C:\Users\relio`. Erişim verilen klasör: `C:\Users\relio\Downloads\Relios Export` (içinde exe, `ayarlar.json`, kaynak `2026-11-14 07-24-10.mkv`, çıktılar). `Yeni klasör (3)` artık yok. Kullanıcının başlangıç `ayarlar.json`'u oturumda `/home/claude/pc-test/yedek/ayarlar_baslangic.json` (testler bitince geri konacak).
- Bilgisayar kullanımı: File Explorer "click" (exe'yi çift tıklayarak açmak olur); program açıkken `computer_resolve_access` adı **`relios export.exe`** ile bulur (full). Sürükle-bırak hem Gezgin'de hem dosya pencerelerinde engelli → **sürükle-bırak testini kullanıcı yapmalı**. Program iki kez açık olabilir (kullanıcı 10:06'da da açmıştı): iki pencere aynı `ayarlar.json`'a yazar, son yazan kazanır.
- **Geçti:** TikTok + NVENC (8 sn; h264_nvenc High@4.2, HRD cbr_flag=1, bit hızı = tampon = 34375 kb/s, B0, ref 3, IDR/30, MOV, PCM s16 48k, eng, tarih; sürücü yeterli); kodlarken kapatma (onay "Dışa aktarma sürüyor", Durdur ve kapat → `.part` silindi); kapatıp açınca platform, kayıt yeri, profil ayarı korundu; YouTube x264 (hedef 80/82 — kalan 2 fark kaynağın kendi kare atlaması: OBS kaydında bir yerde 4 kare boşluk, program koruyor); İptal (.part silindi); `_2` adı; satırdaki klasör düğmesi (dosya seçili açılıyor); Aç; ön ayar kaydet/yükle/sil (silme onaylı); Varsayılan (onaylı); satırı çıkar.
- **Sıradaki:** 1) Yeni exe (app-build **43836568836** → artifact `relios-export-win64` id **14210559791**, 91 286 944 bayt; https://github.com/soilespubgmobile/Relios-Export/actions/runs/43836568836/artifacts/14210559791) bilgisayara: kullanıcı Chrome'dan indirip zip'i klasöre koyar (tarayıcı paneli GitHub'a girişli değil; bulut tarafından artifact indirilemez). Zip'i oturuma al (`device_stage_files`), ayrı klasörde aç, program kapalıyken exe'yi `device_commit_files` (stagedPath `/mnt/user-data/outputs/...`) ile klasöre yaz. 2) Yeni exe ile: TikTok eşitleme (OBS kaydı → standarda göre -14,00 olmalı; 12_sonu_kisik, 14_tepeli, 13_yuksek bit bit aynı), YouTube AAC 128 çıktısı (`hedef_kontrol.py --ses aac`), kayıtlı YouTube ayarının 11'e geçişi; NVENC düzeltmeleri (QP HRD artık 2000 değil, CQ/CBR alanları, 2 B-kare ortadaki + ref 1 → DPB 3, ağırlıklı tahmin + B → B 0); **NVDEC açık/kapalı çıktılar bit bit aynı mı** (05, 01 HEVC 10 bit döndürülmüş, 03 VP9, 15_tam_aralik_yuvj — 15'i bilgisayara bu oturumda koydum). 3) Sürükle-bırak (kullanıcı). 4) `ayarlar.json`'u başlangıç hâline döndür. **TikTok'a yüklemeyi kullanıcı en son kendisi yapacak.**
- Diğer bilgisayar testleri (yapılmadı): günlük düğmesi (yalnızca hatada), YouTube'da "content loudness", HDR telefon videosu.

### Bilgisayar testleri — 2. tur (16 Kasım öğle; eski exe ile, NVENC kipleri ve video ayarları)
- Yardımcılar (oturumda): `/home/claude/pc-test/araclar/h264_bilgi.py` (SPS/PPS/HRD/dilim/QP/IDR/bit hızı), `kare_psnr.py`, `rgb_psnr.py` (her dosya kendi matrisiyle), `pc_dogrula.py`.
- **Geçti:** VBR (HRD = en yüksek / tampon, cbr_flag 0; seviye 4.0 → 4.1 doğru); sabit kalite + 2 B-kare + Main@4.2 + 4 ref + qres; sabit QP + CAVLC + ağırlıklı tahmin + p1 + ll + High + otomatik seviye + NVDEC + `-aud 1`; CBR + Baseline (B 0, CAVLC kendiliğinden); High 4:4:4 (yuv444p, B-kare); intra yenileme (tek IDR, kurtarma noktası SEI); NVDEC ile HEVC 10 bit döndürülmüş / AV1 / VP9 / ProRes (ProRes işlemciye düşer); 1080×1920 ortadan kırpma (referansla 61,8 dB), 50 → 30 fps, MP4 + FDK AAC 128 (faststart, eng, UTC tarih); TikTok "Varsayılan"; kodlarken kapat → "Devam et"; büyüt / eski boyut / simge durumu; satır ipucu. Etiketsiz SD kaynak BT.601 → BT.709'a çevriliyor (doğru; ham YUV PSNR'ı düşük görünür, RGB'de 40 dB).
- **Bulunup düzeltilen (commit 4be81bd):** 1) CQ'da tampon ffmpeg'de yok sayılıyordu (alan gizlendi, komuttan çıktı, "en çok" tahmini seviyenin tamponuyla: `NvencSabitKaliteTamponu`). 2) QP'de `-b:v` yoktu → HRD'de ffmpeg'in 2 Mb/s'i (artık `-b:v 0`; görüntü bit bit aynı, kartta denendi: HRD 16000/25000). 3) CBR'de en yüksek bit hızı kartça yok sayılıyordu (alan gizlendi, `-maxrate` yok). 4) B-kare referans + ref < 3 → "DPB size should be greater than or equal to 3 for B as reference" (DPB en az 3; yalnızca B: 2 — kart B referans değilken kendisi 2'ye çıkarıyordu). 5) Ağırlıklı tahmin + B → "Weighted Prediction not supported with B-frames" (B 0, alanlar kapalı; Baseline'da ağırlıklı tahmin yok). 6) High 4:4:4'te kart yalnızca CAVLC (komutta cavlc; NVIDIA belgesi "4:4:4 encoding (only CAVLC)"). 7) NVDEC kareleri NV12/P010 → ölçekleme farklı → sonuç bit bit aynı değildi (VP9 480p'de RGB 39,87 / 40,24 dB) → filtre zincirinin başına kaynağın piksel biçimi (`format=yuv420p` / `yuv420p10le`; yuvj → yuv), yerelde FFmpeg 6.1 ile bit bit aynı olduğu kanıtlandı (döndürmeli dahil); bilgisayarda yeni exe ile doğrulanacak. 8) Notlar: NVENC'te sahne eşiği yok, açık GOP yalnızca x264; x264 VBV kuralı. Türkçe hata açıklamaları: DPB, ağırlıklı tahmin + B.
- Bilgi: durum yazısı son çalıştırmanın özetidir ("4 video hazır" — listede 5 hazır olsa da); kasıtlı, değiştirilmedi.

## 16 Kasım oturumu
- **Yapıldı (doğrulandı; app-build 43769903001, ffmpeg-dogrula 43769910163, commit 864dc37):** tepe sınırlayıcı 192 kHz'te (öneri 6). Eskiden elle yükseltmede yalnızca örnekleri sınırlıyordu: yerel ölçümde kırpılmış gürültü +4,8 dBTP, kare dalga +1,3 dBTP; 44,1 kHz kaynak 48 kHz'e çevrilirken 0,00 dBFS'te kırpılıyordu. Şimdi: -1,00 dBTP (aşırı tam bant gürültüde en kötü ~-0,6). Yeni CI denemesi: 44,1 kHz tiz ton +12 dB. Arayüz ipucu güncellendi.
- **Yapıldı (doğrulandı; ffmpeg-dogrula 43773824293 commit 30cf4a9, app-build 43774713565 commit 1333b0f):** LUFS eşitlemede dinamik kip kaldırıldı; kullanıcının isteği: "kısıksa her zaman bütün ses stabil -14'e". Neden: kullanıcının videosu (`2026-11-14 07-24-10.mkv`, OBS kaydı; ilk 5 sn sessiz, 6–8 sn -42 LUFS, sonra ~-22; ölçüm -20,8 LUFS, tepe -11,2, LRA 21) LRA > 11 yüzünden dinamik kipe giriyordu: yükseltme video boyunca değişiyordu (+2 → +8 dB), aralık 21 → 5,7 LU sıkışıyordu, sonuç -15,8 "hedefe ulaşılamadı". Şimdi aynı video +6,8 dB sabit → -14,00 (tepe -4,4). Ani tepeli seste sınırlayıcı + telafi → -14,0 (tepe -1,00). Yeni CI denemeleri: geniş aralık (LRA 17,9) ve sabit ton sabit kazançla, tepeli ses -14 ±0,5.
- Kullanıcının bilgisayarında `C:\Users\relio\Downloads\Yeni klasör (3)` klasörüne erişim verildi (içinde exe, zip, `ayarlar.json`, kaynak MKV). İndirilenler ve Videolar köküne erişim isteği sınıflandırıcı tarafından reddedildi: dosya gerekiyorsa kullanıcıdan bu klasöre koymasını iste.
- **Kullanıcı HDR kaydetmiyor** → zimg / ton eşleme gerekmez (yapılmayacak). zimg için tek eksik kütüphane oydu; derleme ~1–1,5 sa, hepsi 3–4 sa.
- **Önemli, yapılmadı:** onaylı FFmpeg derlemesi (run 43369861243) GitHub'da **13 Aralık 2026'da siliniyor** (30 gün). Ondan önce FFmpeg yeniden derlenip doğrulanmalı (saklama 90 güne çıkarılabilir; yeniden derleme bütün bileşenleri o günkü en son kararlı sürüme çeker → hepsi yeniden doğrulanır). Kullanıcıya söylendi, karar bekleniyor.
- **NVENC için sürücü:** FFmpeg nv-codec-headers **n13.1.15.0** ile derlenmiş → Windows'ta NVIDIA sürücüsü **610.0 veya üstü** gerekir (RTX 5060 Ti). Eskiyse program "sürücü çok eski" Türkçe hatasını verir. 16 Kasım'da kullanıcının bilgisayarında NVENC kodlaması çalıştı (sürücü yeterli).
- **Bilgi olarak söylendi (yapılmadı):** YouTube resmi önerisi 2 ardışık B-kare + GOP kare hızının yarısı; bizde B-kare 0 ve GOP 50/26 (XviD4PSP hedefinden). NVENC'de ağırlıklı tahmin (isteğe bağlı, B-kare 0 olduğu için açılabilir). Diğer öneriler: uyku önleme, taramalı kaynak (bwdif), disk alanı kontrolü — kullanıcı yalnızca 6'yı seçti.

## Son konuşulanlar (bilgi)
- YouTube'a PCM 16 bit yükleme, FDK 128'e göre 15–17 kHz'te biraz daha iyi sonuç verdi (kullanıcı testi). YouTube sesi zaten yeniden kodluyor. 16 Kasım'da kullanıcı yine de YouTube'u FDK AAC 128 kb/s istedi ("Varsayılan olsun").
- TikTok'a WAV sesli MOV'u kullanıcı henüz denemedi — ilk yüklemede sesi kontrol etmesi önerildi.
- TikTok indirmesi (1024×576, ~2,2 Mb/s, High@3.1, kodlayıcı bvc0ot) incelendi: muhtemelen tavanlı VBR; kesinlik için dosyanın kendisi gerekiyor.
- Bir kayıt dosyası (Record_…mp4) incelendi: NVENC benzeri, Main@4.2, 1080p60, HRD'de VBR 13 Mb/s, 3 B-kare, 120 karede IDR, tam aralık; görüntü siyahtı. Kayıt cihazının saat dilimi UTC+4 görünüyordu (Türkiye UTC+3).
- Kapsamlı testte bulunan ve düzeltilen: eşitleme + mono çıktı -14 yerine ~-20 LUFS veriyordu (artık önce mono'ya inilir); ölçüm hatasında günlük yazılmıyordu; "en çok" tahmini kısa videoda tamponu saymıyordu.
- Bilgi: x264 qpmax 33 (YouTube varsayılanı, hedef videodan) saf parazit gibi aşırı karmaşık görüntüde VBV üst sınırını aşabiliyor; gerçek videolarda görülmedi, varsayılana dokunulmadı.
- Önceden önerilen ama istenmeyen fikirler: 1440p yükleme seçeneği, H.264 olmayan YouTube kaynaklarında High profil.
