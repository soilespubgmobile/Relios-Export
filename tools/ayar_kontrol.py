#!/usr/bin/env python3
"""Relios Export ciktisinin secilen ayarlara uyup uymadigini kontrol eder.

Kullanim: python -I ayar_kontrol.py <cikti> anahtar=deger [anahtar=deger ...]

Anahtarlar (sayilarda "±tolerans" eklenebilir; yuzde icin "±5%"):
  kap=mp4|mov            kap bicimi
  boyut=GxY              goruntu boyutu
  fps=N/D                kare hizi (r_frame_rate)
  vcodec=h264            video codec
  profil=High|Main       H.264 profili
  vbit=4000000±20%       video bit hizi (bit/s)
  x264:anahtar=deger     x264'un dosyaya yazdigi ayar satirindaki deger (or. x264:rc=2pass)
  ses=yok                ses akisi olmamali
  acodec=aac|pcm_s24le   ses codec
  aprofil=LC|HE-AAC      AAC profili
  ar=48000               ornekleme hizi
  ac=2                   kanal sayisi
  abit=384000±6%         ses bit hizi (bit/s)
  ses_seviye=-35±2.5     sesin ortalama seviyesi (dB, volumedetect)
"""
import json
import re
import subprocess
import sys

cikti = sys.argv[1]
istekler = sys.argv[2:]
rows = []


def chk(name, want, got, ok):
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def run(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          stdin=subprocess.DEVNULL)


def fnum(x, default=None):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def tolerans(deger):
    """'384000±6%' -> (384000, 23040); '-35±2.5' -> (-35, 2.5); '48000' -> (48000, 0)."""
    if "±" in deger:
        a, t = deger.split("±", 1)
        a = float(a)
        t = abs(a) * float(t[:-1]) / 100 if t.endswith("%") else float(t)
        return a, t
    return float(deger), 0.0


d = json.loads(run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", cikti]).stdout or "{}")
streams = d.get("streams", [])
v = next((s for s in streams if s["codec_type"] == "video"), {})
a = next((s for s in streams if s["codec_type"] == "audio"), None)
fmt = d.get("format", {})
x264 = None

for istek in istekler:
    anahtar, deger = istek.split("=", 1)
    if anahtar == "kap":
        ad = fmt.get("format_name", "")
        uzanti = cikti.lower().rsplit(".", 1)[-1]
        chk("Kap", deger, f"{uzanti} ({ad})", deger in ad and uzanti == deger)
    elif anahtar == "boyut":
        got = f"{v.get('width')}x{v.get('height')}"
        chk("Çözünürlük", deger, got, got == deger)
    elif anahtar == "fps":
        chk("Kare hızı", deger, v.get("r_frame_rate"), v.get("r_frame_rate") == deger)
    elif anahtar == "vcodec":
        chk("Video codec", deger, v.get("codec_name"), v.get("codec_name") == deger)
    elif anahtar == "profil":
        chk("Video profili", deger, v.get("profile"), v.get("profile") == deger)
    elif anahtar == "vbit":
        hedef, t = tolerans(deger)
        got = fnum(v.get("bit_rate"))
        if got is None:
            # Bazı kaplarda akış bit hızı yazılmaz: dosyanın bit hızından ses payı düşülür.
            got = fnum(fmt.get("bit_rate"), 0) - (fnum(a.get("bit_rate"), 0) if a else 0)
        chk("Video bit hızı", f"{hedef / 1000:.0f} kb/s ± {t / 1000:.0f}", f"{got / 1000:.0f} kb/s", abs(got - hedef) <= t)
    elif anahtar.startswith("x264:"):
        if x264 is None:
            raw = open(cikti, "rb").read()
            i = raw.find(b"x264 - core")
            x264 = {}
            if i >= 0:
                sei = re.match(rb"[\x20-\x7e]+", raw[i:i + 4000]).group(0).decode()
                x264 = dict(kv.split("=", 1) for kv in sei.split("options: ", 1)[1].split(" ") if "=" in kv)
        k = anahtar[5:]
        chk(f"x264 {k}", deger, x264.get(k, "(yok)"), x264.get(k) == deger)
    elif anahtar == "ses":
        chk("Ses akışı", deger, "var" if a else "yok", (a is not None) == (deger == "var"))
    elif anahtar == "acodec":
        got = a.get("codec_name") if a else "(ses yok)"
        chk("Ses codec", deger, got, got == deger)
    elif anahtar == "aprofil":
        got = a.get("profile") if a else "(ses yok)"
        chk("Ses profili", deger, got, got == deger)
    elif anahtar == "ar":
        got = a.get("sample_rate") if a else "(ses yok)"
        chk("Örnekleme hızı", deger, got, got == deger)
    elif anahtar == "ac":
        got = str(a.get("channels")) if a else "(ses yok)"
        chk("Kanal sayısı", deger, got, got == deger)
    elif anahtar == "abit":
        hedef, t = tolerans(deger)
        got = fnum(a.get("bit_rate"), 0) if a else 0
        chk("Ses bit hızı", f"{hedef / 1000:.0f} kb/s ± {t / 1000:.0f}", f"{got / 1000:.1f} kb/s", abs(got - hedef) <= t)
    elif anahtar == "ses_seviye":
        hedef, t = tolerans(deger)
        r = run(["ffmpeg", "-v", "info", "-nostdin", "-i", cikti, "-map", "0:a:0", "-af", "volumedetect", "-f", "null", "-"])
        m = re.search(r"mean_volume:\s*(-?[\d.]+|-inf) dB", r.stderr)
        got = fnum(m.group(1)) if m else None
        chk("Ses seviyesi (ortalama)", f"{hedef:.1f} dB ± {t:.1f}", "ölçülemedi" if got is None else f"{got:.1f} dB",
            got is not None and abs(got - hedef) <= t)
    else:
        chk("Bilinmeyen kontrol", istek, "-", False)

w = max((len(r[1]) for r in rows), default=10)
for st, name, want, got in rows:
    print(f"{st:5} {name:<{w}}  beklenen: {want:<22} çıktı: {got}")
fark = [r for r in rows if r[0] == "FARK"]
print(f"\nAyar: {len(rows)} kontrol, {len(fark)} fark.")
sys.exit(1 if fark else 0)
