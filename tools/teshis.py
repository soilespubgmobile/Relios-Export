"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın parçası değildir. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü> <Smoke exe>

7. tur:
1) Ölçekleyici: bir boyutta büyük küçültme + öbüründe büyütme (ör. 16×8192). Ölçekleyici × kaynak × küçültme × büyütme
   taraması; programın yeni kuralıyla (küçültme 8 kattan büyükse önce Area ile hedefin 8 katına) düşen kalıyor mu.
2) video402: x264'ün erişim ihlaliyle düştüğü komut (geçmeli, 4:2:2, JVT, QP 1, kroma -12...), ayarlar tek tek kaldırılarak.
"""
import json
import math
import os
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
KOMB = sys.argv[2]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)
manifest = {k['ad']: k for k in json.load(open(os.path.join(KOMB, 'manifest.json'), encoding='utf-8'))}


def kaynak(ad):
    return os.path.join(KOMB, manifest[ad]['dosya'])


def calistir(args):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'error'] + args,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode, r.stderr


CARPAN = {'spline': 20, 'lanczos': 6, 'bicubic': 4, 'bilinear': 2, 'area': 1, 'neighbor': 0}
RENK = 'in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709'


def ara(kw, kh, hw, hh, olcek):
    """Yeni kural (esnetme): küçültme 8 kattan büyükse (Bilinear ve üstü) ya da Area'da süzgeç 250'yi aşarsa önce Area ile
    hedefin 8 katına (her adımda en çok 200 kat)."""
    f = CARPAN[olcek]

    def gerek(k, hd):
        if f == 0 or k <= hd:
            return False
        return k / hd > 8 if f >= 2 else 1 + math.ceil(f * k / hd) >= 250

    def cift(x):
        return max(2, math.ceil(x / 2) * 2)

    def tek(k, hd):
        if not gerek(k, hd):
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


# Kaynaklar: 320x180, 640x360, 1920x1080 (lavfi, 2 kare)
KAYNAKLAR = []
for boy in ('320x180', '640x360', '1920x1080'):
    yol = os.path.join(CIKTI, f'k_{boy}.mkv')
    calistir(['-f', 'lavfi', '-i', f'testsrc2=size={boy}:rate=25', '-frames:v', '2', '-pix_fmt', 'yuv420p', '-c:v', 'ffv1', yol])
    w, h = map(int, boy.split('x'))
    KAYNAKLAR.append((boy, yol, w, h))

print('=== 1) Ölçekleyici: bir boyutta küçültme, öbüründe büyütme')
eski_hata, yeni_hata, toplam = [], [], 0
for kad, yol, kw, kh in KAYNAKLAR:
    for olcek in CARPAN:
        for kucult in (6, 8, 12, 16, 32):
            for buyut in (1, 8, 32):
                for yon in ('yatay', 'dikey'):
                    if yon == 'yatay':
                        hw, hh = max(16, round(kw / kucult) // 2 * 2), min(8192, kh * buyut)
                    else:
                        hw, hh = min(8192, kw * buyut), max(16, round(kh / kucult) // 2 * 2)
                    son = f'scale={hw}:{hh}:flags={olcek}+accurate_rnd+full_chroma_int:{RENK},setsar=1,format=yuv420p'
                    rc, err = calistir(['-i', yol, '-frames:v', '1', '-vf', son, '-f', 'null', '-'])
                    adim = ''.join(f'scale={a}:{b}:flags=area+accurate_rnd+full_chroma_int,' for a, b in ara(kw, kh, hw, hh, olcek))
                    rc2, err2 = calistir(['-i', yol, '-frames:v', '1', '-vf', adim + son, '-f', 'null', '-'])
                    toplam += 1
                    durum = f'{kad} → {hw}x{hh} {olcek}'
                    if rc != 0:
                        eski_hata.append(durum)
                    if rc2 != 0:
                        yeni_hata.append(durum + f' [{adim[:60]}]')
print(f'  {toplam} ölçekleme: doğrudan {len(eski_hata)} hata, yeni kuralla {len(yeni_hata)} hata')
for x in eski_hata:
    print('   doğrudan düştü:', x)
for x in yeni_hata:
    print('   yeni kuralla düştü:', x)

print()
print('=== 2) video402: x264 erişim ihlali')
P = ('threads=1:ref=1:no-deblock=1:me=dia:subme=4:mixed-refs=0:merange=16:chroma-me=0:trellis=2:cqm=jvt:deadzone-inter=0:'
     'fast-pskip=0:nr=0:dct-decimate=1:interlaced=1:bluray-compat=1:constrained-intra=0:bframes=0:keyint=1798:scenecut=100:'
     'intra-refresh=0:rc-lookahead=250:mbtree=0:qp=1:qcomp=0.00:qpmax=33:qpstep=50:vbv-bufsize=400000:nal-hrd=cbr:filler=0:'
     'ipratio=10.00:aq-mode=2:aq-strength=3.00:b-adapt=2:direct=temporal:cabac=0:psy=0:pbratio=10.00:chroma-qp-offset=-12:'
     'cplxblur=0.00:qblur=0.00:vbv-init=0.00:pic-struct=1')
VF = ('fps=60000/1001,scale=1440:2560:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=bicubic+accurate_rnd+'
      'full_chroma_int:in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709,pad=1440:2560:-1:-1:color=black,setsar=1,'
      'format=yuv422p')
EK = ['-preset', 'superfast', '-tune', 'zerolatency', '-profile:v', 'high422', '-level:v', '2.1']


def dene(params, vf=VF, ek=EK, kad='v12'):
    rc, err = calistir(['-i', kaynak(kad), '-map', '0:v:0', '-vf', vf, '-c:v', 'libx264', *ek, '-x264-params', params,
                        os.path.join(CIKTI, 't402.mov')])
    return 'çalıştı' if rc == 0 else f'DÜŞTÜ (kod {rc}) {err.strip().splitlines()[-1][:120] if err.strip() else ""}'


print('  aynısı:', dene(P))
print('  aynısı (ikinci kez):', dene(P))
anahtarlar = []
for p in P.split(':'):
    a = p.split('=')[0]
    if a not in anahtarlar:
        anahtarlar.append(a)
for a in anahtarlar:
    print(f'  {a} yok:', dene(':'.join(x for x in P.split(':') if x.split('=')[0] != a)))
print('  profil high (4:2:0):', dene(P, VF.replace('format=yuv422p', 'format=yuv420p'),
                                   ['-preset', 'superfast', '-tune', 'zerolatency', '-profile:v', 'high', '-level:v', '2.1']))
print('  kaynak v01:', dene(P, kad='v01'))
print('  boyut 320x180:', dene(P, 'scale=320:180,format=yuv422p'))
for qp in (2, 4, 8, 12, 20):
    print(f'  qp={qp}:', dene(P.replace('qp=1:', f'qp={qp}:')))
for ofset in (-6, -3, 0, 12):
    print(f'  chroma-qp-offset={ofset}:', dene(P.replace('chroma-qp-offset=-12', f'chroma-qp-offset={ofset}')))
