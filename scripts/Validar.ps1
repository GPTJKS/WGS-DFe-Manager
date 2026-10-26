$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore WgsDfeManager.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Falha no restore.' }
    dotnet build WgsDfeManager.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Falha no build.' }
    dotnet test tests/WgsDfe.Tests/WgsDfe.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
} finally { Pop-Location }
