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
- Sürüm: **0.8.9** (`app/ReliosExport/ReliosExport.csproj`). Ayar sürümü `ProfilAyarlari.GuncelSurum = 9`.

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
- **Ses (ikisinde de):** WAV (PCM) 16 bit, 48 kHz, stereo; dönüşüm soxr 33 bit (En yüksek), titreşim Üçgen (triangular). 24/32 bit/float kaynak 16 bite titreşimle iner; 16 bit 48 kHz kaynak bit bit aynı kalır.
- Encoded date / Tagged date = dışa aktarma anı (`-metadata creation_time=now`, UTC).
- Varsayılan çıktı klasörü: İndirilenler.
- x264 threads 8.
- **Ses yüksekliği (ikisinde de):** tepe sınırlayıcı açık, -1 dB (alimiter; yalnızca ses yükseltilince — artı dB ya da LUFS eşitleme — devreye girer, yoksa sese dokunmaz). LUFS eşitleme kapalı, hedef -14, yön "yalnızca kısık sesi yükselt" (loudnorm iki geçiş; ±0,5 LU hedefteyse ya da yüksekse dokunulmaz; tepe + kazanç ≤ -1 ve 0 < LRA ≤ 11 ise doğrusal = sabit kazanç, değilse dinamik; bitince çıktı ölçülüp satıra yazılır). TikTok -14 kullanıcının kararı (TikTok resmi değer yayınlamıyor).
- Tekrar işle: bütün videolar bitince Başlat düğmesi "Tekrar işle" olur, hepsini o anki ayarlarla yeniden işler. Bekleyen satırda tahmini dosya boyutu, biten satırda gerçek boyut.
- Ayar geçişleri `app/ReliosExport/Core/AppSettings.cs` içinde (`Yukselt`); ses geçişi ön ayarlara uygulanmaz.

## Doğrulama (her değişiklikte)
1. Push → `app-build.yml` çalışır (ekran görüntüleri log'da base64, `EKRAN|ad|...` satırları).
2. `gh api -X POST repos/soilespubgmobile/Relios-Export/actions/workflows/ffmpeg-dogrula.yml/dispatches -f ref=main -f inputs[run_id]=43369861243`
3. Bakılacaklar: birim kontrol (`--birim-kontrol`, son: 195 kontrol 0 fark), 19 deneme videosu, Diğer ayarlar, Ses dönüşümü, x264 seviye tabloları, Ses yüksekliği (8 deneme) — hepsi 0 fark.
- Yardımcılar: `tools/hedef_kontrol.py` (`--ses pcm16`, `--vbv`, `--kap`), `tools/ayar_kontrol.py`, `tools/saglamlik_kontrol.py`, `tools/ses_donusum_kontrol.py`.
- Commit mesajlarında Co-Authored-By satırı kullanılıyor.

## Son konuşulanlar (bilgi)
- YouTube'a PCM 16 bit yükleme, FDK 128'e göre 15–17 kHz'te biraz daha iyi sonuç verdi (kullanıcı testi). YouTube sesi zaten yeniden kodluyor.
- TikTok'a WAV sesli MOV'u kullanıcı henüz denemedi — ilk yüklemede sesi kontrol etmesi önerildi.
- TikTok indirmesi (1024×576, ~2,2 Mb/s, High@3.1, kodlayıcı bvc0ot) incelendi: muhtemelen tavanlı VBR; kesinlik için dosyanın kendisi gerekiyor.
- Bir kayıt dosyası (Record_…mp4) incelendi: NVENC benzeri, Main@4.2, 1080p60, HRD'de VBR 13 Mb/s, 3 B-kare, 120 karede IDR, tam aralık; görüntü siyahtı. Kayıt cihazının saat dilimi UTC+4 görünüyordu (Türkiye UTC+3).
- Önceden önerilen ama istenmeyen fikirler: 1440p yükleme seçeneği, H.264 olmayan YouTube kaynaklarında High profil.
