"""H.264 akışının bütün başlık alanlarını özetler: SPS / PPS'in ilk değerleri, SEI türleri, kare türleri (çözme sırası,
n = referans değil, D = IDR), IDR aralıkları, dilim başlığı alanlarının kare türüne göre dağılımı (JSON).

Kullanım: python3 -I tools/h264_basliklar.py <dosya> [--kare N]
Önerilen ayarların örnek videolarla karşılaştırılmasında kullanıldı (0.13.1; onerilen_nvenc_kontrol.py da kullanır).
"""
import json, re, subprocess, sys
from collections import Counter, defaultdict

dosya = sys.argv[1]
kare = sys.argv[sys.argv.index("--kare") + 1] if "--kare" in sys.argv else "100000"
# Dolgu verisi (NAL 12; sabit bit hızında kart boşluğu bununla doldurur) başlık değil: her baytı ayrı satır yazdırır ve
# özeti dakikalarca yavaşlatır (CapCut örneğinde ve CBR çıktısında akışın ~%27'si). İzden önce çıkarılır.
r = subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "trace", "-i", dosya, "-map", "0:v:0", "-c", "copy",
                    "-bsf:v", "filter_units=remove_types=12,trace_headers", "-frames:v", kare, "-f", "null", "-"],
                   capture_output=True, text=True, encoding="utf-8", errors="replace", stdin=subprocess.DEVNULL)
alan = re.compile(r"\]\s+\d+\s+(\S+)\s+[01]+\s+=\s+(-?\d+)\s*$")
bolum = None
sps_list, pps_list, sei_list, dilimler, nal = [], [], [], [], []
cur = None
for satir in r.stderr.splitlines():
    if "trace_headers" not in satir:
        continue
    m = alan.search(satir)
    if not m:
        metin = satir.split("]", 1)[-1].strip()
        for bas, ad in (("Sequence Parameter Set", "sps"), ("Picture Parameter Set", "pps"), ("Slice Header", "dilim"),
                        ("Supplemental Enhancement Information", "sei"), ("Access Unit Delimiter", "aud")):
            if metin.startswith(bas):
                bolum = ad
                cur = {}
                {"sps": sps_list, "pps": pps_list, "dilim": dilimler, "sei": sei_list}.get(ad, []).append(cur)
        continue
    ad, deger = m.group(1), int(m.group(2))
    if ad in ("nal_unit_type", "nal_ref_idc", "forbidden_zero_bit"):
        if ad == "forbidden_zero_bit":
            nal.append({})
        nal[-1][ad] = deger if nal else None
        continue
    if cur is not None:
        if ad in cur:
            k = 2
            while f"{ad}#{k}" in cur:
                k += 1
            ad = f"{ad}#{k}"
        cur[ad] = deger

# dilimleri NAL'larla eşle (yalnızca 1/5)
dilim_nal = [n for n in nal if n.get("nal_unit_type") in (1, 5)]
for d, n in zip(dilimler, dilim_nal):
    d["_nut"] = n.get("nal_unit_type"); d["_ref_idc"] = n.get("nal_ref_idc")
tur = {0: "P", 1: "B", 2: "I", 5: "P", 6: "B", 7: "I"}
kare_dilim = []
for d in dilimler:
    if d.get("first_mb_in_slice", 0) == 0:
        kare_dilim.append([])
    if kare_dilim:
        kare_dilim[-1].append(d)
kareler = []
for ds in kare_dilim:
    t = tur.get(ds[0].get("slice_type"), "?")
    if ds[0]["_nut"] == 5:
        t = "IDR"
    kareler.append((t, ds[0].get("_ref_idc"), len(ds)))
ozet = defaultdict(lambda: defaultdict(Counter))
ilginc = ["num_ref_idx_active_override_flag", "num_ref_idx_l0_active_minus1", "num_ref_idx_l1_active_minus1",
          "direct_spatial_mv_pred_flag", "cabac_init_idc", "disable_deblocking_filter_idc", "slice_alpha_c0_offset_div2",
          "slice_beta_offset_div2", "ref_pic_list_modification_flag_l0", "ref_pic_list_modification_flag_l1",
          "adaptive_ref_pic_marking_mode_flag", "long_term_reference_flag", "no_output_of_prior_pics_flag",
          "_ref_idc", "slice_qp_delta", "luma_log2_weight_denom"]
for d in dilimler:
    t = tur.get(d.get("slice_type"), "?") + ("(IDR)" if d.get("_nut") == 5 else "")
    for k in ilginc:
        ozet[t][k][d.get(k, "-")] += 1
idr = [i for i, k in enumerate(kareler) if k[0] == "IDR"]
inon = [i for i, k in enumerate(kareler) if k[0] == "I"]
desen = "".join({"IDR": "D"}.get(k[0], k[0]) + ("" if k[1] else "n") for k in kareler[:120])
sei_tur = Counter()
for s in sei_list:
    for k, v in s.items():
        if k.startswith("last_payload_type_byte"):
            sei_tur[v] += 1
print(json.dumps({
    "sps_sayisi": len(sps_list), "pps_sayisi": len(pps_list), "farkli_sps": len({json.dumps(s, sort_keys=True) for s in sps_list}),
    "farkli_pps": len({json.dumps(s, sort_keys=True) for s in pps_list}),
    "sps": sps_list[0] if sps_list else None, "pps": pps_list[0] if pps_list else None,
    "sei_turleri": dict(sei_tur), "ilk_sei": sei_list[:3],
    "kare_sayisi": len(kareler), "dilim_kare": dict(Counter(k[2] for k in kareler)),
    "desen_ilk120 (n=referans degil, D=IDR)": desen,
    "idr": idr[:20], "idr_araliklari": dict(Counter(b - a for a, b in zip(idr, idr[1:]))),
    "idr_olmayan_I": inon[:20],
    "tur_sayilari": dict(Counter(k[0] for k in kareler)),
    "dilim_alanlari": {t: {k: dict(v) for k, v in d.items()} for t, d in ozet.items()},
}, ensure_ascii=False, indent=1, default=str))
