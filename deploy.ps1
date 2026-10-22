# Kurulu surumu gunceller: calisan RLEDC'yi kapatir, bin\ledsync'e derler ve yonetici olarak baslatir.
# Kullanici onayi gerekmez (22 Ekim 2026 karari). Kullanim: powershell -ExecutionPolicy Bypass -File .\deploy.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'bin\ledsync\RLEDC.exe'

if (Get-Process RLEDC -ErrorAction SilentlyContinue) {
    # Uygulama yonetici olarak calisir; normal yetkiyle durdurulamazsa taskkill yonetici olarak calistirilir.
    try { Stop-Process -Name RLEDC -Force -ErrorAction Stop }
    catch { Start-Process taskkill -ArgumentList '/F /IM RLEDC.exe' -Verb RunAs -WindowStyle Hidden -Wait }
    # Exe ve DLL kilidi kalkana kadar bekle.
    for ($i = 0; $i -lt 50 -and (Get-Process RLEDC -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 100 }
    if (Get-Process RLEDC -ErrorAction SilentlyContinue) { throw 'RLEDC kapatilamadi.' }
    Write-Output 'RLEDC kapatildi.'
}

dotnet build (Join-Path $root 'ledsync\LedSync.csproj') -c Release -o (Join-Path $root 'bin\ledsync') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Derleme basarisiz; eski surum baslatilmadi.' }

Start-Process -FilePath $exe -Verb RunAs
Start-Sleep -Seconds 4
$p = Get-Process RLEDC -ErrorAction SilentlyContinue
if (-not $p) { throw 'RLEDC baslamadi; %AppData%\LedSync\rlc-hata.log dosyasina bak.' }
Write-Output "RLEDC calisiyor (PID $($p.Id))."
