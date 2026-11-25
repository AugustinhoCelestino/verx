@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
title Verx - Launcher

rem Executa a solucao SEM Docker: SQLite (arquivos em run\data) e entrega de eventos por HTTP.
rem Verifica/instala .NET 10 SDK e Node.js, compila e abre as APIs e o Angular.
cd /d "%~dp0.."
set "ROOT=%CD%"

echo [1/5] Verificando .NET 10 SDK...
set "PATH=%PATH%;%ProgramFiles%\dotnet"
dotnet --list-sdks 2>nul | findstr /b "10." >nul
if errorlevel 1 (
    echo .NET 10 SDK nao encontrado. Instalando via winget...
    where winget >nul 2>&1
    if errorlevel 1 (
        echo [ERRO] winget nao encontrado. Instale o .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
        pause & exit /b 1
    )
    winget install -e --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
    set "PATH=%PATH%;%ProgramFiles%\dotnet"
    dotnet --list-sdks 2>nul | findstr /b "10." >nul
    if errorlevel 1 (
        echo [ERRO] Nao foi possivel instalar o .NET 10 SDK. Feche e abra este terminal e tente novamente.
        pause & exit /b 1
    )
)

echo [2/5] Verificando Node.js...
set "PATH=%PATH%;%ProgramFiles%\nodejs"
where node >nul 2>&1
if errorlevel 1 (
    echo Node.js nao encontrado. Instalando via winget...
    winget install -e --id OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements
    set "PATH=%PATH%;%ProgramFiles%\nodejs"
    where node >nul 2>&1
    if errorlevel 1 (
        echo [ERRO] Nao foi possivel instalar o Node.js. Feche e abra este terminal e tente novamente.
        pause & exit /b 1
    )
)

echo [3/5] Compilando as APIs...
dotnet build Verx.sln -v q --nologo
if errorlevel 1 (
    echo [ERRO] Falha na compilacao.
    pause & exit /b 1
)

echo [4/5] Instalando dependencias do Angular (apenas na primeira vez)...
if not exist web\node_modules (
    pushd web
    call npm install --no-audit --no-fund
    popd
    if not exist web\node_modules (
        echo [ERRO] Falha ao instalar as dependencias do Angular.
        pause & exit /b 1
    )
)

echo [5/5] Iniciando os servicos...
if not exist run\data mkdir run\data

set ASPNETCORE_ENVIRONMENT=Development
set Database__Provider=Sqlite
set Messaging__Provider=Http
set Messaging__Http__ConsolidadoUrl=http://localhost:5102
set Messaging__Http__ApiKey=dev-only-internal-key

set "ConnectionStrings__Default=Data Source=%ROOT%\run\data\consolidado.db"
start "Verx - Consolidado API" cmd /k dotnet run --no-build --project src\Verx.Consolidado.Api --urls http://localhost:5102

set "ConnectionStrings__Default=Data Source=%ROOT%\run\data\lancamentos.db"
start "Verx - Lancamentos API" cmd /k dotnet run --no-build --project src\Verx.Lancamentos.Api --urls http://localhost:5101

start "Verx - Web Angular" /d "%ROOT%\web" cmd /k npm start

echo.
echo Aguardando os servicos ficarem prontos...
set /a tentativas=0
:aguardar
powershell -NoProfile -Command "try { $a=(Invoke-WebRequest http://localhost:5101/health/ready -UseBasicParsing -TimeoutSec 3).StatusCode; $b=(Invoke-WebRequest http://localhost:5102/health/ready -UseBasicParsing -TimeoutSec 3).StatusCode; $c=(Invoke-WebRequest http://localhost:4200 -UseBasicParsing -TimeoutSec 3).StatusCode; if ($a -eq 200 -and $b -eq 200 -and $c -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
if not errorlevel 1 goto pronto
set /a tentativas+=1
if !tentativas! geq 60 (
    echo [AVISO] Os servicos ainda nao responderam. Verifique as janelas abertas por erros.
    goto fim
)
timeout /t 3 /nobreak >nul
goto aguardar

:pronto
echo.
echo Tudo pronto!
start "" http://localhost:4200
start "" http://localhost:5101/swagger
start "" http://localhost:5102/swagger

:fim
echo.
echo   Web (Angular)    http://localhost:4200
echo   Lancamentos API  http://localhost:5101/swagger
echo   Consolidado API  http://localhost:5102/swagger
echo   Dados (SQLite)   %ROOT%\run\data
echo.
echo Para parar tudo, execute run\stop.cmd
pause
endlocal
