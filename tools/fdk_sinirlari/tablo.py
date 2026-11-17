"""sinirlar.c çıktısından FdkUyumu.cs tablosunu yazar.
Kullanım: for a in 2 5 29 23 39; do ./sinirlar $a > out_$a.txt; ./sinirlar $a ust > ust_$a.txt; done; python3 -I tablo.py ."""
import collections
import os
import sys

klasor = sys.argv[1] if len(sys.argv) > 1 else '.'
ad = {2: 'aac_low', 5: 'aac_he', 29: 'aac_he_v2', 23: 'aac_ld', 39: 'aac_eld'}


def oku(onek):
    veri = collections.defaultdict(dict)
    for a in ad:
        with open(os.path.join(klasor, f'{onek}_{a}.txt'), encoding='ascii') as f:
            for satir in f:
                p = satir.split()
                veri[(int(p[0]), int(p[1]), int(p[2]))][p[3]] = p[4]
    return veri


def aralik(d):
    """(en az, en çok) gerçek bit hızı (bit/s); FDK bu birleşimi hiç açmıyorsa (0, 0). Doğrulanır: en az ile en çok
    arasındaki her kb/s değerini FDK aynen kullanır (program isteği bu aralığa çekip öyle verir); açılan her birleşimde
    VBR de açılır."""
    iyi = {int(b): int(v) for b, v in d.items() if not b.startswith('vbr') and not v.startswith('H')}
    vbr = [not v.startswith('H') for b, v in d.items() if b.startswith('vbr')]
    if not iyi:
        assert not any(vbr)
        return (0, 0)
    assert all(vbr)
    lo, hi = min(iyi.values()), max(iyi.values())
    hatali = [int(b) for b, v in d.items() if not b.startswith('vbr') and v.startswith('H')]
    assert not any(-(-lo // 1000) <= b <= hi // 1000 for b in hatali), hatali
    for b, g in iyi.items():
        if -(-lo // 1000) <= b <= hi // 1000:
            assert g == b * 1000, (b, g)
        else:
            assert g <= lo + 1000 if b * 1000 < lo else g == hi, (b, g, lo, hi)
    return (lo, hi)


ana, ust = oku('out'), oku('ust')
hizlar = [8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000]
for a in ad:
    for hz in hizlar:
        c = [aralik(ana[(a, hz, ch)]) for ch in range(1, 9)] + [aralik(ust[(a, hz, 8)])]
        print(f'        [("{ad[a]}", {hz})] = [{", ".join(f"({x}, {y})" for x, y in c)}],')
