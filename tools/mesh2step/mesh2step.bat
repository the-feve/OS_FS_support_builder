@echo off
rem mesh2step: drag STL/OBJ/PLY files onto this file to get clean STEP files next to them.
rem Add -- options after the files when running from a terminal, e.g.:
rem   mesh2step.bat part.stl --faces 20000 --iges
rem Set BLENDER_EXE to use a specific Blender.
setlocal
if "%~1"=="" (
  echo Drag one or more STL / OBJ / PLY files onto mesh2step.bat.
  pause
  exit /b 1
)
set "BL=%BLENDER_EXE%"
if not defined BL for /f "delims=" %%B in ('where blender.exe 2^>nul') do if not defined BL set "BL=%%B"
if not defined BL for /f "delims=" %%B in ('dir /b /s /o-n "%ProgramFiles%\Blender Foundation\blender.exe" 2^>nul') do if not defined BL set "BL=%%B"
if not defined BL for /f "delims=" %%B in ('dir /b /s /o-n "%ProgramFiles(x86)%\Steam\steamapps\common\Blender\blender.exe" 2^>nul') do if not defined BL set "BL=%%B"
if not defined BL (
  echo Blender not found. Install it from blender.org, or set BLENDER_EXE to the full path of blender.exe.
  pause
  exit /b 1
)
echo Using %BL%
"%BL%" -b --factory-startup --python-exit-code 1 -P "%~dp0mesh2step.py" -- %* | findstr /l /c:"[mesh2step]" /c:"Error" /c:"Traceback" /c:"ERROR"
echo.
echo Done. STEP files are next to the originals.
pause
