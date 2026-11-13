#!/bin/bash
# Relios FFmpeg: her kutuphaneyi en son KARARLI surum etiketine sabitler.
# Gelistirme dallari / rc / beta etiketleri kullanilmaz.
set -euo pipefail
cd "$(dirname "$0")/.."
SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
OUT="${GITHUB_OUTPUT:-/dev/null}"

latest_tag() { # repo regex
    git ls-remote --tags --refs "$1" | awk '{print $2}' | sed 's#^refs/tags/##' \
        | grep -E "$2" | sort -V | tail -n1
}
tag_commit() { # repo tag
    local sha
    sha="$(git ls-remote "$1" "refs/tags/$2^{}" | cut -f1)"
    [[ -n "$sha" ]] || sha="$(git ls-remote "$1" "refs/tags/$2" | cut -f1)"
    echo "$sha"
}
set_commit() { # script sha
    sed -i "0,/^SCRIPT_COMMIT=.*/s//SCRIPT_COMMIT=\"$2\"/" "scripts.d/$1.sh"
}
pin() { # script regex
    local repo tag sha
    repo="$(grep -m1 '^SCRIPT_REPO=' "scripts.d/$1.sh" | cut -d'"' -f2)"
    tag="$(latest_tag "$repo" "$2")"
    [[ -n "$tag" ]] || { echo "HATA: kararli etiket bulunamadi: $1 ($repo)"; exit 1; }
    sha="$(tag_commit "$repo" "$tag")"
    [[ -n "$sha" ]] || { echo "HATA: commit bulunamadi: $1 $tag"; exit 1; }
    set_commit "$1" "$sha"
    echo "| $1 | $tag | ${sha:0:12} |" >> "$SUMMARY"
}

ONLY="${1:-all}"
echo "| Bilesen | Kararli surum | Commit |" >> "$SUMMARY"
echo "|---|---|---|" >> "$SUMMARY"

if [[ "$ONLY" == all || "$ONLY" == github ]]; then
    pin 50-fdk-aac '^v[0-9]+\.[0-9]+\.[0-9]+$'
    pin 20-zlib    '^v[0-9]+\.[0-9]+(\.[0-9]+)*$'
    # NVENC basliklari: gelistirme dali yerine en son surum etiketi (nX.Y.Z.W)
    pin 50-ffnvcodec '^n[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'

    # FFmpeg: en son kararli surum etiketi (nX.Y.Z)
    FFTAG="$(latest_tag https://github.com/FFmpeg/FFmpeg.git '^n[0-9]+\.[0-9]+(\.[0-9]+)?$')"
    FFVER="$(sed -E 's/^n([0-9]+\.[0-9]+).*/\1/' <<<"$FFTAG")"
    [[ -f "addins/${FFVER}.sh" ]] || { echo "HATA: addin yok: $FFVER"; exit 1; }
    echo "GIT_BRANCH=\"$FFTAG\"" > "addins/${FFVER}.sh"
    echo "ffaddin=$FFVER" >> "$OUT"
    echo "fftag=$FFTAG" >> "$OUT"
    echo "| FFmpeg | $FFTAG | (etiket) |" >> "$SUMMARY"
fi

if [[ "$ONLY" == all ]]; then
    pin 50-dav1d   '^[0-9]+\.[0-9]+\.[0-9]+$'
    # x264: kullanicinin istegiyle XviD4PSP 8.2.14'teki surumun birebir aynisi
    # (H.264 x264 core 165 r3223 0480cb0). Bu commit sabit tutulur, kendiliginden degismez.
    X264SHA="0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee"
    set_commit 50-x264 "$X264SHA"
    echo "| 50-x264 | XviD4PSP ile ayni: core 165 r3223 | ${X264SHA:0:12} |" >> "$SUMMARY"

    # soxr 2018'den beri yeni surum cikarmadi; kullanilan commit ile son etiket bilgi olarak yazilir.
    SOXRREPO="$(grep -m1 '^SCRIPT_REPO=' scripts.d/50-soxr.sh | cut -d'"' -f2)"
    SOXRUSED="$(grep -m1 '^SCRIPT_COMMIT=' scripts.d/50-soxr.sh | cut -d'"' -f2)"
    SOXRTAG="$(latest_tag "$SOXRREPO" '^(v|soxr-)?[0-9]+\.[0-9]+\.[0-9]+$' || true)"
    SOXRTAGSHA=""
    [[ -n "$SOXRTAG" ]] && SOXRTAGSHA="$(tag_commit "$SOXRREPO" "$SOXRTAG" || true)"
    echo "| 50-soxr (bilgi) | son etiket ${SOXRTAG:-?} = ${SOXRTAGSHA:0:12} | kullanilan ${SOXRUSED:0:12} |" >> "$SUMMARY"
fi
