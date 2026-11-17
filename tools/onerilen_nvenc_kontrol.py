"""Önerilen ayarların (ekran kartı, NVENC) kullanıcının bilgisayarında üretilen çıktısını kaynağıyla ve örnek videonun
ölçülen yapısıyla karşılaştırır: kap, görüntü (boyut, kare hızı / sayısı, süre), H.264 başlıkları (profil, DPB, P-karenin
referans sayısı, CABAC, 8x8, ağırlıklı tahmin, dilim, B-kare düzeni, IDR aralığı, HRD, seviye sınırları), bit hızı (VBV
benzetimi), ses (AAC-LC, hız, kanal, bit hızı, dil, süre, -14 LUFS, gerçek tepe <= -1 dBTP).

Kullanım: python3 -I tools/onerilen_nvenc_kontrol.py <kaynak> <çıktı> --onerilen 38m|1717
GitHub'da ekran kartı yok: bu denetim kullanıcının bilgisayarındaki çıktılar içindir (0.13.1'de 9 çıktı, 0 fark).
"""
import json, math, subprocess, sys
from collections import Counter

kaynak, cikti = sys.argv[1], sys.argv[2]
ek = dict(zip(sys.argv[3::2], sys.argv[4::2]))
ON = {
    "38m": dict(profil=100, ref=2, l0=1, dct8=1, b=1, gop_sn=1.0, gop_kare=None, hedef=12000, maks=16000, tampon=16000,
                ses_hiz=44100, ses_kbps=128, kap="mov"),
    "1717": dict(profil=77, ref=3, l0=3, dct8=0, b=0, gop_sn=None, gop_kare=30, hedef=52000, maks=62000, tampon=62000,
                 ses_hiz=48000, ses_kbps=512, kap="mp4"),
}[ek["--onerilen"]]
sat = []


def chk(ad, beklenen, sonuc, ok):
    sat.append(("OK" if ok else "FARK", ad, str(beklenen), str(sonuc)))


def bilgi(ad, sonuc):
    sat.append(("BİLGİ", ad, "-", str(sonuc)))


def calistir(a):
    return subprocess.run(a, capture_output=True, text=True, encoding="utf-8", errors="replace", stdin=subprocess.DEVNULL)


def probe(y):
    return json.loads(calistir(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", y]).stdout or "{}")


def donus(s):
    for sd in s.get("side_data_list", []) or []:
        if "rotation" in sd:
            return int(sd["rotation"])
    return 0


def kesir(x):
    a, b = x.split("/")
    return float(a) / float(b) if float(b) else 0.0


k, c = probe(kaynak), probe(cikti)
kv = next(s for s in k["streams"] if s["codec_type"] == "video")
cv = next(s for s in c["streams"] if s["codec_type"] == "video")
ka = [s for s in k["streams"] if s["codec_type"] == "audio"]
ca = [s for s in c["streams"] if s["codec_type"] == "audio"]

# ---- kap
fmt = c["format"]
brand = (fmt.get("tags") or {}).get("major_brand", "")
kap_ok = brand.strip() == "qt" if ON["kap"] == "mov" else brand.strip() in ("isom", "mp42", "mp41", "iso5", "iso6")
chk("Kap", ON["kap"], brand, kap_ok)
veri = open(cikti, "rb").read(1 << 20)
i, sira = 0, []
with open(cikti, "rb") as fh:
    b = fh.read()
while i + 8 <= len(b):
    n = int.from_bytes(b[i:i + 4], "big"); t = b[i + 4:i + 8].decode("latin1")
    if n == 1:
        n = int.from_bytes(b[i + 8:i + 16], "big")
    sira.append(t)
    if n < 8:
        break
    i += n
chk("moov, mdat'tan önce (hızlı başlatma)", "moov<mdat", ",".join(sira), "moov" in sira and "mdat" in sira and sira.index("moov") < sira.index("mdat"))
ct = (fmt.get("tags") or {}).get("creation_time", "")
chk("Oluşturma tarihi (UTC)", "var", ct, ct.endswith("Z"))

# ---- görüntü
chk("Video codec", "h264", cv.get("codec_name"), cv.get("codec_name") == "h264")
enc = (cv.get("tags") or {}).get("encoder", "")
chk("Kodlayıcı (ekran kartı)", "h264_nvenc", enc, "h264_nvenc" in enc)
chk("Video dili", "eng", (cv.get("tags") or {}).get("language"), (cv.get("tags") or {}).get("language") == "eng")
kw, kh = int(kv["width"]), int(kv["height"])
if abs(donus(kv)) in (90, 270):
    kw, kh = kh, kw
chk("Boyut (kaynakla aynı)", f"{kw}x{kh}", f"{cv['width']}x{cv['height']}", (kw, kh) == (int(cv["width"]), int(cv["height"])))
chk("Kare hızı (kaynakla aynı)", kv.get("r_frame_rate"), cv.get("r_frame_rate"), kv.get("r_frame_rate") == cv.get("r_frame_rate"))
chk("Piksel biçimi", "yuv420p", cv.get("pix_fmt"), cv.get("pix_fmt") == "yuv420p")
fps = kesir(cv["avg_frame_rate"])

def paketler(y, sec):
    r = calistir(["ffprobe", "-v", "error", "-select_streams", sec, "-show_entries", "packet=pts_time,dts_time,size,flags", "-of", "json", y])
    return json.loads(r.stdout or "{}").get("packets", [])

kp, cp = paketler(kaynak, "v:0"), paketler(cikti, "v:0")
chk("Kare sayısı (kaynakla aynı)", len(kp), len(cp), len(kp) == len(cp))
kd, cd = float(k["format"]["duration"]), float(fmt["duration"])
kare_sure = len(kp) / kesir(kv["avg_frame_rate"]) if kp else kd
# Kaynağın kabı (ör. MKV) sesin sonunu da sayar; çıktının süresi kabınkine ya da karelerinkine uymalı.
chk("Süre (± 0,05 sn)", f"{kd:.3f} / {kare_sure:.3f}", f"{cd:.3f}", abs(kd - cd) <= 0.05 or abs(kare_sure - cd) <= 0.05)
r = calistir(["ffmpeg", "-hide_banner", "-v", "error", "-i", cikti, "-f", "null", "-"])
chk("Hatasız çözülüyor", "hata yok", r.stderr.strip()[:80] or "hata yok", not r.stderr.strip())

# başlıklar
der = json.loads(calistir([sys.executable, "-I", __file__.replace("onerilen_nvenc_kontrol.py", "h264_basliklar.py"), cikti]).stdout)
sps, pps = der["sps"], der["pps"]
chk("Profil", ON["profil"], sps["profile_idc"], sps["profile_idc"] == ON["profil"])
chk("En çok referans kare (DPB)", ON["ref"], sps["max_num_ref_frames"], sps["max_num_ref_frames"] == ON["ref"])
chk("P-karenin referans sayısı", ON["l0"], pps["num_ref_idx_l0_default_active_minus1"] + 1, pps["num_ref_idx_l0_default_active_minus1"] + 1 == ON["l0"])
p_over = der["dilim_alanlari"].get("P", {}).get("num_ref_idx_l0_active_minus1", {})
fazla = [int(x) + 1 for x in p_over if x != "-" and int(x) + 1 > ON["l0"]]
chk("Hiçbir P-kare fazlasına bakmıyor", f"<= {ON['l0']}", fazla or "yok", not fazla)
chk("CABAC", 1, pps["entropy_coding_mode_flag"], pps["entropy_coding_mode_flag"] == 1)
chk("8x8 dönüşüm", ON["dct8"], pps.get("transform_8x8_mode_flag", 0), pps.get("transform_8x8_mode_flag", 0) == ON["dct8"])
chk("Ağırlıklı tahmin yok", "0/0", f"{pps['weighted_pred_flag']}/{pps['weighted_bipred_idc']}", pps["weighted_pred_flag"] == 0 and pps["weighted_bipred_idc"] == 0)
chk("Kare başına tek dilim", "{1}", der["dilim_kare"], set(der["dilim_kare"]) == {"1"})
# kare türleri
tur = der["tur_sayilari"]
desen = der["desen_ilk120 (n=referans degil, D=IDR)"]
if ON["b"] == 0:
    chk("B-kare yok", 0, tur.get("B", 0), tur.get("B", 0) == 0)
else:
    b_ref = der["dilim_alanlari"].get("B", {}).get("_ref_idc", {})
    chk("B-kareler referans değil", "{0}", b_ref, set(b_ref) <= {"0"})
    bb = "BnBn" in desen
    chk("Arka arkaya en çok 1 B-kare", "PBPB", "BB var" if bb else "yok", not bb)
    oran = tur.get("B", 0) / max(1, len(cp) - tur.get("IDR", 0))
    bilgi("B-kare oranı (IDR dışı karelere)", f"{oran:.2f}")
    chk("B-kare oranı (~0,5)", "0,40-0,55", f"{oran:.2f}", 0.40 <= oran <= 0.55)
gop = ON["gop_kare"] or round(fps * ON["gop_sn"])
ara = der["idr_araliklari"]
chk("IDR aralığı", gop, ara, set(int(x) for x in ara) <= {gop} and len(ara) >= 1 or (len(cp) <= gop and not ara))
chk("IDR dışında I-kare yok (sahne değişimi kapalı)", 0, len(der["idr_olmayan_I"]), not der["idr_olmayan_I"])
# HRD
if sps.get("nal_hrd_parameters_present_flag"):
    br = (sps["bit_rate_value_minus1[0]"] + 1) * 2 ** (6 + sps["bit_rate_scale"]) / 1000
    cpb = (sps["cpb_size_value_minus1[0]"] + 1) * 2 ** (4 + sps["cpb_size_scale"]) / 1000
    chk("HRD bit hızı = en yüksek", ON["maks"], br, abs(br - ON["maks"]) < 1)
    chk("HRD tampon", ON["tampon"], cpb, abs(cpb - ON["tampon"]) < 1)
    chk("HRD değişken bit hızı (cbr_flag 0)", 0, sps["cbr_flag[0]"], sps["cbr_flag[0]"] == 0)
# seviye sınırları
L = {9: (1485, 99, 396, 64, 175), 10: (1485, 99, 396, 64, 175), 11: (3000, 396, 900, 192, 500), 12: (6000, 396, 2376, 384, 1000),
     13: (11880, 396, 2376, 768, 2000), 20: (11880, 396, 2376, 2000, 2000), 21: (19800, 792, 4752, 4000, 4000),
     22: (20250, 1620, 8100, 4000, 4000), 30: (40500, 1620, 8100, 10000, 10000), 31: (108000, 3600, 18000, 14000, 14000),
     32: (216000, 5120, 20480, 20000, 20000), 40: (245760, 8192, 32768, 20000, 25000), 41: (245760, 8192, 32768, 50000, 62500),
     42: (522240, 8704, 34816, 50000, 62500), 50: (589824, 22080, 110400, 135000, 135000), 51: (983040, 36864, 184320, 240000, 240000),
     52: (2073600, 36864, 184320, 240000, 240000), 60: (4177920, 139264, 696320, 240000, 240000)}
lv = sps["level_idc"]
mbw, mbh = sps["pic_width_in_mbs_minus1"] + 1, sps["pic_height_in_map_units_minus1"] + 1
fs = mbw * mbh
mmbps, mfs, mdpb, mbr, mcpb = L[lv]
nal = 1500 if ON["profil"] == 100 else 1200
sorun = []
if fs * fps > mmbps * 1.0001: sorun.append(f"MB/s {fs*fps:.0f}>{mmbps}")
if fs > mfs or mbw > math.sqrt(mfs * 8) or mbh > math.sqrt(mfs * 8): sorun.append("kare boyutu")
if min(16, mdpb // fs) < sps["max_num_ref_frames"]: sorun.append(f"DPB {mdpb//fs}<{sps['max_num_ref_frames']}")
if ON["maks"] * 1000 > mbr * nal: sorun.append(f"bit hızı {ON['maks']}>{mbr*nal/1000:.0f}")
if ON["tampon"] * 1000 > mcpb * nal: sorun.append(f"tampon {ON['tampon']}>{mcpb*nal/1000:.0f}")
ks = probe(kaynak)
klv = next(s for s in ks["streams"] if s["codec_type"] == "video").get("level", -1)
chk("Seviye sınırları içinde", "uygun", f"{lv/10:.1f} " + (",".join(sorun) or "uygun"), not sorun)
bilgi("Seviye (kaynak / çıktı)", f"{klv/10 if klv and klv > 0 else '?'} / {lv/10:.1f}")
# bit hızı
boy = [int(x["size"]) for x in cp]
ort = sum(boy) * 8 / (len(boy) / fps) / 1000
bilgi("Ortalama bit hızı (kb/s)", f"{ort:.0f} (hedef {ON['hedef']})")
chk("Ortalama, hedefin %15 fazlasını geçmiyor", f"<= {ON['hedef']*1.15:.0f}", f"{ort:.0f}", ort <= ON["hedef"] * 1.15)
w = max(1, round(fps))
kay = max(sum(boy[i:i + w]) for i in range(0, max(1, len(boy) - w + 1))) * 8 / 1000
bilgi("En yüksek 1 sn (kbit)", f"{kay:.0f}")
# VBV: tampon dolu başlar (en serbest durum), en yüksek bit hızıyla dolar; hiçbir kare boşaltmamalı
dolu = ON["tampon"] * 1000.0
alt = dolu
dts = [float(x.get("dts_time") or 0) for x in cp]
for j, (sz, t) in enumerate(zip(boy, dts)):
    if j:
        dolu = min(ON["tampon"] * 1000.0, dolu + ON["maks"] * 1000.0 * (t - dts[j - 1]))
    dolu -= sz * 8
    alt = min(alt, dolu)
chk("VBV taşmıyor (en yüksek / tampon)", ">= 0", f"{alt/1000:.0f} kbit", alt >= 0)

# ---- ses
chk("Ses izi", 1, len(ca), len(ca) == 1)
if ca:
    a = ca[0]
    chk("Ses codec", "aac LC", f"{a.get('codec_name')} {a.get('profile')}", a.get("codec_name") == "aac" and a.get("profile") == "LC")
    chk("Örnekleme hızı", ON["ses_hiz"], a.get("sample_rate"), int(a.get("sample_rate", 0)) == ON["ses_hiz"])
    chk("Kanal", 2, a.get("channels"), a.get("channels") == 2)
    chk("Ses dili", "eng", (a.get("tags") or {}).get("language"), (a.get("tags") or {}).get("language") == "eng")
    ap = paketler(cikti, "a:0")
    asure = len(ap) * 1024 / ON["ses_hiz"]
    akbps = sum(int(x["size"]) for x in ap) * 8 / asure / 1000
    chk("Ses bit hızı (± %3)", ON["ses_kbps"], f"{akbps:.1f}", abs(akbps - ON["ses_kbps"]) <= ON["ses_kbps"] * 0.03)
    ad = float(a.get("duration", 0))

    def ses_sonu(y):
        # Kabın bildirdiği süre yoksa (ör. MKV) son ses paketinin bitişi.
        r = calistir(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "packet=pts_time,duration_time",
                      "-of", "csv=p=0", y])
        son = 0.0
        for satir in r.stdout.split():
            p = satir.split(",")
            try:
                son = max(son, float(p[0]) + float(p[1]))
            except (ValueError, IndexError):
                pass
        return son

    beklenen = min(float(ka[0].get("duration") or ses_sonu(kaynak) or kd), cd) if ka else cd
    chk("Ses süresi (± 0,05 sn)", f"{beklenen:.3f}", f"{ad:.3f}", abs(ad - beklenen) <= 0.05)
    def olc(y):
        r = calistir(["ffmpeg", "-hide_banner", "-nostdin", "-i", y, "-map", "0:a:0", "-af",
                      "ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I", "-f", "null", "-"])
        il = [float(l.split("=")[1]) for l in r.stderr.splitlines() if "lavfi.r128.I=" in l]
        tp = None
        for l in r.stderr.splitlines():
            if "Peak:" in l and "dBFS" in l:
                tp = float(l.split("Peak:")[1].split("dBFS")[0])
        return (il[-1] if il else None), tp
    ki, ktp = olc(kaynak) if ka else (None, None)
    ci, ctp = olc(cikti)
    bilgi("Kaynak yükseklik / tepe", f"{ki} LUFS / {ktp} dBTP")
    if ki is not None and ki < -14.5:
        chk("Yükseklik -14 (± 0,1)", -14, ci, ci is not None and abs(ci + 14) <= 0.1)
    else:
        bilgi("Yükseklik", ci)
    chk("Gerçek tepe <= -1 dBTP", "<= -1.0", ctp, ctp is not None and ctp <= -1.0)

for s in sat:
    print(f"{s[0]:<5} {s[1]:<44} beklenen: {s[2]:<16} çıktı: {s[3]}")
fark = sum(1 for s in sat if s[0] == "FARK")
print(f"== {len([s for s in sat if s[0] != 'BİLGİ'])} kontrol, {fark} fark")
sys.exit(1 if fark else 0)
