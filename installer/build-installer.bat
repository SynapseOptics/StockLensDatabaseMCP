@echo off
setlocal
cd /d "%~dp0\.."

echo === Building StockLensDatabaseMCP Release ===
REM Clean+build forces the dependent-DLL copy step in WPF/SDK so the
REM configurator's transitive references end up in its bin folder.
dotnet clean -c Release
dotnet build -c Release
if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

echo.
echo === Compiling Installer ===
REM STOCK_CATALOGS_DIR (optional) lets a builder point the installer
REM at a non-default catalog source — useful when the sibling
REM SynapseLensHH-LT repo isn't checked out at the usual location.
set ISCC_ARGS=
if defined STOCK_CATALOGS_DIR (
    echo Catalog source: %STOCK_CATALOGS_DIR%
    set ISCC_ARGS=/DSourceCatalogs="%STOCK_CATALOGS_DIR%"
)
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" %ISCC_ARGS% installer\StockLensDatabaseMCP.iss
    if errorlevel 1 (
        echo Inno Setup compile failed!
        exit /b 1
    )
) else (
    echo Inno Setup 6 not found at "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    echo Please run ISCC.exe manually on installer\StockLensDatabaseMCP.iss
    exit /b 1
)

echo.
echo === Done! ===
echo Installer output: installer\Output\
endlocal
