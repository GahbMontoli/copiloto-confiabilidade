[CmdletBinding()]
param()

$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$AppProject = Join-Path $ProjectRoot 'src\AssistentePatinho.App\AssistentePatinho.App.csproj'
$TestProject = Join-Path $ProjectRoot 'tests\AssistentePatinho.Core.SmokeTests\AssistentePatinho.Core.SmokeTests.csproj'

dotnet build $AppProject -c Release -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "A compilação do aplicativo falhou com o código $LASTEXITCODE."
}

dotnet run --project $TestProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Os testes falharam com o código $LASTEXITCODE."
}

Write-Host 'Validação concluída com sucesso.' -ForegroundColor Green
