@echo off
REM ==========================================================================
REM  MgSO4 AR Sim - command-line build (Unity 6.6 / 6000.6.5f1, Android)
REM  Phase 1: install packages + defines.  Phase 2: generate scenes, build APK.
REM  Output: Builds\MgSO4_AR_Sim.apk     Logs: BuildLogs\
REM ==========================================================================
setlocal
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
set "ROOT=%~dp0"
set "PROJ=%ROOT%MgSO4_AR_Simulation"
set "LOGS=%ROOT%BuildLogs"
if not exist "%LOGS%" mkdir "%LOGS%"
if exist "%LOGS%\run_demo.txt" (
  del "%LOGS%\run_demo.txt"
  echo Recording presentation demo...
  start "" "%ROOT%Builds\WindowsPreview\MgSO4_AR_Sim.exe" -demo -screen-width 540 -screen-height 1080 -screen-fullscreen 0 -logFile "%LOGS%\demo_player.log"
  goto :eof
)
if exist "%LOGS%\run_preview.txt" (
  del "%LOGS%\run_preview.txt"
  echo Starting Windows preview...
  start "" "%ROOT%Builds\WindowsPreview\MgSO4_AR_Sim.exe" -autoshot -screen-width 540 -screen-height 1080 -screen-fullscreen 0 -logFile "%LOGS%\player.log"
  goto :eof
)
if exist "%LOGS%\blender_job.py" (
  call :blender
)
if exist "%LOGS%\blender_only.txt" (
  del "%LOGS%\blender_only.txt"
  goto :eof
)
echo running > "%LOGS%\status.txt"

echo [1/2] Installing packages and project settings...
"%UNITY%" -batchmode -projectPath "%PROJ%" -buildTarget Android -executeMethod MgSO4Bootstrap.Run -logFile "%LOGS%\1_bootstrap.log"
echo phase1 exit %ERRORLEVEL% >> "%LOGS%\status.txt"

if exist "%LOGS%\windows_preview.txt" (
  del "%LOGS%\windows_preview.txt"
  echo [2/2] Building Windows preview...
  "%UNITY%" -batchmode -projectPath "%PROJ%" -buildTarget Win64 -executeMethod MgSO4Builder.BuildWindowsPreview -logFile "%LOGS%\3_windows.log"
) else (
  echo [2/2] Building APK - this can take 10-20 minutes the first time...
  "%UNITY%" -batchmode -projectPath "%PROJ%" -buildTarget Android -executeMethod MgSO4Builder.BuildAndroid -logFile "%LOGS%\2_build.log"
)
set RC=%ERRORLEVEL%
echo phase2 exit %RC% >> "%LOGS%\status.txt"
if "%RC%"=="0" (echo BUILD OK - APK in "%ROOT%Builds" & echo done-ok >> "%LOGS%\status.txt") else (echo BUILD FAILED - see BuildLogs\2_build.log & echo done-fail >> "%LOGS%\status.txt")
endlocal
goto :eof

:blender
set "BLENDER="
for /d %%D in ("C:\Program Files\Blender Foundation\Blender*") do if exist "%%D\blender.exe" set "BLENDER=%%D\blender.exe"
if not defined BLENDER for /d %%D in ("%ProgramFiles(x86)%\Steam\steamapps\common\Blender") do if exist "%%D\blender.exe" set "BLENDER=%%D\blender.exe"
if not defined BLENDER for /f "delims=" %%B in ('where blender.exe 2^>nul') do set "BLENDER=%%B"
echo blender: "%BLENDER%" > "%LOGS%\0_blender.log"
if defined BLENDER "%BLENDER%" -b --factory-startup --python "%LOGS%\blender_job.py" >> "%LOGS%\0_blender.log" 2>&1
echo blender exit %ERRORLEVEL% >> "%LOGS%\0_blender.log"
move /y "%LOGS%\blender_job.py" "%LOGS%\blender_job.done" > nul
goto :eof
