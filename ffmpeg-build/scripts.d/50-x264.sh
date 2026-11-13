#!/bin/bash

SCRIPT_REPO="https://code.videolan.org/videolan/x264.git"
SCRIPT_COMMIT="0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee"

ffbuild_enabled() {
    [[ $VARIANT == lgpl* ]] && return -1
    return 0
}

ffbuild_dockerbuild() {
    # Relios: kaynak sig klonlandigi icin (depth=1) x264'un version.sh'i surum satirini
    # bos birakiyor ve videoya yalnizca "x264 core 165" yaziliyor. Resmi tam klonla
    # derlenen ayni commit (XviD4PSP 8.2.14'teki x264) "core 165 r3223 0480cb0" yazar.
    # Kod ayni; yalnizca bu satirin birebir ayni olmasi icin degerler sabitleniyor.
    if [[ "$SCRIPT_COMMIT" == "0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee" ]]; then
        printf '%s\n' '#!/bin/sh' \
            'echo "#define X264_REV 3223"' \
            'echo "#define X264_REV_DIFF 0"' \
            'echo "#define X264_VERSION \" r3223 0480cb0\""' \
            'echo "#define X264_POINTVER \"0.165.3223 0480cb0\""' > version.sh
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
