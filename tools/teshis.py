"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın parçası değildir. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü> <Smoke exe>

6. tur: çok dar / çok yassı hedefte (16×8192, 8192×16) ölçekleyicinin açılmaması ("Failed initializing scaling graph"):
kombinasyon denemesindeki 6 komut birebir, ayarlar tek tek değiştirilerek; programın çözümü (önce Area ile hedefin 8
katına) bütün kaynak × hedef × ölçekleyici × piksel biçimiyle.
"""
import json
import math
import os
import re
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
FP = os.path.join(sys.argv[1], 'ffprobe.exe' if os.name == 'nt' else 'ffprobe')
KOMB = sys.argv[2]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)
manifest = {k['ad']: k for k in json.load(open(os.path.join(KOMB, 'manifest.json'), encoding='utf-8'))}


def kaynak(ad):
    return os.path.join(KOMB, manifest[ad]['dosya'])


def boyut(yol):
    r = subprocess.run([FP, '-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height',
                        '-of', 'csv=p=0', yol], capture_output=True, text=True)
    w, h = r.stdout.strip().split(',')[:2]
    return int(w), int(h)


def dene(yol, vf):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'error', '-i', yol, '-frames:v', '2', '-an',
                        '-vf', vf, '-f', 'null', '-'], capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode == 0, r.stderr.strip().splitlines()[-1][:160] if r.stderr.strip() else ''


CARPAN = {'spline': 20, 'lanczos': 6, 'bicubic': 4, 'bilinear': 2, 'area': 1, 'neighbor': 0}


def ara(kw, kh, hw, hh, olcek, korunur):
    """ArgsBuilder.AraOlcekler ile aynı."""
    f = CARPAN[olcek]

    def asar(k, hd):
        return k > hd and hd > 0 and min(1 + (f * k + hd - 1) // hd, k - 2) >= 250

    def cift(x):
        return max(2, math.ceil(x / 2) * 2)

    if f == 0 or not (asar(kw, hw) or asar(kh, hh)):
        return []
    if korunur:
        oran = max(kw / hw, kh / hh)
        kalan, a, adim = oran / 8, 1.0, []
        while kalan > 1:
            bu = min(kalan, 200)
            a *= bu
            kalan /= bu
            adim.append((cift(kw / a), cift(kh / a)))
        return adim

    def tek(k, hd):
        if not asar(k, hd):
            return []
        kalan, a, liste = k / hd / 8, 1.0, []
        while kalan > 1:
            bu = min(kalan, 200)
            a *= bu
            kalan /= bu
            liste.append(cift(k / a))
        return liste

    ws, hs = tek(kw, hw), tek(kh, hh)
    return [(ws[i] if i < len(ws) else (ws[-1] if ws else kw), hs[i] if i < len(hs) else (hs[-1] if hs else kh))
            for i in range(max(len(ws), len(hs)))]


def basamakli(vf, kw, kh):
    """Programın zinciri: ölçeklemeden önce Area ara ölçekleri."""
    m = re.search(r'scale=(\d+):(\d+):flags=(\w+)(\+accurate_rnd\+full_chroma_int)?', vf)
    hw, hh, olcek, hassas = int(m.group(1)), int(m.group(2)), m.group(3), m.group(4) or ''
    on = ''.join(f'scale={a}:{b}:flags=area{hassas},' for a, b in ara(kw, kh, hw, hh, olcek, False))
    return vf.replace('scale=', on + 'scale=', 1) if 'fps=' not in vf.split(',')[0] else \
        vf.split(',', 1)[0] + ',' + on + vf.split(',', 1)[1]


DURUMLAR = [
    ('video149', 'v11', 'fps=50,scale=16:8192:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p10le'),
    ('video230', 'v04', 'fps=30,scale=16:8192:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=auto:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p'),
    ('video074', 'v06', 'fps=24000/1001,scale=16:8192:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv422p'),
    ('video047', 'v09', 'fps=60000/1001,scale=8192:16:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p'),
    ('video316', 'v05', 'scale=16:8192:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p'),
    ('video356', 'v03', 'fps=30,scale=8192:16:flags=spline+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p'),
]
print('=== 1) Kombinasyon denemesindeki 6 komut ve değişiklikleri')
for ad, kad, vf in DURUMLAR:
    yol = kaynak(kad)
    kw, kh = boyut(yol)
    print(f'--- {ad} ({kad} {kw}x{kh})')
    print('  aynısı:', dene(yol, vf))
    for olcek in ('bicubic', 'lanczos', 'bilinear', 'area', 'neighbor'):
        print(f'  {olcek}:', dene(yol, vf.replace('flags=spline', 'flags=' + olcek)))
    print('  renk dönüşümü yok:', dene(yol, re.sub(r':in_color_matrix=\w+:out_range=tv:out_color_matrix=bt709', '', vf)))
    print('  hassas yuvarlama yok:', dene(yol, vf.replace('+accurate_rnd+full_chroma_int', '')))
    b = basamakli(vf, kw, kh)
    print('  programın çözümü:', dene(yol, b), b.split(',scale=')[0][:80])

print()
print('=== 2) Programın çözümü: kaynak × hedef × ölçekleyici × piksel biçimi')
toplam = hatali = 0
for kad in ('v01', 'v04', 'v05', 'v06', 'v09', 'v11'):
    yol = kaynak(kad)
    kw, kh = boyut(yol)
    for hw, hh in ((16, 8192), (8192, 16), (16, 1080), (1920, 16), (16, 16), (8192, 8192)):
        for olcek in CARPAN:
            for bicim in ('yuv420p', 'yuv420p10le', 'yuv422p', 'yuv444p'):
                if (hw, hh) == (8192, 8192) and (olcek not in ('spline', 'area') or bicim not in ('yuv420p', 'yuv444p')):
                    continue
                vf = (f'scale={hw}:{hh}:flags={olcek}+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:'
                      f'out_color_matrix=bt709,setsar=1,format={bicim}')
                b = basamakli(vf, kw, kh)
                ok, hata = dene(yol, b)
                toplam += 1
                if not ok:
                    hatali += 1
                    if hatali <= 10:
                        print(f'  düştü: {kad} {kw}x{kh} → {hw}x{hh} {olcek} {bicim}: {hata} :: {b[:140]}')
print(f'  programın çözümüyle {toplam} ölçekleme, {hatali} hata')
