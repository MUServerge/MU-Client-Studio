param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

dotnet restore MUClientStudio.sln
dotnet build MUClientStudio.sln -c $Configuration --no-restore

if ($Publish) {
    dotnet publish src/MUClientStudio.App/MUClientStudio.App.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o artifacts/win-x64

    Write-Host "Published: $root\artifacts\win-x64\MUClientStudio.exe"
}
