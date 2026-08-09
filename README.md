# Path of Exile 2 — Türkçe Yama

Path of Exile 2'nin arayüzünü, diyaloglarını, görevlerini ve eşya özelliklerini
Türkçeleştiren topluluk yaması. **94.000'den fazla çeviri.**

Eşya, yetenek ve para birimi **adları bilerek İngilizce bırakıldı** — ticaret
sitesinde arama yaparken ve build rehberi okurken oyundaki isimle aradığın isim
tutsun diye. Açıklamalar Türkçe.

| | |
|---|---|
| Ana metinler (arayüz, diyalog, görev) | %99,9 |
| Stat açıklamaları (eşya modları, pasif ağaç) | %98,3 |
| Çevirisi olmayan metinler | İngilizce gösterilir, Portekizce değil |

---

## Kurulum

1. [Releases](../../releases) sayfasından son ZIP'i indir.
2. **ZIP'i bir klasöre çıkar.** Exe ve `veri` klasörü yan yana durmalı —
   exe'yi ZIP'in içinden çalıştırırsan çalışmaz.
3. Oyunu kapat.
4. `PoE2-Turkce.exe` çalıştır → **KUR**.
5. Yeşil "Tamamlandı" yazınca oyunu başlat.

Türkçe görünmüyorsa: `Options → UI → Language → Português (Brasil)`, sonra
oyunu yeniden başlat. Program bu ayarı genelde kendisi yapar.

> Oyuna yeni dil eklenemiyor (dil listesi istemciye gömülü). Bu yüzden
> kullanılmayan Portekizce dil yuvası Türkçe ile değiştiriliyor.

### ⚠ oo2core gerekiyor — en sık takılınan yer

Yama, oyunun paketlerini açmak için **Oodle** sıkıştırma kütüphanesine
(`oo2core_*_win64.dll`) ihtiyaç duyar. Bu kütüphane RAD Game Tools / Epic'in
tescilli yazılımıdır, **bu depoda dağıtılamaz.**

**Path of Exile 2'nin kendisi bu dosyayı getirmez** — Oodle'ı exe'nin içine
gömmüşler. Yani bilgisayarında Oodle kullanan başka bir oyun yoksa kurulum bu
adımda durur ve senden dosyayı ister.

Program onu otomatik arar. Oodle kullanan yaygın oyunlar: EA Sports FC,
Fortnite, Warframe, Baldur's Gate 3, Control ve pek çok Unreal Engine oyunu.
Kendin aramak istersen PowerShell'de:

```powershell
Get-ChildItem C:\,D:\ -Filter "oo2core*win64.dll" -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 5 FullName
```

Bulduğun dosyayı `oo2core.dll` adıyla exe'nin yanına koyarsan program hiç
sormadan devam eder.

---

## Oyun güncellendiğinde

Her oyun yaması Türkçe dosyaları siler. **Aynı programı tekrar çalıştır** —
kendini "Yeniden Uygula" olarak adlandırır ve oyun klasörünü hatırlar.

Program güncellemeyle gelen **yeni** metinleri de bulur. Onları çevirebilmesi
için yerel bir çeviri motoru gerekir (isteğe bağlı):

1. [ollama.com](https://ollama.com) adresinden Ollama'yı kur
2. `ollama pull gemma3:12b` (~8 GB, ekran kartı ister)
3. Programı tekrar çalıştır

Ollama yoksa yama yine kurulur; yeni metinler **İngilizce** kalır.

---

## Bilmen gerekenler

- **Steam'de "Dosya bütünlüğünü doğrula"** dersen yama tamamen silinir.
  Zararı yok, programı tekrar çalıştırıp geri yüklersin.
- **Oyun dosyalarını değiştirmek GGG kullanım şartlarına aykırıdır.** Yama
  tamamen görsel ve istemci tarafındadır, sunucuya hiçbir şey göndermez; ama
  sıfır risk değildir. Kullanmak senin tercihin.
- **Exe imzasız** olduğu için Windows SmartScreen uyarır:
  `Ek bilgi → Yine de çalıştır`.
- Çeviri yerel bir yapay zekâ modeliyle yapıldı. Terimler bir sözlükle tutarlı
  tutuldu, stat satırları tek kalıba oturtuldu; yine de yer yer tuhaf cümleler
  görebilirsin.

---

## Güvenlik

- **İnternete çıkmaz.** Kodun tamamında tek bir dış adres yok. Yaptığı tek ağ
  isteği `http://localhost:11434` — yani kendi makinendeki Ollama'ya, o da
  yalnızca sen "yeni metinleri çevir" dersen. Telemetri, kullanım istatistiği,
  "eve telefon" yok.
- **Yönetici yetkisi istemez.** Oyun yönetici izni gereken bir yere kuruluysa
  yazma başarısız olur ve programı yönetici olarak çalıştırman gerekir.
- **Exe imzasız.** İndirdiğin dosyanın burada yayımlananla aynı olduğunu
  doğrulamak için sürüm notlarındaki SHA-256'yı karşılaştır:

  ```powershell
  Get-FileHash .\PoE2-Turkce-Yama-*.zip -Algorithm SHA256
  ```

- **ZIP'i kendi kullanıcı klasörüne çıkar** (Masaüstü, Belgeler, İndirilenler).
  Program `oo2core.dll`'i kendi bulunduğu klasörden yükler; `C:\Users\Public`
  gibi herkesin yazabildiği bir yere çıkarırsan başka bir kullanıcı oraya
  zararlı bir dosya bırakabilir. Bu, kütüphaneyi paketleyemediğimiz için
  tasarımın doğal sonucu.
- **Oyun dosyalarını değiştirir** — amacı bu. Geri almak için Steam'de
  "Dosya bütünlüğünü doğrula" yeterli.

## Nasıl çalışıyor

Oyunun metni iki ayrı yerde duruyor ve bu kolayca gözden kaçıyor:

| Hat | Nerede | Ne içerir |
|---|---|---|
| `.datc64` | `data/balance/portuguese/` | Arayüz, diyalog, görev, eşya adları |
| `.csd` | `data/statdescriptions/` | Stat ve mod metinleri, pasif ağaç |

Program her çalıştığında metni **oyunun kendisinden yeniden çıkarır**, sonra
çeviri belleğiyle eşleştirip geri yazar. Hazır bir korpus taşınmıyor; çünkü
korpus birimleri satır numarası içeriyor ve oyun güncellemesi satırları
kaydırdığında hazır korpusla yazmak çevirileri yanlış satırlara koyar. Çeviri
belleği İngilizce metinle anahtarlandığı için sürümden bağımsız.

Fontlara eksik Türkçe glifler (`Ğ ğ İ Ş ş`) fontun kendi parçalarından bileşik
glif üretilerek eklendi — yedek fonttan gelen harfler yan yana farklı stilde
duruyordu.

Teknik ayrıntılar, ölçümler ve yapılan hatalar: **[NOTES.md](NOTES.md)**

---

## Kaynaktan derleme

Gerekenler: .NET 8 SDK, Python 3 + `fonttools` (yalnız font yamalamak için).

`tools/` altındaki iki bağımlılık **submodule**, o yüzden `--recursive` şart:

```bash
git clone --recursive https://github.com/<kullanici>/poe2-turkce-yama.git
cd poe2-turkce-yama
dotnet build src/poe2tr -c Release
powershell -File scripts/paket-yap.ps1     # dağıtılabilir ZIP üretir
```

`--recursive` demeyi unuttuysan: `git submodule update --init --recursive`

`oo2core.dll` depoda yok; derlemek için kendi makinenden `tools/oodle/` altına
koyman gerekir.

### Faydalı komutlar

```
poe2tr denetim        # bkz. scripts/denetim.ps1 — kalan iş listesi
poe2tr stat-duzelt    # stat satırı kalıbını bellekte düzeltir
poe2tr csd-check      # oyuna yazmadan önce bütünlük testi
```

---

## Katkı

En değerli katkı **terim sözlüğü**: `sozluk.tsv`. Yanlış ya da tutarsız bir
karşılık görürsen issue aç ya da PR gönder. Sözlükteki Türkçe sütunu
İngilizcesiyle aynıysa "bu bir isimdir, çevrilmez" demektir.

Oyunda tuhaf bir cümle gördüysen **ekran görüntüsüyle** issue aç; hangi metin
olduğunu bulmak çok daha kolay oluyor.

---

## Lisans ve kapsam

**Kaynak kod** (`src/`, `scripts/`) MIT lisanslı — bkz. [LICENSE](LICENSE).

MIT lisansı **şunları kapsamaz**:

- **Çeviri verisi** (`work/corpus/bellek.jsonl`, `sozluk.tsv`): Grinding Gear
  Games'in oyun metninden türetilmiştir. Telif GGG'ye aittir; burada yalnızca
  oyunun sahiplerinin kendi istemcilerinde kullanması için bulunuyor.
- **Fontlar** (`work/font-yamali/`): oyunun kendi font dosyalarının Türkçe glif
  eklenmiş hâlleri.
- **`oo2core`**: RAD Game Tools / Epic'e aittir, bu depoda **yoktur**.

Kullanılan açık kaynak bileşenler:
[LibGGPK3](https://github.com/aianlinb/LibGGPK3) (bundle okuma/yazma) ve
[poe-tool-dev/dat-schema](https://github.com/poe-tool-dev/dat-schema)
(tablo şeması). Lisansları `tools/` altında.

Bu proje Grinding Gear Games ile ilişkili değildir ve GGG tarafından
onaylanmamıştır.
