# Sözlükte olmayan köşeli-parantez anahtarları için korpustan karşılık ÖNERİR.
#
# Neden türetme: 862 anahtar için sıfırdan karar vermek gereksiz — bu terimler
# korpusta zaten çevrilmiş durumda. En sık kullanılan karşılığı çıkarıp
# gözden geçirilecek bir liste yapmak, 862 kararı bir inceleme turuna indiriyor.
#
# Çıktı: work/corpus/sozluk-oneri.tsv
#   anahtar <TAB> öneri <TAB> güven <TAB> geçiş <TAB> diğer adaylar
#
# Kullanım: powershell -File scripts\sozluk-oner.ps1 [enAzGecis]
param([int]$EnAzGecis = 3)

$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $PSScriptRoot

# --- mevcut sözlük (normalleştirilmiş anahtarlarla) ---
$var = @{}
foreach ($satir in [IO.File]::ReadAllLines("$kok\sozluk.tsv", [Text.Encoding]::UTF8)) {
    $s = $satir.Trim()
    if ($s.Length -eq 0 -or $s.StartsWith('#')) { continue }
    $p = $s -split "`t"
    if ($p.Count -lt 2 -or -not $p[0]) { continue }
    $var[($p[0] -replace '[^\p{L}\p{N}]', '').ToLowerInvariant()] = $true
}

# --- korpustan anahtar -> görünen metin dağılımı ---
$fs = [IO.File]::Open("$kok\work\corpus\bellek.jsonl", 'Open', 'Read', 'ReadWrite')
$sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
$rx = [regex]'\[([^\]\|{}<>]+)\|([^\]]*)\]'
$dagilim = @{}
while ($null -ne ($l = $sr.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($l)) { continue }
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if (-not $o.tr) { continue }
    foreach ($m in $rx.Matches($o.tr)) {
        $k = $m.Groups[1].Value
        $g = $m.Groups[2].Value.Trim()
        if ($g.Length -eq 0) { continue }
        if (-not $dagilim.ContainsKey($k)) { $dagilim[$k] = @{} }
        if ($dagilim[$k].ContainsKey($g)) { $dagilim[$k][$g]++ } else { $dagilim[$k][$g] = 1 }
    }
}
$sr.Close(); $fs.Close()

$satirlar = New-Object Collections.ArrayList
foreach ($k in $dagilim.Keys) {
    $n = ($k -replace '[^\p{L}\p{N}]', '').ToLowerInvariant()
    if ($var.ContainsKey($n)) { continue }          # zaten sözlükte
    if ($k -match 'DNT|UNUSED|^LA[A-Z]') { continue }  # geliştirici artığı
    $toplam = ($dagilim[$k].Values | Measure-Object -Sum).Sum
    if ($toplam -lt $EnAzGecis) { continue }
    $sirali = $dagilim[$k].GetEnumerator() | Sort-Object Value -Descending
    $ilk = $sirali | Select-Object -First 1
    $guven = [math]::Round(100.0 * $ilk.Value / $toplam)
    $digerleri = ($sirali | Select-Object -Skip 1 -First 3 | ForEach-Object { "$($_.Name)($($_.Value))" }) -join ' ; '
    [void]$satirlar.Add([pscustomobject]@{
        Anahtar = $k; Oneri = $ilk.Name; Guven = $guven; Gecis = $toplam; Digerleri = $digerleri
    })
}

$cikti = "$kok\work\corpus\sozluk-oneri.tsv"
$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine("# korpustan turetilmis oneriler - INCELENMELI")
[void]$sb.AppendLine("# anahtar`toneri`tguven%`tgecis`tdiger adaylar")
foreach ($r in ($satirlar | Sort-Object Gecis -Descending)) {
    [void]$sb.AppendLine("$($r.Anahtar)`t$($r.Oneri)`t$($r.Guven)`t$($r.Gecis)`t$($r.Digerleri)")
}
[IO.File]::WriteAllText($cikti, $sb.ToString(), [Text.UTF8Encoding]::new($false))

Write-Output ("oneri uretildi : {0:N0} anahtar" -f $satirlar.Count)
Write-Output ("  guven >= %80 : {0:N0}" -f ($satirlar | Where-Object { $_.Guven -ge 80 }).Count)
Write-Output ("  guven %50-79 : {0:N0}" -f ($satirlar | Where-Object { $_.Guven -ge 50 -and $_.Guven -lt 80 }).Count)
Write-Output ("  guven < %50  : {0:N0}" -f ($satirlar | Where-Object { $_.Guven -lt 50 }).Count)
Write-Output ("cikti          : $cikti")
Write-Output ""
Write-Output "--- en sik 20 ---"
$satirlar | Sort-Object Gecis -Descending | Select-Object -First 20 |
    Format-Table @{n='gecis';e={$_.Gecis}}, @{n='guven';e={"%$($_.Guven)"}}, Anahtar, Oneri, Digerleri -AutoSize
