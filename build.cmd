@echo off
setlocal

set DOTNET=dotnet
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    set DOTNET="C:\Program Files\dotnet\dotnet.exe"
)

echo [1/2] Building EmailChecker...

rem === OPTION A: framework-dependent (~1 MB, requires .NET 8 Desktop Runtime on target) ===
%DOTNET% publish EmailChecker.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o dist

rem === OPTION B: self-contained (~68 MB, zero dependencies - swap comments to use) ===
rem %DOTNET% publish EmailChecker.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained true -o dist

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build FAILED.
    exit /b %ERRORLEVEL%
)

echo [2/2] Done.
echo.
echo Output: dist\EmailChecker.exe
endlocal