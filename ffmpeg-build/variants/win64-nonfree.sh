#!/bin/bash
source "$(dirname "$BASH_SOURCE")"/win64-gpl.sh
# Relios: tamamen cevrimdisi; FFmpeg icinde ag kodu (http, rtmp, tcp, udp...) yok
# Ekran karti kodlamasinda yalnizca H.264 (h264_nvenc) kalir; HEVC ve AV1 NVENC cikarilir.
# Ekran kartiyla cozme (NVDEC / CUVID) tum bicimlerde kalir.
FF_CONFIGURE="--enable-nonfree --disable-network --disable-encoder=hevc_nvenc,av1_nvenc $FF_CONFIGURE"
LICENSE_FILE=""
