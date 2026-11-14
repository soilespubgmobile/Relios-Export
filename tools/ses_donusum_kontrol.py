#!/usr/bin/env python3
"""WAV (PCM) 16 bit çıktının sesini kaynağın sesiyle örnek örnek karşılaştırır.

Kullanım: python3 -I ses_donusum_kontrol.py <kaynak> <çıktı> ayni|titresim [--kaynak-iz N]

ayni:     kaynak 16 bit 48 kHz; çıktı bit bit aynı olmalı (dönüşüm sese hiç dokunmaz).
titresim: kaynak 24 bit 48 kHz; çıktı 16 bit'e üçgen titreşimle (TPDF, ±1 LSB) indirilmiş olmalı:
          her örnekte hata 1,5 LSB'den küçük (başka hiçbir işlem yok), ortalama karesel hata ~0,5 LSB
          (titreşimsiz yuvarlamada ~0,29 LSB olurdu), ortalama ~0 (DC kayması yok).
Örnekler başlangıçtan hizalı karşılaştırılır (iki dosyada da ortak uzunluk kadar).
"""
import array
import json
import math
import subprocess
import sys

kaynak, cikti, kip = sys.argv[1], sys.argv[2], sys.argv[3]
_ek = dict(zip(sys.argv[4::2], sys.argv[5::2]))
KAYNAK_IZ = int(_ek.get("--kaynak-iz", "0"))
if kip not in ("ayni", "titresim"):
    sys.exit(f"kip ayni ya da titresim olmalı: {kip}")

rows = []


def chk(name, want, got, ok=None):
    if ok is None:
        ok = str(want) == str(got)
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def akis(yol, iz):
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", f"a:{iz}", "-show_entries",
                        "stream=codec_name,sample_fmt,bits_per_raw_sample,bits_per_sample,sample_rate,channels",
                        "-of", "json", yol], capture_output=True, text=True, encoding="utf-8")
    s = json.loads(r.stdout or "{}").get("streams") or [{}]
    return s[0]


def ornekler(yol, iz, bicim):
    # Çözülen örnekler hiçbir dönüşüm olmadan ham olarak alınır (s16 -> s16le, s32 -> s32le).
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", yol, "-map", f"0:a:{iz}",
                        "-c:a", f"pcm_{bicim}", "-f", bicim, "-"], capture_output=True)
    if r.returncode != 0:
        sys.exit(f"ffmpeg okuyamadı ({yol}): {r.stderr.decode(errors='replace')[-500:]}")
    a = array.array("h" if bicim == "s16le" else "i")
    a.frombytes(r.stdout[:len(r.stdout) // a.itemsize * a.itemsize])
    if sys.byteorder != "little":
        a.byteswap()
    return a


k_bilgi, c_bilgi = akis(kaynak, KAYNAK_IZ), akis(cikti, 0)
chk("Çıktı ses codec", "pcm_s16le", c_bilgi.get("codec_name"))
chk("Çıktı örnekleme hızı", "48000", c_bilgi.get("sample_rate"))
chk("Kaynak örnekleme hızı (dönüşüm yok)", "48000", k_bilgi.get("sample_rate"))
chk("Kanal sayısı (kaynak / çıktı)", "2 / 2", f"{k_bilgi.get('channels')} / {c_bilgi.get('channels')}")
if kip == "ayni":
    chk("Kaynak ses", "pcm_s16le", k_bilgi.get("codec_name"))
else:
    chk("Kaynak ses", "pcm_s24le", k_bilgi.get("codec_name"))

c = ornekler(cikti, 0, "s16le")
k = ornekler(kaynak, KAYNAK_IZ, "s16le" if kip == "ayni" else "s32le")
n = min(len(k), len(c))
chk("Ortak uzunluk (kaynağın en az %95'i)", f">= {len(k) * 95 // 100} örnek", f"{n} örnek (kaynak {len(k)}, çıktı {len(c)})",
    ok=n >= len(k) * 95 // 100 and n > 0)

if n and kip == "ayni":
    fark = sum(1 for i in range(n) if k[i] != c[i])
    ilk = next((i for i in range(n) if k[i] != c[i]), None)
    chk("Örnekler bit bit aynı", "0 farklı örnek", f"{fark} farklı örnek" + ("" if ilk is None else f" (ilki {ilk}. örnek)"),
        ok=fark == 0)
elif n:
    # Hata 16 bit LSB cinsinden: çıktı * 65536 - kaynak (24 bit kaynak 32 bitin üst 24 bitinde).
    hata = [(c[i] * 65536 - k[i]) / 65536 for i in range(n)]
    en_cok = max(abs(h) for h in hata)
    rms = math.sqrt(sum(h * h for h in hata) / n)
    ort = sum(hata) / n
    kesirli = sum(1 for i in range(n) if k[i] & 0xFFFF) / n
    chk("Kaynakta 16 bit altı ayrıntı (gerçek 24 bit)", "> %50 örnekte", f"%{kesirli * 100:.1f}", ok=kesirli > 0.5)
    chk("En büyük hata (yalnızca 16 bit'e indirme)", "< 1,5 LSB", f"{en_cok:.3f} LSB", ok=en_cok < 1.5)
    chk("Ortalama karesel hata (üçgen titreşim ~0,50; titreşimsiz ~0,29)", "0,45 - 0,55 LSB", f"{rms:.3f} LSB",
        ok=0.45 <= rms <= 0.55)
    chk("Ortalama hata (DC kayması yok)", "|x| < 0,02 LSB", f"{ort:.4f} LSB", ok=abs(ort) < 0.02)

w = max(len(r[1]) for r in rows)
for st, name, want, got in rows:
    print(f"{st:5} {name:<{w}}  hedef: {want:<20} çıktı: {got}")
fark = [r for r in rows if r[0] == "FARK"]
print(f"\nSes dönüşümü: Toplam {len(rows)} kontrol, {len(fark)} fark.")
sys.exit(1 if fark else 0)
