"""Kombinasyon denemesinin kaynakları (Linux, dağıtımın ffmpeg'i ile; Windows'ta programın FFmpeg'iyle denenir).

Her kaynağın ne olduğu manifest.json'a yazılır (ad, dosya, grup, açıklama, harici ses dosyası). Kaynaklar kısa (1–3 sn):
deneme yüzlerce kez kodlar. Ses kaynaklarında her biçim (PCM 8/16/24/32 bit, 32/64 bit float, AAC, MP3, MP2, Opus, Vorbis,
FLAC, ALAC, AC-3, E-AC-3), her örnekleme hızı (7,35–192 kHz) ve kanal düzeni (mono … 7.1, 5.1.4, 22.2), birden fazla ses
izi, geç başlayan / kısa / uzun / sessiz / çok kısa ses ve her uzantıda aynı adlı harici ses (.wav .w64 .flac .aif .aiff
.m4a .aac .mp3 .opus .ogg .ac3 .eac3 .mka).

Kullanım: python3 -I tools/kombinasyon_kaynaklari.py <çıktı klasörü>
"""
import json
import os
import subprocess
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
FF = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y']
MANIFEST = []


def ff(*args):
    komut = FF + [str(a) for a in args]
    r = subprocess.run(komut, capture_output=True, text=True)
    if r.returncode != 0:
        raise SystemExit('ffmpeg hata verdi: ' + ' '.join(komut) + '\n' + r.stderr)


def ekle(ad, dosya, grup, aciklama, harici=None, **ek):
    MANIFEST.append(dict(ad=ad, dosya=dosya, grup=grup, aciklama=aciklama, harici=harici, **ek))


def yol(d):
    return os.path.join(OUT, d)


# Sesin içeriği: her kanalda ayrı ton + gürültü (kanallar birbirinden farklı), yükseklik seviyesi seçilebilir.
SEVIYE = {
    'kisik': 0.02,   # ~ -34 LUFS
    'orta': 0.08,    # ~ -22 LUFS
    'yuksek': 0.55,  # ~ -6 LUFS, tepe 0 dBFS'e yakın
}


def ses_ifadesi(kanal, seviye='orta', tepeli=False, sessiz=False):
    if sessiz:
        return '|'.join(['0'] * kanal)
    a = SEVIYE[seviye]
    parcalar = []
    for c in range(kanal):
        e = f'{a}*(0.6*sin(2*PI*{220 + 97 * c}*t)+0.4*(2*random({c})-1))'
        if tepeli:
            e += r'+if(lt(mod(t\,1)\,0.01)\,0.85*sin(2*PI*1000*t)\,0)'
        parcalar.append(e)
    return '|'.join(parcalar)


def ses_girisi(hz, duzen, kanal, sure, seviye='orta', tepeli=False, sessiz=False):
    e = ses_ifadesi(kanal, seviye, tepeli, sessiz)
    return ['-f', 'lavfi', '-i', f'aevalsrc={e}:c={duzen}:s={hz}:d={sure}']


def video_girisi(boyut='160x90', hiz=30, sure=3):
    return ['-f', 'lavfi', '-i', f'testsrc2=size={boyut}:rate={hiz}:duration={sure}']


H264 = ['-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p']

# ------------------------------------------------------------------------------------------------ video kaynakları
ff(*video_girisi('320x180', 30, 1), *ses_girisi(48000, 'stereo', 2, 1), *H264, '-profile:v', 'high',
   '-c:a', 'aac', '-b:a', '128k', '-shortest', yol('v01_h264_320x180_30.mp4'))
ekle('v01', 'v01_h264_320x180_30.mp4', 'video', 'H.264 High 320x180 30 fps + AAC 48 kHz stereo')

ff(*video_girisi('320x180', 30, 1), *ses_girisi(44100, 'mono', 1, 1), '-vf', 'format=yuv420p10le',
   '-c:v', 'libx265', '-preset', 'ultrafast', '-x265-params', 'log-level=error', '-tag:v', 'hvc1',
   '-c:a', 'aac', '-b:a', '96k', '-shortest', yol('tmp_donuk.mp4'))
ff('-display_rotation:v:0', '90', '-i', yol('tmp_donuk.mp4'), '-map', '0', '-c', 'copy', yol('v02_hevc10_donuk.mp4'))
os.remove(yol('tmp_donuk.mp4'))
ekle('v02', 'v02_hevc10_donuk.mp4', 'video', 'HEVC 10 bit 320x180, 90 derece döndürme bilgili (görüntü 180x320) + AAC mono 44,1 kHz')

ff(*video_girisi('352x288', 25, 1), *ses_girisi(44100, 'stereo', 2, 1),
   '-vf', 'format=yuv420p,setparams=colorspace=unknown:color_primaries=unknown:color_trc=unknown:range=unknown',
   '-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '300k', '-c:a', 'libvorbis', '-q:a', '4',
   '-shortest', yol('v03_vp9_sd_etiketsiz.webm'))
ekle('v03', 'v03_vp9_sd_etiketsiz.webm', 'video', 'VP9 352x288 25 fps renk etiketsiz (SD: BT.601) + Vorbis')

ff(*video_girisi('640x360', 60, 1), *ses_girisi(48000, 'stereo', 2, 1), '-vf', 'format=yuvj420p',
   '-c:v', 'mjpeg', '-q:v', '3', '-c:a', 'pcm_s24le', '-shortest', yol('v04_mjpeg_tam_aralik_60.mov'))
ekle('v04', 'v04_mjpeg_tam_aralik_60.mov', 'video', 'MJPEG tam aralık (yuvj420p) 640x360 60 fps + PCM 24 bit 48 kHz')

ff(*video_girisi('480x270', 60, 1), *ses_girisi(48000, 'stereo', 2, 1),
   '-vf', "select='not(eq(mod(n\\,5)\\,2))',setpts='PTS+0.002*sin(N)/TB'", '-fps_mode', 'passthrough',
   '-video_track_timescale', '90000', *H264, '-c:a', 'aac', '-b:a', '128k', '-shortest', yol('v05_vfr.mp4'))
ekle('v05', 'v05_vfr.mp4', 'video', 'Değişken kare hızı (VFR) H.264 480x270 + AAC')

ff(*video_girisi('321x241', 25, 1), '-vf', 'format=yuv444p', '-c:v', 'ffv1', yol('v06_ffv1_tek_boyut.mkv'))
ekle('v06', 'v06_ffv1_tek_boyut.mkv', 'video', 'FFV1 4:4:4 321x241 (tek sayılı boyut) 25 fps, sessiz')

ff(*video_girisi('720x576', 50, 1), *ses_girisi(48000, 'stereo', 2, 1),
   '-vf', 'tinterlace=mode=interleave_top,setfield=tff', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
   '-flags', '+ildct+ilme', '-x264-params', 'tff=1', '-c:a', 'aac', '-b:a', '128k', '-shortest', yol('v07_gecmeli_576i.mp4'))
ekle('v07', 'v07_gecmeli_576i.mp4', 'video', 'Geçmeli (interlaced, üst alan önce) H.264 720x576 25i + AAC')

ff(*video_girisi('1280x720', 50, 0.5), *ses_girisi(48000, 'stereo', 2, 0.5), '-vf', 'format=yuv422p10le',
   '-c:v', 'prores_ks', '-profile:v', '3', '-c:a', 'pcm_s16le', '-shortest', yol('v08_prores_hq_720p50.mov'))
ekle('v08', 'v08_prores_hq_720p50.mov', 'video', 'ProRes 422 HQ 10 bit 1280x720 50 fps + PCM 16 bit')

ff(*video_girisi('720x576', 25, 1), *ses_girisi(48000, 'stereo', 2, 1), '-vf', 'setsar=16/15', *H264,
   '-c:a', 'aac', '-b:a', '128k', '-shortest', yol('v09_anamorfik_sar.mp4'))
ekle('v09', 'v09_anamorfik_sar.mp4', 'video', 'H.264 720x576 SAR 16:15 (kare olmayan piksel) + AAC')

ff(*video_girisi('320x240', 30, 1), *ses_girisi(48000, 'mono', 1, 1), '-c:v', 'libx264', '-preset', 'ultrafast',
   '-profile:v', 'baseline', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '64k', '-shortest', yol('v10_baseline.mp4'))
ekle('v10', 'v10_baseline.mp4', 'video', 'H.264 Constrained Baseline 320x240 + AAC mono')

ff(*video_girisi('320x180', 30, 1), *ses_girisi(48000, 'stereo', 2, 1), '-vf', 'format=yuv422p10le',
   '-c:v', 'libx264', '-preset', 'ultrafast', '-profile:v', 'high422', '-c:a', 'aac', '-shortest', yol('v11_h264_422_10bit.mp4'))
ekle('v11', 'v11_h264_422_10bit.mp4', 'video', 'H.264 High 4:2:2 10 bit 320x180 + AAC')

ff(*video_girisi('320x180', 30, 0.04), *H264, '-frames:v', '1', yol('v12_tek_kare.mp4'))
ekle('v12', 'v12_tek_kare.mp4', 'video', 'Tek kare H.264 320x180, sessiz')

# ------------------------------------------------------------------------------------------------ ses kaynakları
# (görüntü 160x90 30 fps 3 sn; sesin biçimi değişir)
SES = [
    # ad, dosya, hız, düzen, kanal, codec + seçenekler, seviye, tepeli, açıklama
    ('s01', 's01_pcm16_48k_stereo.mov', 48000, 'stereo', 2, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 48 kHz stereo'),
    ('s02', 's02_pcm24_44k_stereo_tepeli.mov', 44100, 'stereo', 2, ['-c:a', 'pcm_s24le'], 'kisik', True, 'PCM 24 bit 44,1 kHz stereo, kısık + ani tepeler'),
    ('s03', 's03_pcm32_96k_stereo.mov', 96000, 'stereo', 2, ['-c:a', 'pcm_s32le'], 'kisik', False, 'PCM 32 bit 96 kHz stereo, kısık'),
    ('s04', 's04_f32_48k_mono_yuksek.mov', 48000, 'mono', 1, ['-c:a', 'pcm_f32le'], 'yuksek', False, 'PCM 32 bit float 48 kHz mono, yüksek'),
    ('s05', 's05_f64_48k_51side.mov', 48000, '5.1(side)', 6, ['-c:a', 'pcm_f64le'], 'orta', False, 'PCM 64 bit float 48 kHz 5.1(side)'),
    ('s06', 's06_u8_8k_mono.mov', 8000, 'mono', 1, ['-c:a', 'pcm_u8'], 'orta', False, 'PCM 8 bit (u8) 8 kHz mono'),
    ('s07', 's07_pcm16_22k_mono.mov', 22050, 'mono', 1, ['-c:a', 'pcm_s16le'], 'kisik', False, 'PCM 16 bit 22,05 kHz mono'),
    ('s08', 's08_pcm24_192k_stereo_tepeli.mov', 192000, 'stereo', 2, ['-c:a', 'pcm_s24le'], 'orta', True, 'PCM 24 bit 192 kHz stereo, ani tepeler'),
    ('s09', 's09_pcm16_11k_stereo.mov', 11025, 'stereo', 2, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 11,025 kHz stereo'),
    ('s10', 's10_pcm16_32k_stereo_yuksek.mov', 32000, 'stereo', 2, ['-c:a', 'pcm_s16le'], 'yuksek', False, 'PCM 16 bit 32 kHz stereo, yüksek'),
    ('s11', 's11_pcm16_16k_mono.mov', 16000, 'mono', 1, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 16 kHz mono'),
    ('s12', 's12_pcm16_7350_mono.mov', 7350, 'mono', 1, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 7,35 kHz mono (FDK hızı değil)'),
    ('s13', 's13_pcm24_88k_stereo.mov', 88200, 'stereo', 2, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 88,2 kHz stereo'),
    ('s14', 's14_pcm24_176k_stereo.mov', 176400, 'stereo', 2, ['-c:a', 'pcm_s24le'], 'kisik', False, 'PCM 24 bit 176,4 kHz stereo (FDK hızı değil)'),
    ('s15', 's15_aac_44k_stereo.mp4', 44100, 'stereo', 2, ['-c:a', 'aac', '-b:a', '160k'], 'orta', False, 'AAC-LC 44,1 kHz stereo (MP4)'),
    ('s16', 's16_aac_48k_51.mp4', 48000, '5.1', 6, ['-c:a', 'aac', '-b:a', '384k'], 'orta', False, 'AAC-LC 48 kHz 5.1 (MP4)'),
    ('s17', 's17_mp3_44k_stereo.mkv', 44100, 'stereo', 2, ['-c:a', 'libmp3lame', '-b:a', '192k'], 'orta', True, 'MP3 44,1 kHz stereo (MKV), ani tepeler'),
    ('s18', 's18_mp3_32k_mono.mkv', 32000, 'mono', 1, ['-c:a', 'libmp3lame', '-b:a', '64k'], 'kisik', False, 'MP3 32 kHz mono (MKV)'),
    ('s19', 's19_opus_48k_stereo.webm', 48000, 'stereo', 2, ['-c:a', 'libopus', '-b:a', '128k'], 'orta', False, 'Opus 48 kHz stereo (WebM, VP9)'),
    ('s20', 's20_vorbis_44k_stereo.mkv', 44100, 'stereo', 2, ['-c:a', 'libvorbis', '-q:a', '5'], 'yuksek', False, 'Vorbis 44,1 kHz stereo (MKV), yüksek'),
    ('s21', 's21_flac24_96k_stereo.mkv', 96000, 'stereo', 2, ['-c:a', 'flac', '-sample_fmt', 's32', '-bits_per_raw_sample', '24'], 'orta', False, 'FLAC 24 bit 96 kHz stereo (MKV)'),
    ('s22', 's22_alac_44k_stereo.mov', 44100, 'stereo', 2, ['-c:a', 'alac'], 'orta', False, 'ALAC 44,1 kHz stereo (MOV)'),
    ('s23', 's23_ac3_48k_51side.mkv', 48000, '5.1(side)', 6, ['-c:a', 'ac3', '-b:a', '384k'], 'orta', False, 'AC-3 48 kHz 5.1(side) (MKV)'),
    ('s24', 's24_eac3_48k_51side.mkv', 48000, '5.1(side)', 6, ['-c:a', 'eac3', '-b:a', '384k'], 'orta', False, 'E-AC-3 48 kHz 5.1(side) (MKV)'),
    ('s25', 's25_pcm24_48k_quad.mov', 48000, 'quad', 4, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz quad (FDK düzeni değil)'),
    ('s26', 's26_pcm24_48k_71.mov', 48000, '7.1', 8, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 7.1'),
    ('s27', 's27_pcm24_48k_61.mov', 48000, '6.1', 7, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 6.1'),
    ('s28', 's28_pcm16_48k_21.mov', 48000, '2.1', 3, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 48 kHz 2.1'),
    ('s29', 's29_pcm16_44k_50.mov', 44100, '5.0', 5, ['-c:a', 'pcm_s16le'], 'orta', False, 'PCM 16 bit 44,1 kHz 5.0'),
    ('s30', 's30_pcm24_48k_514.mov', 48000, '5.1.4', 10, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 5.1.4 (10 kanal)'),
    ('s31', 's31_pcm16_48k_222.mkv', 48000, '22.2', 24, ['-c:a', 'pcm_s16le'], 'kisik', False, 'PCM 16 bit 48 kHz 22.2 (24 kanal, MKV)'),
    ('s32', 's32_pcm24_48k_60.mov', 48000, '6.0', 6, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 6.0 (FDK-LD açmaz)'),
    ('s33', 's33_pcm24_48k_3.0.mov', 48000, '3.0', 3, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 3.0'),
    ('s34', 's34_pcm24_48k_61back.mov', 48000, '6.1(back)', 7, ['-c:a', 'pcm_s24le'], 'orta', False, 'PCM 24 bit 48 kHz 6.1(back)'),
]
for ad, dosya, hz, duzen, kanal, codec, seviye, tepeli, aciklama in SES:
    ff(*video_girisi(), *ses_girisi(hz, duzen, kanal, 3, seviye, tepeli), *H264, *codec,
       *(['-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '200k'] if dosya.endswith('.webm') else []),
       '-shortest', yol(dosya))
    ekle(ad, dosya, 'ses', aciklama)

# Özel durumlar
ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 3, 'yuksek'), *ses_girisi(48000, 'stereo', 2, 3, 'kisik'),
   '-map', '0:v', '-map', '1:a', '-map', '2:a', *H264, '-c:a:0', 'aac', '-c:a:1', 'pcm_s24le', '-shortest',
   yol('s40_iki_ses_izi.mov'))
ekle('s40', 's40_iki_ses_izi.mov', 'ses', 'İki ses izi: 1. AAC yüksek, 2. PCM 24 bit kısık (program kayıpsız izi seçer)')

ff(*video_girisi(), '-itsoffset', '0.5', *ses_girisi(48000, 'stereo', 2, 2.5), '-map', '0:v', '-map', '1:a', *H264,
   '-c:a', 'aac', yol('s41_ses_gec_basliyor.mp4'))
ekle('s41', 's41_ses_gec_basliyor.mp4', 'ses', 'Ses 0,5 sn geç başlıyor (AAC)')

ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 1), '-map', '0:v', '-map', '1:a', *H264, '-c:a', 'pcm_s16le',
   yol('s42_ses_kisa.mov'))
ekle('s42', 's42_ses_kisa.mov', 'ses', 'Ses 1 sn, görüntü 3 sn')

ff(*video_girisi(sure=3), *ses_girisi(48000, 'stereo', 2, 4.5), '-map', '0:v', '-map', '1:a', *H264, '-c:a', 'pcm_s24le',
   yol('s43_ses_uzun.mov'))
ekle('s43', 's43_ses_uzun.mov', 'ses', 'Ses 4,5 sn, görüntü 3 sn')

ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 3, sessiz=True), *H264, '-c:a', 'pcm_s16le', '-shortest',
   yol('s44_sessiz_ses.mov'))
ekle('s44', 's44_sessiz_ses.mov', 'ses', 'Ses izi var, tamamen sessiz')

ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 0.2), '-map', '0:v', '-map', '1:a', *H264, '-c:a', 'pcm_s24le',
   yol('s45_ses_cok_kisa.mov'))
ekle('s45', 's45_ses_cok_kisa.mov', 'ses', 'Ses 0,2 sn (ölçülemez)')

ff(*video_girisi(), *H264, yol('s46_ses_yok.mp4'))
ekle('s46', 's46_ses_yok.mp4', 'ses', 'Ses izi yok')

ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 3), *H264, '-c:a', 'mp2', '-b:a', '192k', '-shortest', '-f', 'mpegts',
   yol('s47_mpegts_mp2.ts'))
ekle('s47', 's47_mpegts_mp2.ts', 'ses', 'MPEG-TS + MP2 48 kHz stereo')

ff(*video_girisi(), *ses_girisi(44100, 'stereo', 2, 3), '-c:v', 'mpeg4', '-q:v', '5', '-c:a', 'pcm_s16le', '-shortest',
   yol('s48_avi_pcm.avi'))
ekle('s48', 's48_avi_pcm.avi', 'ses', 'AVI (MPEG-4 görüntü) + PCM 16 bit 44,1 kHz')

ff(*video_girisi(), *ses_girisi(48000, 'stereo', 2, 3, 'yuksek'),
   '-filter_complex', '[1:a]volume=9dB,alimiter=limit=0.999:level=0[a]', '-map', '0:v', '-map', '[a]', *H264,
   '-c:a', 'pcm_s24le', '-shortest', yol('s49_hazir_yuksek.mov'))
ekle('s49', 's49_hazir_yuksek.mov', 'ses', 'Hazır yüksek ses (tepe ~0 dBFS)')

# ------------------------------------------------------------------------------------------------ aynı adlı harici ses
HARICI = [
    # ad, uzantı, hız, düzen, kanal, codec seçenekleri, süre, videonun kendi sesi var mı, açıklama
    ('h01', '.wav', 96000, 'mono', 1, ['-c:a', 'pcm_s24le'], 3, True, 'WAV 24 bit 96 kHz mono (videonun kendi AAC sesi de var)'),
    ('h02', '.w64', 48000, 'stereo', 2, ['-c:a', 'pcm_f32le'], 3, False, 'W64 32 bit float 48 kHz stereo'),
    ('h03', '.flac', 44100, '5.1(side)', 6, ['-c:a', 'flac'], 3, False, 'FLAC 44,1 kHz 5.1(side)'),
    ('h04', '.aif', 44100, 'stereo', 2, ['-c:a', 'pcm_s16be'], 3, False, 'AIFF (.aif) 16 bit 44,1 kHz'),
    ('h05', '.aiff', 48000, 'stereo', 2, ['-c:a', 'pcm_s24be'], 3, False, 'AIFF (.aiff) 24 bit 48 kHz'),
    ('h06', '.m4a', 48000, 'stereo', 2, ['-c:a', 'aac', '-b:a', '192k'], 3, False, 'M4A AAC 48 kHz'),
    ('h07', '.aac', 44100, 'stereo', 2, ['-c:a', 'aac', '-b:a', '128k', '-f', 'adts'], 3, False, 'ADTS AAC (.aac) 44,1 kHz (süresi başlıkta yok)'),
    ('h08', '.mp3', 48000, 'stereo', 2, ['-c:a', 'libmp3lame', '-b:a', '192k'], 3, False, 'MP3 48 kHz'),
    ('h09', '.opus', 48000, 'stereo', 2, ['-c:a', 'libopus', '-b:a', '96k'], 3, False, 'Opus 48 kHz'),
    ('h10', '.ogg', 44100, 'mono', 1, ['-c:a', 'libvorbis', '-q:a', '4'], 3, False, 'Ogg Vorbis 44,1 kHz mono'),
    ('h11', '.ac3', 48000, '5.1(side)', 6, ['-c:a', 'ac3', '-b:a', '384k'], 3, False, 'AC-3 48 kHz 5.1(side)'),
    ('h12', '.eac3', 48000, 'stereo', 2, ['-c:a', 'eac3', '-b:a', '192k'], 3, False, 'E-AC-3 48 kHz stereo'),
    ('h13', '.mka', 48000, 'stereo', 2, ['-c:a', 'pcm_s32le'], 3, False, 'Matroska ses (.mka) PCM 32 bit 48 kHz'),
    ('h14', '.wav', 48000, 'stereo', 2, ['-c:a', 'pcm_s16le'], 1, True, 'WAV 1 sn (görüntü 3 sn: sonu sessizlikle dolar)'),
    ('h15', '.wav', 22050, 'stereo', 2, ['-c:a', 'pcm_s16le'], 5, False, 'WAV 5 sn 22,05 kHz (görüntü 3 sn: kesilir)'),
    ('h16', '.wav', 8000, 'mono', 1, ['-c:a', 'pcm_s16le'], 3, False, 'WAV 8 kHz mono'),
    ('h17', '.wav', 48000, '7.1', 8, ['-c:a', 'pcm_s24le'], 3, False, 'WAV 24 bit 48 kHz 7.1'),
]
for ad, uzanti, hz, duzen, kanal, codec, sure, kendi, aciklama in HARICI:
    video = f'{ad}_harici{uzanti.replace(".", "_")}.mp4'
    if kendi:
        ff(*video_girisi(), *ses_girisi(44100, 'stereo', 2, 3, 'yuksek'), *H264, '-c:a', 'aac', '-shortest', yol(video))
    else:
        ff(*video_girisi(), *H264, yol(video))
    ses = video[:-4] + uzanti
    ff(*ses_girisi(hz, duzen, kanal, sure, 'kisik' if ad in ('h01', 'h16') else 'orta'), *codec, yol(ses))
    ekle(ad, video, 'harici', aciklama, harici=ses)

with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8') as f:
    json.dump(MANIFEST, f, ensure_ascii=False, indent=1)
print(f'{len(MANIFEST)} kaynak hazır: {OUT}')
