@ECHO OFF
call "%~dp0build\build.cmd" %*
exit /b %ERRORLEVEL%
