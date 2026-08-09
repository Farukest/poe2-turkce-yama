# PoE2 Türkçe — çeviri inceleme raporu
#
# Çevrilmiş metinleri kategoriye göre gruplayıp yan yana gösterir ve
# gözle bakılması gereken yerleri otomatik işaretler.
#
# Kullanım:  .\inceleme-raporu.ps1 -Kaynak ..\work\corpus\deneme500.jsonl `
#                                  -Bellek ..\work\corpus\deneme500-bellek.jsonl

param(
    [string]$Kaynak,
    [string]$Bellek,
    [string]$Cikti,
    [int]$OrnekPerKategori = 6,
    [string]$Kok = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
if (-not $Kaynak) { $Kaynak = Join-Path $Kok 'work\corpus\deneme500.jsonl' }
if (-not $Bellek) { $Bellek = Join-Path $Kok 'work\corpus\deneme500-bellek.jsonl' }
if (-not $Cikti)  { $Cikti  = Join-Path $Kok 'work\corpus\inceleme.md' }

# ------------------------------------------------------------- sözlük ----
$zorunlu = @()   # çevrilmesi gereken terimler
$korunan = @()   # İngilizce kalması gereken adlar
foreach ($l in Get-Content (Join-Path $Kok 'sozluk.tsv') -Encoding utf8) {
    if ($l -match '^\s*#' -or $l.Trim() -eq '') { continue }
    $p = $l -split "`t"
    if ($p.Count -lt 2) { continue }
    if ($p[0] -ceq $p[1]) { $korunan += $p[0] }
    elseif ($p[0].Length -ge 4) { $zorunlu += ,@($p[0], $p[1]) }
}

# -------------------------------------------------------------- veri ----
$tm = @{}
foreach ($l in Get-Content $Bellek -Encoding utf8) {
    if ($l.Trim() -eq '') { continue }
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if ($o.en) { $tm[$o.en] = $o.tr }
}

$kayitlar = New-Object System.Collections.Generic.List[object]
foreach ($l in Get-Content $Kaynak -Encoding utf8) {
    if ($l.Trim() -eq '') { continue }
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if (-not $tm.ContainsKey($o.en)) { continue }
    $kayitlar.Add([pscustomobject]@{
        Kategori = "$($o.table)|$($o.col)"
        En = $o.en
        Tr = $tm[$o.en]
    })
}

Write-Host "Kaynak birim: $((Get-Content $Kaynak).Count)   Cevrilmis: $($kayitlar.Count)"

# ---------------------------------------------------------- denetimler ----
function Get-Uyarilar {
    param([string]$En, [string]$Tr, [string]$Kategori)
    $u = @()

    # 1) İngilizce kalması gereken adlar korunmuş mu
    foreach ($ad in $korunan) {
        if ($En -clike "*$ad*" -and $Tr -notlike "*$ad*") { $u += "ISIM KAYIP: $ad" }
    }

    # 2) Sözlükteki zorunlu karşılık kullanılmış mı
    foreach ($ç in $zorunlu) {
        if ($En -clike "*$($ç[0])*" -and $Tr -notlike "*$($ç[1])*") {
            $u += "SOZLUK: '$($ç[0])' -> '$($ç[1])' beklenirdi"
        }
    }

    # 3) Arayüz metinlerinde aşırı uzama (taşma riski)
    if ($Kategori -like 'clientstrings*' -and $En.Length -gt 8) {
        $oran = $Tr.Length / $En.Length
        if ($oran -gt 1.45) { $u += ("UZUNLUK: x{0:N2} ({1} -> {2} karakter)" -f $oran, $En.Length, $Tr.Length) }
    }

    # 4) Resmi hitap (biz senli-benli istiyoruz)
    if ($Tr -cmatch '\w(sınız|siniz|sunuz|sünüz)\b') { $u += 'HITAP: resmi (siz) kullanilmis' }

    # 5) Yüzde işareti yeri
    if ($Tr -cmatch '\d\s*%') { $u += 'YUZDE: isaret sayidan SONRA' }

    # 6) Çevrilmemiş
    if ($En.Length -gt 25 -and $En -ceq $Tr) { $u += 'CEVRILMEMIS' }

    return $u
}

# ------------------------------------------------------------- rapor ----
$o = [Text.StringBuilder]::new()
[void]$o.AppendLine('# Çeviri İnceleme Raporu')
[void]$o.AppendLine()
[void]$o.AppendLine("Kaynak: ``$(Split-Path $Kaynak -Leaf)``  ·  Çevrilmiş birim: **$($kayitlar.Count)**")
[void]$o.AppendLine()

# özet: kategori bazında uyarı sayısı
$tumUyari = @{}
foreach ($k in $kayitlar) {
    $k | Add-Member -NotePropertyName Uyari -NotePropertyValue (Get-Uyarilar -En $k.En -Tr $k.Tr -Kategori $k.Kategori) -Force
    foreach ($x in $k.Uyari) {
        $tip = ($x -split ':')[0]
        $tumUyari[$tip] = $tumUyari[$tip] + 1
    }
}

[void]$o.AppendLine('## Otomatik denetim özeti')
[void]$o.AppendLine()
[void]$o.AppendLine('| Denetim | Sayı | Ne demek |')
[void]$o.AppendLine('|---|---|---|')
$aciklama = @{
    'ISIM KAYIP'  = 'İngilizce kalması gereken bir ad çevrilmiş'
    'SOZLUK'      = 'Sözlükteki zorunlu karşılık kullanılmamış'
    'UZUNLUK'     = 'Arayüz metni çok uzamış, ekranda taşabilir'
    'HITAP'       = 'Resmi hitap (siz) kullanılmış, senli-benli olmalı'
    'YUZDE'       = 'Yüzde işareti sayıdan sonra yazılmış'
    'CEVRILMEMIS' = 'Metin İngilizce kalmış'
}
foreach ($t in $tumUyari.Keys | Sort-Object) {
    [void]$o.AppendLine("| $t | $($tumUyari[$t]) | $($aciklama[$t]) |")
}
if ($tumUyari.Count -eq 0) { [void]$o.AppendLine('| — | 0 | Otomatik denetimlerden geçti |') }
[void]$o.AppendLine()

# uyarılı olanlar önce
$uyarili = $kayitlar | Where-Object { $_.Uyari.Count -gt 0 }
if ($uyarili.Count -gt 0) {
    [void]$o.AppendLine("## İşaretlenenler ($($uyarili.Count))")
    [void]$o.AppendLine()
    [void]$o.AppendLine('Bunlara öncelikle bak. Hepsi gerçek hata olmayabilir — denetimler kaba.')
    [void]$o.AppendLine()
    foreach ($k in $uyarili | Select-Object -First 40) {
        [void]$o.AppendLine("**``$($k.Kategori)``** — $($k.Uyari -join ' · ')")
        [void]$o.AppendLine('```')
        [void]$o.AppendLine("EN  $($k.En)")
        [void]$o.AppendLine("TR  $($k.Tr)")
        [void]$o.AppendLine('```')
        [void]$o.AppendLine()
    }
}

# kategori kategori örnekler
[void]$o.AppendLine('## Kategoriye göre örnekler')
[void]$o.AppendLine()
foreach ($grup in $kayitlar | Group-Object Kategori | Sort-Object Name) {
    [void]$o.AppendLine("### ``$($grup.Name)``  ($($grup.Count) birim)")
    [void]$o.AppendLine()
    foreach ($k in $grup.Group | Select-Object -First $OrnekPerKategori) {
        [void]$o.AppendLine('```')
        [void]$o.AppendLine("EN  $($k.En)")
        [void]$o.AppendLine("TR  $($k.Tr)")
        if ($k.Uyari.Count -gt 0) { [void]$o.AppendLine("    !! $($k.Uyari -join ' | ')") }
        [void]$o.AppendLine('```')
    }
    [void]$o.AppendLine()
}

$o.ToString() | Set-Content $Cikti -Encoding utf8
Write-Host "Rapor: $Cikti" -ForegroundColor Green
