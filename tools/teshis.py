"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın parçası değildir. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü> <Smoke exe>

8. tur: ölçekleyici (bir boyutta küçültme, öbüründe büyütme). Yaklaşımlar:
  D: doğrudan (eskisi)
  A: Area ara ölçeği (küçültme 8 kattan büyükse hedefin 8 katına)
  B: iki adım: önce küçülen boyut(lar) seçilen ölçekleyiciyle hedefe (büyüyen boyut kaynakta kalır), sonra büyütme
  C: B + küçültmede süzgeç 250 dokunuşu aşarsa önce Area ile hedefin 8 katına
"""
import math
import os
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)


def calistir(args):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'error'] + args,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode, r.stderr


CARPAN = {'spline': 20, 'lanczos': 6, 'bicubic': 4, 'bilinear': 2, 'area': 1, 'neighbor': 0}
RENK = 'in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709'
H = '+accurate_rnd+full_chroma_int'


def cift(x):
    return max(2, math.ceil(x / 2) * 2)


def area_adimlari(k, hd, f):
    """Küçültmede süzgeç 250 dokunuşu aşarsa Area ile hedefin 8 katına (her adımda en çok 200 kat)."""
    if f == 0 or k <= hd or min(1 + math.ceil(f * k / hd), k - 2) < 250:
        return []
    kalan, a, liste = k / hd / 8, 1.0, []
    while kalan > 1:
        bu = min(kalan, 200)
        a *= bu
        kalan /= bu
        liste.append(cift(k / a))
    return liste


def zincir(yaklasim, kw, kh, hw, hh, olcek):
    son = f'scale={hw}:{hh}:flags={olcek}{H}:{RENK},setsar=1,format=yuv420p'
    f = CARPAN[olcek]
    if yaklasim == 'D':
        return son
    if yaklasim == 'A':
        def tek(k, hd):
            if f == 0 or k <= hd:
                return []
            gerek = k / hd > 8 if f >= 6 else min(1 + math.ceil(f * k / hd), k - 2) >= 250
            if not gerek:
                return []
            kalan, a, liste = k / hd / 8, 1.0, []
            while kalan > 1:
                bu = min(kalan, 200)
                a *= bu
                kalan /= bu
                liste.append(cift(k / a))
            return liste
        ws, hs = tek(kw, hw), tek(kh, hh)
        adim = [(ws[i] if i < len(ws) else (ws[-1] if ws else kw), hs[i] if i < len(hs) else (hs[-1] if hs else kh))
                for i in range(max(len(ws), len(hs)))]
        return ''.join(f'scale={a}:{b}:flags=area{H},' for a, b in adim) + son
    # B / C: küçülen boyut(lar) önce hedefe (büyüyen boyut kaynakta), sonra son ölçek
    kucul_w, kucul_h = hw < kw, hh < kh
    buyu_w, buyu_h = hw > kw, hh > kh
    on = ''
    if (kucul_w or kucul_h) and (buyu_w or buyu_h):
        aw, ah = (hw if kucul_w else kw), (hh if kucul_h else kh)
        if yaklasim == 'C':
            ws, hs = (area_adimlari(kw, hw, f) if kucul_w else []), (area_adimlari(kh, hh, f) if kucul_h else [])
            for i in range(max(len(ws), len(hs))):
                on += f'scale={ws[i] if i < len(ws) else (ws[-1] if ws else kw)}:{hs[i] if i < len(hs) else (hs[-1] if hs else kh)}:flags=area{H},'
        on += f'scale={aw}:{ah}:flags={olcek}{H},'
    elif yaklasim == 'C':
        ws, hs = area_adimlari(kw, hw, f), area_adimlari(kh, hh, f)
        for i in range(max(len(ws), len(hs))):
            on += f'scale={ws[i] if i < len(ws) else (ws[-1] if ws else kw)}:{hs[i] if i < len(hs) else (hs[-1] if hs else kh)}:flags=area{H},'
    return on + son


KAYNAKLAR = []
for boy in ('320x180', '640x360', '1920x1080'):
    yol = os.path.join(CIKTI, f'k_{boy}.mkv')
    calistir(['-f', 'lavfi', '-i', f'testsrc2=size={boy}:rate=25', '-frames:v', '1', '-pix_fmt', 'yuv420p', '-c:v', 'ffv1', yol])
    w, h = map(int, boy.split('x'))
    KAYNAKLAR.append((boy, yol, w, h))

hatalar = {y: [] for y in 'DABC'}
toplam = 0
for kad, yol, kw, kh in KAYNAKLAR:
    for olcek in CARPAN:
        for kucult in (4, 8, 16, 64):
            for buyut in (1, 4, 16, 32):
                for yon in ('yatay', 'dikey'):
                    if yon == 'yatay':
                        hw, hh = max(16, round(kw / kucult) // 2 * 2), min(8192, kh * buyut)
                    else:
                        hw, hh = min(8192, kw * buyut), max(16, round(kh / kucult) // 2 * 2)
                    toplam += 1
                    for y in 'DABC':
                        rc, err = calistir(['-i', yol, '-frames:v', '1', '-vf', zincir(y, kw, kh, hw, hh, olcek), '-f', 'null', '-'])
                        if rc != 0:
                            hatalar[y].append(f'{kad} → {hw}x{hh} {olcek}')
print(f'=== Ölçekleyici: {toplam} hedef × 4 yaklaşım')
for y in 'DABC':
    print(f'  {y}: {len(hatalar[y])} hata')
    for x in hatalar[y][:40]:
        print('     ', x)
# Büyük kaynak: 3840x2160 ve 7680x4320 → 16 piksel (Area adımı gerekiyor mu)
for boy in ('3840x2160', '7680x4320'):
    yol = os.path.join(CIKTI, f'k_{boy}.mkv')
    calistir(['-f', 'lavfi', '-i', f'testsrc2=size={boy}:rate=25', '-frames:v', '1', '-pix_fmt', 'yuv420p', '-c:v', 'ffv1', yol])
    kw, kh = map(int, boy.split('x'))
    for olcek in CARPAN:
        for hw, hh in ((16, 16), (16, kh), (kw, 16), (16, 8192), (8192, 16)):
            sonuc = {y: calistir(['-i', yol, '-frames:v', '1', '-vf', zincir(y, kw, kh, hw, hh, olcek), '-f', 'null', '-'])[0] == 0
                     for y in 'DABC'}
            if not all(sonuc.values()):
                print(f'  {boy} → {hw}x{hh} {olcek}: ' + ' '.join(f'{y}={"+" if v else "DÜŞTÜ"}' for y, v in sonuc.items()))
print('  büyük kaynak taraması bitti')
