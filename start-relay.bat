@echo off
rem DarkSkies NORS relay — starts the server and opens the web admin panel.
rem Edit the password below; give it to whoever should administer the relay.
set ADMIN_PASS=darkskies

cd /d "%~dp0"
rem open the panel a moment after the server has bound its port
start "" cmd /c "timeout /t 2 >nul & start http://localhost:8700/"
"src\NORS.Server\bin\Release\net8.0\NORS.Server.exe" --admin-pass %ADMIN_PASS%
