#!/usr/bin/env python3
"""Tepe sınırlayıcı ve LUFS eşitlemenin gerçek çıktıda doğru çalıştığını ölçer.

Kullanım: python3 -I ses_yukseklik_kontrol.py <çıktı> sinir|kirpik|lufs [--hz 48000] [--hedef -14] [--tavan -1]

sinir:  ses yükseltilmiş, sınırlayıcı açık: en büyük örnek tavanın altında (16 bit titreşim payı 0,05 dB),
        ses kaynaktaki yerinde başlar (sınırlayıcının gecikmesi telafi edilmiş: 1,000 sn ± 1 ms).
kirpik: ses yükseltilmiş, sınırlayıcı kapalı: örnekler tam ölçekte kırpılır (sınırlayıcının işe yaradığının karşılığı).
lufs:   LUFS eşitleme: tümleşik yükseklik hedefte (± 0,5 LU), gerçek tepe tavanın altında (+ 0,3 dB pay).
Kaynak (iş akışında üretilir): ilk 1 sn sessiz, sonra ses.
"""
import array
import json
import math
import re
import subprocess
import sys

cikti, kip = sys.argv[1], sys.argv[2]
_ek = dict(zip(sys.argv[3::2], sys.argv[4::2]))
HZ = int(_ek.get("--hz", "48000"))
HEDEF = float(_ek.get("--hedef", "-14"))
TAVAN = float(_ek.get("--tavan", "-1"))
if kip not in ("sinir", "kirpik", "lufs"):
    sys.exit(f"kip sinir, kirpik ya da lufs olmalı: {kip}")

rows = []


def chk(name, want, got, ok):
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def akis():
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                        "stream=codec_name,sample_rate,channels", "-of", "json", cikti],
                       capture_output=True, text=True, encoding="utf-8")
    return (json.loads(r.stdout or "{}").get("streams") or [{}])[0]


def ornekler():
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", cikti, "-map", "0:a:0",
                        "-c:a", "pcm_s16le", "-f", "s16le", "-"], capture_output=True)
    if r.returncode != 0:
        sys.exit(f"ffmpeg okuyamadı: {r.stderr.decode(errors='replace')[-500:]}")
    a = array.array("h")
    a.frombytes(r.stdout[:len(r.stdout) // 2 * 2])
    if sys.byteorder != "little":
        a.byteswap()
    return a


def db(x):
    return 20 * math.log10(x) if x > 0 else -200.0


bilgi = akis()
chk("Ses codec", "pcm_s16le", bilgi.get("codec_name"), bilgi.get("codec_name") == "pcm_s16le")
chk("Örnekleme hızı", HZ, bilgi.get("sample_rate"), str(bilgi.get("sample_rate")) == str(HZ))

if kip in ("sinir", "kirpik"):
    s = ornekler()
    kanal = int(bilgi.get("channels") or 2)
    enbuyuk = max((abs(v) for v in s), default=0)
    tepe = db(enbuyuk / 32768)
    if kip == "sinir":
        chk("En büyük örnek (dBFS)", f"<= {TAVAN + 0.05:.2f}", f"{tepe:.2f}", tepe <= TAVAN + 0.05)
        chk("Ses yükseltildi (tavana yakın)", f">= {TAVAN - 1.5:.2f}", f"{tepe:.2f}", tepe >= TAVAN - 1.5)
        # Sesin başladığı yer: ilk kanalda 1000'i (-30 dBFS) aşan ilk örnek.
        ilk = next((i // kanal for i in range(0, len(s), kanal) if abs(s[i]) > 1000), -1)
        sn = ilk / HZ if ilk >= 0 else -1
        chk("Sesin başladığı an (sn, gecikme yok)", "1.000 ± 0.001", f"{sn:.4f}", abs(sn - 1.0) <= 0.001)
    else:
        kirpik = sum(1 for v in s if v in (32767, -32768))
        chk("Tam ölçekte kırpılan örnek (sınırlayıcı kapalı)", "> 100", kirpik, kirpik > 100)
else:
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-i", cikti, "-map", "0:a:0",
                        "-af", "ebur128=peak=true", "-f", "null", "-"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    ozet = r.stderr[r.stderr.rfind("Summary:"):]
    i = re.search(r"I:\s+(-?[\d.]+) LUFS", ozet)
    tp = re.search(r"True peak:\s+Peak:\s+(-?[\d.]+|-inf) dBFS", ozet)
    yukseklik = float(i.group(1)) if i else -200.0
    tepe = float(tp.group(1)) if tp and tp.group(1) != "-inf" else -200.0
    chk("Tümleşik yükseklik (LUFS)", f"{HEDEF:.1f} ± 0.5", f"{yukseklik:.1f}", abs(yukseklik - HEDEF) <= 0.5)
    chk("Gerçek tepe (dBTP)", f"<= {TAVAN + 0.3:.1f}", f"{tepe:.2f}", tepe <= TAVAN + 0.3)

for r in rows:
    print(f"{r[0]:<5} {r[1]:<44} hedef: {r[2]:<16} çıktı: {r[3]}")
fark = sum(1 for r in rows if r[0] == "FARK")
print(f"Ses yüksekliği: {len(rows)} kontrol, {fark} fark.")
sys.exit(1 if fark else 0)
