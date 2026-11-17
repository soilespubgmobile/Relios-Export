#!/usr/bin/env python3
"""TikTok'a yüklenecek x264 dosyası için sıkıştırma çalışması. Programın parçası değildir.

Her ayar (varyant) ve CRF için:
  1. Kayıpsız kaynak kesiti programın TikTok komutuyla x264'e kodlanır (yükleme dosyası; ses yok).
  2. Yükleme dosyası TikTok'un yeniden kodlamasının benzetiminden geçirilir:
       S1: 1080p60 HEVC, x265 medium, 2300 kb/s (TikTok'un 1080p60 sürümü H.265 ~2,2-2,4 Mb/s ölçüldü)
       S2: 720p30 HEVC, x265 medium, 1200 kb/s (yaklaşık)
  3. Yükleme dosyası ve benzetimler kaynağa göre ölçülür: VMAF (v0.6.1 ve telefon), PSNR Y/U/V, SSIM;
     bantlanma (CAMBI, her 5. kare). libvmaf yoksa (yerel) yalnızca PSNR ve SSIM.
Sonuç satırları: 'SONUC|{json}' (varyant) ve 'KAYNAK|{json}' (kaynağın kendisi yüklenseydi).

Kullanım:
  tiktok_calisma.py kaynak --kaynak ref.mkv --ad klip
  tiktok_calisma.py olc --kaynak ref.mkv --ad klip --grup A [--crf 22,24,26,28,30] [--varyant B0,AQ3]
  tiktok_calisma.py varyantlar
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

FF = os.environ.get("FFMPEG", "ffmpeg")
FP = os.environ.get("FFPROBE", "ffprobe")
ISP = int(os.environ.get("ISP", str(os.cpu_count() or 4)))  # ölçüm / benzetim iş parçacığı

# Programın TikTok varsayılanı (0.11.0-0.13.1): veryslow, CRF, High, ref 16, önden bakış 250, 8 iş parçacığı,
# anahtar kare en çok 10 sn (60 fps'te 600), en az ve sahne eşiği x264'ün.
TEMEL_PARAMS = "threads=8:ref=16:keyint={keyint}:rc-lookahead=250"

# ad: (grup, hız ayarı ya da None, tune ya da None, ek x264 parametreleri, açıklama)
VARYANTLAR: dict[str, tuple[str, str | None, str | None, str, str]] = {
    "B0": ("A", None, None, "", "şimdiki TikTok varsayılanı"),
    "AQ3": ("A", None, None, "aq-mode=3", "AQ kipi 3 (karanlığa öncelik)"),
    "AQ2": ("A", None, None, "aq-mode=2", "AQ kipi 2 (otomatik varyans)"),
    "AQS08": ("A", None, None, "aq-strength=0.8", "AQ gücü 0,8"),
    "AQS12": ("A", None, None, "aq-strength=1.2", "AQ gücü 1,2"),
    "PSY05": ("B", None, None, "psy-rd=0.5,0", "psy-rd 0,5"),
    "PSY0": ("B", None, None, "psy=0", "psy kapalı"),
    "TSSIM": ("B", None, "ssim", "", "tune ssim"),
    "TFILM": ("B", None, "film", "", "tune film"),
    "DBM1": ("C", None, None, "deblock=-1,-1", "deblock -1:-1"),
    "DBP1": ("C", None, None, "deblock=1,1", "deblock 1:1"),
    "QC05": ("C", None, None, "qcomp=0.5", "qcomp 0,5"),
    "QC07": ("C", None, None, "qcomp=0.7", "qcomp 0,7"),
    "CQO2": ("C", None, None, "chroma-qp-offset=2", "kroma QP ofseti +2"),
    "TESA": ("D", None, None, "me=tesa:merange=32", "hareket arama tesa, aralık 32"),
    "SUB11": ("D", None, None, "subme=11", "alt piksel 11"),
    "NOFPS": ("D", None, None, "fast-pskip=0", "hızlı P atlama kapalı"),
    "BF16": ("D", None, None, "bframes=16", "16 B-kare"),
    "PLACEBO": ("E", "placebo", None, "", "placebo hız ayarı"),
    "AQ3S08": ("E", None, None, "aq-mode=3:aq-strength=0.8", "AQ kipi 3, güç 0,8"),
    "AQ3PSY05": ("E", None, None, "aq-mode=3:psy-rd=0.5,0", "AQ kipi 3, psy-rd 0,5"),
    "MR48": ("E", None, None, "merange=48", "arama aralığı 48"),
}

# TikTok benzetimleri: ad, kısa kenar (None: aynı), kare hızı (None: aynı), x265 bit hızı (kb/s)
BENZETIMLER = [("S1", None, None, 2300), ("S2", 720, 30, 1200)]


def komut(args: list[str], zaman_asimi: float | None = None) -> subprocess.CompletedProcess:
    r = subprocess.run(args, capture_output=True, text=True, errors="replace", timeout=zaman_asimi)
    if r.returncode != 0:
        raise RuntimeError(f"komut başarısız ({r.returncode}): {' '.join(args)}\n{r.stderr[-4000:]}")
    return r


def vmaf_var() -> bool:
    r = subprocess.run([FF, "-hide_banner", "-filters"], capture_output=True, text=True)
    return " libvmaf " in r.stdout


def bilgi(yol: Path) -> dict:
    r = komut([FP, "-v", "error", "-select_streams", "v:0", "-count_packets", "-show_entries",
               "stream=width,height,r_frame_rate,nb_read_packets,pix_fmt", "-of", "json", str(yol)])
    s = json.loads(r.stdout)["streams"][0]
    pay, payda = s["r_frame_rate"].split("/")
    return {"w": int(s["width"]), "h": int(s["height"]), "fps": int(pay) / int(payda),
            "kare": int(s["nb_read_packets"]), "pix": s.get("pix_fmt")}


def akis_bayt(yol: Path) -> int:
    r = komut([FP, "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=size", "-of", "csv=p=0",
               str(yol)])
    return sum(int(x) for x in r.stdout.split() if x.strip().isdigit())


def tb(fps: float) -> int:
    return 30 if round(fps) <= 30 else 60


def olc_vmaf(bozuk: Path, kaynak: Path, kaynak_fps: float, hedef_fps: float | None, olcek: tuple[int, int] | None,
             ad: str, gecici: Path, cambi: bool) -> dict:
    """Bozuk akışı kaynağa göre ölçer. Kare sırası zaman damgasından bağımsız (setpts=N)."""
    kt = tb(kaynak_fps)
    d = "[0:v]"
    if olcek:
        d += f"scale={olcek[0]}:{olcek[1]}:flags=bicubic,"
    r = f"[1:v]settb=1/{kt},setpts=N,"
    if hedef_fps:
        r += f"fps={hedef_fps:g},settb=1/{int(hedef_fps)},setpts=N,"
        d += f"format=yuv420p,settb=1/{int(hedef_fps)},setpts=N[d];"
    else:
        d += f"format=yuv420p,settb=1/{kt},setpts=N[d];"
    r += "format=yuv420p[r];"
    sonuc: dict = {}
    log = gecici / f"{ad}.json"
    if vmaf_var():
        modeller = ("version=vmaf_v0.6.1\\\\:name=vmaf|"
                    "version=vmaf_v0.6.1\\\\:name=vmaf_phone\\\\:enable_transform=true")
        g = (d + r + f"[d][r]libvmaf=model={modeller}:feature=name=psnr|name=float_ssim:"
             f"n_threads={ISP}:log_fmt=json:log_path={log}")
        komut([FF, "-hide_banner", "-nostdin", "-v", "error", "-i", str(bozuk), "-i", str(kaynak), "-lavfi", g,
               "-f", "null", "-"])
        j = json.loads(log.read_text())
        kareler = j["frames"]
        sonuc["kare"] = len(kareler)
        for anahtar, yeni in (("vmaf", "vmaf"), ("vmaf_phone", "vmaf_tel"), ("psnr_y", "psnr_y"),
                              ("psnr_cb", "psnr_u"), ("psnr_cr", "psnr_v"), ("float_ssim", "ssim")):
            v = [k["metrics"][anahtar] for k in kareler if anahtar in k["metrics"]]
            if v:
                sonuc[yeni] = round(sum(v) / len(v), 4)
                if anahtar == "vmaf":
                    s = sorted(v)
                    sonuc["vmaf_p5"] = round(s[max(0, int(len(s) * 0.05) - 1)], 3)
                    sonuc["vmaf_min"] = round(s[0], 3)
        if cambi:
            log2 = gecici / f"{ad}.cambi.json"
            g2 = (d + r + f"[d][r]libvmaf=feature=name=cambi:n_subsample=5:n_threads={ISP}:"
                  f"log_fmt=json:log_path={log2}")
            komut([FF, "-hide_banner", "-nostdin", "-v", "error", "-i", str(bozuk), "-i", str(kaynak), "-lavfi", g2,
                   "-f", "null", "-"])
            k2 = [k["metrics"]["cambi"] for k in json.loads(log2.read_text())["frames"] if "cambi" in k["metrics"]]
            if k2:
                sonuc["cambi"] = round(sum(k2) / len(k2), 4)
                sonuc["cambi_max"] = round(max(k2), 4)
    else:
        # Yerel: PSNR ve SSIM (FFmpeg süzgeçleri)
        st = gecici / f"{ad}.psnr.log"
        g = d + r + f"[d]split[d1][d2];[r]split[r1][r2];[d1][r1]psnr=stats_file={st};[d2][r2]ssim"
        e = komut([FF, "-hide_banner", "-nostdin", "-i", str(bozuk), "-i", str(kaynak), "-lavfi", g, "-f", "null",
                   "-"]).stderr
        ys, us, vs = [], [], []
        for satir in st.read_text().splitlines():
            m = dict(re.findall(r"(\w+):([-\d.inf]+)", satir))
            ys.append(float(m["psnr_y"]))
            us.append(float(m["psnr_u"]))
            vs.append(float(m["psnr_v"]))
        sonuc["kare"] = len(ys)
        sonuc["psnr_y"] = round(sum(ys) / len(ys), 4)
        sonuc["psnr_u"] = round(sum(us) / len(us), 4)
        sonuc["psnr_v"] = round(sum(vs) / len(vs), 4)
        sonuc["psnr_y_min"] = round(min(ys), 3)
        m = re.findall(r"SSIM Y:([\d.]+).*All:([\d.]+)", e)
        if m:
            sonuc["ssim"] = float(m[-1][0])
            sonuc["ssim_all"] = float(m[-1][1])
    return sonuc


def benzet(yukleme: Path, ad: str, gecici: Path, kaynak_bilgi: dict) -> list[tuple[str, Path, dict]]:
    """Yükleme dosyasını TikTok benzetimlerinden geçirir."""
    cikti = []
    for bad, kisa, fps, kbps in BENZETIMLER:
        yol = gecici / f"{ad}_{bad}.mp4"
        vf = []
        w, h = kaynak_bilgi["w"], kaynak_bilgi["h"]
        if fps:
            vf.append(f"settb=1/{tb(kaynak_bilgi['fps'])},setpts=N,fps={fps}")
        if kisa and min(w, h) > kisa:
            if w >= h:
                yw, yh = round(w * kisa / h / 2) * 2, kisa
            else:
                yw, yh = kisa, round(h * kisa / w / 2) * 2
            vf.append(f"scale={yw}:{yh}:flags=bicubic")
        args = [FF, "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", str(yukleme), "-an"]
        if vf:
            args += ["-vf", ",".join(vf)]
        args += ["-c:v", "libx265", "-preset", "medium", "-b:v", f"{kbps}k", "-maxrate", f"{kbps}k", "-bufsize",
                 f"{kbps * 2}k", "-x265-params", f"log-level=error:pools={ISP}:frame-threads=2", str(yol)]
        t0 = time.time()
        komut(args)
        b = bilgi(yol)
        cikti.append((bad, yol, {"kbps": round(akis_bayt(yol) * 8 / (b["kare"] / b["fps"]) / 1000, 1),
                                 "sure": round(time.time() - t0, 1), "fps": fps}))
    return cikti


def olc_benzetimler(yukleme: Path, kaynak: Path, kb: dict, ad: str, gecici: Path) -> dict:
    sonuc = {}
    for bad, yol, bb in benzet(yukleme, ad, gecici, kb):
        olcek = (kb["w"], kb["h"]) if bad != "S1" else None
        m = olc_vmaf(yol, kaynak, kb["fps"], bb["fps"], olcek, f"{ad}_{bad}", gecici, cambi=bad == "S1")
        m["kbps"] = bb["kbps"]
        m["sure"] = bb["sure"]
        sonuc[bad] = m
        yol.unlink(missing_ok=True)
    return sonuc


def x264_komutu(kaynak: Path, cikti: Path, varyant: str, crf: float, keyint: int) -> list[str]:
    _, hiz, tune, ek, _ = VARYANTLAR[varyant]
    p = TEMEL_PARAMS.format(keyint=keyint) + f":crf={crf:g}"
    if ek:
        p += ":" + ek
    args = [FF, "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", str(kaynak), "-an", "-c:v", "libx264",
            "-preset", hiz or "veryslow"]
    if tune:
        args += ["-tune", tune]
    args += ["-profile:v", "high", "-level:v", "5.1", "-x264-params", p, str(cikti)]
    return args


def yaz(onek: str, d: dict) -> None:
    print(f"{onek}|{json.dumps(d, ensure_ascii=False, separators=(',', ':'))}", flush=True)


def x264_surumu(yol: Path) -> str:
    veri = yol.read_bytes()[:400000]
    m = re.search(rb"x264 - core [^\x00]{0,60}", veri)
    return m.group(0).decode("ascii", "replace").split(" - H.264")[0] if m else "?"


def cmd_kaynak(a: argparse.Namespace) -> None:
    kaynak = Path(a.kaynak)
    kb = bilgi(kaynak)
    with tempfile.TemporaryDirectory(dir=a.gecici) as g:
        gecici = Path(g)
        d = {"klip": a.ad, "w": kb["w"], "h": kb["h"], "fps": round(kb["fps"], 3), "kare": kb["kare"]}
        d["dogrudan"] = olc_vmaf(kaynak, kaynak, kb["fps"], None, None, "kaynak", gecici, cambi=True)
        d.update(olc_benzetimler(kaynak, kaynak, kb, "kaynak", gecici))
        yaz("KAYNAK", d)


def cmd_olc(a: argparse.Namespace) -> None:
    kaynak = Path(a.kaynak)
    kb = bilgi(kaynak)
    keyint = round(10 * kb["fps"]) if kb["fps"] else 600
    crfler = [float(x) for x in a.crf.split(",")]
    if a.varyant:
        varyantlar = a.varyant.split(",")
    else:
        varyantlar = [v for v, t in VARYANTLAR.items() if t[0] in a.grup.split(",")]
    sure = kb["kare"] / kb["fps"]
    surum_yazildi = False
    for v in varyantlar:
        for crf in crfler:
            with tempfile.TemporaryDirectory(dir=a.gecici) as g:
                gecici = Path(g)
                yukleme = gecici / f"{v}_{crf:g}.mkv"
                t0 = time.time()
                komut(x264_komutu(kaynak, yukleme, v, crf, keyint))
                kod_sure = time.time() - t0
                if not surum_yazildi:
                    print(f"X264|{x264_surumu(yukleme)}", flush=True)
                    surum_yazildi = True
                bayt = akis_bayt(yukleme)
                d = {"klip": a.ad, "varyant": v, "crf": crf, "bayt": bayt,
                     "kbps": round(bayt * 8 / sure / 1000, 1), "kod_sure": round(kod_sure, 1),
                     "kod_fps": round(kb["kare"] / kod_sure, 2)}
                d["dogrudan"] = olc_vmaf(yukleme, kaynak, kb["fps"], None, None, "yukleme", gecici, cambi=True)
                d.update(olc_benzetimler(yukleme, kaynak, kb, "yukleme", gecici))
                yaz("SONUC", d)


def main() -> None:
    p = argparse.ArgumentParser()
    alt = p.add_subparsers(dest="komut", required=True)
    k = alt.add_parser("kaynak")
    k.add_argument("--kaynak", required=True)
    k.add_argument("--ad", required=True)
    k.add_argument("--gecici", default=None)
    o = alt.add_parser("olc")
    o.add_argument("--kaynak", required=True)
    o.add_argument("--ad", required=True)
    o.add_argument("--grup", default="A")
    o.add_argument("--varyant", default="")
    o.add_argument("--crf", default="22,24,26,28,30")
    o.add_argument("--gecici", default=None)
    alt.add_parser("varyantlar")
    a = p.parse_args()
    if a.komut == "kaynak":
        cmd_kaynak(a)
    elif a.komut == "olc":
        cmd_olc(a)
    else:
        for v, (gr, hiz, tune, ek, aciklama) in VARYANTLAR.items():
            print(f"{v}\t{gr}\t{hiz or 'veryslow'}\t{tune or '-'}\t{ek or '-'}\t{aciklama}")


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as e:
        print(f"HATA|{e}", file=sys.stderr, flush=True)
        sys.exit(1)
