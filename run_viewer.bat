@echo off
rem ============================================================
rem  Star Field Viewer - build & launch (Windows)
rem  Compiles the algorithm sources + viewer, then starts the demo.
rem  Requires .NET Framework 4.x (csc.exe ships with Windows).
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Install .NET Framework 4.x.
    pause
    exit /b 1
)

echo === Compiling Star Field Viewer ===
"%CSC%" /nologo /optimize+ /unsafe /target:winexe /out:StarFieldViewer.exe ^
    src\StarCut.cs src\StarField.cs src\StarView.cs src\SeedCodec.cs viewer\StarFieldViewer.cs
if errorlevel 1 (
    echo [ERROR] Compilation failed.
    pause
    exit /b 1
)

echo === Launching Viewer ===
start "" "StarFieldViewer.exe"
echo Done.
endlocal
