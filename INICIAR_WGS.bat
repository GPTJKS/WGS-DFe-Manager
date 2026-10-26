@echo off
setlocal
cd /d "%~dp0"
title WGS DF-e Manager 0.17.5 - Desenvolvimento
set "WGS_PROJECT=src\WgsDfe.Desktop\WgsDfe.Desktop.csproj"
set "WGS_APP=src\WgsDfe.Desktop\bin\Debug\net10.0-windows\WgsDfe.Desktop.dll"
set "WGS_SDK="

rem Use an explicitly configured portable SDK, the system SDK, or the test SDK.
if defined WGS_DOTNET_EXE if exist "%WGS_DOTNET_EXE%" (
 "%WGS_DOTNET_EXE%" --list-sdks 2>nul | findstr /b /c:"10." >nul
 if not errorlevel 1 set "WGS_SDK=%WGS_DOTNET_EXE%"
)
if defined WGS_SDK goto build
dotnet --list-sdks 2>nul | findstr /b /c:"10." >nul
if not errorlevel 1 set "WGS_SDK=dotnet"
if defined WGS_SDK goto build
if exist "%TEMP%\wgs-dfe-sdk-10\dotnet.exe" (
 "%TEMP%\wgs-dfe-sdk-10\dotnet.exe" --list-sdks 2>nul | findstr /b /c:"10." >nul
 if not errorlevel 1 set "WGS_SDK=%TEMP%\wgs-dfe-sdk-10\dotnet.exe"
)
if defined WGS_SDK goto build

rem A previously built development version needs only the .NET Desktop runtime.
if not exist "%WGS_APP%" goto missing
echo Abrindo a compilacao de desenvolvimento existente.
echo Sem SDK, alteracoes posteriores no codigo precisam ser compiladas antes.
if /i "%~1"=="--verificar" (
 dotnet --list-runtimes 2>nul | findstr /b /c:"Microsoft.WindowsDesktop.App 10." >nul
 if errorlevel 1 goto missing
 echo Runtime .NET Desktop 10 e aplicativo encontrados.
 exit /b 0
)
dotnet "%WGS_APP%"
if errorlevel 1 goto failed
exit /b 0

:build
echo Preparando WGS DF-e Manager 0.17.5...
"%WGS_SDK%" build "%WGS_PROJECT%" -c Debug --nologo
if errorlevel 1 goto failed
if /i "%~1"=="--verificar" (
 echo Iniciador verificado. Compilacao concluida.
 exit /b 0
)
"%WGS_SDK%" "%WGS_APP%"
if errorlevel 1 goto failed
exit /b 0

:missing
echo Nao foi encontrado um SDK .NET 10 nem uma compilacao pronta com runtime compativel.
echo Disponibilize o SDK .NET 10 e execute este BAT novamente.
echo Um SDK portatil pode ser indicado pela variavel WGS_DOTNET_EXE.
pause
exit /b 1

:failed
echo.
echo Nao foi possivel compilar ou abrir o aplicativo. Confira a mensagem acima.
pause
exit /b 1
