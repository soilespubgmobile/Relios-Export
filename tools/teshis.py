"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın ürettiği komutlar (günlükteki hâliyle) bu derlemeyle (x264 core 165) yeniden çalıştırılır, ayarlar tek tek
değiştirilerek hangi ayarın hataya yol açtığı bulunur. Programın parçası değildir.
Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü>
"""
import ctypes
import os
import re
import subprocess
import sys
import threading

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
            if '[error]' in x or '[fatal]' in x or 'malloc' in x.lower() or 'bench:' in x
            or ('[warning]' in x and 'libx264' in x)][:14]


def coz(out, bayrak=()):
    # Kombinasyon denemesinin çözme denetimiyle aynı (kombinasyon.py Araclar.coz).
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-v', 'error', *bayrak, '-i', out, '-map', '0', '-f', 'null', '-'],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    satir = [x for x in r.stderr.splitlines() if x.strip()]
    if r.returncode != 0:
        satir.append(f'çözücü çıkış kodu {r.returncode}')
    return satir


def x264_komutu(kaynak, vf, preset, ek, params, out):
    return ['-i', os.path.join(KAYNAK, kaynak), '-map', '0:v:0', '-map_metadata', '-1', '-map_chapters', '-1',
            '-vf', vf, '-c:v', 'libx264', '-preset', preset, *ek, '-x264-params', params, *RENK,
            '-metadata:s:v:0', 'language=eng', '-an', '-movflags', '+faststart', out]


def dene(ad, kaynak, vf, preset, ek, params, bayrak=()):
    out = os.path.join(CIKTI, ad + '.mov')
    rc, err = calistir(x264_komutu(kaynak, vf, preset, ek, params, out), bayrak)
    if rc != 0:
        return f'KODLANAMADI (kod {rc}): {" | ".join(onemli(err))[:900]}'
    h = coz(out)
    bench = [x for x in onemli(err) if 'bench:' in x]
    ek_bilgi = f' [{bench[-1]}]' if bench else ''
    if not h:
        return 'çözüldü' + ek_bilgi
    tek = coz(out, ['-threads', '1'])
    return f'ÇÖZÜLEMEDİ ({len(h)} satır; tek iş parçacıklı çözücüyle {len(tek)} satır): {" | ".join(h[:2])[:300]}' + ek_bilgi


def parcalar(params):
    return params.split(':')


def cikar(params, anahtar):
    return ':'.join(p for p in params.split(':') if p.split('=')[0] != anahtar)


print('Bellek:', bellek())
print()

# ------------------------------------------------------------------------------------------------ 1) video386
print('=== 1) video386: 1920x16 geçmeli + kısıtlı intra, Main, QP 1 (günlükteki komut)')
P386 = ('threads=1:ref=1:deblock=0,0:me=umh:subme=11:psy-rd=5.00,0.15:mixed-refs=0:merange=4:trellis=2:8x8dct=0:'
        'deadzone-inter=32:fast-pskip=0:dct-decimate=1:interlaced=1:bluray-compat=0:constrained-intra=1:bframes=1:weightp=1:'
        'scenecut=40:intra-refresh=0:rc-lookahead=250:mbtree=1:qp=1:qcomp=0.60:qpmin=51:qpstep=4:vbv-maxrate=34375:'
        'vbv-bufsize=400000:nal-hrd=vbr:filler=0:ipratio=1.00:aq-mode=3:aq-strength=1.00:lookahead-threads=4:b-bias=100:'
        'b-adapt=2:weightb=0:slices=1:partitions=i4x4,i8x8:psy=0:pbratio=1.30:chroma-qp-offset=-12:qblur=99.00:aud=1:'
        'open-gop=1:ref=2')
VF386 = ('scale=1920:16:flags=bicubic+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:'
         'out_color_matrix=bt709,setsar=1,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709')
K386 = 'v03_vp9_sd_etiketsiz.webm'
E386 = ['-profile:v', 'main', '-level:v', '5.1']
sonuc = dene('t386', K386, VF386, 'slower', E386, P386)
print('günlükteki komut:', sonuc)
print('aynı komut ikinci kez:', dene('t386_b', K386, VF386, 'slower', E386, P386))
if sonuc != 'çözüldü' and not sonuc.startswith('çözüldü'):
    print('  -- her ayar tek tek kaldırılınca (x264 / hız ayarının değeri):')
    anahtarlar = []
    for p in parcalar(P386):
        a = p.split('=')[0]
        if a not in anahtarlar and a != 'threads':
            anahtarlar.append(a)
    for a in anahtarlar:
        print(f'  {a} yok:', dene('t386_' + re.sub(r'\W', '_', a), K386, VF386, 'slower', E386, cikar(P386, a)))
    for preset in ('medium', 'veryslow'):
        print(f'  hız ayarı {preset}:', dene('t386_p' + preset, K386, VF386, preset, E386, P386))
    print('  profil high:', dene('t386_high', K386, VF386, 'slower', ['-profile:v', 'high', '-level:v', '5.1'], P386))
for boy in ('1920:32', '1920:48', '1920:64', '1920:1080', '1280:16', '640:16', '640:360', '1280:720'):
    print(f'  boyut {boy}:', dene('t386_' + boy.replace(':', 'x'), K386, VF386.replace('1920:16', boy), 'slower', E386, P386))

# Geçmeli + kısıtlı intra birleşimi genel olarak güvenli mi: farklı kaynak, boyut, hız ayarı, QP ile.
print('  -- geçmeli + kısıtlı intra, olağan ayarlar:')
for kaynak in ('v01_h264_320x180_30.mp4', 'v03_vp9_sd_etiketsiz.webm', 'v07_gecmeli_576i.mp4', 'v09_anamorfik_sar.mp4'):
    for boy in ('1920:1080', '1280:720', '720:576'):
        for preset, crf in (('medium', 23), ('slower', 1), ('veryfast', 30)):
            vf = VF386.replace('1920:16', boy)
            params = f'threads=1:interlaced=1:constrained-intra=1:crf={crf}'
            print(f'  {kaynak[:3]} {boy} {preset} crf {crf}:',
                  dene(f't386g_{kaynak[:3]}_{boy.replace(":", "x")}_{preset}', kaynak, vf, preset, ['-profile:v', 'high'], params))

# ------------------------------------------------------------------------------------------------ 2) 4K + threads=128
print()
print('=== 2) video217 / video032: 2160x3840, threads=128 (günlükteki komutlar)')
K217 = 'v09_anamorfik_sar.mp4'
VF217 = ('fps=30000/1001,scale=2160:3840:flags=bilinear+accurate_rnd+full_chroma_int:in_color_matrix=bt601:out_range=tv:'
         'out_color_matrix=bt709,setsar=1,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709')
E217 = ['-tune', 'animation', '-profile:v', 'baseline', '-level:v', '5.2']
P217 = ('threads=128:ref=1:no-deblock=1:me=hex:subme=8:mixed-refs=1:merange=64:chroma-me=1:trellis=0:8x8dct=0:fast-pskip=0:'
        'nr=0:dct-decimate=1:interlaced=0:bluray-compat=1:constrained-intra=0:weightp=0:keyint=10:min-keyint=1:scenecut=100:'
        'intra-refresh=0:mbtree=0:crf=1:qcomp=1.00:qpmax=69:vbv-maxrate=34375:vbv-bufsize=400000:filler=0:ipratio=10.00:'
        'aq-mode=3:aq-strength=0.00:lookahead-threads=4:b-bias=-90:direct=none:weightb=0:cabac=0:partitions=none:'
        'chroma-qp-offset=-12:cplxblur=999.00:qblur=0.50:vbv-init=0.50:pic-struct=1:open-gop=1:ref=2')
K032 = 'v07_gecmeli_576i.mp4'
VF032 = ('fps=30000/1001,crop=324:576:198:0,scale=2160:3840:flags=bilinear+accurate_rnd+full_chroma_int:in_color_matrix=bt601:'
         'out_range=tv:out_color_matrix=bt709,setsar=1,format=yuv420p10le,setparams=color_primaries=bt709:color_trc=bt709')
E032 = ['-tune', 'fastdecode', '-profile:v', 'high10', '-level:v', '2.1']
P032 = ('threads=128:ref=1:no-deblock=1:me=umh:subme=3:chroma-me=1:deadzone-inter=21:deadzone-intra=32:fast-pskip=1:nr=0:'
        'interlaced=1:bluray-compat=0:constrained-intra=1:weightp=2:keyint=10:min-keyint=6:scenecut=40:intra-refresh=1:'
        'mbtree=1:crf=1:qcomp=0.60:qpmin=10:qpmax=33:qpstep=50:vbv-maxrate=100:vbv-bufsize=400000:nal-hrd=none:filler=0:'
        'ipratio=10.00:aq-mode=3:b-adapt=0:b-pyramid=strict:direct=none:weightb=0:slices=4:cabac=0:partitions=none:'
        'pbratio=1.30:chroma-qp-offset=12:cplxblur=999.00:qblur=0.50:vbv-init=1.00:ref=2')
B = ['-benchmark']
print('Bellek:', bellek())
print('  t217 tek başına:', dene('t217', K217, VF217, 'slow', E217, P217, B))
print('  t032 tek başına:', dene('t032', K032, VF032, 'ultrafast', E032, P032, B))
for th in ('64', '32', '16'):
    print(f'  t217 threads={th}:', dene(f't217_{th}', K217, VF217, 'slow', E217, P217.replace('threads=128', 'threads=' + th), B))
    print(f'  t032 threads={th}:', dene(f't032_{th}', K032, VF032, 'ultrafast', E032, P032.replace('threads=128', 'threads=' + th), B))

print('  -- ikisi aynı anda (kombinasyon denemesi gibi, --paralel 2):')
sonuclar = {}


def is_(ad, *a):
    sonuclar[ad] = dene(ad, *a)


iler = [threading.Thread(target=is_, args=('t217_p', K217, VF217, 'slow', E217, P217, B)),
        threading.Thread(target=is_, args=('t032_p', K032, VF032, 'ultrafast', E032, P032, B))]
for t in iler:
    t.start()
for t in iler:
    t.join()
for ad, s in sonuclar.items():
    print(f'  {ad}:', s)
print('Bellek:', bellek())
