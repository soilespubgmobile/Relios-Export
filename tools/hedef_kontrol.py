#!/usr/bin/env python3
"""Bir MP4 çıktısını kullanıcının hedef meta verisiyle karşılaştırır.

Kullanım: python3 -I hedef_kontrol.py <dosya.mp4> [iş parçacığı] [--boyut GxY] [--fps N/D]
          [--gop N/M] [--kap mp4|mov] [--profil kaynak --kaynak <kaynak video>]

--boyut / --fps: YouTube gibi "kaynakla aynı" profillerde beklenen çözünürlük ve kare hızı
(varsayılan TikTok hedefi: 1216x2160, 30000/1001). --gop: beklenen keyint / min-keyint (varsayılan 50/26;
YouTube'da 30 fps'e göre: 60 fps'te 100/51). Diğer her şey XviD4PSP hedefiyle aynı beklenir.
"""
import json
import re
import struct
import subprocess
import sys

TARGET_X264 = (
    "cabac=1 / ref=3 / deblock=1:-1:-1 / analyse=0x1:0x111 / me=hex / subme=7 / psy=1 / "
    "psy_rd=1.00:0.15 / mixed_ref=1 / me_range=16 / chroma_me=1 / trellis=1 / 8x8dct=0 / "
    "cqm=0 / deadzone=21,11 / fast_pskip=1 / chroma_qp_offset=-3 / threads=THREADS / "
    "lookahead_threads=LOOKAHEAD / sliced_threads=0 / nr=0 / decimate=1 / interlaced=0 / "
    "bluray_compat=0 / constrained_intra=0 / bframes=0 / weightp=2 / keyint=50 / "
    "keyint_min=26 / scenecut=0 / intra_refresh=0 / rc_lookahead=40 / rc=crf / mbtree=1 / "
    "crf=18.0 / qcomp=0.60 / qpmin=0 / qpmax=33 / qpstep=4 / vbv_maxrate=14000 / "
    "vbv_bufsize=28000 / crf_max=0.0 / nal_hrd=none / filler=0 / ip_ratio=1.40 / aq=1:1.00"
)
# XviD4PSP 8.2.14'teki x264 (ilk hedef videoda 164 vardi; kullanici 165 r3223'u kabul etti).
TARGET_CORE = "165 r3223 0480cb0"

path = sys.argv[1]
_ek = sys.argv[2:]
# Kullanıcı 7 iş parçacığı seçti (orijinal hedef 67/11). İkinci argümanla değiştirilebilir.
THREADS = _ek.pop(0) if _ek and not _ek[0].startswith("--") else "7"
_SECENEK = dict(zip(_ek[0::2], _ek[1::2]))
BOYUT = _SECENEK.get("--boyut", "1216x2160")
GOP_N, GOP_MIN = _SECENEK.get("--gop", "50/26").split("/")
KAP = _SECENEK.get("--kap", "mp4")
TARGET_X264 = TARGET_X264.replace("keyint=50 / keyint_min=26", f"keyint={GOP_N} / keyint_min={GOP_MIN}")
FPS = _SECENEK.get("--fps", "30000/1001")
BW, BH = (int(x) for x in BOYUT.split("x"))
# Beklenen H.264 profili / seviyesi: varsayılan TikTok hedefi (Main@L5.0). "kaynak": programdaki
# ArgsBuilder.ProfilCoz / SeviyeCoz'un bağımsız karşılığı (H.264 kaynakta aynısı, değilse Main + otomatik).
BEK_PROFIL, BEK_SEVIYE = "Main", "5.0"
if _SECENEK.get("--profil") == "kaynak":
    _k = json.loads(subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,profile,level",
         "-of", "json", _SECENEK["--kaynak"]], capture_output=True, text=True, encoding="utf-8").stdout)["streams"][0]
    if _k.get("codec_name") == "h264" and _k.get("profile"):
        _p = _k["profile"]
        BEK_PROFIL = "Constrained Baseline" if "Baseline" in _p else "High" if "High" in _p else "Main"
        _l = int(_k.get("level") or 0)
        BEK_SEVIYE = f"{_l / 10:.1f}" if _l > 0 else None
    else:
        BEK_PROFIL, BEK_SEVIYE = "Main", None  # seviyeyi x264 seçer
LOOKAHEAD = {"7": "1", "8": "1", "67": "11"}.get(THREADS, "?")
TARGET_X264 = TARGET_X264.replace("THREADS", THREADS).replace("LOOKAHEAD", LOOKAHEAD)
rows = []


def chk(name, want, got, ok=None):
    if ok is None:
        ok = str(want) == str(got)
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def note(name, want, got):
    rows.append(("BİLGİ", name, str(want), str(got)))


def probe(*args):
    r = subprocess.run(["ffprobe", "-v", "error", *args, "-of", "json", path],
                       capture_output=True, text=True)
    return json.loads(r.stdout or "{}")


d = probe("-show_streams", "-show_format")
streams = d.get("streams", [])
v = next(s for s in streams if s["codec_type"] == "video")
a = next((s for s in streams if s["codec_type"] == "audio"), None)  # sessiz videoda yok
fmt = d["format"]

# ---- Kap
chk("Kap", KAP, KAP if KAP in fmt.get("format_name", "") and path.lower().endswith("." + KAP)
    else fmt.get("format_name"))

# ---- Video
chk("Video codec", "h264", v.get("codec_name"))
chk("Video profil", BEK_PROFIL, v.get("profile"))
_lv = f"{v.get('level', 0) / 10:.1f}"
chk("Video level", BEK_SEVIYE or "otomatik (x264)", _lv, ok=_lv == BEK_SEVIYE if BEK_SEVIYE else float(_lv) > 0)
chk("Çözünürlük", BOYUT, f"{v.get('width')}x{v.get('height')}")
sar = v.get("sample_aspect_ratio")
if sar in (None, "0:1", "N/A"):
    sar = "1:1"
chk("Piksel oranı (SAR)", "1:1", sar)
sn, sd = (int(x) for x in sar.split(":"))
dar = v["width"] * sn / (v["height"] * sd)
chk("Görüntü oranı (DAR)", f"{BW / BH:.3f}", f"{dar:.3f}", ok=abs(dar - BW / BH) < 0.003)
chk("Kare hızı", FPS, v.get("r_frame_rate"))
chk("Ortalama kare hızı", FPS, v.get("avg_frame_rate"))

pk = probe("-select_streams", "v:0", "-show_entries", "packet=duration,flags").get("packets", [])
durs = sorted({int(p.get("duration", 0)) for p in pk})
chk("Kare hızı modu", "Constant", "Constant" if len(durs) == 1 else f"Variable {durs[:6]}")
kidx = [i for i, p in enumerate(pk) if "K" in p.get("flags", "")]
gaps = sorted({b - a_ for a_, b in zip(kidx, kidx[1:])})
chk("GOP N (anahtar kare aralığı)", GOP_N, ",".join(map(str, gaps)) or "?")
chk("GOP M (B-frame yok)", "0", v.get("has_b_frames"))
chk("Piksel formatı", "yuv420p", v.get("pix_fmt"))
chk("Bit derinliği", "8", v.get("bits_per_raw_sample"))
chk("Tarama", "progressive", v.get("field_order", "progressive"))
chk("Renk aralığı", "tv (Limited)", v.get("color_range"), ok=v.get("color_range") == "tv")
chk("Renk primerleri", "bt709", v.get("color_primaries"))
chk("Transfer", "bt709", v.get("color_transfer"))
chk("Matris", "bt709", v.get("color_space"))
chk("Video dili", "eng", v.get("tags", {}).get("language"))

# ---- x264 SEI
raw = open(path, "rb").read()
i = raw.find(b"x264 - core")
if i < 0:
    chk("x264 ayar satırı", "var", "yok")
    opts, core = {}, "?"
else:
    sei = re.match(rb"[\x20-\x7e]+", raw[i:i + 4000]).group(0).decode()
    core = re.search(r"core (\d+[^-]*)", sei).group(1).strip()
    opts = dict(kv.split("=", 1) for kv in sei.split("options: ", 1)[1].split(" ") if "=" in kv)
    # Kaynak High ise program High'ın özelliği olan 8x8 DCT'yi açar (yoksa x264 dosyaya Main yazar).
    # (8x8 DCT açılınca x264 i8x8 bölümlerini de kullanır: analyse 0x1:0x111 -> 0x3:0x113.)
    _hedef_x264 = (TARGET_X264.replace("8x8dct=0", "8x8dct=1").replace("analyse=0x1:0x111", "analyse=0x3:0x113")
                   if BEK_PROFIL == "High" else TARGET_X264)
    for kv in _hedef_x264.split(" / "):
        k, want = kv.split("=", 1)
        chk(f"x264 {k}", want, opts.get(k, "(yok)"))
chk("Yazan kütüphane (XviD4PSP ile aynı)", f"x264 core {TARGET_CORE}", f"x264 core {core}")

# ---- Ses
if a is None:
    note("Ses", "-", "yok (sessiz video)")
else:
    chk("Ses codec", "aac", a.get("codec_name"))
    chk("Ses profili", "LC", a.get("profile"))
    chk("Örnekleme hızı", "48000", a.get("sample_rate"))
    chk("Kanal", "2 (stereo)", f"{a.get('channels')} ({a.get('channel_layout')})")
    br = int(a.get("bit_rate") or 0)
    chk("Ses bitrate", "~128 kb/s", f"{br / 1000:.1f} kb/s", ok=126000 <= br <= 130500)
    chk("Ses dili", "eng", a.get("tags", {}).get("language"))
    chk("Ses Default", "Yes", "Yes" if a.get("disposition", {}).get("default") else "No")


# ---- MP4 kutuları (MediaInfo'nun okuduğu alanlar)
def boxes(b, s, e):
    p = s
    while p + 8 <= e:
        size, t = struct.unpack(">I4s", b[p:p + 8])
        h = 8
        if size == 1:
            size = struct.unpack(">Q", b[p + 8:p + 16])[0]
            h = 16
        elif size == 0:
            size = e - p
        if size < h or p + size > e:
            break
        yield t.decode("latin1"), p + h, p + size
        p += size


def child(b, s, e, name):
    for t, cs, ce in boxes(b, s, e):
        if t == name:
            return cs, ce
    return None


def desc_len(b, p):
    n = 0
    for _ in range(4):
        c = b[p]
        p += 1
        n = (n << 7) | (c & 0x7F)
        if not c & 0x80:
            break
    return n, p


moov = child(raw, 0, len(raw), "moov")
if moov:
    for t, ts, te in boxes(raw, *moov):
        if t != "trak":
            continue
        tk = child(raw, ts, te, "tkhd")
        ver = raw[tk[0]]
        p = tk[0] + 4 + ((8 + 8 + 4 + 4 + 8) if ver == 1 else 20) + 8
        alt = struct.unpack(">H", raw[p + 2:p + 4])[0]
        mdia = child(raw, ts, te, "mdia")
        hd = child(raw, *mdia, "hdlr")
        htype = raw[hd[0] + 8:hd[0] + 12].decode("latin1")
        minf = child(raw, *mdia, "minf")
        stbl = child(raw, *minf, "stbl")
        stsd = child(raw, *stbl, "stsd")
        etype, es, ee = next(boxes(raw, stsd[0] + 8, stsd[1]))
        if htype == "vide":
            note("Video Alternate group", "0 (gösterilmez)", alt)
            kids = {n: (cs, ce) for n, cs, ce in boxes(raw, es + 78, ee)}
            if "pasp" in kids:
                h_, v_ = struct.unpack(">II", raw[kids["pasp"][0]:kids["pasp"][0] + 8])
                chk("Video pasp kutusu", "1:1 ya da yok", f"{h_}:{v_}", ok=h_ == v_)
            if "btrt" in kids:
                _, mx, av = struct.unpack(">III", raw[kids["btrt"][0]:kids["btrt"][0] + 12])
                note("Video btrt (max/ort)", "-", f"{mx}/{av}")
        elif htype == "soun":
            chk("Ses Alternate group", "1", alt)
            sver = struct.unpack(">H", raw[es + 8:es + 10])[0]
            cstart = es + 28 + {0: 0, 1: 16, 2: 36}.get(sver, 0)
            kids = {n: (cs, ce) for n, cs, ce in boxes(raw, cstart, ee)}
            if "esds" in kids:
                q = kids["esds"][0] + 4
                q += 1
                _, q = desc_len(raw, q)
                q += 2
                fl = raw[q]
                q += 1
                if fl & 0x80:
                    q += 2
                if fl & 0x40:
                    q += 1 + raw[q]
                if fl & 0x20:
                    q += 2
                q += 1
                _, q = desc_len(raw, q)
                mx = int.from_bytes(raw[q + 5:q + 9], "big")
                av = int.from_bytes(raw[q + 9:q + 13], "big")
                chk("Ses bitrate modu", "Constant", f"max {mx} / ort {av}",
                    ok=av > 0 and mx <= av * 1.005)

# ---- Rapor
w = max(len(r[1]) for r in rows)
for st, name, want, got in rows:
    print(f"{st:5} {name:<{w}}  hedef: {want:<20} çıktı: {got}")
fark = [r for r in rows if r[0] == "FARK"]
print(f"\nToplam {len(rows)} kontrol, {len(fark)} fark.")
sys.exit(1 if fark else 0)
