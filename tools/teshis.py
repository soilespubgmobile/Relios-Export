"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın parçası değildir. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü> <Smoke exe>

5. tur:
1) Ölçekleyici: çok küçük hedefte (16–96 piksel) hangi ölçekleme × oran "Failed initializing scaling graph" veriyor;
   önce Area ile 8 katına küçültüp son adımı seçilen ölçekleyiciyle yapmak (programın çözümü) her durumda çalışıyor mu.
2) Geçmeli (MBAFF): x264'ün "Assertion failed: a->l0.me16x16.mv[1] <= h->mb.mv_max_spel[1]" düşmesi hangi arama
   aralığında (merange) başlıyor; 16 ve üstü her iş parçacığı sayısında güvenli mi.
3) İlerleme çubuğu: TikTok varsayılanıyla (x264 veryslow, önden bakış 250, 8 B-kare) ffmpeg'in -progress çıktısı
   zamana göre (kullanıcı: "%85–90'da duruyor, sonra birden bitiyor").
"""
import json
import math
import os
import re
import subprocess
import sys
import time

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
KOMB = sys.argv[2]
SMOKE = sys.argv[3]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)
manifest = {k['ad']: k for k in json.load(open(os.path.join(KOMB, 'manifest.json'), encoding='utf-8'))}


def kaynak(ad):
    return os.path.join(KOMB, manifest[ad]['dosya'])


def calistir(args, zaman=600):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'error'] + args,
                       capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=zaman)
    return r.returncode, r.stderr


# ------------------------------------------------------------------------------------------------ 1) ölçekleyici
print('=== 1) Ölçekleyici: çok küçük hedef')
# Büyük kaynaklar: 1920x1080 ve 3840x2160 (lavfi, 1 kare)
for ad, boy in (('b1080', '1920x1080'), ('b2160', '3840x2160')):
    calistir(['-f', 'lavfi', '-i', f'testsrc2=size={boy}:rate=25', '-frames:v', '2', '-pix_fmt', 'yuv420p',
              '-c:v', 'ffv1', os.path.join(CIKTI, ad + '.mkv')])
KAYNAKLAR = [('v03', kaynak('v03'), 352, 288), ('v09', kaynak('v09'), 720, 576),
             ('b1080', os.path.join(CIKTI, 'b1080.mkv'), 1920, 1080), ('b2160', os.path.join(CIKTI, 'b2160.mkv'), 3840, 2160)]
CARPAN = {'lanczos': 6, 'spline': 20, 'bicubic': 4, 'bilinear': 2, 'area': 1, 'neighbor': 0}
RENK = 'in_color_matrix=bt601:out_range=tv:out_color_matrix=bt709'
toplam = hatali = uyusmayan = 0
for kad, kyol, kw, kh in KAYNAKLAR:
    for olcek, f in CARPAN.items():
        for kucuk in (16, 24, 32, 48, 64, 96):
            for yon in ('dar', 'yassi'):
                w, h = (kucuk, 1080) if yon == 'dar' else (1920, kucuk)
                for hassas in (True, False):
                    flags = olcek + ('+accurate_rnd+full_chroma_int' if hassas else '')
                    vf = f'scale={w}:{h}:flags={flags}:{RENK},setsar=1,format=yuv420p'
                    rc, err = calistir(['-i', kyol, '-frames:v', '1', '-vf', vf, '-f', 'null', '-'])
                    toplam += 1
                    # beklenen: swscale süzgeci (1 + çarpan × ceil(oran), kaynak - 2 ile sınırlı) 256'yı aşınca düşer
                    def boy_(src, dst):
                        if f == 0 or src <= dst:
                            return 0
                        return min(1 + (f * src + dst - 1) // dst, src - 2)
                    tahmin = max(boy_(kw, w), boy_(kh, h)) >= 256
                    if rc != 0:
                        hatali += 1
                    if (rc != 0) != tahmin:
                        uyusmayan += 1
                        if uyusmayan <= 12:
                            print(f'  tahmin tutmadı: {kad} {olcek} {"hassas" if hassas else "-"} {w}x{h}: kod {rc}, tahmin '
                                  f'{"düşer" if tahmin else "çalışır"} (süzgeç {boy_(kw, w)} / {boy_(kh, h)}) {err.strip()[:120]}')
print(f'  {toplam} ölçekleme, {hatali} hata, tahminle uyuşmayan {uyusmayan}')

# Programın çözümü: önce Area ile hedefin 8 katına (oran 200'ü aşarsa iki adım), son adım seçilen ölçekleyiciyle.
def ara_boy(src, dst, f):
    if src <= dst or 1 + f * src / dst <= 250:
        return []
    hedef = dst * 8
    adimlar, cur = [], src
    while cur / hedef > 200:
        cur = math.ceil(cur / 200)
        cur += cur % 2
        adimlar.append(cur)
    adimlar.append(hedef + hedef % 2)
    return adimlar


toplam = hatali = 0
for kad, kyol, kw, kh in KAYNAKLAR:
    for olcek, f in CARPAN.items():
        for kucuk in (16, 24, 32, 48, 64, 96):
            for yon in ('dar', 'yassi'):
                w, h = (kucuk, 1080) if yon == 'dar' else (1920, kucuk)
                aw, ah = ara_boy(kw, w, max(f, 1)), ara_boy(kh, h, max(f, 1))
                zincir = []
                for i in range(max(len(aw), len(ah))):
                    zw = aw[i] if i < len(aw) else (aw[-1] if aw else 'iw')
                    zh = ah[i] if i < len(ah) else (ah[-1] if ah else 'ih')
                    zincir.append(f'scale={zw}:{zh}:flags=area+accurate_rnd+full_chroma_int')
                vf = ','.join(zincir + [f'scale={w}:{h}:flags={olcek}+accurate_rnd+full_chroma_int:{RENK},setsar=1,format=yuv420p'])
                rc, err = calistir(['-i', kyol, '-frames:v', '1', '-vf', vf, '-f', 'null', '-'])
                toplam += 1
                if rc != 0:
                    hatali += 1
                    if hatali <= 6:
                        print(f'  basamaklı da düştü: {kad} {olcek} {w}x{h}: {vf[:160]} :: {err.strip()[:160]}')
print(f'  basamaklı (Area + seçilen): {toplam} ölçekleme, {hatali} hata')

# ------------------------------------------------------------------------------------------------ 2) MBAFF
print()
print('=== 2) Geçmeli (MBAFF): arama aralığı × iş parçacığı × boyut')
tablo = {}
toplam = dusen = 0
for kad in ('v07', 'v09'):
    for boy in ('1920:1080', '720:576', '1920:64'):
        for th in (4, 16, 64, 128):
            for merange in (4, 8, 12, 16):
                for me in ('hex', 'umh'):
                    rc, err = calistir(['-i', kaynak(kad), '-an', '-vf', f'scale={boy},format=yuv420p', '-c:v', 'libx264',
                                        '-preset', 'veryfast', '-x264-params',
                                        f'threads={th}:interlaced=1:merange={merange}:me={me}:crf=23',
                                        os.path.join(CIKTI, 'mbaff.mkv')])
                    toplam += 1
                    tablo.setdefault(merange, [0, 0])
                    tablo[merange][0] += 1
                    if rc != 0:
                        dusen += 1
                        tablo[merange][1] += 1
for kad in ('v07', 'v09'):
    for boy in ('1920:1080', '720:576', '1920:64'):
        for th in (4, 16, 64, 128):
            for preset in ('medium', 'slower'):
                for me in ('hex', 'umh'):
                    rc, err = calistir(['-i', kaynak(kad), '-an', '-vf', f'scale={boy},format=yuv420p', '-c:v', 'libx264',
                                        '-preset', preset, '-x264-params', f'threads={th}:interlaced=1:me={me}:crf=23',
                                        os.path.join(CIKTI, 'mbaff.mkv')])
                    toplam += 1
                    tablo.setdefault(f'16 ({preset})', [0, 0])
                    tablo[f'16 ({preset})'][0] += 1
                    if rc != 0:
                        dusen += 1
                        tablo[f'16 ({preset})'][1] += 1
print(f'  {toplam} kodlama, {dusen} düştü')
for k, (n, d) in tablo.items():
    print(f'  merange {k}: {d}/{n} düştü')

# ------------------------------------------------------------------------------------------------ 3) ilerleme
print()
print('=== 3) İlerleme çubuğu: TikTok varsayılanı, 1080x1920 60 fps 12 sn (ses: AAC), -progress zamana göre')
kaynak_yolu = os.path.join(CIKTI, 'ilerleme_kaynak.mp4')
calistir(['-f', 'lavfi', '-i', 'testsrc2=size=1080x1920:rate=60,noise=alls=10:allf=t', '-f', 'lavfi',
          '-i', 'sine=frequency=440:sample_rate=48000', '-t', '12', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '10',
          '-c:a', 'aac', '-shortest', kaynak_yolu], 900)
r = subprocess.run([SMOKE, kaynak_yolu, os.path.join(CIKTI, 'ilerleme'), '--profil', 'tiktok', '--sadece-komut'],
                   capture_output=True, text=True, encoding='utf-8', errors='replace')
komut = next((x.split('Komut 1/1: ffmpeg ', 1)[1] for x in r.stdout.splitlines() if 'Komut 1/1: ffmpeg ' in x), None)
if not komut:
    print('  komut alınamadı:', r.stdout[-400:], r.stderr[-400:])
else:
    args = [a.replace('KAYNAK', kaynak_yolu).replace('CIKTI.mov', os.path.join(CIKTI, 'ilerleme.mov')) for a in komut.split(' ')]
    args = [a for a in args if a not in ('-hide_banner', '-nostdin', '-y')]
    print('  komut:', ' '.join(args)[:600])
    sure = 12.0
    p = subprocess.Popen([FF, '-hide_banner', '-nostdin', '-y'] + args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                         text=True, encoding='utf-8', errors='replace')
    basla = time.time()
    kayit = []
    blok = {}
    for satir in p.stdout:
        satir = satir.strip()
        if '=' not in satir:
            continue
        k, v = satir.split('=', 1)
        blok[k] = v
        if k == 'progress':
            us = blok.get('out_time_us', 'N/A')
            kayit.append((time.time() - basla, int(us) / 1e6 if us.lstrip('-').isdigit() else None, blok.get('frame'), v))
            blok = {}
    p.wait()
    son = time.time() - basla
    print(f'  toplam {son:.1f} sn, çıkış {p.returncode}, {len(kayit)} güncelleme')
    onceki = -1
    for t, o, fr, durum in kayit:
        yuzde = None if o is None else 100 * o / sure
        if yuzde is None or int(yuzde / 5) != onceki or durum == 'end':
            print(f'  {t:7.1f} sn  ilerleme %{yuzde if yuzde is None else round(yuzde, 1)}  kare {fr}  {durum}')
            onceki = -1 if yuzde is None else int(yuzde / 5)
    # en uzun duraklama
    en_uzun, nerede = 0, None
    for (t1, o1, _, _), (t2, o2, _, _) in zip(kayit, kayit[1:]):
        if o1 is not None and o2 is not None and o2 - o1 < 0.01 and t2 - t1 > 0:
            pass
    duran = [(t, o) for t, o, _, _ in kayit if o is not None]
    i = 0
    while i < len(duran):
        j = i
        while j + 1 < len(duran) and duran[j + 1][1] - duran[i][1] < 0.05:
            j += 1
        if duran[j][0] - duran[i][0] > en_uzun:
            en_uzun, nerede = duran[j][0] - duran[i][0], duran[i][1]
        i = j + 1
    print(f'  en uzun duraklama: {en_uzun:.1f} sn, %{(100 * nerede / sure) if nerede is not None else 0:.1f}\'de')
