"""
Oyunun fontlarına eksik Türkçe gliflerini ekler: Ğ ğ İ Ş ş

Yaklaşım: yeni şekil çizmek yerine fontun KENDİ parçalarından bileşik glif
üretmek. Fontin'de S, s, G, g, I ve aksan şekilleri (˘ breve U+02D8,
˙ nokta U+02D9, ¸ sedil U+00B8) zaten var. Bunları üst üste bindirince
sonuç fontun kendi çizgisiyle uyumlu oluyor, yamalı durmuyor.

OptimusPrinceps (başlık/düğme fontu) bir istisna: sedili var ama breve ve
nokta aksanı yok. Eksik aksan konturları FrizQuadrataITC'den ithal ediliyor.
Bu bir kayıp değil, kazanç: o glifler yamalanmazsa oyun harfin TAMAMINI
FrizQuadrata'dan alıyor (kullanıcı "GİRİŞ YAP"taki ş'nin tuhaf durduğunu
böyle fark etti). Sadece aksanı ithal edince taban harf fontun kendisi kalıyor.

Küçük harf glifleri OptimusPrinceps'te küçük kapital olduğu için aksan da
aynı oranda küçültülüyor.

Kullanım:
    python font_yamala.py <girdi_klasoru> <cikti_klasoru>
"""
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.transformPen import TransformPen

# Üretilecek glifler: (unicode, taban karakter, aksan kodu, konum, buyuk_esdegeri)
# konum: "ust" = tabanın üstüne, "alt" = tabanın altına
# buyuk_esdegeri: küçük harflerde aksan ölçeğini hesaplamak için (None = büyük harf)
YENI_GLIFLER = [
    (0x011E, "G", 0x02D8, "ust", None),   # Ğ
    (0x011F, "g", 0x02D8, "ust", "G"),    # ğ
    (0x0130, "I", 0x02D9, "ust", None),   # İ
    (0x015E, "S", 0x00B8, "alt", None),   # Ş
    (0x015F, "s", 0x00B8, "alt", "S"),    # ş
]

# Aksanla taban arasındaki boşluk (font birimi, em=1000 varsayımıyla ölçekleniyor)
UST_BOSLUK = 60

# Aksanı olmayan fontlara parça verecek font. Latin kapsaması tam ve oyunun
# kendi yedek fontu, yani zaten ekranda görünen çizgi.
VERICI_FONT = "frizquadrataitc.ttf"

# Yamalanacak dosya kalıpları
HEDEF_KALIPLAR = ("fontin-*.ttf", "optimusprinceps*.ttf")


def cmap_sozlugu(font):
    """Tüm cmap alt tablolarını birleştirip kod noktası -> glif adı sözlüğü."""
    birlesik = {}
    for alt in font["cmap"].tables:
        birlesik.update(alt.cmap)
    return birlesik


def sinir_kutusu(glyf, ad):
    """Glifin sınır kutusu. Bileşik gliflerde bileşenleri çözerek hesaplar."""
    g = glyf[ad]
    if g.numberOfContours == 0:
        return None
    g.recalcBounds(glyf)
    return (g.xMin, g.yMin, g.xMax, g.yMax)


def kontur_kopyala(kaynak_font, kaynak_ad, hedef_font, hedef_ad, olcek):
    """Bir glifin konturlarını başka fonta ölçekleyerek kopyalar.

    Bileşik glif olsa bile getGlyphSet() çözerek çizdirdiği için hedefte
    olmayan bileşenlere bağımlılık kalmıyor.
    """
    kalem = TTGlyphPen(None)
    hedef_kalem = kalem if olcek == 1.0 else TransformPen(kalem, (olcek, 0, 0, olcek, 0, 0))
    kaynak_font.getGlyphSet()[kaynak_ad].draw(hedef_kalem)

    hedef_font["glyf"][hedef_ad] = kalem.glyph()
    genislik, lsb = kaynak_font["hmtx"][kaynak_ad]
    hedef_font["hmtx"][hedef_ad] = (int(genislik * olcek), int(lsb * olcek))
    return hedef_ad


def aksan_sagla(font, cmap, kod, verici, onbellek):
    """Aksan glifinin adını döndürür; fontta yoksa vericiden ithal eder."""
    if kod in onbellek:
        return onbellek[kod]

    ad = cmap.get(kod)
    if ad is not None:
        onbellek[kod] = ad
        return ad

    if verici is None:
        onbellek[kod] = None
        return None

    verici_ad = cmap_sozlugu(verici).get(kod)
    if verici_ad is None:
        onbellek[kod] = None
        return None

    olcek = font["head"].unitsPerEm / verici["head"].unitsPerEm
    yeni_ad = "trAksan%04X" % kod
    kontur_kopyala(verici, verici_ad, font, yeni_ad, olcek)
    onbellek[kod] = yeni_ad
    return yeni_ad


def aksan_olcegi(glyf, cmap, taban_ad, buyuk_esdegeri):
    """Küçük kapital tabanlar için aksanın küçültme oranı."""
    if buyuk_esdegeri is None:
        return 1.0
    buyuk_ad = cmap.get(ord(buyuk_esdegeri))
    if buyuk_ad is None:
        return 1.0
    kb, bb = sinir_kutusu(glyf, taban_ad), sinir_kutusu(glyf, buyuk_ad)
    if kb is None or bb is None:
        return 1.0
    buyuk_yukseklik = bb[3] - bb[1]
    if buyuk_yukseklik <= 0:
        return 1.0
    oran = (kb[3] - kb[1]) / buyuk_yukseklik
    # Normal fontlarda küçük harf zaten kendi aksanıyla uyumlu; sadece belirgin
    # küçük kapital farkında (oran < 0.95) küçültmeye değer.
    return oran if oran < 0.95 else 1.0


def bilesik_uret(font, yeni_ad, taban_ad, aksan_ad, konum, em):
    glyf = font["glyf"]
    hmtx = font["hmtx"]

    tb = sinir_kutusu(glyf, taban_ad)
    ab = sinir_kutusu(glyf, aksan_ad)
    if tb is None or ab is None:
        return None, "taban veya aksan bos"

    # Yatayda aksanı tabanın ortasına hizala
    dx = ((tb[0] + tb[2]) - (ab[0] + ab[2])) // 2

    olcek = em / 1000.0
    if konum == "ust":
        # Aksanın altı, tabanın üstünden biraz yukarıda dursun
        dy = int(tb[3] + UST_BOSLUK * olcek - ab[1])
    else:
        # Sedil taban çizgisinden aşağı sarksın; aksan zaten aşağıda çizili
        dy = int(tb[1] - ab[3])

    g = Glyph()
    g.numberOfContours = -1
    g.components = []
    for ad, ox, oy in ((taban_ad, 0, 0), (aksan_ad, dx, dy)):
        c = GlyphComponent()
        c.glyphName = ad
        c.x, c.y = int(ox), int(oy)
        c.flags = 0x004  # ROUND_XY_TO_GRID; ARGS_ARE_XY_VALUES derlemede eklenir
        g.components.append(c)

    glyf[yeni_ad] = g
    hmtx[yeni_ad] = hmtx[taban_ad]  # genişlik tabanla aynı
    return g, None


def fontu_yamala(girdi: Path, cikti: Path, verici: TTFont):
    font = TTFont(str(girdi))
    cmap = cmap_sozlugu(font)
    em = font["head"].unitsPerEm
    glyf = font["glyf"]
    aksan_onbellek = {}

    eklenen, ithal, atlanan = [], [], []
    for kod, taban_kar, aksan_kod, konum, buyuk_esdegeri in YENI_GLIFLER:
        isim = f"uni{kod:04X}"
        if kod in cmap:
            atlanan.append(f"{chr(kod)} (zaten var)")
            continue
        taban_ad = cmap.get(ord(taban_kar))
        if taban_ad is None:
            atlanan.append(f"{chr(kod)} (taban yok: {taban_kar})")
            continue

        aksan_ad = aksan_sagla(font, cmap, aksan_kod, verici, aksan_onbellek)
        if aksan_ad is None:
            atlanan.append(f"{chr(kod)} (aksan yok: U+{aksan_kod:04X})")
            continue
        if aksan_ad.startswith("trAksan") and aksan_ad not in ithal:
            ithal.append(aksan_ad)

        # Küçük kapital tabanlarda aksanı da küçült
        olcek = aksan_olcegi(glyf, cmap, taban_ad, buyuk_esdegeri)
        if olcek != 1.0:
            kucuk_ad = f"{aksan_ad}_kck"
            if kucuk_ad not in glyf.glyphOrder:
                kontur_kopyala(font, aksan_ad, font, kucuk_ad, olcek)
            aksan_ad = kucuk_ad

        _, hata = bilesik_uret(font, isim, taban_ad, aksan_ad, konum, em)
        if hata:
            atlanan.append(f"{chr(kod)} ({hata})")
            continue

        # cmap'e ekle. Glif sırasını burada DEĞİŞTİRME: glyf[isim] = g zaten
        # kendi glyphOrder'ına ekliyor; font seviyesinde de eklersek iki liste
        # birbirini tutmaz ve maxp derlenirken assert patlar.
        for alt in font["cmap"].tables:
            if alt.isUnicode():
                alt.cmap[kod] = isim
        eklenen.append(chr(kod))

    # Sırayı tek kaynaktan eşitle
    font.setGlyphOrder(list(glyf.glyphOrder))

    cikti.parent.mkdir(parents=True, exist_ok=True)
    font.save(str(cikti))
    return eklenen, ithal, atlanan


def main():
    girdi_kok = Path(sys.argv[1])
    cikti_kok = Path(sys.argv[2])

    hedefler = []
    for kalip in HEDEF_KALIPLAR:
        hedefler.extend(sorted(girdi_kok.glob(kalip)))
    if not hedefler:
        print(f"HATA: {girdi_kok} altinda hedef font yok ({', '.join(HEDEF_KALIPLAR)})")
        return 1

    verici_yol = girdi_kok / VERICI_FONT
    verici = TTFont(str(verici_yol)) if verici_yol.exists() else None
    if verici is None:
        print(f"UYARI: verici font yok ({verici_yol}) - eksik aksanlar ithal edilemeyecek")

    for f in hedefler:
        eklenen, ithal, atlanan = fontu_yamala(f, cikti_kok / f.name, verici)
        print(f"{f.name}")
        print(f"   eklenen : {' '.join(eklenen) if eklenen else '-'}")
        if ithal:
            print(f"   ithal   : {', '.join(ithal)}")
        if atlanan:
            print(f"   atlanan : {', '.join(atlanan)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
