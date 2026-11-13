#!/bin/bash

SCRIPT_SKIP="1"

ffbuild_depends() {
    # Relios: yalnizca H.264 (x264 + NVENC), FDK AAC, PCM/WAV ve gereken cozuculer
    echo zlib
    echo dav1d
    echo fdk-aac
    echo ffnvcodec
    echo soxr
    echo x264
}

ffbuild_enabled() {
    return 0
}

ffbuild_dockerfinal() {
    return 0
}

ffbuild_dockerdl() {
    return 0
}

ffbuild_dockerlayer() {
    return 0
}

ffbuild_dockerstage() {
    return 0
}

ffbuild_dockerbuild() {
    return 0
}

ffbuild_ldexeflags() {
    return 0
}
