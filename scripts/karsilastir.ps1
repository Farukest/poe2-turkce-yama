# PoE2 Türkçe — model karşılaştırma düzeneği
#
# Aynı 50 metni birden fazla yerel modele aynı istemle çevirtir,
# çıktıyı biçimlendirme kurallarına göre denetler ve yan yana rapor üretir.
#
# Kullanım:  .\karsilastir.ps1 [-Modeller gemma3:12b,qwen3:14b] [-Adet 50]

param(
    [string[]]$Modeller = @('gemma3:12b', 'qwen3:14b'),
    [int]$Adet = 50,
    [string]$Kok = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
$korpus  = Join-Path $Kok 'work\corpus\kaynak.jsonl'
$sozluk  = Join-Path $Kok 'sozluk.tsv'
$ciktiKok = Join-Path $Kok 'work\karsilastirma'
New-Item -ItemType Directory -Force -Path $ciktiKok | Out-Null

# ---------------------------------------------------------------- sözlük ----
$glossaryLines = @()
foreach ($l in Get-Content $sozluk -Encoding utf8) {
    if ($l -match '^\s*#' -or $l.Trim() -eq '') { continue }
    $p = $l -split "`t"
    if ($p.Count -lt 2) { continue }
    $glossaryLines += "$($p[0]) = $($p[1])"
}
Write-Host "Sozluk girdisi: $($glossaryLines.Count)"

$SYSTEM = @"
Sen Path of Exile 2 adlı oyunun metinlerini İngilizceden Türkçeye çeviren bir çevirmensin.

BİÇİMLENDİRME KURALLARI — bunlara uymazsan oyun bozulur:
1. {0}, {1} gibi süslü parantez içindeki SAYILAR yer tutucudur. Aynen kopyala, çevirme, sırasını değiştirme.
2. [Anahtar|Görünen] biçimindeki köşeli parantezlerde SOL taraf oyunun iç bağlantı adıdır, asla değişmez. Sadece SAĞ tarafı çevir. Örnek: [Totem|Totem] -> [Totem|Totem], [Charges|Power Charges] -> [Charges|Güç Yükleri]
3. [Anahtar] biçiminde boru işareti YOKSA, çeviriyi [Anahtar|Türkçe] hâline getir. Örnek: [Strike] -> [Strike|Darbe]
4. <i>, <white>, <b>, <smaller>, <rgb(1,2,3)>, <font:'fontin'>, <xbox_button_a> gibi açılı etiketler aynen kalır.
5. <etiket>{Metin} biçiminde süslü parantez içindeki METİN çevrilir (sayı değilse). Örnek: <smaller>{The Well of Souls} -> <smaller>{Ruhlar Kuyusu}
6. Satır sonlarını (\n) olduğu yerde koru.
7. Baştaki ve sondaki boşlukları koru.

DİL KURALLARI:
- Yüzde işareti Türkçede sayıdan ÖNCE gelir: "50% increased" -> "%50 artırılmış"
- "increased/reduced" ile "more/less" oyunda FARKLI çarpanlardır, farklı çevrilmeli.
- Oyuncuya doğrudan hitap et: "Kazanırsın", "Kazanırsınız" değil.
- Anlatı ve diyalog metinlerinde doğal, akıcı Türkçe kullan; birebir çeviri yapma.

TERİM SÖZLÜĞÜ — bu karşılıklar ZORUNLUDUR:
$($glossaryLines -join "`n")

Sözlükte İngilizcesiyle Türkçesi AYNI yazılmış terimler eşya/yetenek/para birimi ADIDIR; onları asla çevirme, olduğu gibi bırak.

ÇIKTI BİÇİMİ:
Sana <n>metin</n> biçiminde numaralı satırlar verilecek. Tam olarak aynı numaralarla, aynı biçimde SADECE çeviriyi döndür.
Açıklama yazma, yorum ekleme, başka hiçbir şey yazma.
"@

# ------------------------------------------------------------- seçki ----
Write-Host "Korpustan ornek seciliyor..."
$rxEn = [regex]'"en":"((?:[^"\\]|\\.)*)"'
$rxTb = [regex]'"table":"([^"]*)"'
$kovalar = @{ ph=@(); pipe=@(); tag=@(); kisa=@(); orta=@(); uzun=@() }
$rand = [Random]::new(20260806)

foreach ($line in [IO.File]::ReadLines($korpus)) {
    $m = $rxEn.Match($line); if (-not $m.Success) { continue }
    $raw = $m.Groups[1].Value
    if ($raw -match 'DNT|NOAUDIO|UNUSED') { continue }
    $s = $raw -replace '\\n',"`n" -replace '\\r','' -replace '\\"','"' -replace '\\\\','\'
    $tbl = $rxTb.Match($line).Groups[1].Value
    $rec = [pscustomobject]@{ Table=$tbl; En=$s }
    if     ($s -match '\{\d+\}')             { $kovalar.ph   += $rec }
    elseif ($s -match '\[[^\]]+\|[^\]]+\]')  { $kovalar.pipe += $rec }
    elseif ($s -match '<[a-zA-Z]')           { $kovalar.tag  += $rec }
    elseif ($s.Length -lt 30)                { $kovalar.kisa += $rec }
    elseif ($s.Length -lt 160)               { $kovalar.orta += $rec }
    else                                      { $kovalar.uzun += $rec }
}

$plan = @{ ph=10; pipe=10; tag=6; kisa=6; orta=10; uzun=8 }
$secki = @()
foreach ($k in $plan.Keys) {
    $havuz = $kovalar[$k]
    if ($havuz.Count -eq 0) { continue }
    $n = [Math]::Min($plan[$k], $havuz.Count)
    $idx = 0..($havuz.Count-1) | Sort-Object { $rand.Next() } | Select-Object -First $n
    foreach ($i in $idx) { $secki += $havuz[$i] }
}
$secki = $secki | Select-Object -First $Adet
Write-Host "Secilen metin: $($secki.Count)"
$secki | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ciktiKok 'secki.json') -Encoding utf8

# ------------------------------------------------------- doğrulayıcı ----
function Test-Bicimlendirme {
    param([string]$En, [string]$Tr)
    $sorunlar = @()
    if ([string]::IsNullOrWhiteSpace($Tr)) { return @('bos cikti') }

    # {0} yer tutucuları birebir aynı olmalı
    $a = [regex]::Matches($En,'\{\d+\}') | ForEach-Object { $_.Value } | Sort-Object
    $b = [regex]::Matches($Tr,'\{\d+\}') | ForEach-Object { $_.Value } | Sort-Object
    if (($a -join ',') -ne ($b -join ',')) { $sorunlar += "yer tutucu: [$($a -join ',')] -> [$($b -join ',')]" }

    # köşeli parantezlerin SOL tarafı korunmalı
    $ka = [regex]::Matches($En,'\[([^\]\|]+)(?:\|[^\]]*)?\]') | ForEach-Object { $_.Groups[1].Value } | Sort-Object
    $kb = [regex]::Matches($Tr,'\[([^\]\|]+)(?:\|[^\]]*)?\]') | ForEach-Object { $_.Groups[1].Value } | Sort-Object
    if (($ka -join ',') -ne ($kb -join ',')) { $sorunlar += "anahtar: [$($ka -join ',')] -> [$($kb -join ',')]" }

    # açılı etiketler birebir korunmalı
    $ta = [regex]::Matches($En,'<[^>]+>') | ForEach-Object { $_.Value } | Sort-Object
    $tb = [regex]::Matches($Tr,'<[^>]+>') | ForEach-Object { $_.Value } | Sort-Object
    if (($ta -join ',') -ne ($tb -join ',')) { $sorunlar += "etiket: [$($ta -join ',')] -> [$($tb -join ',')]" }

    # satır sayısı
    $na = ($En -split "`n").Count; $nb = ($Tr -split "`n").Count
    if ($na -ne $nb) { $sorunlar += "satir sayisi: $na -> $nb" }

    # çevrilmemiş (birebir aynı) — kısa terimler hariç
    if ($En.Length -gt 25 -and $En -eq $Tr) { $sorunlar += 'cevrilmemis' }

    return $sorunlar
}

# ------------------------------------------------------------ çeviri ----
function Invoke-Ollama {
    param([string]$Model, [string]$Sistem, [string]$Kullanici)
    $body = @{
        model  = $Model
        stream = $false
        think  = $false     # qwen3 gibi düşünen modellerde akıl yürütmeyi kapat
        options = @{ temperature = 0.2; num_ctx = 8192 }
        messages = @(
            @{ role='system'; content=$Sistem },
            @{ role='user';   content=$Kullanici }
        )
    } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $resp = Invoke-RestMethod -Uri 'http://localhost:11434/api/chat' -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 900
    $txt = $resp.message.content
    # düşünen modellerin <think> bloklarını at
    $txt = [regex]::Replace($txt, '(?s)<think>.*?</think>', '')
    return $txt.Trim()
}

$sonuclar = @{}
foreach ($model in $Modeller) {
    Write-Host ""
    Write-Host "=== $model ===" -ForegroundColor Cyan
    $cev = New-Object 'string[]' $secki.Count
    $sure = [Diagnostics.Stopwatch]::StartNew()

    for ($i = 0; $i -lt $secki.Count; $i += 10) {
        $son = [Math]::Min($i+9, $secki.Count-1)
        $sb = [Text.StringBuilder]::new()
        for ($j = $i; $j -le $son; $j++) {
            [void]$sb.AppendLine("<$($j+1)>$($secki[$j].En)</$($j+1)>")
        }
        Write-Host ("  parca {0}-{1} ..." -f ($i+1), ($son+1)) -NoNewline
        try {
            $out = Invoke-Ollama -Model $model -Sistem $SYSTEM -Kullanici $sb.ToString().TrimEnd()
            for ($j = $i; $j -le $son; $j++) {
                $n = $j + 1
                $mm = [regex]::Match($out, "(?s)<$n>(.*?)</$n>")
                if ($mm.Success) { $cev[$j] = $mm.Groups[1].Value } else { $cev[$j] = '' }
            }
            Write-Host " tamam"
        } catch {
            Write-Host " HATA: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    $sure.Stop()
    $sonuclar[$model] = [pscustomobject]@{ Ceviri = $cev; Saniye = [math]::Round($sure.Elapsed.TotalSeconds,1) }
    Write-Host ("  sure: {0} sn" -f $sonuclar[$model].Saniye)
}

# ------------------------------------------------------------- rapor ----
$rapor = Join-Path $ciktiKok 'rapor.md'
$o = [Text.StringBuilder]::new()
[void]$o.AppendLine("# Model Karşılaştırma — $($secki.Count) metin")
[void]$o.AppendLine()
[void]$o.AppendLine("| Model | Süre (sn) | Boş çıktı | Biçim hatası | Temiz |")
[void]$o.AppendLine("|---|---|---|---|---|")
foreach ($model in $Modeller) {
    $r = $sonuclar[$model]
    $bos = 0; $hata = 0
    for ($i=0; $i -lt $secki.Count; $i++) {
        if ([string]::IsNullOrWhiteSpace($r.Ceviri[$i])) { $bos++; continue }
        if ((Test-Bicimlendirme -En $secki[$i].En -Tr $r.Ceviri[$i]).Count -gt 0) { $hata++ }
    }
    $temiz = $secki.Count - $bos - $hata
    [void]$o.AppendLine("| $model | $($r.Saniye) | $bos | $hata | **$temiz** |")
}
[void]$o.AppendLine()

for ($i=0; $i -lt $secki.Count; $i++) {
    [void]$o.AppendLine("### $($i+1). ``$($secki[$i].Table)``")
    [void]$o.AppendLine('```')
    [void]$o.AppendLine("EN  $($secki[$i].En)")
    foreach ($model in $Modeller) {
        $t = $sonuclar[$model].Ceviri[$i]
        $etiket = $model.PadRight(12).Substring(0,3).ToUpper()
        [void]$o.AppendLine("$etiket $t")
        $p = Test-Bicimlendirme -En $secki[$i].En -Tr $t
        if ($p.Count -gt 0) { [void]$o.AppendLine("    !! $($p -join ' | ')") }
    }
    [void]$o.AppendLine('```')
    [void]$o.AppendLine()
}
$o.ToString() | Set-Content $rapor -Encoding utf8
Write-Host ""
Write-Host "Rapor: $rapor" -ForegroundColor Green
