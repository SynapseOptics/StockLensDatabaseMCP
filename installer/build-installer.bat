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
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\StockLensDatabaseMCP.iss
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
