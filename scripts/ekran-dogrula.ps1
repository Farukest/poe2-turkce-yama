# Ekran görüntülerinde şikâyet edilen satırların bellekteki son hâlini gösterir.
# Kullanım: powershell -File scripts\ekran-dogrula.ps1
$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $PSScriptRoot
$bellek = Join-Path $kok 'work\corpus\bellek.jsonl'

# Ekran görüntüsündeki satırların kaynak metinleri
$hedefler = @(
    '{0}% increased [Evasion|Evasion Rating]',
    '{0}% increased maximum [EnergyShield|Energy Shield]',
    '{0}% increased [Armour]',
    '{0}% increased [Armour|Armour]',
    '{0}% increased Area of Effect',
    '{0:+d} to [Dexterity]',
    '{0:+d} to [StunThreshold|Stun Threshold]',
    '{0:+d} to maximum Life',
    '{0:+d} to [Armour|Armour]',
    'Requires: ',
    'Requirements not met',
    'Armour:',
    'Body Armours'
)

$fs = [IO.File]::Open($bellek, 'Open', 'Read', 'ReadWrite')
$sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
$bul = @{}
while ($null -ne ($l = $sr.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($l)) { continue }
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if ($hedefler -contains $o.en) { $bul[$o.en] = $o.tr }   # append-only: son kazanır
}
$sr.Close(); $fs.Close()

foreach ($h in $hedefler) {
    if ($bul.ContainsKey($h)) {
        Write-Output ("EN  {0}" -f $h)
        Write-Output ("TR  {0}" -f $bul[$h])
    } else {
        Write-Output ("EN  {0}" -f $h)
        Write-Output  "TR  (bellekte yok)"
    }
    Write-Output ""
}

# Terim tutarlılığı: aynı kavram iki farklı karşılıkla geçiyor mu
$fs = [IO.File]::Open($bellek, 'Open', 'Read', 'ReadWrite')
$sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
$sayac = @{ 'Kaçınma Değeri' = 0; 'Kaçınma Derecesi' = 0; 'Kaçınma Rating' = 0 }
while ($null -ne ($l = $sr.ReadLine())) {
    foreach ($k in @($sayac.Keys)) { if ($l.Contains($k)) { $sayac[$k]++ } }
}
$sr.Close(); $fs.Close()
Write-Output "--- terim tutarliligi (Evasion Rating) ---"
$sayac.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Output ("{0,-20} {1}" -f $_.Name, $_.Value) }
