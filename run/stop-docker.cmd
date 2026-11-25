@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0.."

echo Parando os containers...
docker compose down
if errorlevel 1 (
    echo [ERRO] Falha ao parar os containers. O Docker esta em execucao?
) else (
    echo Containers parados. Os dados do SQL Server foram descartados junto com os containers.
)
pause
endlocal
