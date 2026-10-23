# Yayin derlemesi + kurulum dosyasi. Kullanim: powershell -ExecutionPolicy Bypass -File .\installer\build-setup.ps1
# 1) Surumu ledsync\LedSync.csproj'daki <Version>'dan okur.
# 2) Kendi icinde .NET bulunduran (self-contained) win-x64 yayin alir: hedef bilgisayarda .NET kurulu olmasi gerekmez.
# 3) Inno Setup ile dist\RHZ-LED-Controller-<surum>-setup.exe uretir.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'ledsync\LedSync.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'LedSync.csproj icinde <Version> yok.' }

$publish = Join-Path $root "bin\publish\$version"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Yayin derlemesi basarisiz.' }
if (-not (Test-Path (Join-Path $publish 'modules\SmbusPIIX4.bin'))) { throw 'Yayinda modules\SmbusPIIX4.bin yok.' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 bulunamadi (winget install JRSoftware.InnoSetup).' }
& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'RLC.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup derlemesi basarisiz.' }

$setup = Join-Path $root "dist\RHZ-LED-Controller-$version-setup.exe"
$item = Get-Item $setup
Write-Output ("Kurulum: {0} ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB))
Write-Output ("SHA-256: {0}" -f (Get-FileHash $setup -Algorithm SHA256).Hash)
