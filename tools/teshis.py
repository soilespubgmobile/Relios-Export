"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın parçası değildir. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü> <Smoke exe>

4. tur: kombinasyon denemesinde (commit cfdb633) program hata verdiren 16 deneme programın kendi koduyla (Smoke)
yeniden çalıştırılır, hata günlüğünün tamamından nedeni yazdırılır; x264'ün geçmeli (MBAFF) kodlamada çok iş
parçacığıyla düştüğü "Assertion failed: a->l0.me16x16.mv[1] <= h->mb.mv_max_spel[1]" durumu boyut × iş parçacığı ×
arama aralığıyla taranır.
"""
import json
import os
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
KOMB = sys.argv[2]
SMOKE = sys.argv[3]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)

ONEMLI = ('x264 [', 'libx264 @', 'Assertion', 'malloc', 'rror', 'failed', 'Invalid', 'not supported', 'must be',
          'require', 'exceed', 'cannot', 'Cannot', 'unable', 'Unable', 'kod -')
GURULTU = ('Task finished', 'Terminating thread', 'Nothing was written', 'Error sending frames', 'Conversion failed',
           'Could not open encoder before EOF', 'Error while opening encoder', 'Error while filtering')


def onemli(satirlar, n=16):
    return [x.strip() for x in satirlar if any(o in x for o in ONEMLI) and not any(g in x for g in GURULTU)][:n]


durumlar = {d['ad']: d for d in json.load(open(os.path.join(KOMB, 'durumlar.json'), encoding='utf-8'))}
manifest = {k['ad']: k for k in json.load(open(os.path.join(KOMB, 'manifest.json'), encoding='utf-8'))}
ADLAR = ['video149', 'video413', 'video022', 'video118', 'video230', 'video074', 'video170', 'video402', 'video047',
         'video431', 'video004', 'video316', 'video356', 'video187', 'video211', 'video227']

print('=== 1) Program hata verdiren denemeler (programın kendi koduyla; hata günlüğünün tamamından)')
for ad in ADLAR:
    d = durumlar.get(ad)
    if d is None:
        print(f'--- {ad}: durumlar.json\'da yok')
        continue
    k = manifest[d['kaynak']]
    cikti = os.path.join(CIKTI, ad)
    os.makedirs(cikti, exist_ok=True)
    komut = [SMOKE, os.path.join(KOMB, k['dosya']), cikti, '--profil', d['profil']]
    for a in d['ayarla']:
        komut += ['--ayarla', a]
    try:
        r = subprocess.run(komut, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=1500)
        cikis = (r.stdout + r.stderr).splitlines()
        rc = r.returncode
    except subprocess.TimeoutExpired:
        cikis, rc = [], 'zaman aşımı'
    print(f'--- {ad} [{d["kaynak"]}] çıkış {rc}')
    for x in cikis:
        if x.startswith(('HATA', 'NOT=', 'Video: ', 'Komut ')):
            print('   ', x[:420])
    gunluk = next((x.split('Günlük: ', 1)[1].strip() for x in cikis if 'Günlük: ' in x), None)
    if gunluk and os.path.isfile(gunluk):
        with open(gunluk, encoding='utf-8-sig', errors='replace') as f:
            for x in onemli(f.read().splitlines()):
                print('    | ' + x[:300])
    elif rc != 0:
        for x in onemli(cikis):
            print('    > ' + x[:300])

print()
print('=== 2) Geçmeli (MBAFF) + çerçeve iş parçacıkları: x264 düşüyor mu (boyut × iş parçacığı × arama aralığı)')
KAYNAK = os.path.join(KOMB, manifest['v07']['dosya'])
toplam = dusen = 0
tablo = {}
for boy in ('1920:1080', '1280:720', '720:576', '1920:64', '640:32'):
    for th in (2, 4, 8, 16, 32, 64, 128):
        for merange in (4, 16):
            for me in ('hex', 'umh'):
                out = os.path.join(CIKTI, 'mbaff.mkv')
                r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'error', '-i', KAYNAK, '-an',
                                    '-vf', f'scale={boy},format=yuv420p', '-c:v', 'libx264', '-preset', 'veryfast',
                                    '-x264-params', f'threads={th}:interlaced=1:merange={merange}:me={me}:crf=23', out],
                                   capture_output=True, text=True, encoding='utf-8', errors='replace')
                toplam += 1
                anahtar = (boy, th, merange)
                tablo.setdefault(anahtar, [0, 0])
                tablo[anahtar][0] += 1
                if r.returncode != 0:
                    dusen += 1
                    tablo[anahtar][1] += 1
                    satir = [x for x in r.stderr.splitlines() if x.strip()]
                    ilk = next((x for x in satir if 'Assertion' in x), satir[-1] if satir else '')
                    if dusen <= 6:
                        print(f'  düştü: {boy} threads={th} merange={merange} me={me} kod {r.returncode}: {ilk[:200]}')
print(f'  {toplam} kodlama, {dusen} düştü')
for (boy, th, merange), (n, h) in sorted(tablo.items()):
    if h:
        print(f'  {boy} threads={th} merange={merange}: {h}/{n}')
