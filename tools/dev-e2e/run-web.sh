#!/bin/bash
export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet
cd /home/claude/repo/TDS-PAS/src/Platform/BrightPAS.Web
pkill -f "BrightPAS.Web.dll" ; pkill -f "dotnet run --no-launch-profile"; sleep 1
export TZ=Pacific/Auckland DataProtection__KeysPath=/tmp/bpkeys/keys
export ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="${PAS_URLS:-https://localhost:5001}" ConnectionStrings__Pas="Server=localhost,1433;User ID=sa;Password=Sandbox!Pass2026;TrustServerCertificate=True;Initial Catalog=${1:-BrightPASDev}" Licensing__MachineId=dev-machine
nohup dotnet run --no-launch-profile --no-build > /tmp/web.log 2>&1 &
for i in $(seq 1 40); do curl -sk https://localhost:5001/health >/dev/null 2>&1 && break; sleep 1; done
curl -sk https://localhost:5001/health; echo
