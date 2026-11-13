#!/usr/bin/env python3
"""Relios Export TikTok ciktisinin saglamlik kontrolu (kaynakla karsilastirarak).

Kullanim: python -I saglamlik_kontrol.py <kaynak> <cikti.mp4> <matris> <aralik> <ses 1/0>
          [--boyut GxY|kaynak] [--fps N/D|kaynak] [--oran kirp|kenar|esnet]

matris / aralik: kaynagin GERCEK renk matrisi ve araligi (deneme kaynagi uretilirken bilinir).
--boyut / --fps: ayarlardaki hedef (varsayilan TikTok: 1216x2160, 30000/1001). "kaynak": kaynakla ayni,
kirpma yok.
Kontroller: hatasiz cozulme, dondurme bilgisi, ses var/yok, sure, kare sayisi,
ses-goruntu suresi ve baslangici, renk + kirpma (bagimsiz hesaplanan referans kareyle).
"""
import json
import math
import subprocess
import sys

kaynak, cikti, matris, aralik, ses = sys.argv[1:6]
_ek = sys.argv[6:]
SECENEK = dict(zip(_ek[0::2], _ek[1::2]))
ses_bekleniyor = ses == "1"
BOYUT = SECENEK.get("--boyut", "1216x2160")
FPS_METNI = SECENEK.get("--fps", "30000/1001")
ORAN = SECENEK.get("--oran", "kirp")
KIRP_YATAY = int(SECENEK.get("--kirp-yatay", "50"))
KIRP_DIKEY = int(SECENEK.get("--kirp-dikey", "50"))
rows = []


def chk(name, want, got, ok):
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def run(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          stdin=subprocess.DEVNULL)


def probe(path):
    r = run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", path])
    return json.loads(r.stdout or "{}")


def fnum(x, default=0.0):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def rotation(stream):
    rot = 0.0
    for sd in stream.get("side_data_list", []) or []:
        if "rotation" in sd:
            rot = fnum(sd["rotation"])
    if rot == 0 and "rotate" in stream.get("tags", {}):
        rot = fnum(stream["tags"]["rotate"])
    deg = int(round(rot)) % 360
    return deg


def even(v):
    # C# Math.Round(v, MidpointRounding.AwayFromZero) + tek ise bir ekle (programla ayni)
    r = int(math.floor(v + 0.5)) if v >= 0 else -int(math.floor(-v + 0.5))
    return r if r % 2 == 0 else r + 1


def gorunen(vs):
    """Dondurme uygulandiktan sonraki genislik, yukseklik ve piksel orani."""
    rot = rotation(vs)
    w, h = vs["width"], vs["height"]
    sar_txt = vs.get("sample_aspect_ratio") or "1:1"
    try:
        sn, sd = (int(x) for x in sar_txt.split(":"))
        sar = sn / sd if sn > 0 and sd > 0 else 1.0
    except ValueError:
        sar = 1.0
    if rot in (90, 270):
        w, h = h, w
        sar = 1.0 / sar
    return w, h, sar


def hedef_oran(w, h):
    # Programdaki ArgsBuilder.HedefOran: yaygin bir orana %0,5'ten yakinsa o oran (1216x2160 -> 9:16).
    r = w / h
    for c in (9 / 16, 16 / 9, 1, 4 / 5, 5 / 4, 3 / 4, 4 / 3, 2 / 3, 3 / 2, 9 / 21, 21 / 9):
        if abs(r - c) / c < 0.005:
            return c
    return r


def crop_plan(vs, target):
    # Programdaki ArgsBuilder.PlanCrop'un bagimsiz karsiligi: gorunen goruntu ortadan hedef orana.
    w, h, sar = gorunen(vs)
    aspect = w * sar / h
    if abs(aspect - target) < 0.002:
        return None
    if aspect > target:
        cw = min(even(h * target / sar), w - (w % 2))
        return (cw, h, konum(w - cw, KIRP_YATAY), 0)
    ch = min(even(w * sar / target), h - (h % 2))
    return (w, ch, 0, konum(h - ch, KIRP_DIKEY))


def konum(fazla, yuzde):
    # Programdaki ArgsBuilder.Konum: 50 -> fazla // 2, digerleri yuzdeye gore (yuvarlama sifirdan uzaga)
    if yuzde == 50:
        return fazla // 2
    return max(0, min(fazla, int(math.floor(fazla * yuzde / 100 + 0.5))))


src = probe(kaynak)
out = probe(cikti)
sv = next(s for s in src["streams"] if s["codec_type"] == "video"
          and not s.get("disposition", {}).get("attached_pic"))
ov = next((s for s in out.get("streams", []) if s["codec_type"] == "video"), None)
oa = next((s for s in out.get("streams", []) if s["codec_type"] == "audio"), None)
if ov is None:
    print("FARK  Cikti dosyasinda video yok")
    sys.exit(1)

# 1) Hatasiz cozulme (bastan sona)
r = run(["ffmpeg", "-v", "error", "-xerror", "-i", cikti, "-f", "null", "-"])
chk("Çıktı baştan sona hatasız çözülüyor", "hata yok", "hata yok" if r.returncode == 0 and not r.stderr.strip()
    else (r.stderr.strip().splitlines() or ["kod %d" % r.returncode])[-1][:120], r.returncode == 0 and not r.stderr.strip())

# 2) Dondurme bilgisi kalmamali (goruntu zaten dondurulmus olmali)
chk("Çıktıda döndürme bilgisi yok", "0", rotation(ov), rotation(ov) == 0)

# 3) Ses var / yok
chk("Ses akışı", "var" if ses_bekleniyor else "yok", "var" if oa else "yok", bool(oa) == ses_bekleniyor)

# 4) Sure (kaynak sure ~ cikti video suresi)
src_dur = fnum(src.get("format", {}).get("duration"))
ov_dur = fnum(ov.get("duration"))
chk("Süre (kaynak / çıktı video)", f"{src_dur:.3f} s", f"{ov_dur:.3f} s", abs(src_dur - ov_dur) <= 0.15)

# 5) Boyut ve kare sayisi
gw, gh, gsar = gorunen(sv)
if BOYUT == "kaynak":
    # Programdaki scale=trunc(iw*sar/2)*2:trunc(ih/2)*2
    W, H = int(gw * gsar / 2) * 2, int(gh / 2) * 2
else:
    W, H = (int(x) for x in BOYUT.split("x"))
chk("Çözünürlük", f"{W}x{H}", f"{ov.get('width')}x{ov.get('height')}", (ov.get("width"), ov.get("height")) == (W, H))


def paket_sayisi(path):
    r = run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_packets",
             "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", path])
    return int(fnum(r.stdout.strip().split(",")[0], 0))


nb = paket_sayisi(cikti)
if FPS_METNI == "kaynak":
    beklenen = paket_sayisi(kaynak)
else:
    _n, _, _d = FPS_METNI.partition("/")
    beklenen = src_dur * float(_n) / float(_d or 1)
chk("Kare sayısı", f"~{beklenen:.1f}", nb, abs(nb - beklenen) <= 3)

# 6) Ses-goruntu suresi ve baslangici
if oa:
    oa_dur = fnum(oa.get("duration"))
    chk("Ses süresi ≈ görüntü süresi", f"{ov_dur:.3f} s", f"{oa_dur:.3f} s", abs(oa_dur - ov_dur) <= 0.1)
    vs0, as0 = fnum(ov.get("start_time")), fnum(oa.get("start_time"))
    chk("Ses-görüntü başlangıcı (senkron)", "fark ≤ 0.05 s", f"{as0 - vs0:+.3f} s", abs(as0 - vs0) <= 0.05)

# 7) Renk + kirpma: kaynagin GERCEK matrisiyle bagimsiz uretilen referans kareyle karsilastir.
# Iki kare 304x540'a kucultulur; piksel basina en buyuk kanal farki (0-255) olculur.
# Keskin renk kenarlarinda (renk alt ornekleme) fark dogal olarak buyuk oldugu icin
# ortanca ve %90 degeri kullanilir: yanlis matris / aralik / kirpma bunlari belirgin buyutur.
SW = 304
SH = 540 if (W, H) == (1216, 2160) else max(2, int(round(SW * H / W / 2)) * 2)
ORTANCA_ESIK, P90_ESIK = 6, 15


def rgb_kare(path, vf):
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", path, "-vf", vf, "-frames:v", "1",
                        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, stdin=subprocess.DEVNULL)
    return r.stdout if r.returncode == 0 and len(r.stdout) == SW * SH * 3 else None


def fark_olcusu(a, b):
    d = sorted(max(abs(a[i] - b[i]), abs(a[i + 1] - b[i + 1]), abs(a[i + 2] - b[i + 2]))
               for i in range(0, len(a), 3))
    return d[len(d) // 2], d[int(len(d) * 0.9)]


crop = crop_plan(sv, hedef_oran(W, H)) if BOYUT != "kaynak" and ORAN == "kirp" else None
chain = [] if FPS_METNI == "kaynak" else [f"fps={FPS_METNI}"]
if crop:
    chain.append("crop=%d:%d:%d:%d" % crop)
if ORAN == "kenar" and BOYUT != "kaynak":
    # Programdaki gibi: orani koruyarak sigdir, kalan yeri siyah kenarla doldur.
    chain += [f"scale={W}:{H}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos:"
              f"in_color_matrix={matris}:in_range={aralik}", f"pad={W}:{H}:-1:-1:color=black"]
else:
    chain.append(f"scale={W}:{H}:flags=lanczos:in_color_matrix={matris}:in_range={aralik}")
chain += ["format=gbrp", r"select=eq(n\,15)", f"scale={SW}:{SH}:flags=area", "format=rgb24"]
ref = rgb_kare(kaynak, ",".join(chain))
en_iyi = None
if ref:
    # Kare hizi donusumunde ilk karenin zamanina gore +-1 kare kayma olabilir (senkron ayrica olculur).
    for n in (14, 15, 16):
        o = rgb_kare(cikti, rf"select=eq(n\,{n}),scale={SW}:{SH}:flags=area:in_color_matrix=bt709:in_range=tv,format=rgb24")
        if o:
            m = fark_olcusu(ref, o)
            if en_iyi is None or m < en_iyi:
                en_iyi = m
kirp = "kırpma %dx%d+%d+%d" % crop if crop else ("siyah kenar" if ORAN == "kenar" else "kırpma yok")
chk(f"Renk ve kırpma ({kirp})", f"ortanca ≤ {ORTANCA_ESIK}, %90 ≤ {P90_ESIK}",
    "hesaplanamadı" if en_iyi is None else f"ortanca {en_iyi[0]}, %90 {en_iyi[1]}",
    en_iyi is not None and en_iyi[0] <= ORTANCA_ESIK and en_iyi[1] <= P90_ESIK)

w = max(len(r[1]) for r in rows)
for st, name, want, got in rows:
    print(f"{st:5} {name:<{w}}  beklenen: {want:<16} çıktı: {got}")
fark = [r for r in rows if r[0] == "FARK"]
print(f"\nSağlamlık: {len(rows)} kontrol, {len(fark)} fark.")
sys.exit(1 if fark else 0)
