// FDK AAC'nin hangi ayarla açıldığını ve gerçekte hangi bit hızıyla kodladığını yazar (app/ReliosExport/Core/FdkUyumu.cs
// tablosu bununla üretildi). Kodlayıcı FFmpeg'in libfdk-aacenc.c'sindeki sırayla açılır: MP4 / MOV (ham + açık hiyerarşik
// sinyal), WAV kanal sırası, afterburner açık.
// Derleme: gcc -O2 -o sinirlar sinirlar.c -I<fdk-aac>/libAACenc/include -I<fdk-aac>/libSYS/include libfdk-aac.a -lstdc++ -lm
// Kullanım: ./sinirlar <AOT: 2 LC, 5 HE, 29 HE v2, 23 LD, 39 ELD> [ust]   ("ust": 8 kanal 5.1.2(back), FDK MODE_7_1_TOP_FRONT)
// Çıktı satırı: "aot hz kanal kbps gercek_bps" (CBR) ya da "aot hz kanal vbrN 0", açılmazsa "... H<kod>".
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "aacenc_lib.h"

static int ust;

static int kip(int ch) {
    switch (ch) {
        case 1: return MODE_1;           // mono
        case 2: return MODE_2;           // stereo
        case 3: return MODE_1_2;         // 3.0
        case 4: return MODE_1_2_1;       // 4.0
        case 5: return MODE_1_2_2;       // 5.0
        case 6: return MODE_1_2_2_1;     // 5.1
        case 7: return MODE_6_1;         // 6.1(back)
        default: return ust ? MODE_7_1_TOP_FRONT : MODE_7_1_REAR_SURROUND; // 5.1.2(back) / 7.1 (7.1(wide) aynı sınırlar)
    }
}

static int dene(int aot, int hz, int ch, int vbr, int bps, int *gercek) {
    HANDLE_AACENCODER h;
    *gercek = -1;
    if (aacEncOpen(&h, 0, ch) != AACENC_OK) return 100;
    int r = 0;
    if (aacEncoder_SetParam(h, AACENC_AOT, aot) != AACENC_OK) { r = 1; goto son; }
    if (aacEncoder_SetParam(h, AACENC_SAMPLERATE, hz) != AACENC_OK) { r = 2; goto son; }
    if (aacEncoder_SetParam(h, AACENC_CHANNELMODE, kip(ch)) != AACENC_OK) { r = 3; goto son; }
    if (aacEncoder_SetParam(h, AACENC_CHANNELORDER, 1) != AACENC_OK) { r = 4; goto son; }
    if (vbr) { if (aacEncoder_SetParam(h, AACENC_BITRATEMODE, vbr) != AACENC_OK) { r = 5; goto son; } }
    else if (aacEncoder_SetParam(h, AACENC_BITRATE, bps) != AACENC_OK) { r = 6; goto son; }
    if (aacEncoder_SetParam(h, AACENC_TRANSMUX, TT_MP4_RAW) != AACENC_OK) { r = 7; goto son; }
    if (aacEncoder_SetParam(h, AACENC_SIGNALING_MODE, 2) != AACENC_OK) { r = 8; goto son; }
    if (aacEncoder_SetParam(h, AACENC_AFTERBURNER, 1) != AACENC_OK) { r = 9; goto son; }
    if (aacEncoder_SetParam(h, AACENC_METADATA_MODE, 0) != AACENC_OK) { r = 12; goto son; }
    AACENC_ERROR e = aacEncEncode(h, NULL, NULL, NULL, NULL);
    if (e != AACENC_OK) { r = 1000 + (int)e; goto son; }
    *gercek = aacEncoder_GetParam(h, AACENC_BITRATE);
son:
    aacEncClose(&h);
    return r;
}

int main(int argc, char **argv) {
    if (argc < 2) { fprintf(stderr, "kullanim: sinirlar <AOT> [ust]\n"); return 2; }
    int aot = atoi(argv[1]);
    ust = argc > 2 && strcmp(argv[2], "ust") == 0;
    // FFmpeg'in FDK AAC'ye verdiği örnekleme hızları (libfdk-aacenc.c aac_sample_rates)
    int hizlar[] = {8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000};
    for (int s = 0; s < 12; s++)
        for (int ch = ust ? 8 : 1; ch <= 8; ch++) {
            for (int b = 8; b <= 800; b++) {
                int g, r = dene(aot, hizlar[s], ch, 0, b * 1000, &g);
                if (r) printf("%d %d %d %d H%d\n", aot, hizlar[s], ch, b, r);
                else printf("%d %d %d %d %d\n", aot, hizlar[s], ch, b, g);
            }
            for (int v = 1; v <= 5; v++) {
                int g, r = dene(aot, hizlar[s], ch, v, 0, &g);
                if (r) printf("%d %d %d vbr%d H%d\n", aot, hizlar[s], ch, v, r);
                else printf("%d %d %d vbr%d %d\n", aot, hizlar[s], ch, v, g);
            }
        }
    return 0;
}
