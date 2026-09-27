@echo off
setlocal

set DOTNET=dotnet
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    set DOTNET="C:\Program Files\dotnet\dotnet.exe"
)

echo [1/2] Building EmailChecker...
%DOTNET% publish EmailChecker.csproj ^
    -c Release ^
    -r win-x64 ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    --self-contained true ^
    -o dist

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build FAILED.
    exit /b %ERRORLEVEL%
)

echo [2/2] Copying .env...
if exist .env (
    copy /Y .env dist\.env >nul
)

echo.
echo Done: dist\EmailChecker.exe
endlocal
