$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\Acr122uNfcReader\Acr122uNfcReader.csproj"
$dist = Join-Path $PSScriptRoot "dist"
$zip = Join-Path $dist "acr122u_nfc_reader-win-x64.zip"

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist | Out-Null

dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None -o $dist

$exe = Join-Path $dist "ACR122U.NFC.Reader.exe"
if (-not (Test-Path $exe)) {
    throw "O executável não foi gerado: $exe"
}

$zipItems = Get-ChildItem $dist | Where-Object { $_.Name -ne (Split-Path $zip -Leaf) }
Compress-Archive -Path $zipItems.FullName -DestinationPath $zip -Force

Write-Host ""
Write-Host "Build concluído."
Write-Host "EXE: $exe"
Write-Host "ZIP: $zip"
