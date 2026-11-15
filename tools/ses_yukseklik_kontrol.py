#!/usr/bin/env python3
"""Tepe sınırlayıcı ve LUFS eşitlemenin gerçek çıktıda doğru çalıştığını ölçer.

Kullanım: python3 -I ses_yukseklik_kontrol.py <çıktı> <kip> [--hz 48000] [--hedef -14] [--tavan -1]
                                               [--kaynak <kaynak>] [--program <programın ölçtüğü LUFS>]
                                               [--codec pcm_s16le]

sinir:    ses yükseltilmiş, sınırlayıcı açık: en büyük örnek tavanın altında (16 bit titreşim payı 0,05 dB),
          örneklerin arasındaki gerçek tepe de tavanın altında (+ 0,3 dB pay; sınırlayıcı 192 kHz'te çalışır),
          ses kaynaktaki yerinde başlar (sınırlayıcının gecikmesi telafi edilmiş: 1,000 sn ± 1 ms).
kirpik:   ses yükseltilmiş, sınırlayıcı kapalı: örnekler tam ölçekte kırpılır (sınırlayıcının işe yaradığının karşılığı).
lufs:     LUFS eşitleme: tümleşik yükseklik hedefte (± 0,5 LU), gerçek tepe tavanın altında (+ 0,3 dB pay).
dogrusal: lufs + ses yalnızca yükseltilmiş / kısılmış olmalı: çıktı = kaynak × sabit kazanç (--kaynak gerekir);
          sabit kazançla açıklanamayan kalan, sesin 60 dB altında olmalı (16 bit titreşim ~ -80 dB).
tepeli:   kısık + ani tepeli ses (dinamik kip): gerçek tepe tavanın altında, hiç kırpılma yok, yükseklik hedefe
          doğru yükseltilmiş (hedef - 4 ile hedef + 0,5 arası; ani tepeler hedefe tam ulaşmayı engelleyebilir).
--program: programın satırda gösterdiği sonuç (kendi ölçümü) bu aracın ölçtüğüyle 0,3 LU içinde aynı olmalı.
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
KAYNAK = _ek.get("--kaynak")
PROGRAM = _ek.get("--program")
CODEC = _ek.get("--codec", "pcm_s16le")
if kip not in ("sinir", "kirpik", "lufs", "dogrusal", "tepeli"):
    sys.exit(f"bilinmeyen kip: {kip}")
if kip == "dogrusal" and not KAYNAK:
    sys.exit("dogrusal kipi --kaynak ister")

rows = []


def chk(name, want, got, ok):
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def akis(yol):
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                        "stream=codec_name,sample_rate,channels", "-of", "json", yol],
                       capture_output=True, text=True, encoding="utf-8")
    return (json.loads(r.stdout or "{}").get("streams") or [{}])[0]


def ornekler(yol, bicim):
    # s16le: 16 bit tamsayı (kırpılma ve tepe için); f32le: kayan nokta (sabit kazanç karşılaştırması için).
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", yol, "-map", "0:a:0",
                        "-c:a", f"pcm_{bicim}", "-f", bicim, "-"], capture_output=True)
    if r.returncode != 0:
        sys.exit(f"ffmpeg okuyamadı ({yol}): {r.stderr.decode(errors='replace')[-500:]}")
    a = array.array("h" if bicim == "s16le" else "f")
    a.frombytes(r.stdout[:len(r.stdout) // a.itemsize * a.itemsize])
    if sys.byteorder != "little":
        a.byteswap()
    return a


def db(x):
    return 20 * math.log10(x) if x > 0 else -200.0


def r128(yol):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-i", yol, "-map", "0:a:0",
                        "-af", "ebur128=peak=true", "-f", "null", "-"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    ozet = r.stderr[r.stderr.rfind("Summary:"):]
    i = re.search(r"I:\s+(-?[\d.]+) LUFS", ozet)
    tp = re.search(r"True peak:\s+Peak:\s+(-?[\d.]+|-inf) dBFS", ozet)
    return (float(i.group(1)) if i else -200.0,
            float(tp.group(1)) if tp and tp.group(1) != "-inf" else -200.0)


bilgi = akis(cikti)
chk("Ses codec", CODEC, bilgi.get("codec_name"), bilgi.get("codec_name") == CODEC)
chk("Örnekleme hızı", HZ, bilgi.get("sample_rate"), str(bilgi.get("sample_rate")) == str(HZ))

if kip in ("sinir", "kirpik"):
    s = ornekler(cikti, "s16le")
    kanal = int(bilgi.get("channels") or 2)
    tepe = db(max((abs(v) for v in s), default=0) / 32768)
    if kip == "sinir":
        chk("En büyük örnek (dBFS)", f"<= {TAVAN + 0.05:.2f}", f"{tepe:.2f}", tepe <= TAVAN + 0.05)
        chk("Ses yükseltildi (tavana yakın)", f">= {TAVAN - 1.5:.2f}", f"{tepe:.2f}", tepe >= TAVAN - 1.5)
        _, gercek = r128(cikti)
        chk("Gerçek tepe (dBTP, örnekler arası)", f"<= {TAVAN + 0.3:.1f}", f"{gercek:.2f}", gercek <= TAVAN + 0.3)
        # Sesin başladığı yer: ilk kanalda 1000'i (-30 dBFS) aşan ilk örnek.
        ilk = next((i // kanal for i in range(0, len(s), kanal) if abs(s[i]) > 1000), -1)
        sn = ilk / HZ if ilk >= 0 else -1
        chk("Sesin başladığı an (sn, gecikme yok)", "1.000 ± 0.001", f"{sn:.4f}", abs(sn - 1.0) <= 0.001)
    else:
        kirpik = sum(1 for v in s if v in (32767, -32768))
        chk("Tam ölçekte kırpılan örnek (sınırlayıcı kapalı)", "> 100", kirpik, kirpik > 100)
else:
    yukseklik, tepe = r128(cikti)
    if kip == "tepeli":
        chk("Tümleşik yükseklik (LUFS, yükseltildi)", f"{HEDEF - 4:.1f} … {HEDEF + 0.5:.1f}", f"{yukseklik:.1f}",
            HEDEF - 4 <= yukseklik <= HEDEF + 0.5)
        s = ornekler(cikti, "s16le")
        kirpik = sum(1 for v in s if v in (32767, -32768))
        chk("Tam ölçekte kırpılan örnek", "0", kirpik, kirpik == 0)
    else:
        chk("Tümleşik yükseklik (LUFS)", f"{HEDEF:.1f} ± 0.5", f"{yukseklik:.1f}", abs(yukseklik - HEDEF) <= 0.5)
    chk("Gerçek tepe (dBTP)", f"<= {TAVAN + 0.3:.1f}", f"{tepe:.2f}", tepe <= TAVAN + 0.3)

    if kip == "dogrusal":
        x = ornekler(KAYNAK, "f32le")
        y = ornekler(cikti, "f32le")
        n = min(len(x), len(y))
        sxy = sum(x[i] * y[i] for i in range(n))
        sxx = sum(x[i] * x[i] for i in range(n))
        syy = sum(y[i] * y[i] for i in range(n))
        g = sxy / sxx if sxx > 0 else 0.0
        kalan = sum((y[i] - g * x[i]) ** 2 for i in range(n))
        oran = 10 * math.log10(kalan / syy) if kalan > 0 and syy > 0 else -200.0
        chk("Uzunluk (örnek, kaynak / çıktı)", "aynı", f"{len(x)} / {len(y)}", len(x) == len(y))
        chk("Sabit kazanç (dB)", "yükseltme / kısma", f"{db(abs(g)):+.2f}", g > 0)
        chk("Sabit kazançla açıklanamayan kalan (dB)", "<= -60", f"{oran:.1f}", oran <= -60)

    if PROGRAM is not None:
        p = float(PROGRAM)
        chk("Programın gösterdiği sonuç (LUFS)", f"{yukseklik:.1f} ± 0.3", f"{p:.2f}", abs(p - yukseklik) <= 0.3)

for r in rows:
    print(f"{r[0]:<5} {r[1]:<44} hedef: {r[2]:<16} çıktı: {r[3]}")
fark = sum(1 for r in rows if r[0] == "FARK")
print(f"Ses yüksekliği: {len(rows)} kontrol, {fark} fark.")
sys.exit(1 if fark else 0)
