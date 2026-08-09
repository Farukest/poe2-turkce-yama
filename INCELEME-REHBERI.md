# Çeviri İnceleme Rehberi

Deneme koşusunu incelerken bakılacaklar. Sıra önem sırasıdır — yukarıdakiler
düzeltilmezse 23 saatlik tam koşuyu tekrarlamak gerekir, aşağıdakiler sonradan
da düzeltilebilir.

Rapor: `work/corpus/inceleme.md`

---

## 1. Terim seçimleri — EN ÖNEMLİ

**Neden önce bu:** Sözlükteki 341 karşılığı ben seçtim, oyunu ben oynamıyorum.
Bir terim yanlışsa metnin **tamamında** yanlış olur ve düzeltmek tam koşuyu
tekrarlamak demektir. Diğer her şey sonradan yamalanabilir, bu yamalanamaz.

Rapordaki `keywordpopups|Term` ve `keywordpopups|Definition` bölümlerine bak.
Kendine sorman gereken: **oyunda bu terimi görsem ne kastedildiğini anlar mıyım?**

Özellikle tartışmalı olabilecek seçimlerim:

| İngilizce | Benim seçimim | Neden şüpheli |
|---|---|---|
| Strike | Darbe | "Hit" ile çakışmasın diye ayırdım, ama oyunda ikisi de "vuruş" gibi |
| Ward | Koruma | "Armour = Zırh" ile karışabilir |
| Spirit | Ruh | PoE2'ye özgü kaynak, alışılmış bir karşılığı yok |
| Recoup | Telafi | Alternatif: "Geri Kazanım" (ama o Recovery'de kullanıldı) |
| Rage | Öfke | Charges bağlamında "Öfke Yükü" tuhaf gelebilir |
| Glory | Şan | Huntress kaynağı, alternatif "Zafer" |
| Presence | Varlık | "Varlığında" cümle içinde nasıl duruyor |
| Elusive | Ele Geçmez | Sıfat olarak akıcı mı |
| Ranger | Korucu | Huntress "Avcı" olduğu için ayırdım |

Değiştirmek istediğin varsa `sozluk.tsv` içindeki satırı düzenle, bana söyle.

## 2. Mekanik doğruluğu

Bir stat açıklaması yanlış çevrilirse oyuncu yanlış build kurar. `activeskills`,
`gemeffects`, `buffdefinitions` bölümlerine bak.

Özellikle kontrol et:

- **increased / reduced** ile **more / less** ayrımı korunmuş mu? Bunlar oyunda
  farklı çarpanlar. "artırılmış" ile "daha fazla" karışmamalı.
- Sayı ve yüzde kalıpları doğru mu: `%50 artırılmış` (işaret sayıdan önce)
- Bir cümlenin anlamı tersine dönmüş mü (çok olur: "cannot" atlanması gibi)

## 3. Arayüz metinlerinde taşma

`clientstrings|Text` bölümü. Türkçe İngilizceden uzun olur; buton ve etiket
metinleri ekrana sığmayabilir.

Rapor `UZUNLUK` uyarısıyla 1,45 katından uzun olanları işaretliyor. Bunlara bak
ve "bu bir butona sığar mı" diye düşün. Sığmayacaklar için daha kısa karşılık
gerekir.

## 4. Diyalog akıcılığı

`npctextaudio`, `npctalkdialoguetextaudio`, `flavourtext` bölümleri.

Buradaki soru doğruluk değil, **Türkçe gibi mi duruyor**. Çeviri kokusu olan
cümleler ("Bizim için bakmaya çalıştığını bilmek yardımcı oluyor" gibi) modelin
tavanı — sözlükle düzelmez. Ne kadarını kabul edilebilir bulduğuna sen karar
vereceksin.

Bu, tam koşudan sonra da düzeltilebilir bir alan (tek tek yeniden çevirtilebilir),
o yüzden burada mükemmeliyetçi olmana gerek yok. Genel tona bak.

## 5. İsim koruması

Otomatik denetleniyor (`ISIM KAYIP` uyarısı) ama gözle de doğrula:

- Açıklama metninin **içinde** geçen `Chaos Orb`, `Exalted Orb`, `Waystone`
  İngilizce kalmış mı?
- Yetenek adları (`Fireball` gibi) diyalog içinde İngilizce mi?

`currencyitems|Description` ve `currencyitems|Directions` bölümleri bunun için.

## 6. Çevrilen adlar kulağa nasıl geliyor

`monstervarieties|Name` ve `worldareas|Name` bölümleri. Bunları çevirmeye karar
vermiştik (hikâyenin parçası, dış kaynakta aranmıyor).

Bak bakalım canavar ve bölge adları saçma mı duruyor. Saçmaysa bunları da
`ceviri-disi.txt`'e ekleyip İngilizce bırakabiliriz — karar senin.

## 7. Hitap tutarlılığı

Oyunun üslubu senli benli olmalı ("Kazanırsın", "Kazanırsınız" değil).
Otomatik denetim `HITAP` uyarısıyla resmi kullanımları işaretliyor.

---

## Rapordaki uyarı türleri

| Uyarı | Anlamı | Ciddiyeti |
|---|---|---|
| `ISIM KAYIP` | İngilizce kalması gereken ad çevrilmiş | Yüksek |
| `SOZLUK` | Zorunlu karşılık kullanılmamış | Orta — kaba denetim, yanlış alarm olabilir |
| `CEVRILMEMIS` | Metin İngilizce kalmış | Orta |
| `UZUNLUK` | Arayüz metni çok uzamış | Orta |
| `YUZDE` | İşaret sayıdan sonra | Düşük |
| `HITAP` | Resmi (siz) kullanılmış | Düşük |

`SOZLUK` uyarıları kabadır: kelimenin çekim eki almış hâlini (`Ateş Hasarını`)
yakalayamayabilir, ya da terim cümlede hiç geçmiyordur. Sayısı yüksekse
endişelenme, örneklerine bak.

---

## Sonuç

İncelemeyi bitirince bana şunu söyle:

1. Değiştirilecek terimler (varsa)
2. İngilizce bırakılmasını istediğin ek alanlar (varsa)
3. Tam koşuya geçelim mi

Terim değişikliği ucuz (sadece `sozluk.tsv` düzenlenir), ama **tam koşudan
sonra pahalı** — o yüzden şimdi karar vermek önemli.
