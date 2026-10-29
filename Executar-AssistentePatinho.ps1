[CmdletBinding()]
param()

$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$ProjectFile = Join-Path $ProjectRoot 'src\AssistentePatinho.App\AssistentePatinho.App.csproj'

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Projeto não encontrado em: $ProjectFile"
}

dotnet run --project $ProjectFile
if ($LASTEXITCODE -ne 0) {
    throw "O Assistente Patinho terminou com o código $LASTEXITCODE."
}
