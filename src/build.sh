#!/usr/bin/env bash
# Збирання UaSwitch.exe під .NET Framework 4.8 (вбудований у Windows 10/11).
# Потрібно: .NET SDK (компілятор Roslyn) + референсні збірки Mono (пакет mono-devel).
set -euo pipefail
cd "$(dirname "$0")"
R=/usr/lib/mono/4.8-api
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
mkdir -p ../out
dotnet "$CSC" -nologo -noconfig -nostdlib+ -target:winexe -langversion:latest -nullable:enable \
  -optimize+ -platform:anycpu -win32manifest:app.manifest -win32icon:app.ico \
  -out:../out/UaSwitch.exe \
  -r:$R/mscorlib.dll -r:$R/System.dll -r:$R/System.Core.dll -r:$R/System.Drawing.dll \
  -r:$R/System.Windows.Forms.dll -r:$R/Accessibility.dll -r:$R/Facades/System.Runtime.dll -r:$R/Facades/System.ValueTuple.dll \
  -resource:uk.txt.gz,UaSwitch.uk.txt.gz -resource:en.txt.gz,UaSwitch.en.txt.gz -resource:apo.txt.gz,UaSwitch.apo.txt.gz \
  *.cs
echo "OK: ../out/UaSwitch.exe"
