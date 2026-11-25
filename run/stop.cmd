@echo off
rem Para os servicos iniciados por run.cmd (modo sem Docker), localizando-os pelas portas.
echo Parando os servicos...
powershell -NoProfile -Command "foreach ($port in 5101,5102,4200) { Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } }"
taskkill /fi "windowtitle eq Verx - *" /t /f >nul 2>&1
echo Servicos parados. Os dados continuam em run\data (apague a pasta para zerar).
pause
