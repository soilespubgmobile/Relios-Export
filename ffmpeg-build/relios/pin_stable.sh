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
    pin 50-x265    '^[0-9]+\.[0-9]+(\.[0-9]+)?$'
    pin 50-svtav1  '^v[0-9]+\.[0-9]+\.[0-9]+$'
    pin 50-libopus '^v[0-9]+\.[0-9]+(\.[0-9]+)?$'
    pin 50-zimg    '^release-[0-9]+\.[0-9]+\.[0-9]+$'
    pin 50-amf     '^v[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$'
    pin 50-onevpl  '^v2\.[0-9]+\.[0-9]+$'
    pin 20-zlib    '^v[0-9]+\.[0-9]+(\.[0-9]+)*$'

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
    pin 50-libvpx  '^v[0-9]+\.[0-9]+\.[0-9]+$'
    # x264'un resmi surum yayini yok; resmi "stable" dali kullanilir.
    X264REPO="$(grep -m1 '^SCRIPT_REPO=' scripts.d/50-x264.sh | cut -d'"' -f2)"
    X264SHA="$(git ls-remote "$X264REPO" refs/heads/stable | cut -f1)"
    [[ -n "$X264SHA" ]] || { echo "HATA: x264 stable dali bulunamadi"; exit 1; }
    set_commit 50-x264 "$X264SHA"
    echo "| 50-x264 | stable dali | ${X264SHA:0:12} |" >> "$SUMMARY"
fi
