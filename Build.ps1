$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish AccurateHireIQ.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o Application
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
} finally { Pop-Location }
