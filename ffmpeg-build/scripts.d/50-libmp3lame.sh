#!/bin/bash

# Relios: en son kararli LAME surumu (3.100) resmi paketinden
SCRIPT_URL="https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz"

ffbuild_depends() {
    echo base
    echo libiconv
}

ffbuild_enabled() {
    return 0
}

ffbuild_dockerdl() {
    echo "retry-tool sh -c \"rm -rf lame lame-3.100 && curl -fsSL '${SCRIPT_URL}' | tar xz && mv lame-3.100 lame\" && cd lame"
    echo "autoreconf -i"
}

ffbuild_dockerbuild() {
    local myconf=(
        --prefix="$FFBUILD_PREFIX"
        --disable-shared
        --enable-static
        --disable-cpml
        --disable-frontend
        --disable-decoder
    )

    if [[ $TARGET == win* || $TARGET == linux* ]]; then
        myconf+=(
            --host="$FFBUILD_TOOLCHAIN"
        )
    else
        echo "Unknown target"
        return -1
    fi

    export CFLAGS="$CFLAGS -DNDEBUG"

    ./configure "${myconf[@]}"
    make -j$(nproc)
    make install DESTDIR="$FFBUILD_DESTDIR"
}

ffbuild_configure() {
    echo --enable-libmp3lame
}

ffbuild_unconfigure() {
    echo --disable-libmp3lame
}
