@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Publishing Discord Shield x64 binaries...
dotnet publish "src\DiscordTunnel.csproj" -c Release -r win-x64 --self-contained false -o "publish"
if errorlevel 1 (
    echo Publish failed.
    exit /b %errorlevel%
)

echo [2/3] Checking Inno Setup compiler...
set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if exist "%ISCC%" (
    echo [3/3] Compiling installer wizard...
    if not exist "releases" mkdir "releases"
    "%ISCC%" "installer\DiscordShield_Installer.iss"
    if errorlevel 1 (
        echo Installer compilation failed.
        exit /b %errorlevel%
    )
    echo Build and packaging completed successfully. Output: releases\Discord Shield Setup.exe
) else (
    echo Inno Setup 6 compiler not found. Published binaries are available in the publish folder.
)

endlocal
