#!/bin/bash
source "$(dirname "$BASH_SOURCE")"/win64-gpl.sh
# Relios: tamamen cevrimdisi; FFmpeg icinde ag kodu (http, rtmp, tcp, udp...) yok
FF_CONFIGURE="--enable-nonfree --disable-network $FF_CONFIGURE"
LICENSE_FILE=""
