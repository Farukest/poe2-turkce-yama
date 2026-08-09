# PoE 2 Türkçe Çeviri — Teknik Notlar

Son güncelleme: 2026-08-06

## Hedef

Kişisel kullanım için tam Türkçeleştirme. Oyun terimleri de Türkçe (tutarlılık
için terim sözlüğüne bağlı olacak). Küçük makine çevirisi hataları kabul edilebilir.

## Oyun kurulumu

| | |
|---|---|
| Yol | `<SteamKütüphanesi>\steamapps\common\Path of Exile 2` |
| Steam app | 2694490 (~150 GB), `AutoUpdateBehavior=0` = her zaman güncel tut |
| İndeks | `Bundles2\_.index.bin` (114 MB, 4.227.137 dosya kaydı) |
| Dil ayarı | `Belgeler\My Games\Path of Exile 2\poe2_production_Config.ini` → `[LANGUAGE] language=en` |

## Araç zinciri

- `.NET SDK 9` (winget ile kuruldu, `Microsoft.DotNet.SDK.9`)
- `tools/LibGGPK3` — https://github.com/aianlinb/LibGGPK3 (bundle okuma/yazma)
- `tools/oodle/oo2core.dll` — Oodle 2.9 native kütüphanesi.
  LibBundle3 bunu `DllImport("oo2core")` ile çağırıyor ve repo ile gelmiyor
  (tescilli, dağıtılamaz). Oodle kullanan herhangi bir oyunun klasöründen
  alınıyor. **PoE2 kendi Oodle'ını exe'ye statik linklediği için oyun
  klasöründen alınamıyor** — doğrulandı, klasörde `oo2core*` yok.
- `src/poe2tr` — kendi CLI aracımız: `info` / `list` / `extract` / `write`

### Tuzak: Index açarken

`new Index(path)` varsayılan `parsePaths:true` ile açılınca
`InvalidDataException: Parsing path failed for 5 files` fırlatıyor.
Çözüm (kütüphanenin kendi yorumunda öneriliyor): `parsePaths:false` ile aç,
sonra `index.ParsePaths()` çağır ve dönen başarısız sayısını yok say.
`src/poe2tr/Program.cs` içindeki `OpenIndex()` bunu yapıyor.

## Metin nerede duruyor

PoE 1'den farklı: tablolar `Data/` kökünde değil, **`data/balance/`** altında.
İndeksteki tüm yollar küçük harf.

| Yer | İçerik |
|---|---|
| `data/balance/*.datc64` | 1021 tablo — İngilizce (temel) |
| `data/balance/<dil>/*.datc64` | **216 tablo** — çevrilmiş sürümler, 69.6 MB |
| `data/statdescriptions/*.csd` | 27 dosya, kökte |
| `data/statdescriptions/specific_skill_stat_descriptions/*.csd` | 562 dosya |
| `data/balance/languages.dat` | Dil tablosu (688 bayt) — dil kodu/adı eşlemesi |
| `metadata/ui/uisettings.<dil>.xml` | Dile özel font ve arayüz ayarları (UTF-16LE) |
| `art/2dart/fonts/*.ttf` | 25 font |

StatDescriptions toplamı 56.1 MB. Çeviri hacmi kabaca **125 MB metin**.

Mevcut diller: french, german, japanese, korean, portuguese, russian, spanish,
thai, traditional chinese (+ temel İngilizce).

Doğrulandı: `data/balance/portuguese/npctextaudio.datc64` içinde Portekizce
diyaloglar UTF-16 düz metin olarak duruyor. Yani çeviri hedefimiz bu tablolar.

En büyük tablolar: `mods` (13.7 MB), `npctextaudio` (12.1 MB),
`npctalkdialoguetextaudio` (7.6 MB), `charactertextaudio` (5.7 MB),
`passiveskills` (5.5 MB), `monstervarieties` (4.7 MB), `baseitemtypes` (2.8 MB).

## Metinlerin içindeki biçimlendirme (kritik)

Oyun metinleri düz cümle değil. `data/statdescriptions/*.csd` örneği:

    # "Supported Skills can only have one active [Totem]"
    # "Supported Skills requires {0} [Glory] to use"
    lang "Portuguese"
    # "Habilidades reforçadas podem ter apenas 1 [Totem|totem] ativo"

| Yapı | Anlamı | Kural |
|---|---|---|
| `{0}`, `{1}` | Sayı yer tutucusu | **Aynen korunmalı** |
| `[Totem\|totem]` | `[İngilizceAnahtar\|GörünenMetin]` | Sol taraf bağlantı hedefi, **sadece sağ taraf çevrilir** |
| `[Strike]` | Boru işaretsiz anahtar; görünen metin anahtarın kendisi | Çevirmek için **`[Strike\|Vuruş]`'a dönüştürülür** — silinmez, olduğu gibi de bırakılmaz |
| `<smaller>{Metin}` | Biçim etiketi + süslü parantez içinde içerik | Etiket aynen kalır, **parantez içi çevrilir** |
| `<i>`, `<white>`, `<b>`, `<default>` | Biçim etiketi | Aynen korunmalı |
| `<rgb(135,134,253)>`, `<font:'fontin'>` | Renk / font | Aynen korunmalı |
| `<xbox_button_a>` | Gamepad tuş simgesi | Aynen korunmalı |
| `\n` | Satır sonu | Konumu korunmalı |

Dikkat: süslü parantez iki farklı anlama geliyor. `{0}` → yer tutucu (dokunma);
`{Metin}` → biçim etiketinin içeriği (çevir). Ayırt edici: içerik sadece rakamsa
yer tutucudur.

Gerçek örnekler:

    Earn all 8 Ascendancy Points by completing the [AscendancyPoints|Trials of Ascendancy]
    Teleport to a enemy and [Strike] them. Consumes [Charges|Power Charges] to perform...
    Location Discovered\n<smaller>{The Withered Willow}
    Spectre: {0}

Ayrıca `.csd` dosyaları **tüm dilleri tek dosyada** tutuyor (`lang "X"` blokları),
`.datc64` tablolarının aksine ayrı klasörde değil.

Sonuç: klasik cümle-cümle çeviren NMT motorları (Opus-MT, NLLB, Argos) bu yapıyı
bozar. Talimat takip edebilen bir model gerekiyor.

## Çeviri hattı

    poe2tr dat-export <index> <cikti.jsonl>   -> her satir bir ceviri birimi
    poe2tr dat-import <index> <giris.jsonl>   -> "tr" alanlarini oyuna yazar

JSONL birimi: `{"id":"mods|98|0","table":"mods","off":98,"row":0,"col":"Name","en":"..."}`
`id` = `tablo|kolonOffseti|satir`. Çeviri motoru `tr` alanını doldurur.

Tam dışa aktarım sonucu (2026-08-06):

| | |
|---|---|
| Metin içeren tablo | 194 (atlanan yok) |
| Çeviri birimi | 142.089 |
| Kaynak karakter | 6.038.592 |
| **Benzersiz metin** | **90.551** (%36,3 tekrar) |

Biçimlendirme içerenler: `{N}` 1.535, `[Anahtar]` 3.808, `[Anahtar|Görünen]` 2.781,
`<etiket>` 4.744, satır sonu 4.617. Yani riskli metin oranı ~%7.

Geri yazma, `DatFile.ApplyStrings` ile: mevcut değişken bölüme dokunulmaz, yeni
metinler sonuna eklenir ve yalnızca ilgili hücrelerin offsetleri güncellenir.
Aynı metin birden çok hücrede geçerse tek kopya yazılır.

## Şema-bağımsız kolon tespiti

Topluluk şeması PoE2'nin son yamasının gerisinde; 4 tablo uyuşmuyordu.
`DatFile.LoadWithoutSchema()` + `DetectStringColumns()` bunu şemasız çözüyor:

1. Satır uzunluğunu dosyadan çıkar (satır sayısına tam bölünen 0xBB ayracı)
2. Her bayt konumunda 8 baytlık pencereyi tüm satırlarda dene; geçerli metin
   offseti verenlerin oranına bak
3. Metin başlangıcı şartı: offset ya 8'dir ya da öncesinde 4 sıfır bayt
   (sonlandırıcı) vardır — bu, rastgele sayıların offset sanılmasını engelliyor

Kayık okumalar sahte aday üretiyor (bir metnin son harfini yakalayıp `"s"`
döndürmek gibi). Üç eleme temizliyor: hem EN hem PT dolu olmalı; anlamlı sayı ve
uzunluk; alanlar 8 bayt olduğu için çakışan adaylardan en çok metin içeren
kazanır; ve aynı içeriği yineleyen aday takma ad sayılıp atılır.

Sonuçlar:

| Tablo | Kolon | Hücre | Karakter |
|---|---|---|---|
| Mods | bayt 98 | 6.330 | 79.165 |
| AlternatePassiveSkills | bayt 24, 128 | 258 | 5.535 |
| EndgameMaps | 7 aday | — | ~16.000 (belirsiz) |

`EndgameMaps` şemadan tam 1 bayt sapıyor (240 vs 239) — şemada tek bir `bool`
eksik. Temiz çözüm o kolonun yerini bulup şemayı yerel yamalamak.
`relicstashtabsubgroup` şemada hiç yok, metin içermiyor olabilir.

## Kapsam: neyin adı çevrilmez

Kullanıcı kararı: **eşya, yetenek ve para birimi ADLARI İngilizce kalır**,
açıklamaları çevrilir. Gerekçe: ticaret sitesinde arama yaparken ve build
rehberi okurken oyundaki isimle aranan isim tutmalı; ismin Türkçesi bir şey
kazandırmıyor, asıl önemli olan o şeyin ne yaptığı.

Kurallar `ceviri-disi.txt` dosyasında (`tablo|Kolon` biçiminde), `dat-export`
otomatik okuyor. 8 kural, 39.057 hücre / 655.197 karakter dışarıda:

| Kolon | İçerik |
|---|---|
| `baseitemtypes\|Name` | Taban eşya adları — para birimleri de burada |
| `mods\|off:98` | Eşya ön/son ekleri ("of the Brute") |
| `words\|Text2` | Sihirli eşya adı kelime havuzu |
| `activeskills\|DisplayedName` | Yetenek adları |
| `passiveskills\|Name` | Pasif ağaç düğüm adları |
| `buffdefinitions\|Name` | Yetenek etkisi adları |
| `mtxtypes\|Name`, `\|Type` | Kozmetik eşya adları |

Çevrilmeye devam edenler (bilinçli tercih): `worldareas.Name` (bölge adları),
`monstervarieties.Name` (canavar adları), `npcs.Name`, `keywordpopups.Term`
(mekanik adları — tanımıyla uyumlu olmalı). Bunlar hikâyenin parçası ve dış
kaynakta aranmıyor.

Çeviri sonrası hacim: **103.032 birim / 5.383.395 karakter**.

## Terim sözlüğü

`sozluk.tsv` — 341 girdi, 16 kategori. Kaynaklar: `KeywordPopups` tablosundaki
759 resmî terim ve korpustaki 1.113 benzersiz köşeli-parantez anahtarı.
Kapsam: kullanım ağırlığıyla **%64,5**. Kalan uzun kuyruk çoğunlukla tekil özel
ad (patron, eşsiz eşya, yer adı) — sözlük gerektirmiyor.

Sözleşme: **Türkçe sütunu İngilizcesiyle aynıysa "olduğu gibi bırak" demek.**
Eşya/yetenek/para adları böyle işaretli; açıklama metinlerinin içinde
geçtiklerinde de çevrilmiyorlar.

Çakışma taraması yapıldı (aynı Türkçe karşılığı paylaşan terimler). Düzeltilenler:

| Çakışma | Çözüm |
|---|---|
| Hit / Strike → "Vuruş" | Strike → **Darbe** |
| Minion / Beast → "Yaratık" | Beast → **Yabani Hayvan** |
| Buff / Augmentation → "Güçlendirme" | Augmentation → **Takviye** |
| Ranger / Huntress → "Avcı" | Ranger → **Korucu** |

Kalan iki çakışma kasıtlı: Critical Hit/Strike eşanlamlı, Augment/Augmentation
aynı kök.

## Yerel çeviri motoru

Karar: API yerine yerel LLM (maliyet tercihi). Ollama 0.32.6 kuruldu.

| Model | Boyut |
|---|---|
| `gemma3:12b` | 8.1 GB |
| `qwen3:14b` | 9.3 GB |

GPU: RTX 3080 **12 GB** (WMI 4 GB diyor, bu bilinen bir Windows hatası —
doğrusu için `nvidia-smi`).

Qwen3 düşünen bir model; `/api/chat` gövdesinde `think: false` ile akıl yürütme
kapatılıyor. Gemma3'te bu alan hata vermiyor, ikisinde de aynı gövde kullanılabilir.

Sözlüksüz çeviri kalitesi neden yetersiz: Qwen3 "Fire Damage" için kendi başına
"Yangın Hasarı" diyor — bizim karşılığımız "Ateş Hasarı".

### Karşılaştırma düzeneği

`scripts/karsilastir.ps1` — katmanlı 50 metin seçer (yer tutuculu, köşeli
parantezli, etiketli, kısa/orta/uzun), aynı istemle her modele gönderir,
çıktıyı denetler ve `work/karsilastirma/rapor.md` üretir.

Doğrulayıcı şunları kontrol ediyor: `{0}` yer tutucuları birebir mi, `[X|Y]`
sol tarafı korunmuş mu, `<etiket>`ler aynen duruyor mu, satır sayısı değişmiş
mi, metin hiç çevrilmemiş mi.

**Tuzak:** Windows PowerShell 5.1, BOM'suz UTF-8 `.ps1` dosyalarını sistem ANSI
kod sayfasıyla okuyor; Türkçe karakterler bozulup betik parse hatasıyla çöküyor.
Yazdıktan sonra BOM eklemek gerekiyor:

    $t = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($p, $t, [Text.UTF8Encoding]::new($true))

## Maskeleme tasarımı — 3 deneme

Metni modele göndermeden önce korunacak parçalar işaretçiye (`⟦0⟧`) çevriliyor,
çeviri sonrası geri konuyor. `src/poe2tr/Masking.cs`.

Üç tasarım denendi, ölçümler:

| Tasarım | Zor 50 metinde başarı |
|---|---|
| 1. Anahtar maskesiz, sadece normalleştirme | model sol tarafı çeviriyordu |
| 2. `⟦0⟧metin⟦1⟧` — parantezler de maskeli | **3/50** |
| 3. `[⟦0⟧\|metin]` — parantezler görünür | **38/50** |

**Neden 2 kötüydü:** açılış ve kapanış ayrı belirteçti, model için doğal
olmayan bir yapı. Uzun metinlerde hataların neredeyse tamamı kapanış
işaretçisinin düşmesiydi (`⟦1⟧ (]) beklenen 6, gelen 4`). Aynı parçaların tek
numarayı paylaşması (dedup) bunu daha da kötüleştirdi — model aynı işaretçiyi
tam 6 kez yazmak zorunda kaldı.

**Neden 3 iyi:** köşeli parantez ve boru işareti açıkta kalınca model tanıdık
bir sözdizimi görüyor, sadece sağdaki metni çeviriyor. Sol taraf işaretçi
olduğu için sözlük onu tetikleyemiyor (`[Rarity|Unique]` → `[Nadirlik|Eşsiz]`
hatası bu yüzden oluyordu).

Maskelenenler: `<etiket>`, `{0}`, `{{`, `}}`, satır sonları, anahtarın sol
tarafı. Aynı parça tekrar ediyorsa aynı numarayı paylaşır; doğrulama geçiş
sayısını karşılaştırır.

Doğrulayıcının yakaladıkları: işaretçi sayısı tutmuyor, uydurma işaretçi,
başıboş `⟦`/`⟧` karakteri, köşeli parantez sayısı değişmiş, anahtar yerine
düz metin yazılmış.

### Kendi kodumda çıkan hatalar (tekrarlanmasın)

1. `s.Replace("{{", Take("{{"))` — `Take()` metinde `{{` olmasa bile çalışıyor,
   hiç eklenmemiş işaretçi kaydediliyordu. İlk testte 20/20 başarısızlığın
   sebebi buydu, modelin çıktısı kusursuzdu.
2. `String.Replace` tüm geçişleri **aynı** işaretçiyle değiştiriyor;
   doğrulayıcı "tam bir kez" istiyordu. Tek geçişli regex + geçiş başına numara.
3. Doğrulayıcı sadece `⟦sayı⟧` kalıbını sayıyordu; modelin ürettiği tek başına
   `⟧` karakteri fark edilmeden oyuna kadar gitti (6 kayıt temizlendi).
4. Model kapanış etiketini (`</1>`) yazmıyor. Ayrıştırıcı onu şart koşmamalı —
   bir açılış etiketinden sonraki metin, bir sonrakine kadar o numaranındır.

## Başarısızlık oranı uzunlukla artıyor

Deneme koşusu ölçümü (tasarım 2 ile):

| Kaynak uzunluğu | Başarısız oranı |
|---|---|
| 0-100 karakter | %6 |
| 100-300 | %26 |
| 300-600 | %45 |
| 1000+ | %75 |

Uzun metin = daha çok işaretçi = bir tanesini kaçırma olasılığı yüksek.
Tasarım 3 bunu büyük ölçüde çözdü. Yeniden deneme mantığı: 1. deneme tüm
partiyi birlikte, sonraki denemeler **tek tek** ve daha yüksek sıcaklıkla
(0,2 → 0,45).

## Ad maskeleme: ölçüldü, yapılmadı

Eşya/yetenek adlarının başka metinlerin *içinde* geçtiğinde de korunması
düşünüldü. `names-export` komutu yazıldı ve ölçüm yapıldı:

- Çeviri dışı kolonlardan 12.447 ad (filtre: büyük harfle başlayan, sözlükte
  çevrilebilir olmayan, çok kelimeli ya da 8+ karakter tek kelime)
- Bunların **4.523'ü** gerçekten başka metinlerin içinde geçiyor
- Ama neredeyse hepsi **kozmetik eşya adı** (`Doryani's Refracted Lasers Rare
  Finisher Effect` gibi), ödül mesajlarının içinde

Kullanıcının gerekçesi ticaret sitesi ve build rehberiyle eşleşmeydi; kozmetik
isimler oraya girmiyor. Ayrıca deneme koşusunda **sıfır isim kaybı** hatası
çıktı — sözlüğün "çevirme" mekanizması zaten çalışıyor.

Karar: yapılmadı. 45 saatlik koşuya düşük değerli bir kazanç için performans
riski (12 bin adı 69 bin metinde aramak) ve karmaşıklık eklemek doğru değildi.
`korunan-adlar.txt` ve `names-export` komutu duruyor, gerekirse kullanılabilir.

**Dikkat:** `passiveskills|Name` ve `words|Text2` ad kaynağı olarak
KULLANILMAMALI. Pasif düğüm adları çoğu zaman stat cümlesinin kendisi
(`Energy Shield and Armour applies to Elemental Damage`); maskelenirse aynı
cümle mod açıklamasında da çevrilemez.

## Silah adı karışıklığı

Model `Mace`'i "mızrak" (Spear!), `wand`/`staff`/`sceptre` üçünü birden "asa"
çeviriyordu. Sisteme istemine açık bir ayrım bloğu eklendi (`Translator.cs`
içinde `DİKKAT — sık karıştırılan silahlar`). Doğrulandı: `Mace → Topuz`,
`wand, staff or sceptre → değnek, asa veya hükümdar asası`.

Çoğul biçimler hâlâ kaçabiliyor (`wands → Asa`).

## Modelin düzeltilemeyen sınırları

Doğrulayıcının yakalayamadığı, kural değişikliğiyle çözülmeyen hatalar:

- **Anlam takası:** `[Consume|Consumes] [Rage]` → `[Consume|Öfke] [Rage|Tüketin]`
  (görünen metinler yer değiştirmiş, yapı geçerli)
- **Kip hataları:** "If she found him" → "Eğer onu bulsaydı"
- **Resmi hitap:** yer yer "vereceksiniz" (senli benli olmalı)
- **Ünsüz yumuşaması:** `Your Hideout → Sığınakın` (Sığınağın olmalı)
- **Anlam kayması:** `Porcupine Goliath → Porsuk Goliath` (porcupine = kirpi)

Kişisel kullanım için kabul edilebilir bulundu.

## İKİNCİ HAT: stat açıklamaları (.csd)

**Bu, projenin başında gözden kaçan en büyük parçaydı.** Eşya modları, pasif
düğüm açıklamaları, yetenek statları — oyundaki en oyun-kritik metinler —
`.datc64` tablolarında DEĞİL, `data/statdescriptions/*.csd` dosyalarında.
İlk tam çeviri koşusundan sonra oyunda arayüz Türkçe, statlar Portekizce çıktı.

Biçim: düz metin, UTF-16LE, **tüm diller aynı dosyada**.

    description
        1 melee_physical_damage_taken_%_to_deal_to_attacker
        1
            # "{0}% of [Melee] Physical Damage taken reflected to Attacker"
        lang "Portuguese"
        1
            # "{0}% do dano físico [Melee|corpo a corpo] sofrido..."
        lang "Traditional Chinese"
        ...

İlk (lang'siz) bölüm İngilizce varsayılan. Her dil bölümü aynı sayıda ve aynı
sırada varyant içerir, bu yüzden İngilizce varyant *i* ile Portekizce varyant
*i* birebir eşleşir. `CsdFile.cs` bu eşleşmeye dayanıyor.

Varyant satırı: `<önek> "metin" [değiştiriciler]` — örnek
`#|-1 "..." negate 1 canonical_line`. Önek ve değiştiriciler korunmalı.

Bir `description` bloğunda `lang "Portuguese"` yoksa oyun İngilizceye düşer.

| | |
|---|---|
| Dosya | 589 |
| Çeviri birimi | 29.505 |
| Karakter | ~1,74 milyon |

**Gidiş-dönüş testi zorunlu.** `csd-check` komutu 589 dosyanın tamamını
değiştirmeden okuyup yazıyor ve bayt bayt karşılaştırıyor. Oyuna yazmadan önce
bunun geçmesi gerekiyor — geçti (589/589).

## Stat satırı kalıbı: ölçüm ve düzeltme (2026-08-09)

Kullanıcı oyundan ekran görüntüsüyle geldi: aynı bilgi her eşyada başka
kuruluyordu. Ölçüm (94.188 kayıtlık bellek üzerinde):

| Kalıp | Bulgu |
|---|---|
| `{0}% increased ...` | 2.874 satırda `{0}%`, 2.843 satırda `%{0}` — yazım ikiye bölünmüş |
| aynı | 1.121 satırda sıfat sona atılmış (`Zırh %{0} artırılmış`) |
| `{0:+d} to X` | 26 satırda `ila` (`+10 ila Çeviklik`) |
| `Evasion Rating` | 47 "Kaçınma Derecesi", 12 "Kaçınma Değeri" — terim ikiye bölünmüş |

**"ila" neden yanlış değil ama yanlış:** `ila` ARALIK demek.
`{0} to {1} Fire Damage` → `{0} ila {1} Ateş Hasarı` **doğru**;
`+10 to Dexterity` → `+10 ila Çeviklik` **yanlış**. Ayırt edici tamamen
mekanik: `to`nun sağındaki şey yer tutucu mu, değil mi.

Kanonik biçim (`StatText.cs`):

    {0}% increased Armour  -> %{0} artırılmış Zırh
    {0}% reduced Armour    -> %{0} azaltılmış Zırh
    {0:+d} to Dexterity    -> {0:+d} Çeviklik
    {0:+d}% to Fire Res    -> {0:+d}% Ateş Direnci

Dirençlerde yüzde sayıdan **sonra** kalıyor: işareti yer tutucunun kendisi
basıyor, `%{0:+d}` ekranda `%+35` olur. `+35%` daha az kötü.

`StatText` iki ayrı iş yapıyor ve ayrımı korumak önemli:
**`Normalize`** anlama dokunmadan yazım/dizim düzeltir (yüzde konumu, uydurma
`ila`, sıfatı öne alma, taşınan isimden hâl ekini düşürme, terim geri çekme).
**`Problem`** yalnızca makineyle düzeltilemeyeni bildirir; o kayıt modele
geri gider. İkisi hem `Masking.Validate` içinden (yeni çeviriler) hem de
`stat-duzelt` komutundan (mevcut bellek) çağrılıyor.

Sonuç: **5.518 satır makineyle düzeldi**, 617'si modele geri gitti.

### Sıfatı öne alırken düşülen tuzak

`tr.Contains("%{0} artırılmış")` "zaten kalıpta" demek DEĞİL — devrik biçim de
o kalıbı içeriyor (`Hasar %{0} artırılmış`). Sonu fiille bitiyorsa hâlâ devrik.
Bu kontrolü atlayınca düzeltme sessizce hiçbir şey yapmıyordu.

İsim başa alınırken **hâl eki düşmeli**: `[Evasion|Kaçınma Değeri]'ne %{0}
artırılmış` → `%{0} artırılmış [Evasion|Kaçınma Değeri]`. Kesme işaretiyle
bağlanan ek bu konumda her zaman ektir, güvenle atılıyor. Belirtme eki `-nI`
de öyle (`Nadirliğini` → `Nadirliği`); ama tek başına `-I` belirsiz (iyelik
eki de olabilir), ona dokunulmuyor.

### Modelin süslü parantez yutması — asıl darboğaz buydu

Yeniden çeviri koşusu **%16 başarıyla** gidiyordu. Sebep doğrulayıcının katı
olması sanıldı; `POE2TR_DEBUG=1` ile bakınca gerçek sebep çıktı:

    kaynak  {0}% increased Damage per [Power Charge]
    cevap   %0 artırılmış Hasar başına [Güç Yükü]     <- yer tutucu yok oldu

Model yüzdeyi Türkçe söz dizimine göre öne alırken süslüleri yutuyordu.
İki sebep vardı ve ikisi de düzeltildi:

1. Sistem isteminde örnek **`"50% increased" -> "%50 artırılmış"`** yazıyordu.
   Model `{0}`'ı sayı sanıp `%0` yazıyordu. Örnek yer tutucuyla değiştirildi.
2. `Masking.Repair` eklendi. Hata tek biçimli olduğu için onarımı çıkarım,
   tahmin değil: kaynakta `{N}%` varsa ve çeviride `{N}` **hiç** yoksa,
   çeviride tek başına duran `%N` o yer tutucudur.

Aynı örneklemde başarı **%16 → %94**. Ders, doğrulayıcı açığının aynadaki
hâli: **doğrulama neyin bozulduğunu söyler, NEDEN bozulduğunu söylemez.**
Kalıbı sıkılaştırmadan önce ham cevaba bakmak gerekiyordu.

### Yer tutucunun İNDEKSİ olmayabilir — üçüncü doğrulama açığı

Kaynakta iki biçim var ve ikincisi uzun süre görülmedi:

    {0:+d} to Maximum Power Charges          <- indeksli
    {:+d}  to Maximum Power Charges          <- İNDEKSSİZ, aynı dosyada

`Masking.PlaceholderRx` ve `StatText`'in tamamı `\{\d+...\}` arıyordu, yani
indekssiz yer tutucular **hiç denetlenmiyordu**. Sonuç: model birinde jetonu
`{%+d}` diye bozdu, doğrulayıcı hiçbir uyarı vermedi ve oyuna öyle gitti —
ekranda birebir `{%+d}` yazacaktı.

Kapsam küçük (35 kayıt, 4'ü hatalı) ama ders büyük. Regexler `\d*` oldu.
Ayırt edici: süslü parantezin içi ya rakamdır ya da `:` ile başlayan bir
biçimdir; `{Metin}` biçim etiketi içeriğidir ve ÇEVRİLİR, karıştırılmamalı.

Bu, NOTES'taki aynı dersin üçüncü tekrarı: `%` denetlenmiyordu, `{0:+d}`
denetlenmiyordu, şimdi de `{:+d}`. **Doğrulayıcı yalnızca AÇIKÇA aradığını
korur** ve "aradığı" her seferinde sandığından dar çıktı.

### Yüzde işaretinin yeri: kural tek değil, İKİ

Bu ayrım kodda kolayca kaybolur:

| Yer tutucu | Ekranda | Türkçe biçim | Neden |
|---|---|---|---|
| `{0}`, `{:d}` (işaretsiz) | `35` | **`%{0}`** | Türkçe yazım: yüzde sayıdan önce |
| `{0:+d}` (işaretli) | `+35` | **`{0:+d}%`** | `%{0:+d}` ekranda `%+35` olur |

İşareti yer tutucunun kendisi bastığı için işaretli olanlarda yüzdeyi öne
almak mümkün değil. `+35%` en az kötü seçenek, bilerek öyle bırakıldı.
`StatText` bu ayrımı biçim dizisinde `+` arayarak yapıyor.

## Tam denetim turu (2026-08-09) — üç sessiz hata sınıfı

`scripts/denetim.ps1` bütün belleği tarayıp kalan iş listesi çıkarıyor. İlk
koşusunda üç sınıf çıktı; üçü de yapısal doğrulamayı geçmişti.

### 1. Modelin cevap biçimi çeviriye sızmış — 1.066 kayıt

    TR  Tüm Silah ve Zırh yuvalarını ... doldur.</8>

Modele `<1>metin</1>` biçiminde numaralı bloklar gönderiyoruz. Model bazen
kapanış numarasını kaydırıyor (`<7>...</8>`). `ParseReply` yalnızca **tam
eşleşen** numarayı kırpıyordu, artan `</8>` çeviride kalıp oyuna gidiyordu.

Düzeltme iki yerde: `ParseReply` artık numaraya bakmadan `</N>` kırpıyor
(o konumda oyun metninde sayıdan oluşan etiket yok), ve mevcut bellek
temizlendi. Sonuç 0.

### 2. Anahtar / görünen metin takası — 494 kayıt

    EN  Trigger this Spell on [Melee] Hit while [Curse|Cursed]
    TR  [Melee|Lanetli] iken bir [Curse|Yakın Dövüş] Vuruşu ile ...

Görünen metinler doğru çevrilmiş ama **yanlış anahtara bağlanmış**. Yapısal
doğrulama göremiyor: sayılar tutuyor. Oyunda yanlış terime bağlantı veriyor,
yani mekanik olarak yanlış bilgi.

Sebep `Masking.Restore`'un anahtarları **konuma göre** geri koyması. Model
cümleyi Türkçe söz dizimine göre yeniden sıraladığında konumlar kayıyor.

Çözüm: konum yerine **içerik eşleştirmesi**. Her görünen metin, `sozluk.tsv`'de
karşılığı olduğu anahtara bağlanıyor; sözlük dışı anahtarlar ve sözlükte
karşılığı bulunmayan görünen metinler yerlerinde bırakılıyor — "bilmiyorum"
varsayımla doldurulmamalı. 494 → **4**.

İlk iki deneme fazla dardı ve bu öğreticiydi: "metindeki TÜM anahtarlar
sözlükte olsun" (494→394), sonra "tam permütasyon olsun" (→361). Sözlük
kullanım ağırlığıyla %64,5 kapsıyor, yani "hepsi bilinsin" şartı vakaların
çoğunu daha baştan eliyordu. Kısmi bilgiyle çalışan eşleştirme doğru tasarım.

### Sözlük genişletmesi: ölçüldü, çoğu YAPILMADI — gerekçesiyle

Takas onarımı sözlük kapsamasına bağlı, o yüzden "sözlüğü büyütelim" doğal
fikir. Ölçüm (`scripts/sozluk-oner.ps1` ve `scripts/denetim.ps1`):

| | |
|---|---|
| Korpustaki benzersiz köşeli-parantez anahtarı | 1.000 |
| Sözlükte olan | 116 (%11,6) |
| Geçiş ağırlığıyla kapsama | %56,4 |

İlk simülasyon "en sık 100 anahtarı eklersek onarım %30'dan %81'e çıkar"
diyordu. **Bu tahmin yanlıştı** ve nedeni öğretici: simülasyon her anahtarın
bire bir terim olduğunu varsayıyordu. Türetilmiş önerilere bakınca ters
korelasyon çıktı — **yüksek frekanslı anahtarlar düşük güvenli, yüksek
güvenliler düşük frekanslı**:

    1252x  %39  HitDamage   -> Vuruş        (Vuruşlar, Vuruşları, Hasar)
     196x  %10  Biome       -> Çöl          (Şehir, Orman, Swamp)
      95x  %22  ItemRarity  -> Nadir        (Eşsiz, Sihirli)
       3x %100  GraspingVines -> Vuruş      <- 3/3, ve kendisi bir takas artığı

`Biome`, `ItemRarity`, `Shrine` gibi anahtarların görünen metni **bağlama göre
değişiyor**; bunları sözlüğe koymak onarımı yanlış eşleştirmeye zorlar, yani
veriyi bozar. Yüksek güvenlilerin neredeyse tamamı 3-5 geçişlik tek seferlik
özel ad (madalyon, kutsal alan, patron adı) — toplam etkisi ~600 geçiş.

Öneri dosyası duruyor: `work/corpus/sozluk-oneri.tsv`. Toplu uygulanmamalı;
tek tek incelenip gerçekten 1:1 olanlar `sozluk.tsv`'ye alınabilir.

### Bunun yerine yapılanlar — ikisi de sözlük kararı gerektirmedi

**1. Anahtar aramasında normalleştirme.** Oyun anahtarları bitişik yazıyor
(`[EnergyShield|...]`), sözlük doğal yazımla tutuyor (`Energy Shield`). Tek
başına 22 anahtar / 1.352 geçiş sözlük dışı görünüyordu. Sözlüğe kopya girdi
eklemek yerine aramayı normalleştirmek doğru çözüm: onaylı sözlük şişmiyor ve
ileride çıkacak varyantlar da kendiliğinden eşleşiyor.
Kapsama %56,4 → **%61,0**, onarımın gördüğü kayıt 2.296 → **2.692**.

**2. Ek duyarsız eşleştirme.** `HitDamage`(1252), `Critical`(835),
`BuffMagnitude`(329) aslında bire bir terim; güvenleri düşük çünkü karşılıkları
Türkçe **ad çekim eki** almış (Vuruş / Vuruşlar / Vuruşları). Eşleştirme artık
bir ek listesiyle bunları aynı terim sayıyor.

Düz önek karşılaştırması YAPILMAMALI: "Güç" ile "Güçlendirme"yi eşler, biri
Strength öteki Buff. Ek listesi bu çarpışmayı kesiyor — `lendirme` listede yok.

### İkili takas: dar tutmanın nedeni ölçüldü

Ek duyarsız eşleştirmenin ilk hâli "her görünen metni anahtarına bağla,
kalanları sırayla doldur" idi. 156 kayıt değişti ama örnekleme **bozulma**
gösterdi: bir eşleşme bulununca kalanlar kuyruğu kayıyor ve yanındaki DOĞRU
çifti dağıtıyordu.

    ÖNCE   [HitDamage|Vuruşlarınla]  ... [Affinity|Uyumunu]     <- doğruydu
    SONRA  [HitDamage|Elementlere]   ... [Affinity|Vuruşlarınla] <- bozuldu

Algoritma ikili takasa indirildi: yalnızca **i ve j'nin ikisi de yanlışsa** ve
i'nin beklediği metin j'de duruyorsa takas yapılıyor. Takas en az birini doğru
yapar, ötekini bozamaz — zaten yanlıştı. Başka hiçbir konuma dokunulmuyor.
Aynı anahtarın iki geçişi arasında takas da atlanıyor (anlamsız churn).

135 kayıt düzeldi, 12 örneğin tamamı düzelme ya da nötr, bozulma yok.

**Ders: "daha çok kayıt değişti" daha iyi demek değil.** İlk sürüm 156 kayıt
değiştiriyordu, ikincisi 135 — ama ilki veri bozuyordu. Örnekleme yapılmasa
fark edilmezdi.

**Yöntemin sınırı, bilinerek kabul edildi.** Takas ancak sözlük kanıtlıyorsa
onarılabiliyor:

    EN  Create an [Orb|Orb] of electricity that fires [Chain|Chaining] bolts
    TR  ... [Orb|Zincirleme] [Chain|Küre] ...        <- takas, ama onarilamaz

Sözlükte `Orb → Orb` (çevrilmez) ve `Chain → Sekme` yazıyor; modelin ürettiği
"Küre" ve "Zincirleme" ikisi de sözlük dışı, dolayısıyla hangisinin hangi
anahtara ait olduğu kanıtlanamıyor. Onarım bilerek dokunmuyor.

Körlemesine "anahtar sözlükteyse görünen metni sözlük değeriyle ez" YAPILMAMALI:
görünen metinler Türkçe hâl eki taşıyor (`[Armour|Zırhını]`, `[Armour|Zırhın]`)
ve bunları `Zırh`a indirmek cümleyi bozar. Ölçüm: yalnız `Armour` için 12
farklı biçim, çoğu meşru çekim.

Kalan takasları görmek isteyen sözlüğü genişletmeli; mekanik çözüm yok.

### 3. Süslü parantez dengesi bozuk — 193 kayıt

    EN  <red>{Cursor and crafting space must be empty}
    TR  <red> İmleç ve işleme alanı boş olmalıdır.

Süslü parantezin oyunda iki işi var: yer tutucu (`{0}`) ve **biçim etiketinin
içeriği** (`<red>{metin}`). İkincisi hiç denetlenmiyordu — yer tutucu denetimi
bunu yakalayamıyor çünkü `{0}` sayıları tutuyor. Model ya parantezleri
tamamen düşürüyor ya da olmayan yere ekliyor; ikisi de biçimlendirmeyi bozuyor.

`StatText.Problem` artık `{` ve `}` sayısını birebir karşılaştırıyor; uymayan
kayıtlar yeniden çeviri korpusuna düşüyor.

### Modelin köşeli parantezli terimi yutması

1.036 çevrilememiş metnin yeniden koşusu **%3 başarıyla** gidiyordu. Teşhis:
`anahtar sayisi: beklenen 10, gelen 9`. İngilizcede parantezli terim fiil
olabiliyor (`[Consume] [Freeze] on Hit`); Türkçede fiil sona gidince model
onu parantezden çıkarıyor ve bir anahtar kayboluyor.

İsteme açık kural ve örnek eklendi ("parantezi taşı, çıkarma; ekleri dışına
yaz"). Başarı **%3 → %68**.

## Son denetim turu: dört kalem, üçü gerçek

### Üçüncü yer tutucu biçimi: `{}`

`{0:+d}` ve `{:+d}`'den sonra bir biçim daha çıktı: **`{}`** — ne indeks ne
biçim. İçinde tutunacak karakter olmadığı için model onu `{ }` (boşluk ekliyor)
ya da `{%0}` (yüzdeyi içeri alıyor) yapıyordu; ikisi de oyunda birebir öyle
görünür, sayı hiç basılmaz. 9 kayıt, hedefli kuralla düzeltildi.

**Buna bağlı bir tuzak:** `Masking.Repair`'ın `%N → %{N}` onarımı `{}` için
`inner` boş olduğundan `%` + `""` regex'i üretiyor ve rakam gelmeyen HER yüzde
işaretini yer tutucu sanıp bozuyordu. Boş `inner` için koruma eklendi.

### Modelin uydurduğu biçim etiketi

Kaynakta hiç etiket yokken çeviride `<span>`, `<span class="highlight">` gibi
HTML kalıpları çıkmış (5 kayıt). Oyun bunları tanımaz, ekrana olduğu gibi
yazar. Kaynakta yoksa çeviride de olmamalı — kural eklendi.

### "Bozuk yer tutucu jetonu" — YANLIŞ ALARM

Denetim 96 kayıt gösteriyor ama incelendi: hepsi `<white>{... %12 ...}` gibi
**meşru** biçim etiketi içeriği. Süslü parantez içinde yüzde işareti olması
bozukluk değil. Denetim betiğinin bu sayısı ham, elle ayıklanmalı.

### PARTİ KİRLENMESİ — yeni bir hata biçimi

En ciddi bulgu buydu ve yalnızca çıktıya bakınca görüldü. 21 çevrilmemiş metin
10'luk partide çevrilince:

    EN  ... on enemies with Heavy Strike      TR  Cleave ile düşmanlara ...
    EN  Grants more damage for Heavy Strike   TR  Cleave için daha fazla ...

Model, **aynı partideki başka bir yeteneğin adını taşımış**. Yapısal doğrulama
göremez: yer tutucu, etiket, parantez sayıları tutuyor. Oyunda yanlış yetenek
adı yazacaktı.

`partiBoyutu 1` ile yeniden koşunca düzeldi. Ders: **birbirine benzeyen ve
özel ad içeren metinler aynı partide çevrilmemeli.** Yetenek destek taşı
açıklamaları tam olarak böyle — hepsi aynı kalıpta, yalnız yetenek adı farklı.

Kalan sorun `Heavy Strike → Ağır Darbe` idi; yetenek adları İngilizce kalmalı
(karar #3) ama ikisi de `sozluk.tsv`'de yoktu. Sözlüğün "EN = TR ise dokunma"
mekanizması tam bunun için: `Heavy Strike` ve `Cleave` öyle eklendi ve çeviri
düzeldi. Aynı sorunu başka yetenek adlarında görürsen çözüm bu.

## Çevirisi olmayan metin: Portekizce DEĞİL, İngilizce yazılır

Uzun süre şöyleydi: bir metnin bellekte karşılığı yoksa hücre **atlanıyordu**.
Atlamak "İngilizce kalsın" sanılıyordu — `dat-import` ekrana birebir bunu
yazıyordu: `Cevirisi yok : N (Ingilizce kalacak)`.

**Yanlıştı.** Yazdığımız slot Portekizce dil yuvası; dokunmadığımız hücrede
oyunun kendi **Portekizce** metni durur. Yani her çevrilemeyen satır oyunda
Portekizce görünüyordu. `csd-import` bunu dürüstçe `(Portekizce kalacak)` diye
yazıyordu, `dat-import` ise davranışını yanlış belgeliyordu.

Bu, oyun güncellemesi senaryosunda en can alıcı nokta: güncellemeyle gelen
yeni metinlerin hiçbirinin çevirisi olmaz ve hepsi Portekizce çıkardı.

Artık her iki hat da çevirisi olmayan birime **İngilizceyi yazıyor**. Türk
oyuncu için İngilizce, Portekizceden kıyaslanamaz ölçüde iyi — zaten eşya,
yetenek ve para birimi adları bilerek İngilizce bırakılmış durumda.

Ölçüm (mevcut bellekle): 364 stat satırı + 85 tablo hücresi.

## Doğrulama açıkları (sessiz hatalar)

Bunlar doğrulamadan geçip oyuna yanlış bilgi olarak gitti, sonradan yakalandı:

| Açık | Etki | Düzeltme |
|---|---|---|
| `%` düz karakter, denetlenmiyordu | 834 stat satırı yüzdesini kaybetti (`{0}% reduced` → `{0} azaltılmış`) | Kaynak/çeviri `%` sayısı karşılaştırılıyor |
| `{0:+d}` biçimli yer tutucular | `\{\d+\}` regex'ine uymuyor, hiç denetlenmiyordu | Regex `\{\d+(?::[^}]*)?\}` oldu |

Ders: doğrulayıcı yalnızca **açıkça aradığı** şeyi korur. Metinde anlam taşıyan
her karakter sınıfı ayrıca düşünülmeli.

Katı doğrulamanın bedeli: yüzde içeren çevirilerin %1,8'inde yapısal İngilizce
kelime kalıyor (model kuralı sağlamak için birebir çeviriye kaçıyor). Mekanik
doğruluk akıcılıktan önce geldiği için kabul edildi.

## Koşu sırası tuzağı

Başarısız metinler her koşuda yeniden deneniyor ve her biri 3 istek harcıyor.
Ana korpusta 222 inatçı metin kaldığında `kaynak.jsonl` ile koşu başlatmak
hızı **72/dk'dan 3-5/dk'ya** düşürüyor — çünkü koşu önce onları çiğniyor.

Yeni içerik çevirirken ayrı korpus dosyası kullan. Kalıcı çözüm: başarısızları
ayrı bir listede tutup yeniden denemeyi isteğe bağlı yapmak (yapılmadı).

## Font yamalama: Fontin'e Türkçe glifleri

Fontin'de `Ğ ğ İ Ş ş` yoktu; oyun onları yedek fonttan (FrizQuadrataITC)
alıyordu ve harfler farklı ölçüde görünüyordu (kullanıcı `ş`'nin küçük
durduğunu fark etti).

**Çözüm: yeni şekil çizmek yerine fontun kendi parçalarından bileşik glif.**
Fontin'de gereken her parça zaten vardı:

| Parça | Kod | Kullanım |
|---|---|---|
| `S`, `s`, `G`, `g`, `I` | — | taban harfler |
| aralıklı breve `˘` | U+02D8 | `Ğ ğ` |
| nokta aksanı `˙` | U+02D9 | `İ` |
| aralıklı sedil `¸` | U+00B8 | `Ş ş` |

`scripts/font_yamala.py` bunları TrueType bileşik glifi olarak birleştiriyor:
tabanın sınır kutusundan yatay merkez, üst/alt konum için dikey ofset
hesaplanıyor. Sonuç fontun kendi çizgisiyle uyumlu.

Dört Fontin dosyası da yamalandı (regular, bold, italic, smallcaps), hepsinde
5 glif de eklendi. Görsel doğrulama yapıldı (System.Drawing ile PNG'ye çizip
bakarak).

### OptimusPrinceps (başlık/düğme fontu) — sonradan yamalandı

Önce "yamalanamaz" denmişti: fontta breve ve nokta aksanı yok. Ama kullanıcı
giriş ekranından geldi, `GİRİŞ YAP`taki `ş` tuhaf duruyordu — çünkü glif
olmayınca oyun **harfin tamamını** FrizQuadrataITC'den alıyor ve o font küçük
kapital değil, gerçek küçük harf çiziyor. Yan yana duran iki farklı yazı stili.

Çözüm: aksanı **ithal et, tabanı fontun kendisinde bırak.**

| Glif | Taban | Aksan |
|---|---|---|
| `Ş ş` | Optimus'un kendi `S`/`s` | Optimus'un kendi sedili (U+00B8, zaten vardı) |
| `Ğ ğ İ` | Optimus'un kendi `G`/`g`/`I` | FrizQuadrata'dan breve ve nokta konturu |

İthal kayıp değil kazanç: o glifler yamasız kalsa harfin *tamamı* zaten
FrizQuadrata'dan geliyordu. İki font da em=1000, ölçekleme gerekmedi.

Optimus'ta küçük harfler **küçük kapitaldir** (`s` yüksekliği `S`'nin %81'i),
o yüzden aksan da taban/büyük-harf oranıyla küçültülüyor — yoksa `ş`in sedili
harften taşıyor. Ölçek dinamik hesaplanıyor, sabit değil.

Doğrulama: `System.Drawing` + `PrivateFontCollection` ile ham ve yamalı fontlar
yan yana PNG'ye çizilip bakıldı. Ham satır kullanıcının ekran görüntüsündeki
hatayı birebir yeniden üretti.

### fontTools tuzağı

`glyf` tablosu KENDİ `glyphOrder` listesini tutuyor ve `glyf[isim] = glif`
ataması onu otomatik güncelliyor. Ayrıca `font.setGlyphOrder()` çağırırsan iki
liste birbirini tutmaz ve `maxp` derlenirken şu patlar:

    assert len(self.glyphOrder) == len(self.glyphs)

Doğrusu: döngüde yalnızca `glyf[isim] = glif` ve cmap güncellemesi yap, sonda
bir kez `font.setGlyphOrder(list(glyf.glyphOrder))` ile eşitle.

## Anahtar regex'i fazla yakalıyordu

Doğrulama taraması `[{0}|{0}]` gibi bozuk çıktılar gösterdi. Sebep: anahtar
kelime regex'i `\[([^\]\|]+)...\]` **tuş göstergelerini de anahtar sanıyordu**:

    EN  Open Temple Map [{0}]
    TR  Tapınak Haritası'nı aç [{0}|{0}]     <- yer tutucu ikiye katlandi

Anahtar adı asla süslü/açılı parantez içermez. Regex daraltıldı:
`\[([^\]\|{}<>]+)(?:\|([^\]]*))?\]`

Etkilenen yalnızca 6 kayıttı ama **hata sessizdi** — oyunda bozuk görünecekti,
hiçbir uyarı vermiyordu. Bu, tam tarama yapmanın değerini gösteriyor.

Yan etki: anahtar olmayan köşeli parantezler artık hiç denetlenmiyor, model
bazen düşürüyor. Ölçüldü: 15.995 parantezli çevirinin 4'ünde kayıp (%0,03),
kapatıldı.

## Dosya kilidi: teşhis hatası

`TranslationMemory` yazarken dosyayı kilitliyor sanıldı ve `FileShare.ReadWrite`
ile açıldı — **sorun orada değildi.**

`File.ReadAllLines` kendini `FileShare.Read` ile açar, yani "başkaları yalnızca
okuyabilir" der. Dosyada açık bir YAZICI varken bu istek her hâlükârda
reddedilir; yazıcının paylaşım izni ne olursa olsun.

Belirleyici olan okuyan taraf. Okuma şöyle yapılmalı:

    [IO.File]::Open($yol, 'Open', 'Read', 'ReadWrite')

Yazıcıdaki değişiklik zararsız ama iddia edilen sorunu çözmüyor.

## Strateji: dil slotu ele geçirme

Oyuna yeni dil eklenemiyor (dil listesi istemciye gömülü). Bunun yerine
kullanılmayan bir dilin dosyaları Türkçe ile değiştirilir. **Portekizce** seçildi
(Latin alfabesi, kullanılmıyor).

Bu yöntemin çalıştığının kanıtı: LibGGPK3'ün `Examples/PoeChinese3` örneği tam
olarak bunu yapıyor — `Languages.dat` içinde Fransızca satırıyla Çince satırının
alanlarını takas ediyor.

## Font durumu (kritik bulgu)

`System.Windows.Media.GlyphTypeface` ile glif taraması yapıldı:

| Font | ğ Ğ | ı | İ | ş Ş | ç ö ü |
|---|---|---|---|---|---|
| **fontin-*** (ana arayüz) | ✗ | ✓ | ✗ | ✗ | ✓ |
| **optimusprincepssemibold** (başlık) | ✗ | ✗ | ✗ | ✗ | ✓ |
| **frizquadrataitc** | ✓ | ✓ | ✓ | ✓ | ✓ |
| caveat-*, kanit-*, koruri-regular | ✓ | ✓ | ✓ | ✓ | ✓ |

Eksik olan yalnızca **5 glif**: `Ğ ğ İ Ş ş`

`uisettings.portuguese.xml` içinde şu satır var:

    <FallbackFont id="Portuguese" ranges="Any" fonts="FrizQuadrataITC,Simsun,PMinglu,Gulim,MS UI Gothic"/>

FrizQuadrataITC zaten ilk yedek font ve tüm Türkçe karakterleri içeriyor.
Ayrıca listede Windows sistem fontları (Simsun, Gulim) da var — yani motor
sistem fontlarına da düşebiliyor.

Çözüm seçenekleri:
1. **Yedek fonta güven** — motor eksik glifi FrizQuadrata'dan alır. Bedava ama
   harf stilleri karışık görünebilir. Önce bu test edilmeli.
2. **Fontin'e 5 glif ekle** — `G+breve`, `S+cedilla`, `I+dot` birleştirilerek
   fonttools ile üretilebilir. Görsel olarak kusursuz sonuç.
3. **Fontları tamamen değiştir** — en kaba çözüm, oyunun görsel kimliğini bozar.

Özel font yükleme yöntemi (PoeChinese3'ten):

    <InstalledFont id="Custom" typeface="Custom" value="Art/2DArt/Fonts/xxx.ttf"/>

## Güncelleme dayanıklılığı

Her Steam yaması `_.index.bin`'i yeniden yazıp değişiklikleri siliyor.
Bunun canlı kanıtı: `Bundles2\LibGGPK3\0.bundle.bin` (20.07.2026) daha önceki
bir değişiklikten kalma öksüz dosya; 06.08.2026 yaması indeksten sildi.

Sonuç: değişiklikler **elle yapılmamalı**. Çeviriler ayrı bir kaynak deposunda
tutulmalı, her yama sonrası tek komutla yeniden uygulanmalı.
`LibBundle3.Index.Replace(index, zipEntries, cb)` bunun için hazır API sağlıyor.

## Riskler

- İstemci dosyalarını değiştirmek GGG kullanım şartlarına aykırı. Tamamen
  görsel/istemci tarafı olduğu için pratikte ban vakası bilinmiyor, ama sıfır
  risk değil.
- Steam "Verify integrity of game files" tüm değişiklikleri geri alır.
