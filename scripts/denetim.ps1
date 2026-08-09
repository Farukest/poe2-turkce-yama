# Projenin bütününü tarar ve KALAN İŞ listesi çıkarır.
# Yalnızca sayar, hiçbir şeyi değiştirmez. Koşu sürerken de çalıştırılabilir
# (bellek dosyası FileShare.ReadWrite ile açılıyor).
#
# Kullanım: powershell -File scripts\denetim.ps1
$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $PSScriptRoot
$corpus = Join-Path $kok 'work\corpus'

function OkuJsonl($yol) {
    $fs = [IO.File]::Open($yol, 'Open', 'Read', 'ReadWrite')
    $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
    $liste = New-Object Collections.ArrayList
    while ($null -ne ($l = $sr.ReadLine())) {
        if ([string]::IsNullOrWhiteSpace($l)) { continue }
        try { $o = $l | ConvertFrom-Json } catch { continue }
        [void]$liste.Add($o)
    }
    $sr.Close(); $fs.Close()
    return $liste
}

Write-Output "=== KAYNAK KAPSAMI ==="
$bellek = @{}
foreach ($o in (OkuJsonl "$corpus\bellek.jsonl")) { if ($o.en) { $bellek[$o.en] = $o.tr } }
Write-Output ("bellekteki benzersiz ceviri : {0:N0}" -f [int]$bellek.Keys.Count)

foreach ($ad in @('kaynak.jsonl', 'csd-kaynak.jsonl')) {
    $yol = Join-Path $corpus $ad
    if (-not (Test-Path $yol)) { continue }
    $t = 0; $var = 0
    $seen = @{}
    foreach ($o in (OkuJsonl $yol)) {
        if (-not $o.en) { continue }
        if ($seen.ContainsKey($o.en)) { continue }
        $seen[$o.en] = 1
        $t++
        if ($bellek.ContainsKey($o.en)) { $var++ }
    }
    $oran = if ($t) { 100.0 * $var / $t } else { 0 }
    Write-Output ("{0,-18} {1,7:N0} / {2,7:N0} benzersiz  = %{3:N1}   EKSIK: {4:N0}" -f $ad, $var, $t, $oran, ($t - $var))
}

Write-Output "`n=== CEVIRI KALITESI (bellek uzerinde) ==="
$ceviriYok = 0      # tr bos
$ayni = 0           # tr = en (cevrilmemis)
$tutucuFark = 0     # yer tutucu sayisi tutmuyor
$etiketFark = 0     # <etiket> sayisi tutmuyor
$yuzdeKayip = 0     # kaynakta % var, ceviride yok
$bozukJeton = 0     # {%+d} gibi bozulmus yer tutucu
$satirKacis = 0     # kaynakta "\n" kacisi, ceviride gercek satir sonu

$phRx = [regex]'\{\d*(?::[^}]*)?\}'
$tagRx = [regex]'<[^>]+>'
$bozukRx = [regex]'\{[^}]*[%\\][^}]*\}'

foreach ($en in $bellek.Keys) {
    $tr = $bellek[$en]
    if ([string]::IsNullOrWhiteSpace($tr)) { $ceviriYok++; continue }
    if ($tr -ceq $en) { $ayni++ }

    $a = @($phRx.Matches($en) | ForEach-Object { $_.Value }) | Sort-Object
    $b = @($phRx.Matches($tr) | ForEach-Object { $_.Value }) | Sort-Object
    if (($a -join '|') -ne ($b -join '|')) { $tutucuFark++ }

    if ($tagRx.Matches($en).Count -ne $tagRx.Matches($tr).Count) { $etiketFark++ }

    $ep = ([regex]::Matches($en, '%')).Count
    $tp = ([regex]::Matches($tr, '%')).Count
    if ($tp -lt $ep) { $yuzdeKayip++ }

    foreach ($m in $bozukRx.Matches($tr)) {
        if ($m.Value -notmatch '^\{[^}]*\}$') { continue }
        if ($m.Value -match '^\{\d*(:[^}]*)?\}$') { continue }
        $bozukJeton++; break
    }

    if ($en.Contains('\n') -and -not ($en.Contains("`n")) -and ($tr.Contains("`n"))) { $satirKacis++ }
}

Write-Output ("bos ceviri                  : {0:N0}" -f $ceviriYok)
Write-Output ("ceviri = kaynak (Ingilizce) : {0:N0}" -f $ayni)
Write-Output ("yer tutucu farkli           : {0:N0}" -f $tutucuFark)
Write-Output ("etiket sayisi farkli        : {0:N0}" -f $etiketFark)
Write-Output ("yuzde isareti kaybolmus     : {0:N0}" -f $yuzdeKayip)
Write-Output ("bozuk yer tutucu jetonu     : {0:N0}" -f $bozukJeton)
Write-Output ("satir sonu kacisi bozulmus  : {0:N0}" -f $satirKacis)

Write-Output "`n=== TERIM TUTARLILIGI ==="
# Ayni kavramin birden fazla Turkce karsiligi var mi: koseli parantez
# anahtarindan sonra gelen ilk kelimenin dagilimi
$anahtarlar = @('Evasion', 'Armour', 'EnergyShield', 'StunThreshold', 'Critical', 'Charges')
foreach ($a in $anahtarlar) {
    $dag = @{}
    foreach ($tr in $bellek.Values) {
        foreach ($m in [regex]::Matches($tr, "\[$a\|([^\]]+)\]")) {
            $k = $m.Groups[1].Value
            if ($dag.ContainsKey($k)) { $dag[$k]++ } else { $dag[$k] = 1 }
        }
    }
    if ($dag.Count -le 1) { continue }
    $ilk = $dag.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 4
    $ozet = ($ilk | ForEach-Object { "$($_.Name) ($($_.Value))" }) -join ' | '
    Write-Output ("{0,-14} {1} farkli karsilik : {2}" -f $a, $dag.Count, $ozet)
}

Write-Output "`n=== KALAN IS DOSYALARI ==="
foreach ($f in @('stat-kalanlar-son.jsonl', 'kalanlar.jsonl')) {
    $y = Join-Path $corpus $f
    if (Test-Path $y) {
        Write-Output ("{0,-26} {1:N0} satir" -f $f, (Get-Content $y | Measure-Object -Line).Lines)
    }
}
