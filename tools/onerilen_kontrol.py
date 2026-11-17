"""Önerilen ayarla kodlanmış dosya, örnek videonun ölçülen kodlamasıyla aynı mı (H.264 başlıkları, kare türleri,
anahtar kare aralığı, bit hızı, ses).

Kullanım: python -X utf8 -I tools/onerilen_kontrol.py <dosya> <38m|1717504691091> <kaynak kare hızı>
"""
import json
import re
import subprocess
import sys

dosya, ad, fps = sys.argv[1], sys.argv[2], float(sys.argv[3])
fark = 0
toplam = 0


def chk(baslik, beklenen, cikti, ok):
    global fark, toplam
    toplam += 1
    if not ok:
        fark += 1
    print(f"{'OK  ' if ok else 'FARK'}  {baslik:<58} beklenen: {beklenen:<22} çıktı: {cikti}")


def calistir(args):
    return subprocess.run(args, capture_output=True, text=True, encoding='utf-8', errors='replace', stdin=subprocess.DEVNULL)


BEK = {
    '38m': dict(profil=100, refs=2, cabac=1, dct=1, wp=0, wb=0, b=True, gop=round(fps), bit=12000, vbv=16000,
                kap='qt', ses=('LC', 44100, 2, 128000)),
    '1717504691091': dict(profil=77, refs=3, cabac=1, dct=0, wp=0, wb=0, b=False, gop=30, bit=52000, vbv=62000,
                          kap='isom', ses=('LC', 48000, 2, 512000)),
}[ad]

# ---- H.264 başlıkları ve kare türleri (trace_headers)
r = calistir(['ffmpeg', '-hide_banner', '-loglevel', 'trace', '-i', dosya, '-map', '0:v:0', '-c', 'copy',
              '-bsf:v', 'trace_headers', '-f', 'null', '-'])
alan = re.compile(r'\]\s+\d+\s+(\S+)\s+[01]+\s+=\s+(-?\d+)\s*$')
bolum, sps, pps, dilimler, nal = None, {}, {}, [], []
for satir in r.stderr.splitlines():
    if 'trace_headers' not in satir:
        continue
    m = alan.search(satir)
    if not m:
        metin = satir.split(']', 1)[-1].strip()
        if metin.startswith('Sequence Parameter Set'):
            bolum = 'sps'
        elif metin.startswith('Picture Parameter Set'):
            bolum = 'pps'
        elif metin.startswith('Slice Header'):
            bolum = 'dilim'
            dilimler.append({})
        elif metin.startswith(('Supplemental Enhancement', 'Access Unit Delimiter')):
            bolum = 'diger'
        continue
    k, v = m.group(1), int(m.group(2))
    if k == 'nal_unit_type' and v in (1, 5):
        nal.append(v)
    if bolum == 'sps':
        sps.setdefault(k, v)
    elif bolum == 'pps':
        pps.setdefault(k, v)
    elif bolum == 'dilim' and dilimler:
        dilimler[-1].setdefault(k, v)

tur = {0: 'P', 1: 'B', 2: 'I', 5: 'P', 6: 'B', 7: 'I'}
kareler = ''.join(tur.get(d.get('slice_type', -1), '?') for d in dilimler if d.get('first_mb_in_slice', 0) == 0)
idr = [i for i, t in enumerate(nal) if t == 5]
chk('Profil (profile_idc)', BEK['profil'], sps.get('profile_idc'), sps.get('profile_idc') == BEK['profil'])
chk('Referans kare (max_num_ref_frames)', BEK['refs'], sps.get('max_num_ref_frames'), sps.get('max_num_ref_frames') == BEK['refs'])
chk('CABAC', BEK['cabac'], pps.get('entropy_coding_mode_flag'), pps.get('entropy_coding_mode_flag') == BEK['cabac'])
dct = pps.get('transform_8x8_mode_flag', 0)
chk('8x8 DCT', BEK['dct'], dct, dct == BEK['dct'])
chk('Ağırlıklı tahmin (P)', BEK['wp'], pps.get('weighted_pred_flag'), pps.get('weighted_pred_flag') == BEK['wp'])
chk('Ağırlıklı tahmin (B)', BEK['wb'], pps.get('weighted_bipred_idc'), pps.get('weighted_bipred_idc') == BEK['wb'])
chk('Kare yapısı', 'IPBPB...' if BEK['b'] else 'yalnızca I / P', kareler[:24] + '...',
    ('BB' not in kareler and 'PP' not in kareler.replace('IP', 'I').replace('PI', 'I') and kareler.count('B') > 0)
    if BEK['b'] else ('B' not in kareler and len(kareler) > 0))
araliklar = [b - a for a, b in zip(idr, idr[1:])]
chk('Anahtar kare aralığı (kare)', BEK['gop'], sorted(set(araliklar)), bool(araliklar) and set(araliklar) == {BEK['gop']})
chk('Anahtar kareler IDR (kapalı GOP), arada I yok', len(idr), kareler.count('I'), kareler.count('I') == len(idr))

# ---- bit hızı: ortalama ve en yüksek 1 saniye
pk = calistir(['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_entries', 'packet=pts_time,size', '-of', 'json', dosya])
paketler = [(float(p['pts_time']), int(p['size'])) for p in json.loads(pk.stdout)['packets'] if p.get('pts_time') not in (None, 'N/A')]
sure = len(paketler) / fps
ort = sum(s for _, s in paketler) * 8 / sure / 1000
chk('Ortalama görüntü bit hızı (kb/s, ±5%)', BEK['bit'], round(ort), abs(ort - BEK['bit']) <= 0.05 * BEK['bit'])
paketler.sort()
pencere = max(1, round(fps))
en = 0
boyutlar = [s for _, s in paketler]
for i in range(0, max(1, len(boyutlar) - pencere + 1)):
    en = max(en, sum(boyutlar[i:i + pencere]) * 8 / 1000)
# VBV'nin söylediği: her 1 saniyede en çok (en yüksek bit hızı × 1 sn + tampon); tampon = en yüksek bit hızı (1 sn).
chk('En yüksek 1 saniye (kb/s; VBV: bit hızı + tampon)', f"<= {2 * BEK['vbv']}", round(en), en <= 2 * BEK['vbv'] * 1.02)

# ---- kap ve ses
pr = json.loads(calistir(['ffprobe', '-v', 'error', '-show_format', '-show_streams', '-of', 'json', dosya]).stdout)
marka = pr['format'].get('tags', {}).get('major_brand', '').strip()
chk('Kap', BEK['kap'], marka, marka == BEK['kap'])
a = [s for s in pr['streams'] if s['codec_type'] == 'audio']
if not a:
    chk('Ses', 'AAC', 'yok', False)
else:
    a = a[0]
    p, hz, ch, br = BEK['ses']
    chk('Ses (AAC profili, örnekleme, kanal)', f'{p} {hz} {ch}', f"{a.get('profile')} {a.get('sample_rate')} {a.get('channels')}",
        a.get('profile') == p and int(a.get('sample_rate', 0)) == hz and int(a.get('channels', 0)) == ch)
    gercek = int(a.get('bit_rate') or 0)
    chk('Ses bit hızı (±5%)', br, gercek, abs(gercek - br) <= 0.05 * br)

print(f'\nÖnerilen ayar ({ad}): {toplam} kontrol, {fark} fark.')
sys.exit(1 if fark else 0)
