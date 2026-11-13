#!/bin/bash
# Relios Export Windows testi icin deneme kaynaklari uretir (Linux, dagitimin ffmpeg'i ile).
# Her kaynak bilinen renk matrisi / araligi ile uretilir; sonuc dogrulamasi buna gore yapilir.
# Kullanim: test_kaynaklari.sh <cikti_klasoru>
set -euo pipefail
OUT="${1:?cikti klasoru}"
mkdir -p "$OUT"
cd "$OUT"
FF=(ffmpeg -hide_banner -loglevel error -y)
MANIFEST=()

tags() { # matris primer aktarim aralik
    echo "setparams=colorspace=$1:color_primaries=$2:color_trc=$3:range=$4"
}
video() { # boyut hiz sure matris aralik pikselformati etiketler
    echo "-f lavfi -i testsrc2=size=$1:rate=$2:duration=$3"
}
vf() { # matris aralik pikselformati etiket_filtresi
    echo "format=gbrp,scale=out_color_matrix=$1:out_range=$2,format=$3,$4"
}
add() { # dosya matris aralik ses aciklama [windows_adi] [ek_json]
    local extra=""
    [[ -n "${6:-}" ]] && extra=", \"windows_adi\": \"$6\""
    [[ -n "${7:-}" ]] && extra="$extra, $7"
    MANIFEST+=("{\"dosya\": \"$1\", \"matris\": \"$2\", \"aralik\": \"$3\", \"ses\": $4, \"aciklama\": \"$5\"$extra}")
}
T709="$(tags bt709 bt709 bt709 tv)"
TNONE="$(tags unknown unknown unknown unknown)"

# 1) Yatay 1080p 60 fps H.264, etiketsiz (gercekte BT.709), 44.1 kHz 16 bit PCM
f="01_yatay_1080p60_h264_wav16_44k.mkv"
"${FF[@]}" $(video 1920x1080 60 4) -f lavfi -i sine=frequency=440:sample_rate=44100:duration=4 \
    -vf "$(vf bt709 tv yuv420p "$TNONE")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a pcm_s16le "$f"
add "$f" bt709 tv true "1920x1080 60 fps H.264 etiketsiz + 44.1 kHz 16 bit WAV"

# 2) Dikey 1080x1920 29.97 fps H.264 + 48 kHz 24 bit PCM (MOV) - kirpilmamali
f="02_dikey_1080x1920_h264_wav24_48k.mov"
"${FF[@]}" $(video 1080x1920 30000/1001 4) -f lavfi -i sine=frequency=330:sample_rate=48000:duration=4 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a pcm_s24le "$f"
add "$f" bt709 tv true "1080x1920 29.97 fps H.264 + 48 kHz 24 bit WAV (kirpma yok)"

# 3) Telefon: HEVC 10 bit 1920x1080 kayitli, 90 derece dondurme bilgili (goruntu 1080x1920), AAC mono
"${FF[@]}" $(video 1920x1080 30 3) -f lavfi -i sine=frequency=550:sample_rate=44100:duration=3 \
    -vf "$(vf bt709 tv yuv420p10le "$T709")" -c:v libx265 -preset ultrafast -crf 14 \
    -x265-params log-level=error -tag:v hvc1 -c:a aac -b:a 128k -ac 1 tmp_donuk.mp4
f="03_telefon_donuk_hevc10.mp4"
"${FF[@]}" -display_rotation:v:0 90 -i tmp_donuk.mp4 -map 0 -c copy "$f"
rm -f tmp_donuk.mp4
add "$f" bt709 tv true "Telefon: HEVC 10 bit, 90 derece dondurme bilgili + AAC mono 44.1 kHz"

# 4) AV1 720p + Opus (WebM)
f="04_av1_720p_opus.webm"
"${FF[@]}" $(video 1280x720 30 3) -f lavfi -i sine=frequency=660:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libsvtav1 -preset 12 -crf 20 \
    -ac 2 -c:a libopus -b:a 128k "$f"
add "$f" bt709 tv true "AV1 1280x720 + Opus"

# 5) VP9 SD 640x480 25 fps, etiketsiz (gercekte BT.601) + Vorbis (WebM)
f="05_vp9_sd_vorbis.webm"
"${FF[@]}" $(video 640x480 25 3) -f lavfi -i sine=frequency=770:sample_rate=44100:duration=3 \
    -vf "$(vf bt601 tv yuv420p "$TNONE")" -c:v libvpx-vp9 -deadline realtime -cpu-used 8 \
    -crf 15 -b:v 0 -ac 2 -c:a libvorbis -q:a 6 "$f"
add "$f" bt601 tv true "VP9 640x480 25 fps etiketsiz (SD) + Vorbis"

# 6) 4K 50 fps ProRes 422 10 bit + 48 kHz 32 bit float PCM (MOV)
f="06_4k50_prores_wav32f.mov"
"${FF[@]}" $(video 3840x2160 50 2) -f lavfi -i sine=frequency=880:sample_rate=48000:duration=2 \
    -vf "$(vf bt709 tv yuv422p10le "$T709")" -c:v prores_ks -profile:v 1 \
    -ac 2 -c:a pcm_f32le "$f"
add "$f" bt709 tv true "3840x2160 50 fps ProRes 422 10 bit + 48 kHz 32 bit float WAV"

# 7) Sessiz kare 1080x1080 H.264
f="07_sessiz_kare_1080.mp4"
"${FF[@]}" $(video 1080x1080 30 3) \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 "$f"
add "$f" bt709 tv false "1080x1080 kare, sessiz"

# 8) Turkce karakterli, bosluklu dosya adi: aktarim icin ASCII adla uretilir,
#    Windows'ta bu ada kopyalanip oyle denenir (windows_adi).
f="08_turkce_isim.mp4"
"${FF[@]}" $(video 1920x1080 25 3) -f lavfi -i sine=frequency=500:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt709 tv true "Turkce karakterli bosluklu dosya adi, 1920x1080 25 fps" "08_Türkçe ğüşıöç İsim Ç.mp4"

# 9) Degisken kare hizi (telefon kaydi gibi): 60 fps'den kare dusurulmus + zaman titremesi
f="09_degisken_kare_hizi.mp4"
"${FF[@]}" $(video 1920x1080 60 4) -f lavfi -i sine=frequency=600:sample_rate=48000:duration=4 \
    -vf "$(vf bt709 tv yuv420p "$T709"),select='not(eq(mod(n\,5)\,2))',setpts='PTS+0.002*sin(N)/TB'" \
    -fps_mode passthrough -video_track_timescale 90000 \
    -c:v libx264 -preset veryfast -crf 14 -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt709 tv true "Degisken kare hizi (VFR), 1920x1080"

# 10) Tam aralik (full range, pc) H.264
f="10_tam_aralik_pc.mp4"
"${FF[@]}" $(video 1920x1080 30 3) -f lavfi -i sine=frequency=450:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 pc yuv420p "$(tags bt709 bt709 bt709 pc)")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt709 pc true "Tam aralik (full range) 1920x1080"

# 11) HDR HLG HEVC 10 bit (ton esleme yok; cokmemeli)
f="11_hdr_hlg_hevc10.mp4"
"${FF[@]}" $(video 1920x1080 30 3) -f lavfi -i sine=frequency=350:sample_rate=48000:duration=3 \
    -vf "$(vf bt2020 tv yuv420p10le "$(tags bt2020nc bt2020 arib-std-b67 tv)")" \
    -c:v libx265 -preset ultrafast -crf 14 -x265-params log-level=error -tag:v hvc1 \
    -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt2020 tv true "HDR (HLG, BT.2020) HEVC 10 bit"

# 12) Genis 21:9 2560x1080 24 fps (MKV)
f="12_genis_2560x1080_24.mkv"
"${FF[@]}" $(video 2560x1080 24 3) -f lavfi -i sine=frequency=520:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt709 tv true "2560x1080 (21:9) 24 fps"

# 13) Uzun dikey 1080x2400 (ustten/alttan kirpma)
f="13_uzun_dikey_1080x2400.mp4"
"${FF[@]}" $(video 1080x2400 30 3) -f lavfi -i sine=frequency=480:sample_rate=44100:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
add "$f" bt709 tv true "1080x2400 uzun dikey (ustten/alttan kirpma)"

# 14) Yaninda ayni adli WAV (videodan KISA): videonun kendi yuksek sesi (ortalama ~-12 dB) kullanilmamali,
#     WAV'in alcak sesi (~-36.5 dB; mono -> stereo -3 dB) kullanilmali; WAV bitince kalan sure sessiz
#     (3 saniyenin ortalamasi ~-38.3 dB).
f="14_harici_ses_kisa.mp4"
"${FF[@]}" $(video 1920x1080 30 3) -f lavfi -i sine=frequency=440:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -af volume=4 -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
"${FF[@]}" -f lavfi -i sine=frequency=1000:sample_rate=48000:duration=2 -af volume=0.24 -ac 2 -c:a pcm_s24le "${f%.mp4}.wav"
add "$f" bt709 tv true "Yaninda ayni adli 24 bit WAV (videodan kisa: sonu sessiz)" "" \
    '"ek": "ses_seviye=-38.3±2.5", "eski_komut": false'

# 15) Yaninda ayni adli WAV (videodan UZUN): video bitince ses de kesilmeli.
f="15_harici_ses_uzun.mp4"
"${FF[@]}" $(video 1920x1080 30 3) -f lavfi -i sine=frequency=440:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -af volume=4 -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
"${FF[@]}" -f lavfi -i sine=frequency=1000:sample_rate=44100:duration=5 -af volume=0.24 -ac 2 -c:a pcm_s16le "${f%.mp4}.wav"
add "$f" bt709 tv true "Yaninda ayni adli 16 bit 44.1 kHz WAV (videodan uzun: kesilir)" "" \
    '"ek": "ses_seviye=-36.5±2.5", "eski_komut": false'

# 16-18) Ses donusumu: 1 kHz, tepe 0.5 (-6 dBFS) sinus; ortalama seviye -9.0 dB. Cikti (FDK AAC 128 kb/s,
#     48 kHz, stereo) ayni seviyede olmali: bit derinligi, ornekleme hizi ya da mono -> stereo seviyeyi degistirmez.
# 16) Videonun icinde mono 24 bit 44.1 kHz PCM (stereoya iki kanala tam seviyede acilmali; eskiden -3 dB idi).
f="16_ses_mono_pcm24_44k.mov"
"${FF[@]}" $(video 1280x720 30 3) -f lavfi -i "aevalsrc=0.5*sin(2*PI*1000*t):s=44100:d=3" \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 -c:a pcm_s24le "$f"
add "$f" bt709 tv true "Videonun icinde mono 24 bit 44.1 kHz PCM (stereo cikti tam seviye)" "" \
    '"ek": "ses_seviye=-9.0±0.6", "eski_komut": false'

# 17) Videonun icinde stereo 32 bit float 96 kHz PCM.
f="17_ses_stereo_f32_96k.mov"
"${FF[@]}" $(video 1280x720 30 3) -f lavfi -i "aevalsrc=0.5*sin(2*PI*1000*t)|0.5*sin(2*PI*1000*t):s=96000:d=3" \
    -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 -c:a pcm_f32le "$f"
add "$f" bt709 tv true "Videonun icinde stereo 32 bit float 96 kHz PCM" "" \
    '"ek": "ses_seviye=-9.0±0.6", "eski_komut": false'

# 18) Yaninda ayni adli mono 24 bit 48 kHz WAV; videonun kendi (yuksek) sesi kullanilmamali.
f="18_harici_ses_mono.mp4"
"${FF[@]}" $(video 1280x720 30 3) -f lavfi -i sine=frequency=440:sample_rate=48000:duration=3 \
    -vf "$(vf bt709 tv yuv420p "$T709")" -af volume=4 -c:v libx264 -preset veryfast -crf 14 \
    -ac 2 -c:a aac -b:a 192k "$f"
"${FF[@]}" -f lavfi -i "aevalsrc=0.5*sin(2*PI*1000*t):s=48000:d=3" -c:a pcm_s24le "${f%.mp4}.wav"
add "$f" bt709 tv true "Yaninda ayni adli mono 24 bit WAV (stereo cikti tam seviye)" "" \
    '"ek": "ses_seviye=-9.0±0.6", "eski_komut": false'

# 19) Ayni videoda iki ses izi: once AAC (yuksek sesli, ~-12 dB), sonra 24 bit PCM (WAV, -9.0 dB).
#     FDK AAC'ye WAV izinden cevrilmeli (AAC'yi yeniden sikistirmak kalite kaybettirir).
f="19_iki_ses_izi_aac_pcm.mov"
"${FF[@]}" $(video 1280x720 30 3) -f lavfi -i sine=frequency=440:sample_rate=48000:duration=3 \
    -f lavfi -i "aevalsrc=0.5*sin(2*PI*1000*t)|0.5*sin(2*PI*1000*t):s=48000:d=3" \
    -map 0:v -map 1:a -map 2:a -vf "$(vf bt709 tv yuv420p "$T709")" -c:v libx264 -preset veryfast -crf 14 \
    -filter:a:0 volume=4 -c:a:0 aac -b:a:0 192k -ac:a:0 2 -c:a:1 pcm_s24le "$f"
add "$f" bt709 tv true "Ayni videoda AAC + WAV (PCM) iki ses izi: WAV izi kullanilir" "" \
    '"ek": "ses_seviye=-9.0±0.6", "eski_komut": false'

{
    echo "["
    for i in "${!MANIFEST[@]}"; do
        sep=","
        [[ $i -eq $((${#MANIFEST[@]} - 1)) ]] && sep=""
        echo "  ${MANIFEST[$i]}$sep"
    done
    echo "]"
} > manifest.json
ls -la
