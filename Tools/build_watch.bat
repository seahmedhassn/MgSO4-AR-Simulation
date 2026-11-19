@echo off
REM ==========================================================================
REM  MgSO4 AR Sim - build helper. Leave this window open while building:
REM  it runs build_android.bat each time BuildLogs\request.txt appears.
REM  Close the window (or press Ctrl+C) to stop.
REM ==========================================================================
set "ROOT=%~dp0"
if not exist "%ROOT%BuildLogs" mkdir "%ROOT%BuildLogs"
echo idle > "%ROOT%BuildLogs\watch_alive.txt"
echo Waiting for build requests... (close this window to stop)
call "%ROOT%build_android.bat"
:loop
if exist "%ROOT%BuildLogs\request.txt" (
  del "%ROOT%BuildLogs\request.txt"
  echo [%date% %time%] Build requested
  call "%ROOT%build_android.bat"
  echo Waiting for build requests...
)
echo %date% %time% > "%ROOT%BuildLogs\watch_alive.txt"
timeout /t 10 /nobreak > nul
goto loop
