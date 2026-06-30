@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo No se encuentra 'dotnet'. Instala el .NET 9 SDK:
  echo   winget install Microsoft.DotNet.SDK.9
  echo   o https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

echo Compilando la app portable ^(win-x64^)...
echo.
dotnet publish src\GPhotosSyncer.App\GPhotosSyncer.App.csproj -c Release -r win-x64 -o publish\GPhotosTakeoutSync --nologo

echo.
if errorlevel 1 (
  echo *** La compilacion fallo. Revisa los mensajes de arriba. ***
) else (
  echo Listo. Ejecutable: publish\GPhotosTakeoutSync\GPhotosTakeoutSync.exe
  echo Es un unico .exe portable; copialo donde quieras y ejecutalo sin instalar nada.
)
echo.
pause
endlocal
