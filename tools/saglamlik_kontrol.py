#!/usr/bin/env python3
"""Relios Export TikTok ciktisinin saglamlik kontrolu (kaynakla karsilastirarak).

Kullanim: python -I saglamlik_kontrol.py <kaynak> <cikti.mp4> <matris> <aralik> <ses 1/0>

matris / aralik: kaynagin GERCEK renk matrisi ve araligi (deneme kaynagi uretilirken bilinir).
Kontroller: hatasiz cozulme, dondurme bilgisi, ses var/yok, sure, kare sayisi,
ses-goruntu suresi ve baslangici, renk + kirpma (bagimsiz hesaplanan referans kareyle).
"""
import json
import math
import subprocess
import sys

kaynak, cikti, matris, aralik, ses = sys.argv[1:6]
ses_bekleniyor = ses == "1"
FPS = 30000 / 1001
W, H = 1216, 2160
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


def crop_plan(vs):
    # Programdaki ArgsBuilder.PlanCrop916'nin bagimsiz karsiligi: gorunen goruntu ortadan 9:16.
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
    target = 9 / 16
    aspect = w * sar / h
    if abs(aspect - target) < 0.002:
        return None
    if aspect > target:
        cw = min(even(h * target / sar), w - (w % 2))
        return (cw, h, (w - cw) // 2, 0)
    ch = min(even(w * sar / target), h - (h % 2))
    return (w, ch, 0, (h - ch) // 2)


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

# 5) Kare sayisi (29.97 fps sabit)
r = run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_packets",
         "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", cikti])
nb = int(fnum(r.stdout.strip().split(",")[0], 0))
beklenen = src_dur * FPS
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
SW, SH = 304, 540
ORTANCA_ESIK, P90_ESIK = 6, 15


def rgb_kare(path, vf):
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", path, "-vf", vf, "-frames:v", "1",
                        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, stdin=subprocess.DEVNULL)
    return r.stdout if r.returncode == 0 and len(r.stdout) == SW * SH * 3 else None


def fark_olcusu(a, b):
    d = sorted(max(abs(a[i] - b[i]), abs(a[i + 1] - b[i + 1]), abs(a[i + 2] - b[i + 2]))
               for i in range(0, len(a), 3))
    return d[len(d) // 2], d[int(len(d) * 0.9)]


crop = crop_plan(sv)
chain = ["fps=30000/1001"]
if crop:
    chain.append("crop=%d:%d:%d:%d" % crop)
chain += [f"scale={W}:{H}:flags=lanczos:in_color_matrix={matris}:in_range={aralik}",
          "format=gbrp", r"select=eq(n\,15)", f"scale={SW}:{SH}:flags=area", "format=rgb24"]
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
kirp = "kırpma %dx%d+%d+%d" % crop if crop else "kırpma yok"
chk(f"Renk ve kırpma ({kirp})", f"ortanca ≤ {ORTANCA_ESIK}, %90 ≤ {P90_ESIK}",
    "hesaplanamadı" if en_iyi is None else f"ortanca {en_iyi[0]}, %90 {en_iyi[1]}",
    en_iyi is not None and en_iyi[0] <= ORTANCA_ESIK and en_iyi[1] <= P90_ESIK)

w = max(len(r[1]) for r in rows)
for st, name, want, got in rows:
    print(f"{st:5} {name:<{w}}  beklenen: {want:<16} çıktı: {got}")
fark = [r for r in rows if r[0] == "FARK"]
print(f"\nSağlamlık: {len(rows)} kontrol, {len(fark)} fark.")
sys.exit(1 if fark else 0)
