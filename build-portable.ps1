# Genera la app portable (self-contained, sin instalacion) -> un unico .exe.
# Uso:  .\build-portable.ps1            -> publish\GPhotosTakeoutSync (win-x64)
#       .\build-portable.ps1 -Rid win-arm64
# Si PowerShell bloquea el script por la directiva de ejecucion:
#       powershell -ExecutionPolicy Bypass -File .\build-portable.ps1
param(
    [string]$Rid = "win-x64",
    [string]$Out = "publish/GPhotosTakeoutSync"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot   # ejecuta siempre desde la raiz del proyecto

Write-Host "Compilando app portable ($Rid)..." -ForegroundColor Cyan
dotnet publish src/GPhotosSyncer.App/GPhotosSyncer.App.csproj -c Release -r $Rid -o $Out --nologo

if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path $Out "GPhotosTakeoutSync.exe"
    $sizeMB = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ""
    Write-Host ("Listo. Ejecutable: {0}  ({1:N0} MB)" -f $exe, $sizeMB) -ForegroundColor Green
    Write-Host "Es un unico .exe portable; copialo donde quieras y ejecutalo sin instalar nada."
}
else {
    Write-Host "La compilacion fallo (codigo $LASTEXITCODE)." -ForegroundColor Red
}
