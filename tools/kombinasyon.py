"""Relios Export kombinasyon denemesi: programın her ayarının her değeri ve değer çiftleri gerçek kodlamayla denenir.

Gruplar
  video   x264 ve görüntünün bütün ayarları (her değer çifti en az bir kez birlikte), 12 görüntü kaynağıyla
  ses     sesin bütün ayarları (her değer çifti), 66 ses kaynağıyla (her biçim, hız, kanal düzeni, aynı adlı harici ses)
  capraz  görüntü ve sesin birbirini etkileyen ayarları birlikte
  varsayilan  her kaynak × TikTok / YouTube / dikey 9:16 varsayılanı (hiç değiştirmeden)
  kenar   elle yazılmış uç durumlar (FDK'nin açmadığı birleşimler, 192 kHz, 24 kanal, gecikme ±60 sn, kayıpsız…)
  nvenc   NVENC'in bütün ayarları: GitHub'da ekran kartı yok, yalnızca komut üretilir ve denetlenir

Her deneme: program (Smoke aracı, programın kendi kodlama kodu) çalışır, çıktı ffprobe ile okunur, baştan sona çözülür
(hata olmamalı), görüntü / ses beklenenle karşılaştırılır, ses yüksekliği ve gerçek tepe BS.1770 ölçeriyle ölçülür.
Kapsam: programın bütün ayarları (Smoke --ayar-alanlari) ve arayüzdeki her listenin her seçeneği (Secenekler.cs) en az bir
başarılı gerçek kodlamada kullanılmış olmalı; değilse deneme "eksik" diye başarısız olur.

Kullanım
  python -I tools/kombinasyon.py uret --cikti durumlar.json
  python -I tools/kombinasyon.py calistir --durumlar durumlar.json --smoke smoke/ReliosExport.Smoke.exe \
      --kaynak kaynaklar --ffmpeg smoke/ffmpeg --sonuc sonuc_1.json --parca 1/4 --paralel 2
  python -I tools/kombinasyon.py ozet --durumlar durumlar.json --sonuc 'sonuc_*.json' --alanlar alanlar.txt \
      --secenekler app/ReliosExport/Arayuz/Secenekler.cs
"""
import argparse
import concurrent.futures
import glob
import itertools
import json
import math
import os
import random
import re
import shutil
import subprocess
import sys
import time

# ================================================================================================= ayar alanları


class Alan:
    """Bir ayar: --ayarla yolu, denenecek değerler (Smoke'un Ayarla biçimiyle), etkin olma koşulu (başka ayarların
    değerleri), arayüzdeki seçenek listesi (Secenekler.cs; kapsam denetimi)."""

    def __init__(self, yol, degerler, kosul=None, liste=None):
        self.yol = yol
        self.degerler = list(degerler)
        self.kosul = kosul or {}
        self.liste = liste


SEVIYELER = ['kaynak', '', '1.0', '1.1', '1.2', '1.3', '2.0', '2.1', '2.2', '3.0', '3.1', '3.2', '4.0', '4.1', '4.2',
             '5.0', '5.1', '5.2', '6.0', '6.1', '6.2']
X264_MODLARI = ['Crf', 'SabitQp', 'OrtalamaBitHizi', 'IkiGecis', 'UcGecis', 'Cbr']
YAVAS = {'slower', 'veryslow', 'placebo'}


def boyut_kosulu():
    return {'Video.BoyutSecimi': {'1216x2160', '1080x1920', '1440x2560', '2160x3840', '1080x1350', '1080x1080', '1920x1080',
                                  '2560x1440', '3840x2160', 'ozel'}}


VIDEO = [
    Alan('Video.BoyutSecimi', ['kaynak', '1216x2160', '1080x1920', '1440x2560', '2160x3840', '1080x1350', '1080x1080',
                               '1920x1080', '2560x1440', '3840x2160', 'ozel'], liste='Boyutlar'),
    Alan('Video.Genislik', ['16', '17', '321', '1920', '8192'], {'Video.BoyutSecimi': {'ozel'}}),
    Alan('Video.Yukseklik', ['16', '31', '241', '1080', '8192'], {'Video.BoyutSecimi': {'ozel'}}),
    Alan('Video.Oran', ['OrtadanKirp', 'KenarEkle', 'Esnet'], boyut_kosulu(), liste='OranUyumlari'),
    Alan('Video.Olcekleme', ['lanczos', 'spline', 'bicubic', 'bilinear', 'area', 'neighbor'], liste='Olceklemeler'),
    Alan('Video.HassasYuvarlama', ['true', 'false']),
    Alan('Video.KareHiziSecimi', ['kaynak', '24000/1001', '24', '25', '30000/1001', '30', '50', '60000/1001', '60'],
         liste='KareHizlari'),
    Alan('Video.Kap', ['Mp4', 'Mov'], liste='Kaplar'),
    Alan('Video.X264.OnAyar', ['ultrafast', 'superfast', 'veryfast', 'faster', 'fast', 'medium', 'slow', 'slower',
                               'veryslow', 'placebo'], liste='X264OnAyarlar'),
    Alan('Video.X264.Ayar', ['', 'film', 'animation', 'grain', 'stillimage', 'psnr', 'ssim', 'fastdecode', 'zerolatency'],
         liste='X264Ayarlar'),
    Alan('Video.X264.Profil', ['kaynak', '', 'baseline', 'main', 'high', 'high10', 'high422', 'high444'],
         liste='X264Profiller'),
    Alan('Video.X264.Seviye', SEVIYELER, liste='Seviyeler'),
    Alan('Video.X264.Mod', X264_MODLARI, liste='X264Modlari'),
    Alan('Video.X264.Crf', ['0', '0.5', '1', '18', '23.5', '51'], {'Video.X264.Mod': {'Crf'}}),
    Alan('Video.X264.Qp', ['0', '1', '20', '51', '69'], {'Video.X264.Mod': {'SabitQp'}}),
    Alan('Video.X264.BitHizi', ['1', '64', '4000', '60000', '1000000'],
         {'Video.X264.Mod': {'OrtalamaBitHizi', 'IkiGecis', 'UcGecis', 'Cbr'}}),
    Alan('Video.X264.HizliIlkGecis', ['true', 'false'], {'Video.X264.Mod': {'IkiGecis', 'UcGecis'}}),
    Alan('Video.X264.IsParcacigi', ['null', '1', '3', '8', '128']),
    Alan('Video.X264.IleriBakisIsParcacigi', ['null', '1', '4']),
    Alan('Video.X264.Me', ['null', 'dia', 'hex', 'umh', 'esa', 'tesa'], liste='HareketArama'),
    Alan('Video.X264.MeAraligi', ['null', '4', '16', '64']),
    Alan('Video.X264.AltPiksel', ['null'] + [str(i) for i in range(12)], liste='AltPikseller'),
    Alan('Video.X264.DogrudanMv', ['null', 'none', 'spatial', 'temporal', 'auto'], liste='DogrudanMvler'),
    Alan('Video.X264.AgirlikliP', ['null', '0', '1', '2'], liste='AgirlikliPler'),
    Alan('Video.X264.AgirlikliB', ['true', 'false']),
    Alan('Video.X264.KarisikRef', ['null', 'true', 'false']),
    Alan('Video.X264.KromaMe', ['null', 'true', 'false']),
    Alan('Video.X264.Ref', ['null', '1', '2', '4', '16']),
    Alan('Video.X264.BKare', ['null', '0', '1', '3', '16']),
    Alan('Video.X264.BSapma', ['null', '-90', '0', '100']),
    Alan('Video.X264.BUyarlamali', ['null', '0', '1', '2'], liste='BUyarlamalilar'),
    Alan('Video.X264.BPiramit', ['null', 'none', 'strict', 'normal'], liste='BPiramitler'),
    Alan('Video.X264.IleriBakis', ['null', '0', '40', '250']),
    Alan('Video.X264.Dilim', ['null', '1', '4']),
    Alan('Video.X264.Deblock', ['true', 'false']),
    Alan('Video.X264.DeblockAlfa', ['null', '-6', '0', '6'], {'Video.X264.Deblock': {'true'}}),
    Alan('Video.X264.DeblockBeta', ['null', '-6', '0', '6'], {'Video.X264.Deblock': {'true'}}),
    Alan('Video.X264.Cabac', ['true', 'false']),
    Alan('Video.X264.Gecmeli', ['true', 'false']),
    Alan('Video.X264.Bolumler', ['null', 'none', 'i4x4,i8x8', 'p8x8,b8x8,i8x8,i4x4', 'all'], liste='Bolumler'),
    Alan('Video.X264.Dct8x8', ['null', 'true', 'false']),
    Alan('Video.X264.Trellis', ['null', '0', '1', '2'], liste='Trellisler'),
    Alan('Video.X264.PsyRd', ['null', '0', '1', '5']),
    Alan('Video.X264.PsyTrellis', ['null', '0', '0.15', '1']),
    Alan('Video.X264.Psy', ['true', 'false']),
    Alan('Video.X264.OluBolgeInter', ['null', '0', '21', '32']),
    Alan('Video.X264.OluBolgeIntra', ['null', '0', '11', '32']),
    Alan('Video.X264.GurultuAzaltma', ['null', '0', '1000']),
    Alan('Video.X264.MbAgaci', ['null', 'true', 'false']),
    Alan('Video.X264.HizliPAtlama', ['null', 'true', 'false']),
    Alan('Video.X264.DctSeyreltme', ['null', 'true', 'false']),
    Alan('Video.X264.QpMin', ['null', '0', '10', '51']),
    Alan('Video.X264.QpMax', ['null', '0', '33', '69']),
    Alan('Video.X264.QpAdim', ['null', '1', '4', '50']),
    Alan('Video.X264.IpOrani', ['null', '1', '1.4', '10']),
    Alan('Video.X264.PbOrani', ['null', '1', '1.3', '10']),
    Alan('Video.X264.KromaQpOfseti', ['null', '-12', '0', '12']),
    Alan('Video.X264.Qcomp', ['null', '0', '0.6', '1']),
    Alan('Video.X264.CplxBlur', ['null', '0', '20', '999']),
    Alan('Video.X264.QBlur', ['null', '0', '0.5', '99']),
    Alan('Video.X264.Cqm', ['null', 'flat', 'jvt'], liste='Cqmler'),
    Alan('Video.X264.AqModu', ['null', '0', '1', '2', '3'], liste='AqModlari'),
    Alan('Video.X264.AqGucu', ['null', '0', '1', '3']),
    Alan('Video.X264.VbvMaks', ['null', '100', '34375', '200000']),
    Alan('Video.X264.VbvTampon', ['null', '100', '55000', '400000']),
    Alan('Video.X264.VbvBaslangic', ['null', '0', '0.5', '1']),
    Alan('Video.X264.NalHrd', ['null', 'none', 'vbr', 'cbr'], liste='NalHrdler'),
    Alan('Video.X264.Dolgu', ['true', 'false']),
    Alan('Video.X264.Aud', ['true', 'false']),
    Alan('Video.X264.PicStruct', ['true', 'false']),
    Alan('Video.X264.BlurayUyumlu', ['true', 'false']),
    Alan('Video.X264.KisitliIntra', ['true', 'false']),
    Alan('Video.X264.EkParametreler', ['', 'ref=2', 'no-mbtree=1:keyint=30', 'sync-lookahead=0:rc-lookahead=10']),
    Alan('AnahtarKare.Birim', ['Kare', 'Kare30', 'Saniye'], liste='AralikBirimleri'),
    Alan('AnahtarKare.EnBuyuk', ['null', '1', '2', '10', '30', '600']),
    Alan('AnahtarKare.EnKucuk', ['null', '1', '26', '600']),
    Alan('AnahtarKare.SahneDegisimi', ['null', '0', '40', '100']),
    Alan('AnahtarKare.AcikGop', ['true', 'false']),
    Alan('AnahtarKare.IntraYenileme', ['true', 'false']),
]

SES_VAR = {'Ses.Kaynak': {'AyniAdliDosyaOncelikli', 'VideonunSesi'}}
FDK = {**SES_VAR, 'Ses.Kodlayici': {'FdkAac'}}
SES = [
    Alan('Ses.Kaynak', ['AyniAdliDosyaOncelikli', 'VideonunSesi', 'Sessiz'], liste='SesKaynaklari'),
    Alan('Ses.Gecikme', ['0', '250', '-250', '1500', '-1500', '60000', '-60000'], SES_VAR),
    Alan('Ses.Seviye', ['0', '-60', '-6', '6', '30'], SES_VAR),
    Alan('Ses.OrnekHizi', ['null', '44100', '48000', '96000'], SES_VAR, liste='OrnekHizlari'),
    Alan('Ses.Kalite', ['Yuksek', 'CokYuksek', 'EnYuksek'], SES_VAR, liste='Kaliteler'),
    Alan('Ses.Titresim', ['Yok', 'Ucgen', 'UcgenYuksekGecis', 'Shibata'], SES_VAR, liste='Titresimler'),
    Alan('Ses.Kanal', ['Kaynak', 'Mono', 'Stereo'], SES_VAR, liste='Kanallar'),
    Alan('Ses.Kodlayici', ['FdkAac', 'Wav'], SES_VAR, liste='SesKodlayicilar'),
    Alan('Ses.Fdk.Profil', ['aac_low', 'aac_he', 'aac_he_v2', 'aac_ld', 'aac_eld'], FDK, liste='FdkProfiller'),
    Alan('Ses.Fdk.Mod', ['Cbr', 'Vbr1', 'Vbr2', 'Vbr3', 'Vbr4', 'Vbr5'], FDK, liste='FdkModlari'),
    Alan('Ses.Fdk.BitHizi', ['8', '32', '64', '128', '256', '320', '512', '800'], {**FDK, 'Ses.Fdk.Mod': {'Cbr'}}),
    Alan('Ses.Fdk.Kesim', ['0', '1', '4000', '16000', '20000'], FDK),
    Alan('Ses.Fdk.Afterburner', ['true', 'false'], FDK),
    Alan('Ses.Fdk.Sinyal', ['default', 'implicit', 'explicit_sbr', 'explicit_hierarchical'], FDK, liste='FdkSinyaller'),
    Alan('Ses.Fdk.DrcProfili', ['null', '0', '1', '2', '3', '4', '5'], FDK, liste='DrcProfilleri'),
    Alan('Ses.Fdk.SikistirmaProfili', ['null', '0', '1', '2', '3', '4', '5'], FDK, liste='DrcProfilleri'),
    Alan('Ses.Fdk.DrcHedef', ['null', '-31', '-20', '0'], FDK),
    Alan('Ses.Fdk.SikistirmaHedef', ['null', '-31', '-16', '0'], FDK),
    Alan('Ses.Fdk.ProgramSeviyesi', ['null', '-31', '-23', '0'], FDK),
    Alan('Ses.WavBit', ['Bit16', 'Bit24', 'Bit32', 'Float32'], {**SES_VAR, 'Ses.Kodlayici': {'Wav'}}, liste='WavBitleri'),
    Alan('Ses.Sinirlayici', ['true', 'false'], SES_VAR),
    Alan('Ses.SinirlayiciTavan', ['-12', '-1', '-0.1'], SES_VAR),
    Alan('Ses.Esitleme', ['true', 'false'], SES_VAR),
    Alan('Ses.EsitlemeHedef', ['-40', '-23', '-14', '-5'], {**SES_VAR, 'Ses.Esitleme': {'true'}}),
    Alan('Ses.EsitlemeYonu', ['YalnizcaYukselt', 'IkiYon'], {**SES_VAR, 'Ses.Esitleme': {'true'}}, liste='EsitlemeYonleri'),
    Alan('Video.Kap', ['Mp4', 'Mov'], liste='Kaplar'),
]

NVENC = [
    Alan('Video.Nvenc.OnAyar', ['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'], liste='NvencOnAyarlar'),
    Alan('Video.Nvenc.Ayar', ['hq', 'll', 'ull', 'lossless'], liste='NvencAyarlar'),
    Alan('Video.Nvenc.Profil', ['kaynak', '', 'baseline', 'main', 'high', 'high444p'], liste='NvencProfiller'),
    Alan('Video.Nvenc.Seviye', SEVIYELER, liste='Seviyeler'),
    Alan('Video.Nvenc.Mod', ['SabitKalite', 'SabitQp', 'DegiskenBitHizi', 'Cbr'], liste='NvencModlari'),
    Alan('Video.Nvenc.Cq', ['0', '19', '51'], {'Video.Nvenc.Mod': {'SabitKalite'}}),
    Alan('Video.Nvenc.Qp', ['0', '20', '51'], {'Video.Nvenc.Mod': {'SabitQp'}}),
    Alan('Video.Nvenc.BitHizi', ['1', '12000', '34375', '1000000'], {'Video.Nvenc.Mod': {'DegiskenBitHizi', 'Cbr'}}),
    Alan('Video.Nvenc.MaksBitHizi', ['null', '1000', '50000']),
    Alan('Video.Nvenc.Tampon', ['null', '1000', '55000']),
    Alan('Video.Nvenc.CokGecis', ['disabled', 'qres', 'fullres'], liste='CokGecisler'),
    Alan('Video.Nvenc.IleriBakis', ['null', '0', '32']),
    Alan('Video.Nvenc.UzamsalAq', ['true', 'false']),
    Alan('Video.Nvenc.AqGucu', ['1', '8', '15']),
    Alan('Video.Nvenc.ZamansalAq', ['true', 'false']),
    Alan('Video.Nvenc.BKare', ['null', '0', '2', '4']),
    Alan('Video.Nvenc.BRefModu', ['disabled', 'each', 'middle'], liste='BRefModlari'),
    Alan('Video.Nvenc.Ref', ['null', '1', '3', '16']),
    Alan('Video.Nvenc.AgirlikliTahmin', ['true', 'false']),
    Alan('Video.Nvenc.Kodlama', ['cabac', 'cavlc'], liste='Kodlamalar'),
    Alan('Video.Nvenc.GpuCozme', ['true', 'false']),
    Alan('Video.Nvenc.EkParametreler', ['', '-surfaces 32']),
    Alan('Video.BoyutSecimi', ['kaynak', '1080x1920', '1920x1080', '3840x2160']),
    Alan('Video.KareHiziSecimi', ['kaynak', '30', '60']),
    Alan('AnahtarKare.Birim', ['Kare', 'Kare30', 'Saniye']),
    Alan('AnahtarKare.EnBuyuk', ['null', '1', '30', '600']),
    Alan('AnahtarKare.SahneDegisimi', ['null', '0', '40']),
    Alan('AnahtarKare.IntraYenileme', ['true', 'false']),
]

# Görüntünün ve sesin birbirini etkileyen ayarları (kap, çok geçiş, kare hızı + ses)
CAPRAZ = [
    Alan('Video.Kap', ['Mp4', 'Mov']),
    Alan('Video.X264.Mod', ['Crf', 'IkiGecis', 'UcGecis', 'Cbr', 'OrtalamaBitHizi', 'SabitQp']),
    Alan('Video.KareHiziSecimi', ['kaynak', '24', '60']),
    Alan('Video.BoyutSecimi', ['kaynak', '1080x1920', '1080x1080']),
    Alan('Ses.Kaynak', ['AyniAdliDosyaOncelikli', 'VideonunSesi', 'Sessiz']),
    Alan('Ses.Kodlayici', ['FdkAac', 'Wav'], SES_VAR),
    Alan('Ses.Fdk.Profil', ['aac_low', 'aac_he', 'aac_he_v2', 'aac_ld', 'aac_eld'], FDK),
    Alan('Ses.Esitleme', ['true', 'false'], SES_VAR),
    Alan('Ses.Sinirlayici', ['true', 'false'], SES_VAR),
    Alan('Ses.Gecikme', ['0', '400', '-400'], SES_VAR),
    Alan('Ses.Kanal', ['Kaynak', 'Mono', 'Stereo'], SES_VAR),
    Alan('Ses.OrnekHizi', ['null', '44100', '96000'], SES_VAR),
]

# Denemelerde hep açıkça verilen ayarlar (kapsam denetimi için): kodlayıcı (x264 gerçek kodlama, NVENC yalnızca komut)
SABIT = [Alan('Video.Kodlayici', ['X264', 'Nvenc'], liste='VideoKodlayicilar')]

# Görüntü grubunda hızlı ses, ses grubunda hızlı görüntü (yalnızca o grubun ayarları değişsin)
HIZLI_GORUNTU = ['Video.Kodlayici=X264', 'Video.BoyutSecimi=kaynak', 'Video.KareHiziSecimi=kaynak', 'Video.X264.OnAyar=ultrafast',
                 'Video.X264.Mod=Crf', 'Video.X264.Crf=30', 'Video.X264.Ref=null', 'Video.X264.IleriBakis=null',
                 'Video.X264.Profil=', 'Video.X264.Seviye=', 'Video.X264.IsParcacigi=null']


# ================================================================================================= çift kapsamı (pairwise)


def etkin(alanlar, test, ad):
    """Ayar bu denemede etkin mi: koşulundaki her ayar etkin ve izin verilen değerde."""
    a = alanlar[ad]
    for k, izin in a.kosul.items():
        if k not in test or test[k] not in izin or not etkin(alanlar, test, k):
            return False
    return True


def ciftler_uret(alanlar, sira, uygun, gerekli=None):
    """Birlikte denenebilecek bütün (ayar, değer, ayar, değer) çiftleri (gerekli: hangi ayar çiftleri birlikte denenmeli)."""
    cift = set()
    for i, a in enumerate(sira):
        for b in sira[i + 1:]:
            if gerekli and not gerekli(a, b):
                continue
            for va in alanlar[a].degerler:
                for vb in alanlar[b].degerler:
                    if uyumlu(alanlar, {a: va, b: vb}) and uygun({a: va, b: vb}):
                        cift.add((a, va, b, vb))
    return cift


def kosullari_zorla(alanlar, test, ad, rnd):
    """Ayarın etkin olması için koşullarını teste yazar (çakışırsa False)."""
    for k, izin in alanlar[ad].kosul.items():
        if k in test:
            if test[k] not in izin:
                return False
        else:
            test[k] = rnd.choice(sorted(izin))
        if not kosullari_zorla(alanlar, test, k, rnd):
            return False
    return True


def uyumlu(alanlar, sabit):
    test = dict(sabit)
    rnd = random.Random(0)
    for ad in list(sabit):
        if not kosullari_zorla(alanlar, test, ad, rnd):
            return False
    return all(test[k] == v for k, v in sabit.items())


def pairwise(alanlar_listesi, uygun, tohum, ek_degerler=None, gerekli=None):
    """Her etkin değer çiftini en az bir kez birlikte içeren deneme listesi (AETG benzeri açgözlü yöntem)."""
    alanlar = {}
    for a in alanlar_listesi:
        alanlar[a.yol] = a
    if ek_degerler:
        for ad, degerler in ek_degerler.items():
            alanlar[ad] = Alan(ad, degerler)
    sira = list(alanlar)
    rnd = random.Random(tohum)
    kalan = ciftler_uret(alanlar, sira, uygun, gerekli)
    toplam = len(kalan)
    testler = []
    ulasilamaz = set()
    while kalan - ulasilamaz:
        aday = sorted(kalan - ulasilamaz)
        tohum_cift = aday[rnd.randrange(len(aday))]
        a, va, b, vb = tohum_cift
        test = {a: va, b: vb}
        if not (kosullari_zorla(alanlar, test, a, rnd) and kosullari_zorla(alanlar, test, b, rnd)) or not uygun(test):
            ulasilamaz.add(tohum_cift)
            continue
        sabit = set(test)
        diger = [x for x in sira if x not in test]
        rnd.shuffle(diger)
        for ad in diger:
            en_iyi, en_iyi_puan = [], -1
            for v in alanlar[ad].degerler:
                test[ad] = v
                if not uygun(test):
                    continue
                puan = 0
                for x in test:
                    if x == ad:
                        continue
                    anahtar = (x, test[x], ad, v) if sira.index(x) < sira.index(ad) else (ad, v, x, test[x])
                    if anahtar in kalan:
                        puan += 1
                if puan > en_iyi_puan:
                    en_iyi, en_iyi_puan = [v], puan
                elif puan == en_iyi_puan:
                    en_iyi.append(v)
            if not en_iyi:
                en_iyi = [alanlar[ad].degerler[0]]
            test[ad] = rnd.choice(en_iyi)
        if not uygun(test):
            ulasilamaz.add(tohum_cift)
            continue
        # Koşulları sonradan bozulmuş sabit değerleri düzelt (sabitler zaten zorlandı)
        etkinler = [x for x in sira if etkin(alanlar, test, x)]
        yeni = set()
        for i, x in enumerate(etkinler):
            for y in etkinler[i + 1:]:
                k = (x, test[x], y, test[y]) if sira.index(x) < sira.index(y) else (y, test[y], x, test[x])
                if k in kalan:
                    yeni.add(k)
        if tohum_cift not in yeni:
            ulasilamaz.add(tohum_cift)
        if yeni:
            kalan -= yeni
            testler.append(test)
    return testler, toplam, len(ulasilamaz & kalan), alanlar


# ================================================================================================= denemeleri üret


def kaynaklar_manifest(klasor):
    with open(os.path.join(klasor, 'manifest.json'), encoding='utf-8') as f:
        return json.load(f)


def ayarla_listesi(alanlar, test):
    """Testin etkin ayarlarını --ayarla sırasıyla (boyut seçimi kutulardan önce)."""
    oncelik = {'Video.BoyutSecimi': 0, 'Video.KareHiziSecimi': 0, 'Video.X264.Mod': 1, 'Ses.Kodlayici': 1}
    adlar = [a for a in test if a in alanlar and a != 'kaynak' and etkin(alanlar, test, a)]
    adlar.sort(key=lambda a: oncelik.get(a, 5))
    return [f'{a}={test[a]}' for a in adlar]


def uret(arg):
    with open(arg.manifest, encoding='utf-8') as f:
        manifest = json.load(f)
    video_k = [k['ad'] for k in manifest if k['grup'] == 'video']
    ses_k = [k['ad'] for k in manifest if k['grup'] in ('ses', 'harici')]
    durumlar = []

    def piksel(test):
        b = test.get('Video.BoyutSecimi', 'kaynak')
        if b == 'ozel':
            return int(test.get('Video.Genislik', '16')) * int(test.get('Video.Yukseklik', '16'))
        if b == 'kaynak':
            return 640 * 360
        w, h = b.split('x')
        return int(w) * int(h)

    def video_uygun(test):
        # Süre için: büyük boyut yavaş hız ayarı / çok geçiş / tam aramayla birlikte denenmez (ikisi ayrı ayrı denenir).
        p = piksel(test)
        hizli_kare = test.get('Video.KareHiziSecimi') in ('50', '60', '60000/1001')
        yavas = (test.get('Video.X264.OnAyar') in YAVAS or test.get('Video.X264.Me') in ('esa', 'tesa')
                 or test.get('Video.X264.AltPiksel') in ('10', '11'))
        if yavas and (p > 1280 * 720 or hizli_kare):
            return False
        if test.get('Video.X264.Mod') in ('IkiGecis', 'UcGecis') and (p > 1920 * 1080 or (hizli_kare and p > 1280 * 720)):
            return False
        if p > 2560 * 1440 and hizli_kare:
            return False
        if p > 4096 * 2160 and test.get('Video.X264.OnAyar') not in ('ultrafast', 'superfast', 'veryfast'):
            return False
        return True

    testler, toplam, ulasilamaz, alanlar = pairwise(VIDEO, video_uygun, 1, {'kaynak': video_k})
    for i, t in enumerate(testler):
        durumlar.append(dict(ad=f'video{i + 1:03d}', grup='video', kaynak=t['kaynak'], profil='tiktok',
                             ayarla=['Video.Kodlayici=X264'] + ayarla_listesi(alanlar, t)))
    print(f'video: {len(testler)} deneme, {toplam} değer çifti ({ulasilamaz} çift birlikte denenemez)')

    # Kaynakla (sesin biçimi) yalnızca sesin biçimini, uzunluğunu, yüksekliğini değiştiren ayarlar birlikte denenir.
    bicimsel = {'Ses.Kaynak', 'Ses.Gecikme', 'Ses.Seviye', 'Ses.OrnekHizi', 'Ses.Kanal', 'Ses.Kodlayici', 'Ses.Fdk.Profil',
                'Ses.Fdk.Mod', 'Ses.Fdk.BitHizi', 'Ses.WavBit', 'Ses.Sinirlayici', 'Ses.Esitleme', 'Ses.EsitlemeYonu', 'Video.Kap'}

    def ses_gerekli(a, b):
        return (a != 'kaynak' and b != 'kaynak') or (a if b == 'kaynak' else b) in bicimsel

    testler, toplam, ulasilamaz, alanlar = pairwise(SES, lambda t: True, 2, {'kaynak': ses_k}, ses_gerekli)
    for i, t in enumerate(testler):
        durumlar.append(dict(ad=f'ses{i + 1:03d}', grup='ses', kaynak=t['kaynak'], profil='tiktok',
                             ayarla=HIZLI_GORUNTU + ayarla_listesi(alanlar, t)))
    print(f'ses: {len(testler)} deneme, {toplam} değer çifti ({ulasilamaz} çift birlikte denenemez)')

    capraz_k = ['v01', 'v04', 'v05', 'v07', 's01', 's04', 's08', 's16', 's41', 's42', 's43', 'h01', 'h07', 'h14', 'h15']
    testler, toplam, ulasilamaz, alanlar = pairwise(CAPRAZ, lambda t: True, 3, {'kaynak': capraz_k})
    for i, t in enumerate(testler):
        durumlar.append(dict(ad=f'capraz{i + 1:03d}', grup='capraz', kaynak=t['kaynak'], profil='tiktok',
                             ayarla=['Video.Kodlayici=X264', 'Video.X264.OnAyar=veryfast', 'Video.X264.Ref=null',
                                     'Video.X264.IleriBakis=null', 'Video.X264.BitHizi=3000'] + ayarla_listesi(alanlar, t)))
    print(f'capraz: {len(testler)} deneme, {toplam} değer çifti')

    for k in manifest:
        for profil in ('tiktok', 'youtube', 'tiktok-916'):
            durumlar.append(dict(ad=f'varsayilan_{profil}_{k["ad"]}', grup='varsayilan', kaynak=k['ad'], profil=profil, ayarla=[]))

    testler, toplam, ulasilamaz, alanlar = pairwise(NVENC, lambda t: True, 4, {'kaynak': video_k})
    for i, t in enumerate(testler):
        durumlar.append(dict(ad=f'nvenc{i + 1:03d}', grup='nvenc', kaynak=t['kaynak'], profil='tiktok-nvenc', komut=True,
                             ayarla=['Video.Kodlayici=Nvenc'] + ayarla_listesi(alanlar, t)))
    print(f'nvenc (yalnızca komut): {len(testler)} deneme, {toplam} değer çifti')

    for i, (kaynak, profil, ayar, aciklama) in enumerate(KENAR):
        durumlar.append(dict(ad=f'kenar{i + 1:02d}', grup='kenar', kaynak=kaynak, profil=profil, ayarla=ayar, aciklama=aciklama))
    print(f'kenar: {len(KENAR)} deneme')

    derin = derin_ses()
    mevcut = {k['ad'] for k in manifest}
    derin = [x for x in derin if x[1] in mevcut]
    for i, (alt, kaynak, ayar) in enumerate(derin):
        durumlar.append(dict(ad=f'derin_{alt}{i + 1:04d}', grup='derin', kaynak=kaynak, profil='tiktok', ayarla=H + ayar))
    print(f'derin ses: {len(derin)} deneme')

    with open(arg.cikti, 'w', encoding='utf-8') as f:
        json.dump(durumlar, f, ensure_ascii=False, indent=0)
    print(f'toplam {len(durumlar)} deneme: {arg.cikti}')


def derin_ses():
    """Sesin ayrıntılı taraması (çift kapsamının dışında, ızgara): (alt grup, kaynak, ayarlar)."""
    d = []
    # A1: FDK'nin her hızı ("kaynakla aynı" hız; kaynaklar her FDK hızında + FDK'de olmayanlar) × mono / stereo ×
    # profil × CBR alt / orta / üst + VBR 1 / 5
    hiz_kaynaklari = ['s12', 's06', 's09', 's35', 's11', 's07', 's36', 's10', 's02', 's01', 's37', 's13', 's03', 's14', 's08']
    modlar = [['Ses.Fdk.Mod=Cbr', 'Ses.Fdk.BitHizi=8'], ['Ses.Fdk.Mod=Cbr', 'Ses.Fdk.BitHizi=128'],
              ['Ses.Fdk.Mod=Cbr', 'Ses.Fdk.BitHizi=800'], ['Ses.Fdk.Mod=Vbr1'], ['Ses.Fdk.Mod=Vbr5']]
    profiller = ['aac_low', 'aac_he', 'aac_he_v2', 'aac_ld', 'aac_eld']
    for kaynak in hiz_kaynaklari:
        for kanal in ('Mono', 'Stereo'):
            for profil in profiller:
                for mod in modlar:
                    d.append(('fdkhiz', kaynak, ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=FdkAac', 'Ses.OrnekHizi=null',
                                                 f'Ses.Kanal={kanal}', f'Ses.Fdk.Profil={profil}'] + mod))
    # A2: çok kanallı ses × seçilen hız (kaynakla aynı / 44,1 / 96 kHz) × profil × CBR orta / üst + VBR 3
    for kaynak in ('s05', 's26', 's30', 's34', 's33', 's25', 's29', 's38', 's39', 's16', 's31'):
        for hz in ('null', '44100', '96000'):
            for profil in profiller:
                for mod in (['Ses.Fdk.Mod=Cbr', 'Ses.Fdk.BitHizi=128'], ['Ses.Fdk.Mod=Cbr', 'Ses.Fdk.BitHizi=800'], ['Ses.Fdk.Mod=Vbr3']):
                    d.append(('fdkkanal', kaynak, ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=FdkAac', f'Ses.OrnekHizi={hz}',
                                                   'Ses.Kanal=Kaynak', f'Ses.Fdk.Profil={profil}'] + mod))
    # B: WAV bit derinliği × titreşim × hız × dönüşüm kalitesi
    i = 0
    for bit in ('Bit16', 'Bit24', 'Bit32', 'Float32'):
        for titresim in ('Yok', 'Ucgen', 'UcgenYuksekGecis', 'Shibata'):
            for hz in ('null', '44100', '48000', '96000'):
                for kalite in ('Yuksek', 'CokYuksek', 'EnYuksek'):
                    i += 1
                    kaynak = ('s01', 's02', 's09', 's08')[i % 4]
                    d.append(('wav', kaynak, ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=Wav', f'Ses.WavBit={bit}',
                                              f'Ses.Titresim={titresim}', f'Ses.OrnekHizi={hz}', f'Ses.Kalite={kalite}']))
    # C: kanal indirme / düzen çevirme: her çok kanallı kaynak × kanal × kodlayıcı × eşitleme
    for kaynak in ('s05', 's16', 's23', 's24', 's25', 's26', 's27', 's28', 's29', 's30', 's31', 's32', 's33', 's34', 's38',
                   'h03', 'h11', 'h17'):
        for kanal in ('Mono', 'Stereo', 'Kaynak'):
            for kod in ('FdkAac', 'Wav'):
                for esit in ('true', 'false'):
                    d.append(('kanal', kaynak, ['Ses.Kaynak=AyniAdliDosyaOncelikli', f'Ses.Kanal={kanal}', f'Ses.Kodlayici={kod}',
                                                f'Ses.Esitleme={esit}']))
    # D: gecikme × uzunluk (kısa / uzun / geç başlayan ses, MPEG-TS, harici dosyalar) × kodlayıcı
    for kaynak in ('s01', 's41', 's42', 's43', 's47', 's44', 's45', 'h01', 'h07', 'h14', 'h15'):
        for gecikme in ('-60000', '-1500', '-250', '0', '250', '1500', '60000'):
            for kod in ('FdkAac', 'Wav'):
                d.append(('gecikme', kaynak, ['Ses.Kaynak=AyniAdliDosyaOncelikli', f'Ses.Gecikme={gecikme}', f'Ses.Kodlayici={kod}']))
    # E: iki ses izi
    for kod in ('FdkAac', 'Wav'):
        for esit in ('true', 'false'):
            for kanal in ('Kaynak', 'Mono', 'Stereo'):
                d.append(('iz', 's40', ['Ses.Kaynak=VideonunSesi', f'Ses.Kodlayici={kod}', f'Ses.Esitleme={esit}', f'Ses.Kanal={kanal}']))
    # F: FDK DRC ve kesim
    for drc in ('1', '2', '3', '4', '5'):
        for hedef in ('-31', '-20', '0'):
            for sik in ('null', '3'):
                for esit_hedef in ('-14', '-23'):
                    d.append(('drc', 's01', ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=FdkAac', f'Ses.Fdk.DrcProfili={drc}',
                                             f'Ses.Fdk.DrcHedef={hedef}', f'Ses.Fdk.SikistirmaProfili={sik}',
                                             'Ses.Fdk.SikistirmaHedef=-16', f'Ses.EsitlemeHedef={esit_hedef}']))
    for prog in ('-31', '-23', '0'):
        d.append(('drc', 's49', ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=FdkAac', f'Ses.Fdk.ProgramSeviyesi={prog}']))
    for kesim in ('1', '100', '1000', '4000', '16000', '20000'):
        for kaynak in ('s06', 's01', 's03'):
            d.append(('kesim', kaynak, ['Ses.Kaynak=VideonunSesi', 'Ses.Kodlayici=FdkAac', 'Ses.OrnekHizi=null', f'Ses.Fdk.Kesim={kesim}']))
    # G: ses yüksekliği: hedef × tavan × sınırlayıcı × yön × kaynak (kısık / yüksek / tepeli) × kodlayıcı
    for hedef in ('-40', '-23', '-14', '-5'):
        for tavan in ('-12', '-1', '-0.1'):
            for sinir in ('true', 'false'):
                for yon in ('YalnizcaYukselt', 'IkiYon'):
                    for kaynak in ('s01', 's49', 's08', 's02', 's04'):
                        for kod in ('FdkAac', 'Wav'):
                            d.append(('yukseklik', kaynak, ['Ses.Kaynak=VideonunSesi', f'Ses.Kodlayici={kod}', 'Ses.Esitleme=true',
                                                            f'Ses.EsitlemeHedef={hedef}', f'Ses.SinirlayiciTavan={tavan}',
                                                            f'Ses.Sinirlayici={sinir}', f'Ses.EsitlemeYonu={yon}']))
    for seviye in ('-60', '6', '30'):
        for kaynak in ('s01', 's49'):
            for kod in ('FdkAac', 'Wav'):
                for sinir in ('true', 'false'):
                    for esit in ('true', 'false'):
                        d.append(('seviye', kaynak, ['Ses.Kaynak=VideonunSesi', f'Ses.Kodlayici={kod}', f'Ses.Seviye={seviye}',
                                                     f'Ses.Sinirlayici={sinir}', f'Ses.Esitleme={esit}']))
    return d


H = HIZLI_GORUNTU
KENAR = [
    ('s08', 'youtube', H + ['Ses.OrnekHizi=null', 'Ses.Seviye=6'], '192 kHz kaynak + FDK + örnekleme kaynakla aynı + sınırlayıcı (eskiden -ar 192000: FDK açılmazdı)'),
    ('s08', 'youtube', H + ['Ses.OrnekHizi=null', 'Ses.Esitleme=false'], '192 kHz kaynak + FDK, sınırlayıcısız: 96 kHz'),
    ('s14', 'tiktok', H + ['Ses.OrnekHizi=null'], '176,4 kHz kaynak + FDK: 96 kHz'),
    ('s12', 'tiktok', H + ['Ses.OrnekHizi=null'], '7,35 kHz kaynak + FDK: 8 kHz'),
    ('s12', 'tiktok', H + ['Ses.OrnekHizi=null', 'Ses.Kodlayici=Wav'], '7,35 kHz kaynak + WAV: 7,35 kHz kalır'),
    ('s06', 'tiktok', H + ['Ses.OrnekHizi=null', 'Ses.Fdk.Profil=aac_he', 'Ses.Kanal=Kaynak'], '8 kHz mono + HE-AAC: AAC-LC'),
    ('s09', 'tiktok', H + ['Ses.OrnekHizi=null', 'Ses.Fdk.Profil=aac_he_v2'], '11,025 kHz + HE-AAC v2: AAC-LC'),
    ('s04', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_he_v2', 'Ses.Fdk.BitHizi=48'], 'Mono kaynak + kanal kaynakla aynı + HE-AAC v2: HE-AAC'),
    ('s01', 'tiktok', H + ['Ses.Kanal=Mono', 'Ses.Fdk.Profil=aac_he_v2', 'Ses.Fdk.BitHizi=32'], 'Kanal mono + HE-AAC v2: HE-AAC'),
    ('s16', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_he_v2', 'Ses.Fdk.BitHizi=160'], '5.1 + HE-AAC v2: HE-AAC 5.1'),
    ('s26', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_ld', 'Ses.Fdk.BitHizi=320', 'Ses.OrnekHizi=48000'], '7.1 + AAC-LD: AAC-LC'),
    ('s32', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_eld', 'Ses.Fdk.BitHizi=320', 'Ses.OrnekHizi=48000'], '6.0 (FDK 7.1 kodlar) + AAC-ELD: AAC-LC'),
    ('s30', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_ld', 'Ses.Fdk.BitHizi=320', 'Ses.OrnekHizi=48000'], '5.1.4 + AAC-LD: 5.1.2(back), LD kalır'),
    ('s34', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.Profil=aac_eld', 'Ses.Fdk.BitHizi=320', 'Ses.OrnekHizi=48000'], '6.1(back) + AAC-ELD: ELD kalır'),
    ('s03', 'tiktok', H + ['Ses.Kanal=Mono', 'Ses.Fdk.Profil=aac_ld', 'Ses.Fdk.BitHizi=800', 'Ses.OrnekHizi=96000'], 'AAC-LD mono 96 kHz 800 kb/s: 614 (üstünde FDK açılmaz)'),
    ('s01', 'youtube', H + ['Ses.Fdk.BitHizi=800'], 'AAC-LC 48 kHz stereo 800 kb/s: FDK 576 kodlar, komut ve dosya 576'),
    ('s01', 'youtube', H + ['Ses.Fdk.BitHizi=8'], 'AAC-LC 48 kHz stereo 8 kb/s: FDK en az 14'),
    ('s31', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.OrnekHizi=48000'], '24 kanal (22.2, düzeni yazılı değil) + FDK kaynakla aynı kanal'),
    ('s31', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Kodlayici=Wav'], '24 kanal + WAV kaynakla aynı kanal'),
    ('s31', 'tiktok', H + ['Ses.Kanal=Stereo'], '24 kanal + stereo'),
    ('s25', 'tiktok', H + ['Ses.Kanal=Kaynak'], 'quad + FDK kaynakla aynı kanal: 5.0'),
    ('s28', 'tiktok', H + ['Ses.Kanal=Kaynak'], '2.1 + FDK kaynakla aynı kanal: stereo'),
    ('s05', 'tiktok', H + ['Ses.Kanal=Kaynak', 'Ses.Fdk.BitHizi=384'], '5.1(side) 64 bit float + FDK kaynakla aynı kanal: 5.1'),
    ('s01', 'tiktok', H + ['Ses.Gecikme=-60000'], 'Gecikme -60 sn (ses tamamen dışarıda), FDK: dosya 3 sn, ses izi sessiz'),
    ('s01', 'tiktok', H + ['Ses.Gecikme=-60000', 'Ses.Kodlayici=Wav'], 'Gecikme -60 sn, WAV: ses izi sessiz'),
    ('s01', 'tiktok', H + ['Ses.Gecikme=60000'], 'Gecikme +60 sn: ses izi sessiz, dosya 3 sn'),
    ('s01', 'tiktok', H + ['Ses.Gecikme=250', 'Ses.Kodlayici=Wav'], 'Gecikme +250 ms, WAV: dosya 3 sn (eskiden 3,25)'),
    ('s43', 'tiktok', H + ['Ses.Gecikme=-500'], 'Uzun ses (4,5 sn) + gecikme -500 ms'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.X264.Profil=main', 'Video.X264.Mod=SabitQp', 'Video.X264.Qp=0'], 'x264 Main + QP 0: kayıpsız yok, QP 1, dosya Main'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.X264.Profil=', 'Video.X264.Mod=Crf', 'Video.X264.Crf=0'], 'x264 profil otomatik + CRF 0: kayıpsız (High 4:4:4 Predictive)'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=medium', 'Video.X264.Profil=baseline', 'Video.X264.BKare=3', 'Video.X264.Dct8x8=true',
                       'Video.X264.AgirlikliP=2', 'Video.X264.Cqm=jvt', 'Video.X264.Gecmeli=true'], 'x264 Baseline + B-kare, 8x8, ağırlıklı P, matris, geçmeli: dosya Baseline'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=medium', 'Video.X264.Profil=main', 'Video.X264.Dct8x8=true', 'Video.X264.Cqm=jvt'], 'x264 Main + 8x8 DCT + matris: dosya Main'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.X264.Profil=high10', 'Video.X264.Mod=SabitQp', 'Video.X264.Qp=0'], 'x264 High 10 + QP 0: QP 1, dosya High 10'),
    ('v01', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.X264.Profil=high422', 'Video.X264.Mod=Crf', 'Video.X264.Crf=0.5'], 'x264 High 4:2:2 + CRF 0,5: CRF 1, dosya High 4:2:2'),
    ('v12', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.BoyutSecimi=ozel', 'Video.Genislik=8192', 'Video.Yukseklik=8192'], 'Tek kare 8192x8192'),
    ('v06', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.BoyutSecimi=ozel', 'Video.Genislik=16', 'Video.Yukseklik=16', 'Video.Oran=KenarEkle'], 'Tek sayılı kaynak 16x16 siyah kenar'),
    ('v06', 'tiktok', ['Video.X264.OnAyar=ultrafast', 'Video.BoyutSecimi=ozel', 'Video.Genislik=8192', 'Video.Yukseklik=16', 'Video.Oran=OrtadanKirp'], '8192x16 ortadan kırp'),
    ('s49', 'youtube', H + ['Ses.EsitlemeHedef=-40', 'Ses.EsitlemeYonu=IkiYon'], 'Hazır yüksek ses, hedef -40, iki yön: kısılır'),
    ('s03', 'youtube', H + ['Ses.EsitlemeHedef=-5'], 'Kısık ses, hedef -5: sınırlayıcı, tepe -1 dBTP'),
    ('s40', 'youtube', H, 'İki ses izi + eşitleme: kayıpsız iz'),
    ('h07', 'youtube', H, 'ADTS .aac harici ses + eşitleme'),
    ('s44', 'tiktok', H, 'Tamamen sessiz ses + eşitleme: ölçülemedi, dokunulmaz'),
    ('s45', 'tiktok', H, '0,2 sn ses + eşitleme: ölçülemedi'),
    ('s46', 'tiktok', H, 'Ses izi yok'),
    ('v12', 'youtube', [], 'Tek kare, YouTube varsayılanı'),
]


# ================================================================================================= çalıştır


def calistir(arg):
    with open(arg.durumlar, encoding='utf-8') as f:
        durumlar = json.load(f)
    parca, toplam = (int(x) for x in arg.parca.split('/'))
    benim = [d for i, d in enumerate(durumlar) if i % toplam == parca - 1]
    manifest = {k['ad']: k for k in kaynaklar_manifest(arg.kaynak)}
    ff = Araclar(arg.ffmpeg)
    kaynak_bilgi = {}
    for k in manifest.values():
        kaynak_bilgi[k['ad']] = ff.probe(os.path.join(arg.kaynak, k['dosya']))
        if k.get('harici'):
            kaynak_bilgi[k['ad'] + ':harici'] = ff.probe(os.path.join(arg.kaynak, k['harici']))
    print(f'{len(benim)} deneme (parça {parca}/{toplam}), {arg.paralel} paralel', flush=True)
    sonuclar = []
    basla = time.time()

    def bir(d):
        return deneme_calistir(d, arg, manifest, kaynak_bilgi, ff)

    with concurrent.futures.ThreadPoolExecutor(max_workers=arg.paralel) as ex:
        for i, s in enumerate(ex.map(bir, benim)):
            sonuclar.append(s)
            durum = 'OK  ' if s['tamam'] else 'FARK'
            print(f'{durum} {s["ad"]} ({s["sure"]:.1f} sn) {s.get("ozet", "")}', flush=True)
            for f in s['farklar']:
                print(f'       FARK {f}', flush=True)
            if not s['tamam'] and s.get('gunluk'):
                print('       --- günlük (son satırlar):\n' + '\n'.join('       ' + x for x in s['gunluk'][-25:]), flush=True)
    with open(arg.sonuc, 'w', encoding='utf-8') as f:
        json.dump(sonuclar, f, ensure_ascii=False)
    hatali = sum(1 for s in sonuclar if not s['tamam'])
    print(f'\n{len(sonuclar)} deneme, {hatali} sorunlu, {time.time() - basla:.0f} sn')


class Araclar:
    def __init__(self, klasor):
        uz = '.exe' if os.name == 'nt' else ''
        self.ffmpeg = os.path.join(klasor, 'ffmpeg' + uz)
        self.ffprobe = os.path.join(klasor, 'ffprobe' + uz)

    def probe(self, dosya):
        r = subprocess.run([self.ffprobe, '-v', 'error', '-print_format', 'json', '-show_format', '-show_streams', '-show_data',
                            dosya], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if r.returncode != 0:
            return {'hata': r.stderr.strip()[-400:]}
        return json.loads(r.stdout)

    def coz(self, dosya):
        """Dosyayı baştan sona çözer; FFmpeg'in yazdığı her hata satırı."""
        r = subprocess.run([self.ffmpeg, '-hide_banner', '-nostdin', '-v', 'error', '-i', dosya, '-map', '0', '-f', 'null', '-'],
                           capture_output=True, text=True, encoding='utf-8', errors='replace')
        satirlar = [x for x in r.stderr.splitlines() if x.strip()]
        if r.returncode != 0:
            satirlar.append(f'çözücü çıkış kodu {r.returncode}')
        return satirlar

    def ses_bitisi(self, dosya, indeks):
        """Ses izinin FFmpeg'in zaman çizelgesinde (dosyanın başlangıcına göre) bittiği an, çözülerek (sn)."""
        r = subprocess.run([self.ffmpeg, '-hide_banner', '-nostdin', '-nostats', '-progress', 'pipe:1', '-i', dosya,
                            '-map', f'0:{indeks}', '-f', 'null', '-'],
                           capture_output=True, text=True, encoding='utf-8', errors='replace')
        us = re.findall(r'out_time_us=(\d+)', r.stdout)
        return int(us[-1]) / 1e6 if us else None

    def r128(self, dosya, iz=0):
        """Sesin tümleşik yüksekliği (LUFS) ve gerçek tepesi (dBTP, çerçeve bilgisinden; son ~100 ms özetten)."""
        r = subprocess.run([self.ffmpeg, '-hide_banner', '-nostdin', '-nostats', '-i', dosya, '-map', f'0:a:{iz}', '-vn',
                            '-af', 'ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,'
                                   'ametadata=mode=print:key=lavfi.r128.true_peak', '-f', 'null', '-'],
                           capture_output=True, text=True, encoding='utf-8', errors='replace')
        g = r.stderr
        iler = re.findall(r'lavfi\.r128\.I=(\S+)', g)
        tepeler = re.findall(r'lavfi\.r128\.true_peak=(\S+)', g)
        ozet = g[g.rfind('Summary:'):] if 'Summary:' in g else ''
        m = re.search(r'True peak:\s+Peak:\s+(\S+) dBFS', ozet)
        ozet_tepe = None
        if m:
            ozet_tepe = -math.inf if m.group(1) == '-inf' else float(m.group(1))
        tepe = None
        if tepeler:
            dog = float(tepeler[-1])
            tepe = 20 * math.log10(dog) if dog > 0 else -math.inf
        if ozet_tepe is not None and (tepe is None or ozet_tepe > tepe + 0.05):
            tepe = ozet_tepe
        mi = re.search(r'I:\s+(\S+) LUFS', ozet)
        i = float(iler[-1]) if iler else (float(mi.group(1)) if mi else None)
        return i, tepe


def deneme_calistir(d, arg, manifest, kaynak_bilgi, ff):
    basla = time.time()
    k = manifest[d['kaynak']]
    kaynak = os.path.join(arg.kaynak, k['dosya'])
    cikti = os.path.join(arg.calisma, d['ad'])
    shutil.rmtree(cikti, ignore_errors=True)
    os.makedirs(cikti, exist_ok=True)
    komut = [arg.smoke, kaynak, cikti, '--profil', d['profil']]
    for a in d['ayarla']:
        komut += ['--ayarla', a]
    if d.get('komut'):
        komut.append('--sadece-komut')
    sonuc = dict(ad=d['ad'], grup=d['grup'], kaynak=d['kaynak'], profil=d['profil'], ayarla=d['ayarla'], farklar=[], tamam=False)
    try:
        r = subprocess.run(komut, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=arg.zaman)
        gunluk = (r.stdout + r.stderr).splitlines()
        rc = r.returncode
    except subprocess.TimeoutExpired as e:
        gunluk = ((e.stdout or b'').decode('utf-8', 'replace') if isinstance(e.stdout, bytes) else (e.stdout or '')).splitlines()
        rc = 'zaman aşımı'
    gunluk_yolu = next((x.split('Günlük: ', 1)[1].strip() for x in gunluk if 'Günlük: ' in x), None)
    if rc != 0 and gunluk_yolu and os.path.isfile(gunluk_yolu):
        # Programın hata günlüğünden nedeni gösteren satırlar (x264 / FFmpeg iletileri).
        onemli = ('x264 [', 'libx264 @', 'Assertion', 'malloc', 'rror', 'failed', 'Invalid', 'not supported', 'must be')
        gurultu = ('Task finished', 'Terminating thread', 'Nothing was written', 'Error sending frames', 'Conversion failed',
                   'Could not open encoder before EOF', 'Error while opening encoder', 'using cpu capabilities', 'using SAR')
        with open(gunluk_yolu, encoding='utf-8-sig', errors='replace') as gf:
            gunluk += ['GÜNLÜK| ' + y.strip()[:300] for y in gf.read().splitlines()
                       if any(o in y for o in onemli) and not any(g in y for g in gurultu)][:12]
    sonuc['gunluk'] = gunluk
    ayar = None
    for x in gunluk:
        if x.startswith('AYAR_JSON='):
            ayar = json.loads(x[len('AYAR_JSON='):])
    sonuc['ayar'] = ayar
    try:
        farklar, ozet = dogrula(d, rc, gunluk, ayar, k, kaynak_bilgi, ff, arg)
    except Exception as e:  # doğrulayıcının kendi hatası da fark sayılır
        farklar, ozet = [f'doğrulayıcı hata verdi: {type(e).__name__}: {e}'], ''
    sonuc['farklar'] = farklar
    sonuc['ozet'] = ozet
    sonuc['tamam'] = not farklar
    sonuc['sure'] = time.time() - basla
    if sonuc['tamam']:
        sonuc['gunluk'] = [x for x in gunluk if x.startswith(('Komut ', 'Bilgi: ', 'ESITLEME_', 'TEPE_', 'Eşitleme satırı'))][-12:]
        if not arg.sakla:
            shutil.rmtree(cikti, ignore_errors=True)
    return sonuc


# ================================================================================================= doğrula

FDK_HIZLARI = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000]


def fdk_hizi(hz):
    en, fark = FDK_HIZLARI[0], None
    for h in FDK_HIZLARI:
        f = abs(hz - h)
        if fark is None or f < fark:
            en, fark = h, f
    return en


def fdk_duzeni(duzen, kanal):
    d = (duzen or '').strip().lower()
    tablo = {
        'mono': 'mono', 'stereo': 'stereo', '2.1': 'stereo', 'downmix': 'stereo', 'binaural': 'stereo', '3.0': '3.0', '3.1': '3.0',
        '4.0': '4.0', '3.0(back)': '5.0', 'quad': '5.0', 'quad(side)': '5.0', '5.0': '5.0', '5.0(side)': '5.0', '4.1': '5.1',
        '5.1': '5.1', '5.1(side)': '5.1', '6.1(back)': '6.1(back)', '6.0': '7.1', 'hexagonal': '7.1', '6.1': '7.1', '7.0': '7.1',
        '7.1': '7.1', 'octagonal': '7.1', '7.1.2': '7.1', '7.1.4': '7.1', '7.2.3': '7.1', '6.0(front)': '7.1(wide)',
        '6.1(front)': '7.1(wide)', '7.0(front)': '7.1(wide)', '7.1(wide)': '7.1(wide)', '7.1(wide-side)': '7.1(wide)',
        '9.1.4': '7.1(wide)', '9.1.6': '7.1(wide)', 'hexadecagonal': '7.1(wide)', '22.2': '7.1(wide)', '3.1.2': '5.1.2(back)',
        '5.1.2': '5.1.2(back)', '5.1.2(back)': '5.1.2(back)', 'cube': '5.1.2(back)', '5.1.4': '5.1.2(back)',
    }
    if d in tablo:
        return tablo[d]
    return {1: 'mono', 2: 'stereo', 3: '3.0', 4: '4.0', 5: '5.0', 6: '5.1', 7: '6.1(back)'}.get(kanal, '7.1' if kanal > 7 else 'mono')


DUZEN_KANAL = {'mono': 1, 'stereo': 2, '3.0': 3, '4.0': 4, '5.0': 5, '5.1': 6, '6.1(back)': 7, '7.1': 8, '7.1(wide)': 8, '5.1.2(back)': 8}
AAC_PROFIL = {'aac_low': 'LC', 'aac_he': 'HE-AAC', 'aac_he_v2': 'HE-AACv2', 'aac_ld': 'LD', 'aac_eld': 'ELD'}


VBR_KANAL = {'mono': [32000, 40000, 56000, 72000, 112000], 'diger': [20000, 32000, 48000, 64000, 96000]}
ETKIN_KANAL = {'mono': 1, 'stereo': 2, '3.0': 3, '4.0': 4, '5.0': 5, '5.1': 5, '6.1(back)': 6, '7.1': 7, '7.1(wide)': 7, '5.1.2(back)': 7}


def vbr_bit_hizi(mod, duzen):
    """FDK'nin VBR kipinde hedeflediği bit hızı (aacenc.cpp configTabVBR; mono'da kanal başına ayrı tablo)."""
    if duzen == 'mono':
        return VBR_KANAL['mono'][mod - 1]
    return VBR_KANAL['diger'][mod - 1] * ETKIN_KANAL[duzen]


def eld_sbrsiz_en_az(hz, kanal):
    """FDK eldSbrAutoConfigTab: bu bit hızının altında ELD SBR kullanır (FFmpeg çözemez). Ayrı yazılmış."""
    if kanal not in (1, 2) or hz > 48000 or hz < 16000:
        return None
    esikler = {1: [(44100, 64000), (32000, 56000), (22050, 40000), (16000, 28000)],
               2: [(44100, 128000), (32000, 96000), (22050, 80000), (16000, 64000)]}[kanal]
    for alt, esik in esikler:
        if hz >= alt:
            return esik
    return None


def fdk_karari(profil, hz, duzen, mod, bit):
    """Program FDK'nin açmadığı ya da FFmpeg'in çözemediği birleşimde hangi ayara geçmeli (FdkUyumu.Hesapla'nın
    kuralları, ayrı yazılmış): (profil, hız, VBR kipi 0 = CBR, CBR'de istenen bit hızı alt sınırı)."""
    if profil not in AAC_PROFIL:
        profil = 'aac_low'
    kanal = DUZEN_KANAL[duzen] if duzen else None
    if profil == 'aac_he_v2' and kanal and kanal != 2:
        profil = 'aac_he'
    if profil in ('aac_he', 'aac_he_v2') and hz and hz < 16000:
        profil = 'aac_low'
    if profil == 'aac_he' and hz == 96000 and kanal and kanal >= 3:
        hz = 88200
    if profil in ('aac_ld', 'aac_eld') and duzen in ('7.1', '7.1(wide)', '5.1.2(back)'):
        profil = 'aac_low'
    if profil in ('aac_ld', 'aac_eld') and hz and (hz < 22050 or hz > 48000):
        hz = 48000 if hz > 48000 else 22050
    en_az = None
    if profil == 'aac_eld' and hz and kanal in (1, 2):
        esik = eld_sbrsiz_en_az(hz, kanal)
        if esik:
            if mod == 0:
                en_az = esik // 1000
            else:
                while mod < 5 and vbr_bit_hizi(mod, duzen) < esik:
                    mod += 1
    return profil, hz, mod, en_az


def fdk_profili(profil, hz, duzen):
    return fdk_karari(profil, hz, duzen, 0, 128)[0]


def asc_oku(extradata_metin):
    """ffprobe -show_data çıktısındaki AudioSpecificConfig: (nesne türü, kanal yapılandırması)."""
    if not extradata_metin:
        return None
    hexler = ''.join(re.findall(r'^[0-9a-f]{8}: ((?:[0-9a-f]{4} ?)+)', extradata_metin, re.M)).replace(' ', '')
    if not hexler:
        return None
    bitler = ''.join(f'{int(hexler[i:i + 2], 16):08b}' for i in range(0, len(hexler), 2))
    p = 0

    def al(n):
        nonlocal p
        v = int(bitler[p:p + n], 2)
        p += n
        return v
    aot = al(5)
    if aot == 31:
        aot = 32 + al(6)
    hz_i = al(4)
    if hz_i == 15:
        al(24)
    kanal = al(4)
    return aot, kanal


def kesir(metin):
    if not metin or metin in ('0/0', 'N/A'):
        return None
    if '/' in metin:
        a, b = metin.split('/')
        return float(a) / float(b) if float(b) else None
    return float(metin)


def akislar(bilgi, tur):
    return [s for s in bilgi.get('streams', []) if s.get('codec_type') == tur
            and not (s.get('disposition') or {}).get('attached_pic')]


def video_suresi(bilgi):
    v = akislar(bilgi, 'video')
    if v and v[0].get('duration'):
        return float(v[0]['duration'])
    for t, deger in (v[0].get('tags') or {}).items() if v else []:
        if t.upper().startswith('DURATION'):
            sa, dk, sn = deger.split(':')
            return int(sa) * 3600 + int(dk) * 60 + float(sn)
    return float(bilgi.get('format', {}).get('duration', 0) or 0)


def dosya_suresi(bilgi):
    return float(bilgi.get('format', {}).get('duration', 0) or 0)


def akis_suresi(s, bilgi):
    if s.get('duration'):
        return float(s['duration'])
    for t, deger in (s.get('tags') or {}).items():
        if t.upper().startswith('DURATION'):
            sa, dk, sn = deger.split(':')
            return int(sa) * 3600 + int(dk) * 60 + float(sn)
    return dosya_suresi(bilgi)


def x264_secenekleri(dosya):
    """x264'ün dosyaya yazdığı ayar satırı (SEI): anahtar → değer."""
    with open(dosya, 'rb') as f:
        veri = f.read()
    i = veri.find(b'x264 - core')
    if i < 0:
        return None
    sei = re.match(rb'[\x20-\x7e]+', veri[i:i + 4000]).group(0).decode()
    if 'options: ' not in sei:
        return None
    return dict(x.split('=', 1) for x in sei.split('options: ', 1)[1].split(' ') if '=' in x)


def dogrula(d, rc, gunluk, ayar, k, kaynak_bilgi, ff, arg):
    farklar = []
    hata = [x for x in gunluk if x.startswith('HATA: ')]
    if rc != 0:
        return [f'program hata verdi (kod {rc}): {hata[0] if hata else "-"}'], ''
    if ayar is None:
        return ['AYAR_JSON satırı yok'], ''
    if d.get('komut'):
        return nvenc_dogrula(gunluk, ayar), 'komut'
    cikti = [x[len('Çıktı: '):] for x in gunluk if x.startswith('Çıktı: ')]
    if not cikti or not os.path.isfile(cikti[0]):
        return ['çıktı dosyası yok'], ''
    dosya = cikti[0]
    if any(f.endswith('.part' + os.path.splitext(dosya)[1]) for f in os.listdir(os.path.dirname(dosya))):
        farklar.append('yarım dosya (.part) kalmış')
    bilgi = ff.probe(dosya)
    if 'hata' in bilgi:
        return [f'çıktı okunamadı: {bilgi["hata"]}'], ''
    src = kaynak_bilgi[k['ad']]
    v, s = ayar['Video'], ayar['Ses']
    kap = ayar['Video']['Kap']
    uzanti = '.mov' if kap == 'Mov' or s['Kodlayici'] == 'Wav' else '.mp4'
    if not dosya.lower().endswith(uzanti):
        farklar.append(f'uzantı {os.path.splitext(dosya)[1]} (beklenen {uzanti})')
    marka = bilgi.get('format', {}).get('tags', {}).get('major_brand', '')
    if uzanti == '.mov' and marka.strip() != 'qt':
        farklar.append(f'MOV markası {marka!r}')
    if uzanti == '.mp4' and marka.strip() not in ('isom', 'mp42', 'iso5', 'iso6'):
        farklar.append(f'MP4 markası {marka!r}')
    if not bilgi.get('format', {}).get('tags', {}).get('creation_time'):
        farklar.append('dosyada oluşturma zamanı yok')

    # ----------------------------------------------------------------------------------------- görüntü
    vs = akislar(bilgi, 'video')
    svs = akislar(src, 'video')
    if len(vs) != 1:
        farklar.append(f'{len(vs)} görüntü izi')
        return farklar, ''
    o = vs[0]
    if o.get('codec_name') != 'h264':
        farklar.append(f'görüntü {o.get("codec_name")}')
    sv = svs[0]
    rot = 0
    for sd in sv.get('side_data_list', []) or []:
        if 'rotation' in sd:
            rot = int(round(float(sd['rotation']))) % 360
    sw, sh = int(sv['width']), int(sv['height'])
    dw, dh = (sh, sw) if rot in (90, 270) else (sw, sh)
    sar = kesir((sv.get('sample_aspect_ratio') or '1:1').replace(':', '/')) or 1
    if rot in (90, 270):
        sar = 1 / sar
    if v['Cozunurluk'] == 'Kaynak':
        bw, bh = int(math.floor(dw * sar / 2)) * 2, dh // 2 * 2
    else:
        bw, bh = v['Genislik'] - v['Genislik'] % 2, v['Yukseklik'] - v['Yukseklik'] % 2
    x_ = v['X264']
    p_ = x_['Profil']
    if p_ == 'kaynak':
        sp_ = sv.get('profile') or ''
        p_ = 'main' if sv.get('codec_name') != 'h264' or not sp_ else 'baseline' if 'Baseline' in sp_ else 'high' if 'High' in sp_ else 'main'
    if v['Kodlayici'] == 'X264' and x_.get('Gecmeli') and p_ != 'baseline' and p_ not in ('high422', 'high444'):
        bh -= bh % 4  # x264: geçmeli 4:2:0'da yükseklik 4'ün katı
    if (int(o['width']), int(o['height'])) != (bw, bh):
        farklar.append(f'boyut {o["width"]}x{o["height"]} (beklenen {bw}x{bh})')
    if (o.get('sample_aspect_ratio') or '1:1') not in ('1:1', '0:1', 'N/A'):
        farklar.append(f'SAR {o.get("sample_aspect_ratio")}')
    x = v['X264']
    profil = x['Profil']
    if profil == 'kaynak':
        sp = (sv.get('profile') or '')
        profil = 'main' if sv.get('codec_name') != 'h264' or not sp else 'baseline' if 'Baseline' in sp else 'high' if 'High' in sp else 'main'
    pf_bek = {'high10': 'yuv420p10le', 'high422': 'yuv422p', 'high444': 'yuv444p'}.get(profil, 'yuv420p')
    if o.get('pix_fmt') != pf_bek:
        farklar.append(f'piksel biçimi {o.get("pix_fmt")} (beklenen {pf_bek})')
    for alan, bek in (('color_range', 'tv'), ('color_space', 'bt709'), ('color_primaries', 'bt709'), ('color_transfer', 'bt709')):
        if o.get(alan) != bek:
            farklar.append(f'{alan} {o.get(alan)}')
    if (o.get('tags') or {}).get('language') != 'eng':
        farklar.append(f'görüntü dili {(o.get("tags") or {}).get("language")}')
    kayipsiz = (x['Mod'] == 'SabitQp' and x['Qp'] <= 0 and profil in ('', 'high444')) or \
               (x['Mod'] == 'Crf' and x['Crf'] < 1 and profil in ('', 'high444'))
    izin = {
        'baseline': {'Constrained Baseline', 'Baseline'},
        'main': {'Constrained Baseline', 'Main'},
        'high': {'Constrained Baseline', 'Main', 'High'},
        'high10': {'High 10', 'High 10 Intra'},
        'high422': {'High 4:2:2', 'High 4:2:2 Intra'},
        'high444': {'High 4:4:4 Predictive', 'High 4:4:4 Intra'},
        '': {'Constrained Baseline', 'Main', 'High', 'High 4:4:4 Predictive'},
    }[profil]
    ek = x.get('EkParametreler') or ''
    if kayipsiz:
        izin = {'High 4:4:4 Predictive', 'High 4:4:4 Intra'}
    if o.get('profile') not in izin and not ek:
        farklar.append(f'profil {o.get("profile")} (seçilen {profil or "otomatik"}: {sorted(izin)})')
    if x['Seviye'] not in ('kaynak', '') and not ek:
        bek_sev = int(round(float(x['Seviye']) * 10))
        if int(o.get('level', -1)) != bek_sev:
            farklar.append(f'seviye {o.get("level")} (seçilen {x["Seviye"]})')
    # kare hızı ve süre
    s_fps = kesir(sv.get('avg_frame_rate')) or kesir(sv.get('r_frame_rate'))
    o_fps = kesir(o.get('avg_frame_rate'))
    kare_sayisi = int(o.get('nb_frames') or 0)
    if v['KareHiziModu'] == 'Sabit' and kare_sayisi > 2:
        bek_fps = kesir(v['KareHizi'])
        if not o_fps or abs(o_fps - bek_fps) > 0.01 * bek_fps:
            farklar.append(f'kare hızı {o.get("avg_frame_rate")} (beklenen {v["KareHizi"]})')
    sure_v = video_suresi(src)
    o_sure = akis_suresi(o, bilgi)
    tolerans = 2.5 / (o_fps or 25) + 0.06
    if sure_v and abs(o_sure - sure_v) > tolerans:
        farklar.append(f'görüntü süresi {o_sure:.3f} (kaynak {sure_v:.3f})')
    sec = x264_secenekleri(dosya)
    if sec is None:
        farklar.append('x264 bilgi notu (SEI) yok')
    else:
        farklar += x264_dogrula(sec, x, ayar['AnahtarKare'], o_fps or s_fps or 30, profil, gunluk)

    # ----------------------------------------------------------------------------------------- ses
    farklar += ses_dogrula(d, gunluk, ayar, k, src, kaynak_bilgi, bilgi, dosya, ff, sure_v, os.path.join(arg.kaynak, k['dosya']))

    # ----------------------------------------------------------------------------------------- çözme
    cozme = ff.coz(dosya)
    if cozme:
        farklar.append('çözerken hata: ' + ' | '.join(cozme[:3]))

    # ----------------------------------------------------------------------------------------- tahmini boyut
    for xs in gunluk:
        m = re.match(r'TAHMIN_BAYT=(\d+) TAHMIN_TUR=(\S+)', xs)
        if m and m.group(2) == 'EnCok':
            gercek = os.path.getsize(dosya)
            if gercek > int(m.group(1)) * 1.02 + 200_000:
                farklar.append(f'"en çok" tahmini {int(m.group(1))} bayt, dosya {gercek}')
    ozet = f'{o["width"]}x{o["height"]} {o.get("profile")} {o.get("pix_fmt")}'
    return farklar, ozet


def x264_dogrula(sec, x, kare, fps, profil, gunluk):
    """x264'ün dosyaya yazdığı ayarlar, programın verdikleriyle (x264'ün kendi düzeltmeleri hesaba katılarak)."""
    f = []
    ek = x.get('EkParametreler') or ''
    komut = next((y for y in gunluk if y.startswith('Komut ') and 'libx264' in y), '')
    m = re.search(r'-x264-params (\S+)', komut)
    params = dict(p.split('=', 1) for p in (m.group(1).split(':') if m else []) if '=' in p)
    mod = x['Mod']
    rc = sec.get('rc')
    bek_rc = {'Crf': 'crf', 'SabitQp': 'cqp', 'OrtalamaBitHizi': 'abr', 'IkiGecis': '2pass', 'UcGecis': '2pass', 'Cbr': 'cbr'}[mod]
    if mod == 'OrtalamaBitHizi' and params.get('vbv-bufsize') and (
            not params.get('vbv-maxrate') or int(params['vbv-maxrate']) <= int(x['BitHizi'])):
        # x264: tampon var ama en yüksek bit hızı yok ya da ortalamadan düşük → "assuming CBR"
        bek_rc = {'abr', 'cbr'}
    if mod == 'Crf' and 'crf' in params and float(params['crf']) < 1 and profil != 'high10':
        # 8 bitte CRF 0 kayıpsızdır: x264 sabit QP 0'a geçer
        bek_rc = 'cqp'
    if isinstance(bek_rc, set):
        if rc not in bek_rc:
            f.append(f'x264 rc={rc} (beklenen {bek_rc})')
    elif rc != bek_rc:
        f.append(f'x264 rc={rc} (beklenen {bek_rc})')
    if mod == 'Crf' and 'crf' in params and sec.get('crf') and abs(float(sec['crf']) - float(params['crf'])) > 0.01:
        f.append(f'x264 crf={sec["crf"]} (komutta {params["crf"]})')
    if mod == 'SabitQp' and 'qp' in params and sec.get('qp') and int(sec['qp']) != min(int(params['qp']), 69):
        f.append(f'x264 qp={sec["qp"]} (komutta {params["qp"]})')
    # programın hesapladığı anahtar kare aralığı
    if not ek and 'keyint' in params and sec.get('keyint') not in (None, 'infinite'):
        if int(sec['keyint']) != int(params['keyint']):
            f.append(f'x264 keyint={sec["keyint"]} (komutta {params["keyint"]})')
    if not ek and 'scenecut' in params and 'scenecut' in sec and kare.get('IntraYenileme') is False:
        if int(sec['scenecut']) != int(params['scenecut']):
            f.append(f'x264 scenecut={sec["scenecut"]} (komutta {params["scenecut"]})')
    if not ek and 'threads' in params:
        isteneN = int(params['threads'])
        if isteneN >= 1 and int(sec.get('threads', 0)) != min(isteneN, 128) and 'sliced_threads' not in sec:
            pass  # x264 bazı durumlarda (ör. dilimli iş parçacığı, 1 B-kare) sayıyı değiştirir: bilgi
    # profilin sınırları: dosya seçilen profilde
    if profil == 'baseline' and not ek:
        for anahtar, bek in (('bframes', '0'), ('cabac', '0'), ('8x8dct', '0'), ('weightp', '0'), ('interlaced', '0')):
            if sec.get(anahtar) not in (bek, None):
                f.append(f'Baseline profilde x264 {anahtar}={sec.get(anahtar)}')
    if profil == 'main' and not ek and sec.get('8x8dct') not in ('0', None):
        f.append(f'Main profilde x264 8x8dct={sec.get("8x8dct")}')
    if x.get('Gecmeli') and profil != 'baseline' and not ek and sec.get('interlaced') in ('0', None):
        f.append('geçmeli seçili ama x264 interlaced=0')
    if x.get('Gecmeli') and profil != 'baseline' and not ek:
        if x.get('KisitliIntra') and sec.get('constrained_intra') not in ('0', None):
            f.append('geçmelide kısıtlı intra açık kalmış (x264 çözülemeyen kare üretebiliyor)')
        if x.get('IsParcacigi') != 1 and sec.get('me_range') and int(sec['me_range']) < 16:
            f.append(f'geçmelide arama aralığı {sec["me_range"]} (en az 16 olmalı)')
    return f


def ses_dogrula(d, gunluk, ayar, k, src, kaynak_bilgi, bilgi, dosya, ff, sure_v, kaynak_yolu):
    f = []
    s = ayar['Ses']
    harici_var = bool(k.get('harici'))
    kaynak_ses = akislar(src, 'audio')
    sessiz = s['Kaynak'] == 'Sessiz'
    harici = not sessiz and s['Kaynak'] == 'AyniAdliDosyaOncelikli' and harici_var
    if harici:
        hb = kaynak_bilgi[k['ad'] + ':harici']
        secilen = akislar(hb, 'audio')[0]
        secilen_bilgi = hb
    elif not sessiz and kaynak_ses:
        # program kayıpsız izi seçer (yoksa ilki)
        secilen = next((a for a in kaynak_ses if a.get('codec_name', '').startswith('pcm_')
                        or a.get('codec_name') in ('flac', 'alac', 'wavpack', 'tta', 'truehd', 'mlp', 'ape', 's302m')), kaynak_ses[0])
        secilen_bilgi = src
    else:
        secilen = None
        secilen_bilgi = None
    oa = akislar(bilgi, 'audio')
    if secilen is None:
        if oa:
            f.append(f'ses olmamalı, {len(oa)} ses izi var')
        return f
    if len(oa) != 1:
        f.append(f'{len(oa)} ses izi (beklenen 1)')
        return f
    a = oa[0]
    if (a.get('tags') or {}).get('language') != 'eng':
        f.append(f'ses dili {(a.get("tags") or {}).get("language")}')
    komut = next((y for y in gunluk if y.startswith('Komut ') and ('-c:a' in y)), '')
    s_hz = int(secilen.get('sample_rate') or 0)
    s_kanal = int(secilen.get('channels') or 0)
    s_duzen = secilen.get('channel_layout')
    fdk = s['Kodlayici'] == 'FdkAac'
    # beklenen örnekleme hızı
    hz = s['OrnekHizi'] or s_hz
    if fdk:
        hz = fdk_hizi(hz)
    o_hz = int(a.get('sample_rate') or 0)
    fdk_mod = 0 if s['Fdk']['Mod'] == 'Cbr' else int(s['Fdk']['Mod'][3:])
    # beklenen kanal düzeni
    if s['Kanal'] == 'Mono':
        duzen, kanal = 'mono', 1
    elif s['Kanal'] == 'Stereo':
        duzen, kanal = 'stereo', 2
    elif fdk:
        duzen = fdk_duzeni(s_duzen, s_kanal)
        kanal = DUZEN_KANAL[duzen]
    else:
        duzen, kanal = s_duzen, s_kanal
    if fdk:
        if a.get('codec_name') != 'aac':
            f.append(f'ses {a.get("codec_name")} (beklenen aac)')
            return f
        bek_profil, hz, bek_mod, bit_en_az = fdk_karari(s['Fdk']['Profil'], hz, duzen, fdk_mod, s['Fdk']['BitHizi'])
        if bek_mod:
            if f' -vbr {bek_mod} ' not in komut + ' ':
                f.append(f'komutta VBR kipi (beklenen -vbr {bek_mod})')
        elif bit_en_az:
            m = re.search(r'-b:a (\d+)k', komut)
            if not m or int(m.group(1)) < bit_en_az:
                f.append(f'komutta bit hızı {m.group(1) if m else "-"} (ELD SBR\'siz en az {bit_en_az})')
        m = re.search(r'-profile:a (\S+)', komut)
        if not m or m.group(1) != bek_profil:
            f.append(f'komutta profil {m.group(1) if m else "-"} (beklenen {bek_profil})')
        asc = asc_oku(a.get('extradata'))
        bek_aot = {'aac_low': 2, 'aac_he': 5, 'aac_he_v2': 29, 'aac_ld': 23, 'aac_eld': 39}[bek_profil]
        sinyal = s['Fdk']['Sinyal']
        if asc is None:
            f.append('AAC AudioSpecificConfig okunamadı')
        else:
            aot, kc = asc
            kabul = {bek_aot}
            if bek_profil in ('aac_he', 'aac_he_v2') and sinyal in ('implicit', 'explicit_sbr'):
                kabul |= {2, 5}
            if aot not in kabul:
                f.append(f'AAC nesne türü {aot} (beklenen {bek_aot}, profil {bek_profil})')
            bek_kc = {1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 6, 7: 11, 8: 12}.get(kanal)
            if bek_profil == 'aac_he_v2':
                # parametrik stereo: çekirdek mono, ASC'de kanal yapılandırması 1 (her sinyalleşmede)
                bek_kc = 1
            if duzen in ('7.1', '7.1(wide)', '5.1.2(back)') and kc in (0, 7, 12, 13, 14):
                pass  # 8 kanal: düzene göre farklı yapılandırma (0: PCE)
            elif aot != 29 and kc != bek_kc and not (kc == 0 and kanal > 2):
                f.append(f'AAC kanal yapılandırması {kc} (beklenen {bek_kc}, {duzen})')
        if o_hz not in (hz, hz // 2):
            f.append(f'örnekleme hızı {o_hz} (beklenen {hz})')
        elif o_hz != hz and not (bek_profil in ('aac_he', 'aac_he_v2') and sinyal == 'implicit'):
            f.append(f'örnekleme hızı {o_hz} (beklenen {hz})')
        if s['Fdk']['Mod'] == 'Cbr':
            m = re.search(r'-b:a (\d+)k', komut)
            if not m:
                f.append('komutta -b:a yok')
            else:
                bek = int(m.group(1)) * 1000
                gercek = int(a.get('bit_rate') or 0)
                sure = akis_suresi(a, bilgi)
                en_az = any(y.startswith('NOT=') and 'en az' in y and 'kb/s kodlar' in y for y in gunluk)
                pay = max(0.12 * bek, 4000) if not en_az else max(0.25 * bek, 8000)
                if sure >= 2 and gercek and abs(gercek - bek) > pay:
                    f.append(f'AAC bit hızı {gercek} (komutta {bek})')
    else:
        bek_codec = {'Bit16': 'pcm_s16le', 'Bit24': 'pcm_s24le', 'Bit32': 'pcm_s32le', 'Float32': 'pcm_f32le'}[s['WavBit']]
        if a.get('codec_name') != bek_codec:
            f.append(f'ses {a.get("codec_name")} (beklenen {bek_codec})')
        if o_hz != hz:
            f.append(f'örnekleme hızı {o_hz} (beklenen {hz})')
        if int(a.get('channels') or 0) != kanal:
            f.append(f'{a.get("channels")} kanal (beklenen {kanal})')
    # süre: sesin bittiği an (başlangıç + süre)
    o_son = float(a.get('start_time') or 0) + akis_suresi(a, bilgi)
    if harici:
        bek_son = sure_v
    elif s['Gecikme'] != 0:
        bek_son = dosya_suresi(src)
    else:
        # sesin çözülünce bittiği an (kabın yazdığı süre MPEG-TS'de güvenilmez): FFmpeg'in zaman çizelgesinde
        bek_son = ff.ses_bitisi(kaynak_yolu, int(secilen.get('index', 0)))
    if bek_son and abs(o_son - bek_son) > 0.06:
        f.append(f'ses {o_son:.3f} sn\'de bitiyor (beklenen {bek_son:.3f})')
    # yükseklik ve gerçek tepe
    i, tepe = ff.r128(dosya)
    karar = next((y[len('ESITLEME_KARAR='):] for y in gunluk if y.startswith('ESITLEME_KARAR=')), None)
    satir = next((y[len('Eşitleme satırı: '):] for y in gunluk if y.startswith('Eşitleme satırı: ')), '')
    tavan = s['SinirlayiciTavan']
    if s['Esitleme'] and karar and i is not None and i > -69:
        hedef = s['EsitlemeHedef']
        ana = karar.split('/')[0]
        ulasilamadi = 'ulaşılamadı' in satir
        telafi = next((float(y[len('ESITLEME_TELAFI='):]) for y in gunluk if y.startswith('ESITLEME_TELAFI=')), 0.0)
        sinirda = abs(abs(telafi) - 6) < 0.005  # telafi en çok ± 6 dB: hedefe 0,5 LU (programın payı) içinde kalabilir
        if ana == 'Dogrusal' and not ulasilamadi:
            # 0.13.0: program sonucu hedeften 0,1 LU'dan uzaksa "hedefe ulaşılamadı" yazar (eskiden 0,5)
            if abs(i - hedef) > 0.105:
                f.append(f'yükseklik {i:.2f} LUFS (hedef {hedef}, karar {karar})')
        elif ana == 'Dogrusal' and ulasilamadi and not sinirda:
            # telafi sınırına (± 6 dB) gelinmeden hedefe ulaşılamadıysa nedeni incelenmeli (denemeler bitti, ölçüm...)
            bitti = any(y.startswith('Eşitleme ayrıntısı') for y in gunluk) and any('Denemeler bitti' in y for y in gunluk)
            f.append(f'hedefe ulaşılamadı: yükseklik {i:.2f} LUFS (hedef {hedef}, telafi {telafi:+.2f}, karar {karar}'
                     + (', denemeler bitti' if bitti else '') + ')')
        else:
            # dokunulmayan seste tepe koruması sınırlayıcıyı tavanın altına çektiyse yükseklik o kadar düşebilir
            pay = 0.6 + (tepe_payi(gunluk) if '/Sinirli' in karar else 0)
            if ana == 'Hedefte' and abs(i - hedef) > pay:
                f.append(f'yükseklik {i:.2f} LUFS, karar Hedefte (hedef {hedef})')
            elif ana == 'ZatenYuksek' and i < hedef - pay:
                f.append(f'yükseklik {i:.2f} LUFS, karar ZatenYuksek (hedef {hedef})')
        if ulasilamadi and i > hedef + 0.1 and not (sinirda and telafi < 0):
            f.append(f'"hedefe ulaşılamadı" ama yükseklik {i:.2f} > hedef {hedef}')
    yukseltildi = s['Esitleme'] and karar is not None and karar.startswith('Dogrusal')
    if (s['Sinirlayici'] or yukseltildi) and tepe is not None and tepe > tavan + 0.001:
        if not (s['Seviye'] > 0 and not s['Sinirlayici'] and not yukseltildi):
            f.append(f'gerçek tepe {tepe:.3f} dBTP > tavan {tavan} (karar {karar})')
    return f


def tepe_payi(gunluk):
    y = next((y for y in gunluk if y.startswith('TEPE_PAYI=')), '')
    m = re.match(r'TEPE_PAYI=(\S+)', y)
    return float(m.group(1)) if m else 0.0


def nvenc_dogrula(gunluk, ayar):
    f = []
    komut = next((y for y in gunluk if y.startswith('Komut 1/')), '')
    n = ayar['Video']['Nvenc']
    if '-c:v h264_nvenc' not in komut:
        return ['komutta h264_nvenc yok']
    kayipsiz = n['Ayar'] == 'lossless'
    if kayipsiz and ('-profile:v' in komut or '-maxrate' in komut or '-bufsize' in komut or '-spatial-aq 1' in komut):
        f.append('kayıpsızda profil / bit hızı / AQ olmamalı')
    if n['Mod'] == 'Cbr' and not kayipsiz and ('-maxrate' in komut or '-bufsize' not in komut):
        f.append('CBR: -maxrate olmamalı, -bufsize olmalı')
    m = re.search(r'-bf (\d+)', komut)
    if '-weighted_pred 1' in komut and m and m.group(1) != '0':
        f.append('ağırlıklı tahmin + B-kare')
    p = re.search(r'-profile:v (\S+)', komut)
    if p and p.group(1) in ('baseline', 'high444p') and '-coder cavlc' not in komut:
        f.append(f'{p.group(1)}: CAVLC olmalı')
    dpb = re.search(r'-dpb_size (\d+)', komut)
    if m and int(m.group(1)) > 0 and n['BRefModu'] != 'disabled' and dpb and int(dpb.group(1)) < 3:
        f.append('B-kare referans: DPB en az 3')
    if '-hwaccel cuda' in komut and not n['GpuCozme']:
        f.append('ekran kartıyla çözme seçili değil ama komutta var')
    return f


# ================================================================================================= özet ve kapsam


def secenekler_oku(dosya):
    """Secenekler.cs: liste adı → seçeneklerin değerleri (Smoke'un Ayarla biçiminde; O: null)."""
    metin = open(dosya, encoding='utf-8').read()
    sonuc = {}
    for m in re.finditer(r'public static Secenek\[\] (\w+) \{ get; \} = L\((.*?)\);', metin, re.S):
        ad, govde = m.group(1), m.group(2)
        degerler = []
        if '.. new[]' in govde:
            degerler += re.findall(r'\("([^"]*)", "', govde.split('..')[0])
            degerler += re.findall(r'"([0-9.]+)"', govde.split('.. new[]')[1].split('}')[0])
        else:
            for d in re.findall(r'\(\s*([^,()]+?|"[^"]*")\s*,\s*"', govde):
                d = d.strip()
                if d == 'O':
                    degerler.append('null')
                elif d.startswith('"'):
                    degerler.append(d.strip('"'))
                elif '.' in d:
                    degerler.append(d.split('.')[-1])
                else:
                    degerler.append(d)
        sonuc[ad] = degerler
    return sonuc


def duz(sozluk, onek=''):
    for anahtar, deger in sozluk.items():
        yol = onek + anahtar
        if isinstance(deger, dict):
            yield from duz(deger, yol + '.')
        else:
            yield yol, deger


def deger_metni(d):
    if d is None:
        return 'null'
    if isinstance(d, bool):
        return 'true' if d else 'false'
    if isinstance(d, float) and d.is_integer():
        return str(int(d))
    return str(d)


def ozet(arg):
    with open(arg.durumlar, encoding='utf-8') as f:
        durumlar = json.load(f)
    sonuclar = []
    for dosya in sorted(glob.glob(arg.sonuc)):
        with open(dosya, encoding='utf-8') as f:
            sonuclar += json.load(f)
    yapilan = {s['ad'] for s in sonuclar}
    eksik_deneme = [d['ad'] for d in durumlar if d['ad'] not in yapilan]
    sorunlu = [s for s in sonuclar if not s['tamam']]
    tablo = ['| Grup | Deneme | Sorunlu |', '|---|---|---|']
    for g in ('video', 'ses', 'capraz', 'varsayilan', 'kenar', 'nvenc', 'derin'):
        n = sum(1 for s in sonuclar if s['grup'] == g)
        h = sum(1 for s in sorunlu if s['grup'] == g)
        tablo.append(f'| {g} | {n} | {h} |')

    # ----------------------------------------------------------------- kapsam: her ayarın her değeri gerçek kodlamada
    alanlar = [json.loads(x) for x in open(arg.alanlar, encoding='utf-8') if x.strip().startswith('{')]
    secenekler = secenekler_oku(arg.secenekler)
    liste_eslemesi = {}
    for grup in (VIDEO, SES, NVENC, SABIT):
        for a in grup:
            if a.liste:
                liste_eslemesi.setdefault(a.liste, set()).add(a.yol)
    tanimli = {}
    for grup in (VIDEO, SES, NVENC, CAPRAZ, SABIT):
        for a in grup:
            tanimli.setdefault(a.yol, set()).update(a.degerler)
    kapsam_eksik = []
    for liste in secenekler:
        if liste not in liste_eslemesi:
            kapsam_eksik.append(f'Secenekler.{liste}: hiçbir ayara bağlanmamış (denenmiyor)')
    # Arayüzün kendi alanları (ayar dosyasına yazılmaz) ve sürüm: kapsamda yok
    atla = {'Surum', 'Video.Cozunurluk', 'Video.KareHiziModu', 'Video.KareHizi'}
    for alan in alanlar:
        yol = alan['yol']
        if yol in atla:
            continue
        if yol not in tanimli:
            kapsam_eksik.append(f'{yol}: deneme listesinde yok (yeni ayar mı?)')
            continue
        if alan['degerler']:
            for e in alan['degerler']:
                if e not in tanimli[yol]:
                    kapsam_eksik.append(f'{yol}={e}: denenecek değerlerde yok')
        if alan['tur'] == 'Boolean':
            for e in ('true', 'false'):
                if e not in tanimli[yol]:
                    kapsam_eksik.append(f'{yol}={e}: denenecek değerlerde yok')
        if alan['bos'] and 'null' not in tanimli[yol] and alan['tur'] != 'String':
            kapsam_eksik.append(f'{yol}=null (boş): denenecek değerlerde yok')
    for liste, yollar in liste_eslemesi.items():
        for e in secenekler.get(liste, []):
            for yol in yollar:
                if e not in tanimli.get(yol, set()):
                    kapsam_eksik.append(f'{yol}={e} (Secenekler.{liste}): denenecek değerlerde yok')
    # gerçekten denendi mi: başarılı denemelerin ayarlarında (son hâli) ya da --ayarla listesinde
    gorulen = {}
    for s in sonuclar:
        if not s['tamam'] or s['grup'] == 'nvenc' and not s.get('ayar'):
            continue
        tur = 'komut' if s['grup'] == 'nvenc' else 'kodlama'
        if s.get('ayar'):
            for yol, deger in duz(s['ayar']):
                gorulen.setdefault((yol, deger_metni(deger)), set()).add(tur)
        for a in s['ayarla']:
            yol, _, deger = a.partition('=')
            gorulen.setdefault((yol, deger), set()).add(tur)
    for yol, degerler in sorted(tanimli.items()):
        for deger in sorted(degerler):
            anahtar = (yol, deger)
            # sayı biçimleri: "1" ile "1.0" aynı
            esit = [g for g in gorulen if g[0] == yol and (g[1] == deger or sayi_esit(g[1], deger))]
            turler = set().union(*(gorulen[g] for g in esit)) if esit else set()
            if (yol.startswith('Video.Nvenc.') or anahtar == ('Video.Kodlayici', 'Nvenc')) and 'komut' in turler:
                continue
            if 'kodlama' not in turler:
                kapsam_eksik.append(f'{yol}={deger}: hiçbir başarılı gerçek kodlamada denenmedi')

    satirlar = ['', '===== KOMBİNASYON DENEMESİ =====', *tablo, '']
    if eksik_deneme:
        satirlar.append(f'Çalışmayan deneme: {len(eksik_deneme)} ({", ".join(eksik_deneme[:20])})')
    for s in sorunlu:
        satirlar.append(f'FARK {s["ad"]} [{s["kaynak"]}, {s["profil"]}]: ' + ' | '.join(s['farklar']))
        satirlar.append('     ayarlar: ' + ' '.join(s['ayarla']))
    satirlar.append('')
    satirlar.append(f'Kapsam: {len(tanimli)} ayar, {sum(len(v) for v in tanimli.values())} değer; arayüzde {len(secenekler)} seçenek listesi')
    for e in kapsam_eksik:
        satirlar.append('EKSİK ' + e)
    print('\n'.join(satirlar))
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as f:
            f.write('\n'.join(satirlar[:400]) + '\n')
    toplam = len(sonuclar)
    print(f'\n{toplam} deneme, {len(sorunlu)} sorunlu, {len(eksik_deneme)} çalışmadı, kapsamda {len(kapsam_eksik)} eksik')
    return 1 if sorunlu or eksik_deneme or kapsam_eksik else 0


def sayi_esit(a, b):
    try:
        return abs(float(a) - float(b)) < 1e-9
    except ValueError:
        return False


def main():
    p = argparse.ArgumentParser()
    alt = p.add_subparsers(dest='komut', required=True)
    u = alt.add_parser('uret')
    u.add_argument('--manifest', required=True)
    u.add_argument('--cikti', required=True)
    c = alt.add_parser('calistir')
    c.add_argument('--durumlar', required=True)
    c.add_argument('--smoke', required=True)
    c.add_argument('--kaynak', required=True)
    c.add_argument('--ffmpeg', required=True)
    c.add_argument('--sonuc', required=True)
    c.add_argument('--calisma', default='komb_cikti')
    c.add_argument('--parca', default='1/1')
    c.add_argument('--paralel', type=int, default=2)
    c.add_argument('--zaman', type=int, default=900)
    c.add_argument('--sakla', action='store_true')
    o = alt.add_parser('ozet')
    o.add_argument('--durumlar', required=True)
    o.add_argument('--sonuc', required=True)
    o.add_argument('--alanlar', required=True)
    o.add_argument('--secenekler', required=True)
    a = p.parse_args()
    if a.komut == 'uret':
        uret(a)
    elif a.komut == 'calistir':
        calistir(a)
    else:
        sys.exit(ozet(a))


if __name__ == '__main__':
    main()
