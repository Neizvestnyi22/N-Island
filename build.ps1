﻿# Сборка релизов N-Island в папку dist\
#   .\build.ps1            — все варианты
#   .\build.ps1 -Only x64  — только полная версия x64
#
# NIsland-<версия>-win-x64.zip       — всё внутри, ничего ставить не нужно (рекомендуется)
# NIsland-<версия>-win-x86.zip       — для старых 32-битных Windows 10
# NIsland-<версия>-win-x64-lite.exe  — маленький файл, нужен .NET 8 Desktop Runtime
#
# Полные версии не сжимаются внутри exe: так Windows подгружает код прямо из файла и памяти уходит меньше,
# а размер скачивания уменьшает zip. SHA256SUMS.txt нужен встроенному обновлению — оно сверяет файл перед установкой.

param([string]$Only = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
# нужен .NET 8 SDK: «dotnet» из PATH бывает только средой выполнения — тогда берём SDK из профиля пользователя
$dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not (& dotnet --list-sdks 2>$null)) { $dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe" }
$version = ([xml](Get-Content NIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
New-Item -ItemType Directory -Force dist | Out-Null

function Build($rid, $self, $suffix) {
    $out = "obj\dist-$rid$suffix"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish NIsland.csproj -c Release -r $rid --self-contained $self -p:PublishSingleFile=true -p:DebugType=none `
        -p:IncludeNativeLibrariesForSelfExtract=true -o $out -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "сборка $rid$suffix не удалась" }
    $name = "NIsland-$version-$rid$suffix"
    if ($self) {
        Copy-Item README.txt "$out\README.txt" -Force
        Compress-Archive -Path "$out\NIsland.exe", "$out\README.txt" -DestinationPath "dist\$name.zip" -Force -CompressionLevel Optimal
        "{0,-40} {1,8:N1} МБ" -f "$name.zip", ((Get-Item "dist\$name.zip").Length / 1MB)
    } else {
        Copy-Item "$out\NIsland.exe" "dist\$name.exe" -Force
        "{0,-40} {1,8:N1} МБ" -f "$name.exe", ((Get-Item "dist\$name.exe").Length / 1MB)
    }
}

if (!$Only -or $Only -eq "x64")  { Build "win-x64" $true "" }
if (!$Only -or $Only -eq "x86")  { Build "win-x86" $true "" }
if (!$Only -or $Only -eq "lite") { Build "win-x64" $false "-lite" }

Get-ChildItem dist\*.zip, dist\*.exe | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash, $_.Name } | Set-Content dist\SHA256SUMS.txt -Encoding ascii
"Готово: dist\"
