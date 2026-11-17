"""Kombinasyon denemesinde yalnızca GitHub'daki (Windows) FFmpeg derlemesinde görülen hataların teşhisi.

Programın ürettiği komutlar bu derlemeyle (x264 core 165) yeniden çalıştırılır, ayarlar tek tek değiştirilerek
hangi ayarın hataya yol açtığı bulunur. Kullanım: python -I tools/teshis.py <ffmpeg klasörü> <kaynak klasörü>
"""
import json
import os
import re
import subprocess
import sys

FF = os.path.join(sys.argv[1], 'ffmpeg.exe' if os.name == 'nt' else 'ffmpeg')
KAYNAK = sys.argv[2]
CIKTI = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'teshis_cikti')
os.makedirs(CIKTI, exist_ok=True)


def calistir(args):
    r = subprocess.run([FF, '-hide_banner', '-nostdin', '-y', '-loglevel', 'level+info'] + args,
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode, r.stderr


def onemli(stderr):
    return [x for x in stderr.splitlines() if '[error]' in x or '[fatal]' in x or 'malloc' in x.lower()
            or ('[warning]' in x and 'libx264' in x)][:12]


def kodla_coz(ad, vf, params, ek=(), kaynak='v03_vp9_sd_etiketsiz.webm'):
    out = os.path.join(CIKTI, ad + '.mkv')
    rc, err = calistir(['-i', os.path.join(KAYNAK, kaynak), '-an', '-vf', vf, '-c:v', 'libx264', *ek,
                        '-x264-params', params, out])
    if rc != 0:
        return f'KODLANAMADI: {" | ".join(onemli(err))[:600]}'
    rc2, err2 = calistir(['-i', out, '-f', 'null', '-'])
    hata = [x for x in err2.splitlines() if '[error]' in x]
    return 'çözüldü' if rc2 == 0 and not hata else f'ÇÖZÜLEMEDİ ({len(hata)} hata): {hata[0][:200] if hata else rc2}'


print('=== 1) 1920x16 geçmeli + kısıtlı intra (video386): hangi ayar çözülemeyen akış üretiyor')
temel = ('threads=1:ref=1:deblock=0,0:me=umh:subme=11:psy-rd=5.00,0.15:mixed-refs=0:merange=4:trellis=2:8x8dct=0:'
         'deadzone-inter=32:fast-pskip=0:dct-decimate=1:interlaced=1:bluray-compat=0:constrained-intra=1:bframes=1:weightp=1:'
         'scenecut=40:intra-refresh=0:rc-lookahead=250:mbtree=1:qp=1:qcomp=0.60:qpmin=51:qpstep=4:vbv-maxrate=34375:'
         'vbv-bufsize=400000:nal-hrd=vbr:filler=0:ipratio=1.00:aq-mode=3:aq-strength=1.00:lookahead-threads=4:b-bias=100:'
         'b-adapt=2:weightb=0:slices=1:partitions=i4x4,i8x8:psy=0:pbratio=1.30:chroma-qp-offset=-12:qblur=99.00:aud=1:open-gop=1:ref=2')
ek = ['-preset', 'slower', '-profile:v', 'main', '-level:v', '5.1']
vf = 'scale=1920:16:flags=bicubic,format=yuv420p'
print('tam komut:', kodla_coz('t386', vf, temel, ek))
degisiklikler = [('interlaced=1', 'interlaced=0'), ('constrained-intra=1', 'constrained-intra=0'), ('bframes=1', 'bframes=0'),
                 ('weightp=1', 'weightp=0'), ('partitions=i4x4,i8x8', 'partitions=all'), ('subme=11', 'subme=7'),
                 ('qp=1', 'qp=20'), ('mbtree=1', 'mbtree=0'), ('aq-mode=3', 'aq-mode=0'), ('trellis=2', 'trellis=0'),
                 ('open-gop=1', 'open-gop=0'), ('aud=1', 'aud=0'), ('ref=2', 'ref=1'), ('me=umh', 'me=hex'),
                 ('psy-rd=5.00,0.15', 'psy-rd=1.00,0.00'), ('dct-decimate=1', 'dct-decimate=0')]
for eski, yeni in degisiklikler:
    print(f'  {eski} → {yeni}:', kodla_coz('t386_' + re.sub(r'\W', '_', yeni), vf, temel.replace(eski, yeni), ek))
for boy in ('1920:32', '1920:48', '1920:64', '320:16', '640:16', '1920:20'):
    print(f'  boyut {boy}:', kodla_coz('t386_' + boy.replace(':', 'x'), f'scale={boy}:flags=bicubic,format=yuv420p', temel, ek))
for prof in ('high', 'baseline'):
    print(f'  profil {prof}:', kodla_coz('t386_' + prof, vf, temel, ['-preset', 'slower', '-profile:v', prof]))
sade = 'threads=1:interlaced=1:constrained-intra=1'
print('  yalnızca interlaced=1 + constrained-intra=1 (medium):', kodla_coz('t386_sade', vf, sade, ['-profile:v', 'high']))
print('  yalnızca interlaced=1 (medium):', kodla_coz('t386_sade2', vf, 'threads=1:interlaced=1', ['-profile:v', 'high']))
print('  1920x16 ilerleyen + constrained-intra (medium):', kodla_coz('t386_sade3', vf, 'threads=1:constrained-intra=1', ['-profile:v', 'high']))
for kaynak in ('v01_h264_320x180_30.mp4', 'v07_gecmeli_576i.mp4'):
    print(f'  kaynak {kaynak}:', kodla_coz('t386_k' + kaynak[:3], vf, temel, ek, kaynak))

print()
print('=== 2) 4K + threads=128 (video217, video032): kodlayıcının açılmama nedeni')
for ad, kaynak, vf2, params, ek2 in [
    ('t217', 'v09_anamorfik_sar.mp4', 'fps=30000/1001,scale=2160:3840:flags=bilinear,format=yuv420p',
     'threads=128:ref=1:keyint=10:min-keyint=1:crf=1:vbv-maxrate=34375:vbv-bufsize=400000:lookahead-threads=4:cabac=0',
     ['-preset', 'slow', '-tune', 'animation', '-profile:v', 'baseline', '-level:v', '5.2']),
    ('t032', 'v07_gecmeli_576i.mp4', 'fps=30000/1001,scale=2160:3840:flags=bilinear,format=yuv420p10le',
     'threads=128:interlaced=1:constrained-intra=1:intra-refresh=1:crf=1:slices=4:cabac=0',
     ['-preset', 'ultrafast', '-profile:v', 'high10', '-level:v', '2.1']),
]:
    for th in ('128', '64', '32', '16', '8'):
        print(f'  {ad} threads={th}:', kodla_coz(f'{ad}_{th}', vf2, params.replace('threads=128', f'threads={th}'), ek2, kaynak))
