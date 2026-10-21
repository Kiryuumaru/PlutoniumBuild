@ECHO OFF
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1" %*
exit /b %ERRORLEVEL%
