"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın ürettiği komutlar bu derlemeyle (x264 core 165) yeniden çalıştırılır, ayarlar tek tek değiştirilerek hangi
ayarın hataya yol açtığı bulunur. Programın parçası değildir.
Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü>

3. tur: geçmeli (MBAFF) + kısıtlı intra tahmin birleşiminde çözülemeyen akış hangi koşulda çıkıyor; TikTok
varsayılanının 4K'da ne kadar bellek kullandığı.
"""
import ctypes
import os
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
KAYNAK = sys.argv[2]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)

RENK = ['-color_range', 'tv', '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709']


def bellek():
    if os.name != 'nt':
        return 'bilinmiyor'

    class Durum(ctypes.Structure):
        _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong),
                    ('ullAvailPhys', ctypes.c_ulonglong), ('ullTotalPageFile', ctypes.c_ulonglong),
                    ('ullAvailPageFile', ctypes.c_ulonglong), ('ullTotalVirtual', ctypes.c_ulonglong),
                    ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]

    d = Durum()
    d.dwLength = ctypes.sizeof(Durum)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(d))
    g = 1024 ** 3
    return (f'fiziksel {d.ullTotalPhys / g:.1f} GB (boş {d.ullAvailPhys / g:.1f}), '
            f'sayfalama dahil {d.ullTotalPageFile / g:.1f} GB (boş {d.ullAvailPageFile / g:.1f}), işlemci {os.cpu_count()}')


def calistir(args, bayrak=()):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'level+info', *bayrak] + args,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode, r.stderr


def onemli(stderr):
    return [x.strip() for x in stderr.splitlines()
            if '[error]' in x or '[fatal]' in x or 'malloc' in x.lower() or 'bench:' in x][:10]


def coz(out):
    # Kombinasyon denemesinin çözme denetimiyle aynı (kombinasyon.py Araclar.coz).
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-v', 'error', '-i', out, '-map', '0', '-f', 'null', '-'],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    satir = [x for x in r.stderr.splitlines() if x.strip()]
    if r.returncode != 0:
        satir.append(f'çözücü çıkış kodu {r.returncode}')
    return satir


def vf(boy):
    return (f'scale={boy}:flags=bicubic+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:'
            'out_color_matrix=bt709,setsar=1,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709')


def dene(ad, giris, filtre, preset, params, ek=('-profile:v', 'high'), bayrak=()):
    out = os.path.join(CIKTI, ad + '.mov')
    rc, err = calistir([*giris, '-map', '0:v:0', '-vf', filtre, '-c:v', 'libx264', '-preset', preset, *ek,
                        '-x264-params', params, *RENK, '-an', '-movflags', '+faststart', out], bayrak)
    if rc != 0:
        return False, f'KODLANAMADI (kod {rc}): {" | ".join(onemli(err))[:700]}'
    h = coz(out)
    bench = [x for x in onemli(err) if 'bench:' in x]
    ek_bilgi = f' [{bench[-1]}]' if bench else ''
    try:
        os.remove(out)
    except OSError:
        pass
    return (not h), ('çözüldü' if not h else f'ÇÖZÜLEMEDİ: {" | ".join(h[:2])[:240]}') + ek_bilgi


def kaynak(dosya):
    return ['-i', os.path.join(KAYNAK, dosya)]


print('Bellek:', bellek())
print()
print('=== 1) v07 720x576, medium, CRF 23, High (2. turda çözülemedi): hangi ayar gerekli')
V07 = kaynak('v07_gecmeli_576i.mp4')
for ad, params in [
    ('ikisi', 'threads=1:interlaced=1:constrained-intra=1:crf=23'),
    ('yalniz_gecmeli', 'threads=1:interlaced=1:crf=23'),
    ('yalniz_kisitli', 'threads=1:constrained-intra=1:crf=23'),
    ('ikisi_b0', 'threads=1:interlaced=1:constrained-intra=1:crf=23:bframes=0'),
    ('ikisi_tff', 'threads=1:interlaced=1:tff=1:constrained-intra=1:crf=23'),
    ('ikisi_bff', 'threads=1:interlaced=1:bff=1:constrained-intra=1:crf=23'),
    ('ikisi_8x8dct0', 'threads=1:interlaced=1:constrained-intra=1:crf=23:8x8dct=0'),
    ('ikisi_ref1', 'threads=1:interlaced=1:constrained-intra=1:crf=23:ref=1'),
    ('ikisi_threads4', 'threads=4:interlaced=1:constrained-intra=1:crf=23'),
    ('ikisi_weightp0', 'threads=1:interlaced=1:constrained-intra=1:crf=23:weightp=0'),
    ('ikisi_cabac0', 'threads=1:interlaced=1:constrained-intra=1:crf=23:cabac=0'),
]:
    print(f'  {ad}:', dene('t1_' + ad, V07, vf('720:576'), 'medium', params)[1])

KAYNAKLAR = ['v01_h264_320x180_30.mp4', 'v03_vp9_sd_etiketsiz.webm', 'v07_gecmeli_576i.mp4', 'v09_anamorfik_sar.mp4']
BOYUTLAR = ['1920:1080', '1280:720', '720:576', '640:480']


def tarama(baslik, ek_params, presetler, crfler):
    toplam = hatali = 0
    hatalar = []
    for k in KAYNAKLAR:
        for boy in BOYUTLAR:
            for preset in presetler:
                for crf in crfler:
                    ok, s = dene('t2', kaynak(k), vf(boy), preset, f'{ek_params}:crf={crf}')
                    toplam += 1
                    if not ok:
                        hatali += 1
                        hatalar.append(f'{k[:3]} {boy} {preset} crf {crf}: {s[:160]}')
    print(f'  {baslik}: {toplam} kodlama, {hatali} sorunlu')
    for h in hatalar[:12]:
        print('     ', h)


print()
print('=== 2) Taramalar (4 kaynak × 4 boyut × 2 hız ayarı × 2 CRF)')
P4 = ['veryfast', 'medium']
tarama('yalnızca geçmeli', 'interlaced=1', P4, [18, 30])
tarama('yalnızca kısıtlı intra', 'constrained-intra=1', P4, [18, 30])
tarama('geçmeli + kısıtlı intra', 'interlaced=1:constrained-intra=1', P4, [18, 30])
tarama('geçmeli + kısıtlı intra + B-kare 0', 'interlaced=1:constrained-intra=1:bframes=0', P4, [18, 30])

print()
print('=== 3) TikTok varsayılanı (veryslow, CRF 24, ref 16, önden bakış 250, threads 8) 4K 60 fps, 330 kare: bellek')
print('Bellek:', bellek())
TT = 'threads=8:ref=16:keyint=600:rc-lookahead=250:crf=24'
for boy in ('3840x2160', '2160x3840'):
    giris = ['-f', 'lavfi', '-i', f'testsrc2=size={boy}:rate=60,noise=alls=12:allf=t', '-frames:v', '330']
    print(f'  {boy}:', dene('t3_' + boy, giris, 'format=yuv420p', 'veryslow', TT, ('-profile:v', 'high', '-level:v', '5.2'),
                            ['-benchmark'])[1])
print('Bellek:', bellek())
