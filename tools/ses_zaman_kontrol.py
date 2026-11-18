#!/usr/bin/env python3
"""Çıktının ses izinin zaman çizgisini denetler (0.16.1: tepe sınırlayıcının gecikme telafisi).

0.16.0'a kadar sınırlayıcının gecikmesini FFmpeg'in kendi seçeneği (alimiter latency=1) gideriyordu. O seçenek sesin
zaman damgasında tek örneklik bir düzensizlik görünce (CapCut'ın dışa aktardığı MP4 / MOV dosyalarında var) telafiyi
bırakıyor: çıktının ses izinde 5 ms'lik bir boşluk kalıyor, sonrası 5 ms geç etiketleniyordu (örnekler tam ve yerinde).
0.16.1'de gecikme zincirde giderilir (apad + alimiter + atrim + asetpts). Bu araç üç işi yapar:

  python3 -I ses_zaman_kontrol.py duzensiz <girdi.mp4> <çıktı.mp4> <kare:fark> [<kare:fark> ...]
      Ses izinin zaman çizelgesine (stts) düzensizlik koyar: verilen ses karesinin süresi fark kadar örnek değişir
      (ör. 100:+1 → 100. kare 1025 örnek sürer; CapCut dosyalarındaki gibi). Ses verisi değişmez. moov sonda olmalı.

  python3 -I ses_zaman_kontrol.py denetle <çıktı> --kaynak <kaynak> [--duzensiz] [--pay 2]
      Çıktının ses paketleri arasında boşluk / üst üste binme var mı (en çok --pay örnek), toplam boşluk ve ses süresi
      kaynağınkiyle aynı mı, ses kaynaktaki yerinde mi başlıyor. --duzensiz: kaynağın gerçekten düzensiz olduğu da
      denetlenir (yoksa deneme bir şey kanıtlamaz). Kaynağın zaman çizgisi FFmpeg'in çözdüğü karelerden okunur
      (programın süzgeç zincirine giren ses): her kap ve kodlayıcıda çalışır (MKV / WebM'in milisaniyelik damgaları,
      Opus, Vorbis, PCM). Kaynakta gerçek bir boşluk varsa (--pay'dan büyük) çıktıda da aynısı beklenir.

  python3 -I ses_zaman_kontrol.py zincir <düzenli kaynak> --zincir "<programın -af zinciri>"
                                         [--duzensiz-kaynak <dosya>] [--kodlayici libfdk_aac] [--pay 2]
      Programın sınırlayıcı zinciri, zaman damgası düzenli seste 0.16.0'ın zinciriyle (alimiter latency=1) bit bit aynı
      sesi veriyor mu (192 kHz'te sınırlayıcının çıkışı ve zincirin tamamı; 64 bit, titreşimsiz). --duzensiz-kaynak:
      karşı deneme — aynı dosyada eski zincir boşluk bırakmalı (hata bu FFmpeg'de görülüyor, deneme onu yakalıyor),
      yeni zincir bırakmamalı.

ffmpeg ve ffprobe PATH'ten çalıştırılır. Çıkış kodu: 0 hepsi OK, 1 fark var, 2 kullanım / okuma hatası.
"""
import collections
import hashlib
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
from fractions import Fraction

rows = []


def chk(name, want, got, ok):
    rows.append(("OK" if ok else "FARK", name, str(want), str(got)))


def bitir():
    for durum, ad, beklenen, cikti in rows:
        print(f"{durum:4s} {ad:74s} hedef: {beklenen:34s} çıktı: {cikti}")
    fark = sum(1 for r in rows if r[0] != "OK")
    print(f"\n{len(rows)} kontrol, {fark} fark")
    sys.exit(1 if fark else 0)


def secenekler(argv, bayraklar=()):
    """--ad değer çiftleri ve değersiz bayraklar."""
    ek, i = {}, 0
    while i < len(argv):
        a = argv[i]
        if a in bayraklar:
            ek[a] = True
            i += 1
        elif a.startswith("--") and i + 1 < len(argv):
            ek[a] = argv[i + 1]
            i += 2
        else:
            sys.stderr.write(f"anlaşılmayan seçenek: {a}\n")
            sys.exit(2)
    return ek


# ---------------------------------------------------------------------------------------------------------------------
# duzensiz: stts'e düzensizlik koyar
# ---------------------------------------------------------------------------------------------------------------------

def kutular(veri, bas, son):
    """[bas, son) aralığındaki kutular: (tür, başlangıç, başlık uzunluğu, bitiş)."""
    i = bas
    while i + 8 <= son:
        boy, tur = struct.unpack(">I4s", veri[i:i + 8])
        baslik = 8
        if boy == 1:
            boy = struct.unpack(">Q", veri[i + 8:i + 16])[0]
            baslik = 16
        elif boy == 0:
            boy = son - i
        if boy < baslik or i + boy > son:
            sys.stderr.write(f"bozuk kutu: {tur!r} @ {i}\n")
            sys.exit(2)
        yield tur, i, baslik, i + boy
        i += boy


def kutu(tur, icerik):
    return struct.pack(">I4s", 8 + len(icerik), tur) + icerik


def bul(veri, bas, son, tur):
    return next((k for k in kutular(veri, bas, son) if k[0] == tur), None)


def ses_izi_mi(veri, bas, son):
    mdia = bul(veri, bas, son, b"mdia")
    if mdia is None:
        return False
    hdlr = bul(veri, mdia[1] + mdia[2], mdia[3], b"hdlr")
    # hdlr: sürüm / bayraklar (4), önceden tanımlı (4), işleyici türü (4)
    return hdlr is not None and veri[hdlr[1] + hdlr[2] + 8:hdlr[1] + hdlr[2] + 12] == b"soun"


def ic_ice_degistir(veri, bas, son, yol, fn):
    """Kutunun içeriğini [bas, son) yeniden kurar: yol[0] türündeki ilk çocuğun içine iner; yolun sonundaki kutunun
    içeriği fn(içerik) ile değişir. Öbür kutular bayt bayt kopyalanır; üst kutuların boyları yeniden yazılır."""
    parcalar, bulundu = [], False
    for tur, b, bl, s in kutular(veri, bas, son):
        if tur == yol[0] and not bulundu:
            bulundu = True
            ic = fn(veri[b + bl:s]) if len(yol) == 1 else ic_ice_degistir(veri, b + bl, s, yol[1:], fn)
            parcalar.append(kutu(tur, ic))
        else:
            parcalar.append(veri[b:s])
    if not bulundu:
        sys.stderr.write(f"kutu bulunamadı: {yol[0]!r}\n")
        sys.exit(2)
    return b"".join(parcalar)


def stts_degistir(icerik, degisiklikler):
    surum_bayrak, n = icerik[:4], struct.unpack(">I", icerik[4:8])[0]
    sureler = []
    for i in range(n):
        adet, sure = struct.unpack(">II", icerik[8 + 8 * i:16 + 8 * i])
        sureler += [sure] * adet
    for kare, fark in degisiklikler:
        if not 0 <= kare < len(sureler) - 1:
            sys.stderr.write(f"kare aralık dışında: {kare} (ses izinde {len(sureler)} kare var)\n")
            sys.exit(2)
        sureler[kare] += fark
    # Yeniden sıkıştır (ardışık eşit süreler tek girdi).
    girdiler = []
    for s in sureler:
        if girdiler and girdiler[-1][1] == s:
            girdiler[-1][0] += 1
        else:
            girdiler.append([1, s])
    print(f"stts: {n} girdi → {len(girdiler)} girdi ({len(sureler)} kare): "
          + " ".join(f"{a}×{s}" for a, s in girdiler))
    return surum_bayrak + struct.pack(">I", len(girdiler)) + b"".join(struct.pack(">II", a, s) for a, s in girdiler)


def duzensiz(argv):
    if len(argv) < 3:
        sys.stderr.write(__doc__)
        sys.exit(2)
    girdi, cikti = argv[0], argv[1]
    degisiklikler = []
    for d in argv[2:]:
        m = re.fullmatch(r"(\d+):([+-]\d+)", d)
        if not m:
            sys.stderr.write(f"kare:fark biçiminde olmalı (ör. 100:+1): {d}\n")
            sys.exit(2)
        degisiklikler.append((int(m.group(1)), int(m.group(2))))
    with open(girdi, "rb") as f:
        veri = f.read()
    ust = list(kutular(veri, 0, len(veri)))
    moov = next((k for k in ust if k[0] == b"moov"), None)
    mdat = next((k for k in ust if k[0] == b"mdat"), None)
    if moov is None or mdat is None:
        sys.stderr.write("moov / mdat bulunamadı (MP4 / MOV değil mi?)\n")
        sys.exit(2)
    if moov[1] < mdat[1]:
        # moov büyürse ses / görüntü verisinin dosyadaki yeri kayar (stco yeniden yazılmalı olurdu).
        sys.stderr.write("moov, mdat'tan önce: dosyayı -movflags faststart olmadan üretin\n")
        sys.exit(2)
    parcalar, bulundu = [], False
    for tur, b, bl, s in kutular(veri, moov[1] + moov[2], moov[3]):
        if tur == b"trak" and not bulundu and ses_izi_mi(veri, b + bl, s):
            bulundu = True
            parcalar.append(kutu(b"trak", ic_ice_degistir(
                veri, b + bl, s, [b"mdia", b"minf", b"stbl", b"stts"], lambda ic: stts_degistir(ic, degisiklikler))))
        else:
            parcalar.append(veri[b:s])
    if not bulundu:
        sys.stderr.write("ses izi bulunamadı\n")
        sys.exit(2)
    with open(cikti, "wb") as f:
        f.write(veri[:moov[1]] + kutu(b"moov", b"".join(parcalar)) + veri[moov[3]:])
    print(f"{os.path.basename(cikti)}: ses izine {len(degisiklikler)} düzensizlik kondu")
    sys.exit(0)


# ---------------------------------------------------------------------------------------------------------------------
# denetle: ses paketlerinin zaman çizgisi
# ---------------------------------------------------------------------------------------------------------------------

class Cizgi:
    """Bir dosyanın ilk ses izinin paketleri, örnek biriminde (izin kendi örnekleme hızında)."""

    def __init__(self, yol):
        r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                            "packet=pts,duration,size:stream=codec_name,sample_rate,time_base,channels,bits_per_sample",
                            "-of", "json", yol],
                           capture_output=True, text=True, encoding="utf-8")
        if r.returncode != 0:
            sys.stderr.write(f"ffprobe okuyamadı ({yol}): {r.stderr[-400:]}\n")
            sys.exit(2)
        j = json.loads(r.stdout or "{}")
        akis = (j.get("streams") or [{}])[0]
        if not akis:
            sys.stderr.write(f"ses izi yok: {yol}\n")
            sys.exit(2)
        self.codec = akis.get("codec_name", "?")
        self.hz = int(akis["sample_rate"])
        pay, payda = akis["time_base"].split("/")
        carpan = Fraction(int(pay), int(payda)) * self.hz
        # PCM'de (WAV sesi; ör. OBS'in MKV kaydı) paketlerin uzunluğu sabit değildir (21 / 22 ms'lik paketler):
        # her paketin örnek sayısı bayt sayısından bilinir.
        ornek_bayt = int(akis.get("channels") or 0) * (int(akis.get("bits_per_sample") or 0) // 8)
        pcm = self.codec.startswith("pcm_") and ornek_bayt > 0
        self.paketler, uzunluklar = [], []
        for p in j.get("packets") or []:
            if "pts" not in p or "duration" not in p or (pcm and "size" not in p):
                continue
            self.paketler.append((int(p["pts"]) * carpan, int(p["duration"]) * carpan))
            if pcm:
                uzunluklar.append(Fraction(int(p["size"]), ornek_bayt))
        if len(self.paketler) < 2:
            sys.stderr.write(f"ses paketi okunamadı: {yol}\n")
            sys.exit(2)
        # Ardışık paketlerin başlangıçları arasındaki süre, karenin kendi uzunluğundan (en sık görülen süre: AAC'de
        # 1024 örnek; PCM'de paketin kendi örnek sayısı) ne kadar farklı (örnek): artı boşluk, eksi üst üste binme.
        # Paketin dosyada yazan süresine bakılmaz: MP4 / MOV'da o süre zaten sonraki paketin başlangıcına göre yazılır
        # (boşluk onun içinde görünür).
        farklar = [self.paketler[i + 1][0] - self.paketler[i][0] for i in range(len(self.paketler) - 1)]
        if pcm:
            self.kare = collections.Counter(uzunluklar[:-1]).most_common(1)[0][0]
            self.aralar = [(f - u, i + 1) for i, (f, u) in enumerate(zip(farklar, uzunluklar))]
        else:
            self.kare = collections.Counter(farklar).most_common(1)[0][0]
            self.aralar = [(f - self.kare, i + 1) for i, f in enumerate(farklar)]
        self.en_buyuk = max(self.aralar, key=lambda a: a[0])
        self.en_kucuk = min(self.aralar, key=lambda a: a[0])
        self.toplam = sum(a[0] for a in self.aralar)
        self.duzensiz = [a for a in self.aralar if a[0] != 0]
        # Sunulan ses: 0'dan önce biten paketler kodlayıcının ön doldurmasıdır (AAC), sayılmaz.
        self.bas = max(Fraction(0), min(p[0] for p in self.paketler if p[0] + p[1] > 0))
        self.son = max(p[0] + p[1] for p in self.paketler)

    def ms(self, ornek):
        return float(ornek) * 1000 / self.hz

    def sn(self, ornek):
        return Fraction(ornek) / self.hz


class Cozulen:
    """Kaynağın ilk ses izi, FFmpeg'in çözdüğü hâliyle: karelerin başlangıcı ve örnek sayısı (örnek biriminde).
    Programın süzgeç zincirine giren ses budur. Paketlere bakılmaz: MKV / WebM'in milisaniyelik damgalarını, Opus'un
    baştaki ve sondaki atlanan örneklerini, Vorbis'in değişken uzunluktaki karelerini FFmpeg çözerken düzeltir;
    MP4 / MOV'daki tek örneklik düzensizlikler çözülen karelere aynen geçer (sınırlayıcının gördüğü zaman çizgisi)."""

    def __init__(self, yol):
        # Çözülen her kare bir PCM paketi olur; framecrc her paketin zamanını, süresini ve bayt sayısını yazar.
        r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", yol, "-map", "0:a:0", "-c:a", "pcm_s16le",
                            "-f", "framecrc", "-"], capture_output=True, text=True, encoding="utf-8", errors="replace")
        if r.returncode != 0:
            sys.stderr.write(f"ffmpeg kaynağın sesini çözemedi ({yol}): {r.stderr[-400:]}\n")
            sys.exit(2)
        taban, self.hz, kareler = None, None, []
        for satir in r.stdout.splitlines():
            if satir.startswith("#"):
                m = re.match(r"#tb 0: (\d+)/(\d+)", satir)
                if m:
                    taban = Fraction(int(m.group(1)), int(m.group(2)))
                m = re.match(r"#sample_rate 0: (\d+)", satir)
                if m:
                    self.hz = int(m.group(1))
                continue
            p = [x.strip() for x in satir.split(",")]
            if len(p) < 5 or p[0] != "0":
                continue
            try:
                kareler.append((int(p[2]), int(p[3]), int(p[4])))  # zaman, süre, bayt
            except ValueError:
                continue
        # Karenin süresi örnek sayısıdır: bayt sayısıyla orantılı olmalı (örnek başına 2 bayt × kanal), her karede aynı.
        if (not taban or not self.hz or len(kareler) < 2 or any(sure <= 0 for _, sure, _ in kareler)
                or len({Fraction(bayt, sure) for _, sure, bayt in kareler}) != 1):
            sys.stderr.write(f"kaynağın çözülen ses kareleri okunamadı: {yol}\n")
            sys.exit(2)
        carpan = taban * self.hz
        self.kareler = [(zaman * carpan, sure * carpan) for zaman, sure, _ in kareler]
        # Bir karenin bittiği yerle sonrakinin başladığı yer arasındaki fark (örnek): artı boşluk, eksi üst üste binme.
        self.aralar = [(self.kareler[i + 1][0] - (self.kareler[i][0] + self.kareler[i][1]), i + 1)
                       for i in range(len(self.kareler) - 1)]
        self.en_buyuk = max(self.aralar, key=lambda a: a[0])
        self.en_kucuk = min(self.aralar, key=lambda a: a[0])
        self.toplam = sum(a[0] for a in self.aralar)
        self.duzensiz = [a for a in self.aralar if a[0] != 0]
        self.sure = max(k[0] + k[1] for k in self.kareler) - min(k[0] for k in self.kareler)

    def ms(self, ornek):
        return float(ornek) * 1000 / self.hz

    def sn(self, ornek):
        return Fraction(ornek) / self.hz


def ornek_metni(c, ornek, paket=None):
    yer = f", paket {paket}" if paket is not None else ""
    return f"{float(ornek):+.2f} örnek ({c.ms(ornek):+.3f} ms{yer})"


def denetle(argv):
    if not argv:
        sys.stderr.write(__doc__)
        sys.exit(2)
    cikti = argv[0]
    ek = secenekler(argv[1:], bayraklar=("--duzensiz",))
    pay = float(ek.get("--pay", "2"))
    if "--kaynak" not in ek:
        sys.stderr.write("denetle --kaynak ister\n")
        sys.exit(2)
    c, k, kc = Cizgi(cikti), Cizgi(ek["--kaynak"]), Cozulen(ek["--kaynak"])
    print(f"Kaynak: {k.codec} {k.hz} Hz, {len(k.paketler)} paket; çözülen {len(kc.kareler)} karede "
          f"düzensizlik {len(kc.duzensiz)} (toplam {ornek_metni(kc, kc.toplam)})")
    print(f"Çıktı:  {c.codec} {c.hz} Hz, {len(c.paketler)} paket (kare {float(c.kare):g} örnek), "
          f"düzensizlik {len(c.duzensiz)} (toplam {ornek_metni(c, c.toplam)})")
    if ek.get("--duzensiz"):
        chk("Kaynağın ses zaman çizgisi düzensiz (deneme anlamlı)", ">= 1 örnek",
            f"{len(kc.duzensiz)} yerde, en büyük {float(max(abs(a[0]) for a in kc.aralar)):.2f} örnek",
            any(abs(a[0]) >= 1 for a in kc.aralar))
    # Kaynağın kendi boşluğu / üst üste binmesi --pay'dan büyükse (gerçek bir kopukluk) çıktıda da aynısı beklenir:
    # program kaynağın zaman çizgisini korur. Küçükse (tek örneklik düzensizlikler) sınır --pay'dır.
    oran = Fraction(c.hz, kc.hz)
    k_ust, k_alt = kc.en_buyuk[0] * oran, kc.en_kucuk[0] * oran
    ust = pay if k_ust <= pay else float(k_ust) + pay
    alt = -pay if k_alt >= -pay else float(k_alt) - pay
    chk("Ses paketleri arasında boşluk yok", f"<= {ust:g} örnek",
        ornek_metni(c, c.en_buyuk[0], c.en_buyuk[1]), c.en_buyuk[0] <= ust)
    chk("Ses paketleri üst üste binmiyor", f">= {alt:g} örnek",
        ornek_metni(c, c.en_kucuk[0], c.en_kucuk[1]), c.en_kucuk[0] >= alt)
    # Kaynağın düzensizlikleri çıktıya aynen geçer (her biri hız dönüşümünde en çok 1 örnek yuvarlanır); fazlası olmamalı.
    tolerans = c.sn(pay + len(kc.duzensiz))
    fark = c.sn(c.toplam) - kc.sn(kc.toplam)
    chk("Toplam boşluk kaynağınkiyle aynı", f"{float(kc.sn(kc.toplam)) * 1000:+.3f} ms ± {float(tolerans) * 1000:.3f}",
        f"{float(c.sn(c.toplam)) * 1000:+.3f} ms", abs(fark) <= tolerans)
    chk("Ses kaynaktaki yerinde başlıyor", f"{float(k.sn(k.bas)) * 1000:.3f} ms ± {float(c.sn(pay)) * 1000:.3f}",
        f"{float(c.sn(c.bas)) * 1000:.3f} ms", abs(c.sn(c.bas) - k.sn(k.bas)) <= c.sn(pay))
    # Süre: örnek sayısı aynı, zaman çizgisi aynı → bitiş de aynı (hız dönüşümünün uç payı 1 ms). Kaynağın süresi iki
    # yoldan okunur ve çıktı birine uymalıdır: paketlerden (MP4 / MOV'da son karenin kısaltılmış süresi orada yazar)
    # ve çözülen karelerden (Opus'ta sondaki dolgu ancak çözülünce atılır).
    sure_c = c.sn(c.son - c.bas)
    sure_k = min((k.sn(k.son - k.bas), kc.sn(kc.sure)), key=lambda v: abs(sure_c - v))
    chk("Ses süresi kaynağınkiyle aynı", f"{float(sure_k):.4f} sn ± 0.001", f"{float(sure_c):.4f} sn",
        abs(sure_c - sure_k) <= Fraction(1, 1000))
    bitir()


# ---------------------------------------------------------------------------------------------------------------------
# zincir: yeni zincir = eski zincir (düzenli seste), karşı deneme (düzensiz seste)
# ---------------------------------------------------------------------------------------------------------------------

YENI = re.compile(r"apad=pad_len=(\d+),alimiter=limit=([0-9.]+):attack=(\d+):level=0,"
                  r"atrim=start_sample=(\d+),asetpts=PTS-(\d+)")


def ham(kaynak, zincir):
    r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-i", kaynak, "-map", "0:a:0", "-af", zincir,
                        "-c:a", "pcm_f64le", "-f", "f64le", "-"], capture_output=True)
    if r.returncode != 0:
        sys.stderr.write(f"ffmpeg zinciri çalıştıramadı ({zincir}): {r.stderr.decode(errors='replace')[-600:]}\n")
        sys.exit(2)
    return r.stdout


def zincir(argv):
    if not argv:
        sys.stderr.write(__doc__)
        sys.exit(2)
    kaynak = argv[0]
    ek = secenekler(argv[1:])
    pay = float(ek.get("--pay", "2"))
    yeni = ek.get("--zincir", "")
    m = YENI.search(yeni)
    if not m:
        sys.stderr.write(f"zincirde beklenen sınırlayıcı bölümü yok: {yeni}\n")
        sys.exit(2)
    doldurma, tavan, atak, kirpma, geri = m.groups()
    chk("Zincir: doldurma = kırpma = zaman damgası (örnek)", "959 = 959 = 959", f"{doldurma} = {kirpma} = {geri}",
        doldurma == kirpma == geri == "959")
    chk("Zincir: öne bakma (attack, ms)", "5", atak, atak == "5")
    # 0.16.0'ın zinciri: aynı sınırlayıcı, gecikmeyi kendi telafi ediyor.
    eski = yeni[:m.start()] + f"alimiter=limit={tavan}:attack={atak}:level=0:latency=1" + yeni[m.end():]
    print("Yeni zincir: " + yeni)
    print("Eski zincir: " + eski)

    def titresimsiz(z):
        return re.sub(r":dither_method=[a-z_]+", "", z)

    def sinirlayiciya_kadar(z, son):
        return z[:son]

    # 1) Sınırlayıcının çıkışı (192 kHz), 2) zincirin tamamı (çıktının hızında). 64 bit kayan nokta, titreşimsiz.
    for ad, y, e in (
            ("Sınırlayıcının çıkışı (192 kHz)", sinirlayiciya_kadar(yeni, m.end()),
             sinirlayiciya_kadar(eski, m.start() + len(f"alimiter=limit={tavan}:attack={atak}:level=0:latency=1"))),
            ("Zincirin tamamı", titresimsiz(yeni), titresimsiz(eski))):
        a, b = ham(kaynak, y), ham(kaynak, e)
        chk(f"{ad}: örnek sayısı eski zincirle aynı", len(b) // 8, len(a) // 8, len(a) == len(b) and len(a) > 0)
        chk(f"{ad}: örnekler eski zincirle bit bit aynı", hashlib.sha256(b).hexdigest()[:16],
            hashlib.sha256(a).hexdigest()[:16], a == b and len(a) > 0)

    if "--duzensiz-kaynak" in ek:
        dk = ek["--duzensiz-kaynak"]
        kodlayici = ek.get("--kodlayici", "libfdk_aac")
        k = Cizgi(dk)
        chk("Karşı deneme: kaynağın ses zaman çizgisi düzensiz", ">= 1 örnek",
            f"{len(k.duzensiz)} yerde", any(abs(a[0]) >= 1 for a in k.aralar))
        with tempfile.TemporaryDirectory() as gecici:
            sonuc = {}
            for ad, z in (("eski", eski), ("yeni", yeni)):
                yol = os.path.join(gecici, ad + ".mov")
                r = subprocess.run(["ffmpeg", "-v", "error", "-nostdin", "-y", "-i", dk, "-map", "0:a:0", "-af", z,
                                    "-c:a", kodlayici, "-b:a", "128k", yol], capture_output=True)
                if r.returncode != 0:
                    sys.stderr.write(f"ffmpeg kodlayamadı ({ad}): {r.stderr.decode(errors='replace')[-600:]}\n")
                    sys.exit(2)
                sonuc[ad] = Cizgi(yol)
            e, y = sonuc["eski"], sonuc["yeni"]
            # Sınırlayıcının gecikmesi 959 örnek (192 kHz) = 4,995 ms: çıktının hızında ~220 (44,1 kHz) / ~240 (48 kHz).
            beklenen = Fraction(959, 192000) * e.hz
            chk("Karşı deneme: eski zincir bu kaynakta boşluk bırakıyor (hata görülüyor)",
                f">= {float(beklenen) - 8:.0f} örnek", ornek_metni(e, e.en_buyuk[0], e.en_buyuk[1]),
                e.en_buyuk[0] >= beklenen - 8)
            chk("Karşı deneme: yeni zincir boşluk bırakmıyor", f"<= {pay:g} örnek",
                ornek_metni(y, y.en_buyuk[0], y.en_buyuk[1]), y.en_buyuk[0] <= pay)
            tolerans = y.sn(pay + len(k.duzensiz))
            chk("Karşı deneme: yeni zincirde toplam boşluk kaynağınkiyle aynı",
                f"{float(k.sn(k.toplam)) * 1000:+.3f} ms ± {float(tolerans) * 1000:.3f}",
                f"{float(y.sn(y.toplam)) * 1000:+.3f} ms", abs(y.sn(y.toplam) - k.sn(k.toplam)) <= tolerans)
    bitir()


if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in ("duzensiz", "denetle", "zincir"):
        sys.stderr.write(__doc__)
        sys.exit(2)
    {"duzensiz": duzensiz, "denetle": denetle, "zincir": zincir}[sys.argv[1]](sys.argv[2:])
