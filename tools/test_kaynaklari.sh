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
add() { # dosya matris aralik ses aciklama [windows_adi]
    local extra=""
    [[ -n "${6:-}" ]] && extra=", \"windows_adi\": \"$6\""
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
