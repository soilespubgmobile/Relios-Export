#!/bin/bash

SCRIPT_REPO="https://code.videolan.org/videolan/x264.git"
SCRIPT_COMMIT="0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee"

ffbuild_enabled() {
    [[ $VARIANT == lgpl* ]] && return -1
    return 0
}

ffbuild_dockerdl() {
    # Relios: kaynak resmi depodan (code.videolan.org) tam gecmisiyle indirilir; boylece
    # surum etiketini ("r3223 0480cb0") x264'un kendi version.sh'i hesaplar. Sig klonda
    # bu etiket bos kalir ve videoya yalnizca "x264 core 165" yazilir.
    echo "git init ."
    echo "git remote add origin \"$SCRIPT_REPO\""
    echo "retry-tool git fetch origin +refs/heads/master:refs/remotes/origin/master"
    echo "git -c advice.detachedHead=false checkout --detach \"$SCRIPT_COMMIT\""
    echo "./version.sh > relios-x264-version.h"
    echo "cat relios-x264-version.h"
}

ffbuild_dockerbuild() {
    # Indirme asamasinda x264'un kendi version.sh'inin urettigi etiket aynen kullanilir
    # (derleme asamasinda git gecmisine erisilemeyebilir). Etiket commit ile uyusmuyorsa dur.
    if [[ -s relios-x264-version.h ]]; then
        grep -q "X264_VERSION \" r[0-9]* ${SCRIPT_COMMIT:0:7}\"" relios-x264-version.h || {
            echo "HATA: x264 surum etiketi commit ${SCRIPT_COMMIT:0:7} ile uyusmuyor:"
            cat relios-x264-version.h
            return 1
        }
        printf '%s\n' '#!/bin/sh' 'cat "$(dirname "$0")/relios-x264-version.h"' > version.sh
        chmod +x version.sh
    fi

    local myconf=(
        --disable-cli
        --enable-static
        --enable-pic
        --disable-lavf
        --disable-swscale
        --prefix="$FFBUILD_PREFIX"
    )

    if [[ $TARGET == win* || $TARGET == linux* ]]; then
        myconf+=(
            --host="$FFBUILD_TOOLCHAIN"
            --cross-prefix="$FFBUILD_CROSS_PREFIX"
        )
    else
        echo "Unknown target"
        return -1
    fi

    ./configure "${myconf[@]}"
    make -j$(nproc)
    make install DESTDIR="$FFBUILD_DESTDIR"
}

ffbuild_configure() {
    echo --enable-libx264
}

ffbuild_unconfigure() {
    echo --disable-libx264
}

ffbuild_cflags() {
    return 0
}

ffbuild_ldflags() {
    return 0
}
