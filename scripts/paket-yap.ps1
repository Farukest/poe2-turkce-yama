# Dağıtılabilir ZIP üretir: iki exe + veri + OKUBENI.
#
# Paket İÇERMEZ:
#   · oo2core.dll  — RAD/Epic'in tescilli kütüphanesi, dağıtım hakkımız yok.
#                     Kurulum programı kullanıcının makinesinde arıyor.
#   · kaynak.jsonl / csd-kaynak.jsonl — korpus oyundan üretiliyor (bkz. Kurulum.cs).
#                     Hem 24 MB tasarruf hem de sürüm kayması riskinin çözümü.
#
# Kullanım: powershell -File scripts\paket-yap.ps1
$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $PSScriptRoot
$cikti = Join-Path $kok 'paket'
$stage = Join-Path $cikti 'PoE2-Turkce'

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Output "1/5  Tek dosya exe derleniyor (self-contained, win-x64)..."
$yayin = Join-Path $cikti 'yayin'
if (Test-Path $yayin) { Remove-Item -Recurse -Force $yayin }
# EnableCompressionInSingleFile: WinForms bagimliliklari exe'yi ~95 MB yapiyor
# ve paketde iki kopyasi var. Sikistirma bunu yariya indiriyor; bedeli ilk
# acilista ~1 sn gecikme.
dotnet publish (Join-Path $kok 'src\poe2tr') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $yayin --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish basarisiz" }

# oo2core.dll publish ciktisina kopyalaniyor; PAKETE GIRMEMELI
Remove-Item (Join-Path $yayin 'oo2core.dll') -ErrorAction SilentlyContinue

$exe = Join-Path $yayin 'poe2tr.exe'
if (-not (Test-Path $exe)) { throw "poe2tr.exe uretilmedi" }

Write-Output "2/5  Exe yerlestiriliyor..."
# TEK exe. Kurulum ve guncelleme ayni islem oldugu icin ikinci bir kopya
# tasimak 68 MB'i sirf farkli bir pencere basligi icin cogaltmak demekti.
# Pencere, daha once kurulup kurulmadigina bakip kendini "Kur" ya da
# "Yeniden Uygula" diye adlandiriyor.
Copy-Item $exe (Join-Path $stage 'PoE2-Turkce.exe')

Write-Output "3/5  Veri dosyalari kopyalaniyor..."
$veri = Join-Path $stage 'veri'
New-Item -ItemType Directory -Force -Path (Join-Path $veri 'fontlar') | Out-Null

Copy-Item (Join-Path $kok 'work\corpus\bellek.jsonl')            (Join-Path $veri 'bellek.jsonl')
Copy-Item (Join-Path $kok 'sozluk.tsv')                          $veri
Copy-Item (Join-Path $kok 'terim-duzeltme.tsv')                  $veri
Copy-Item (Join-Path $kok 'ceviri-disi.txt')                     $veri
Copy-Item (Join-Path $kok 'tools\dat-schema\schema.min.json')    $veri
Copy-Item (Join-Path $kok 'work\font-yamali\*.ttf')              (Join-Path $veri 'fontlar')

# Ucuncu taraf lisanslari
$lis = Join-Path $stage 'lisanslar'
New-Item -ItemType Directory -Force -Path $lis | Out-Null
Copy-Item (Join-Path $kok 'tools\LibGGPK3\LICENSE')   (Join-Path $lis 'LibGGPK3-LICENSE.txt')
Copy-Item (Join-Path $kok 'tools\dat-schema\LICENSE') (Join-Path $lis 'dat-schema-LICENSE.txt')

Copy-Item (Join-Path $kok 'paket\OKUBENI.txt') $stage

Write-Output "4/5  Icerik dogrulaniyor..."
$beklenen = @(
    'PoE2-Turkce.exe', 'OKUBENI.txt',
    'veri\bellek.jsonl', 'veri\sozluk.tsv', 'veri\schema.min.json',
    'veri\ceviri-disi.txt', 'veri\terim-duzeltme.tsv',
    'veri\fontlar\fontin-regular.ttf', 'veri\fontlar\fontin-bold.ttf',
    'veri\fontlar\fontin-italic.ttf', 'veri\fontlar\fontin-smallcaps.ttf',
    'veri\fontlar\optimusprincepssemibold.ttf'
)
foreach ($b in $beklenen) {
    if (-not (Test-Path (Join-Path $stage $b))) { throw "PAKETTE EKSIK: $b" }
}
if (Get-ChildItem $stage -Recurse -Filter 'oo2core*.dll') { throw "oo2core pakete girmis - dagitilamaz" }

Write-Output "5/5  ZIP olusturuluyor..."
$tarih = Get-Date -Format 'yyyyMMdd'
$zip = Join-Path $cikti "PoE2-Turkce-Yama-$tarih.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output ""
Write-Output "TAMAM  $zip  ($mb MB)"
Get-ChildItem $stage -Recurse -File |
    Select-Object @{n='dosya';e={$_.FullName.Substring($stage.Length+1)}}, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} |
    Sort-Object MB -Descending | Format-Table -AutoSize
