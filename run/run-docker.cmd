@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
title Verx - Fluxo de Caixa

rem Verifica/instala os pre-requisitos (Docker Desktop) e sobe toda a solucao via Docker Compose.
cd /d "%~dp0.."
set "DOCKER_BIN=%ProgramFiles%\Docker\Docker\resources\bin"
set "DOCKER_EXE=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"

echo [1/4] Verificando Docker...
where docker >nul 2>&1
if errorlevel 1 if exist "%DOCKER_BIN%\docker.exe" set "PATH=%PATH%;%DOCKER_BIN%"

where docker >nul 2>&1
if not errorlevel 1 goto docker_ok

echo Docker nao encontrado. Sera instalado via winget.
where winget >nul 2>&1
if errorlevel 1 (
    echo [ERRO] winget nao encontrado. Instale o Docker Desktop manualmente:
    echo https://www.docker.com/products/docker-desktop
    pause
    exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
    echo A instalacao exige permissao de administrador. Solicitando elevacao...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

winget install -e --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements
if not exist "%DOCKER_BIN%\docker.exe" (
    echo [ERRO] A instalacao do Docker Desktop nao foi concluida.
    pause
    exit /b 1
)
set "PATH=%PATH%;%DOCKER_BIN%"
echo.
echo Docker Desktop instalado. Se ele pedir para habilitar o WSL 2/reiniciar o Windows,
echo faca isso e execute este script novamente.

:docker_ok
echo [2/4] Verificando Docker em execucao e virtualizacao (WSL 2)...
call :chk_docker
if not errorlevel 1 goto docker_rodando

powershell -NoProfile -Command "if ((Get-CimInstance Win32_ComputerSystem).HypervisorPresent) { exit 0 } else { exit 1 }"
if not errorlevel 1 goto virt_ok

echo A virtualizacao do Windows (WSL 2 / Plataforma de Maquina Virtual) nao esta ativa.
net session >nul 2>&1
if errorlevel 1 (
    echo Habilitar esses recursos exige administrador. Solicitando elevacao...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)
echo Habilitando recursos do Windows...
dism /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
dism /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
wsl --install --no-distribution
echo.
echo =====================================================================
echo  REINICIE O WINDOWS e execute run\run.cmd novamente.
echo  Se apos reiniciar esta mensagem voltar a aparecer, habilite a
echo  virtualizacao (Intel VT-x / AMD-V / SVM) na BIOS do computador.
echo =====================================================================
pause
exit /b 1

:virt_ok
if not exist "%DOCKER_EXE%" (
    echo [ERRO] O Docker nao esta em execucao. Inicie-o e tente novamente.
    pause
    exit /b 1
)
echo Iniciando o Docker Desktop (pode levar alguns minutos)...
tasklist /fi "imagename eq Docker Desktop.exe" | find /i "Docker Desktop.exe" >nul || start "" "%DOCKER_EXE%"
set /a espera=0
:aguardar_docker
call :chk_docker
if not errorlevel 1 goto docker_rodando
set /a espera+=1
if !espera! geq 36 (
    echo [ERRO] O Docker nao ficou pronto a tempo. Abra o Docker Desktop, aceite os termos e verifique se ele mostra algum erro.
    pause
    exit /b 1
)
<nul set /p "=."
timeout /t 5 /nobreak >nul
goto aguardar_docker

rem "docker info" pode ficar pendurado quando o motor nao sobe; limita a 8 segundos.
:chk_docker
powershell -NoProfile -Command "$p = Start-Process docker -ArgumentList 'info' -WindowStyle Hidden -PassThru; if ($p.WaitForExit(8000)) { exit $p.ExitCode } else { $p.Kill(); exit 1 }"
exit /b %errorlevel%

:docker_rodando
docker compose version >nul 2>&1
if errorlevel 1 (
    echo [ERRO] O plugin "docker compose" nao esta disponivel. Atualize o Docker Desktop.
    pause
    exit /b 1
)

echo [3/4] Construindo e iniciando os containers (a primeira execucao pode levar varios minutos)...
docker compose up --build -d
if errorlevel 1 (
    echo [ERRO] Falha ao iniciar os containers.
    pause
    exit /b 1
)

echo [4/4] Aguardando a aplicacao ficar pronta...
set /a tentativas=0
:aguardar
powershell -NoProfile -Command "try { $a=(Invoke-WebRequest http://localhost:5101/health/ready -UseBasicParsing -TimeoutSec 3).StatusCode; $b=(Invoke-WebRequest http://localhost:5102/health/ready -UseBasicParsing -TimeoutSec 3).StatusCode; if ($a -eq 200 -and $b -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
if not errorlevel 1 goto pronto
set /a tentativas+=1
if !tentativas! geq 60 (
    echo [AVISO] As APIs ainda nao responderam. Verifique os logs: docker compose logs -f
    goto fim
)
timeout /t 3 /nobreak >nul
goto aguardar

:pronto
echo.
echo Tudo pronto!
start "" http://localhost:4200

:fim
echo.
echo   Web (Angular)    http://localhost:4200
echo   Lancamentos API  http://localhost:5101
echo   Consolidado API  http://localhost:5102
echo   RabbitMQ         http://localhost:15672  (guest / guest)
echo   Prometheus       http://localhost:9090
echo.
echo Para parar tudo, execute run\stop.cmd
pause
endlocal
