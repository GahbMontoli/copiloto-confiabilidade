[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$Project = Join-Path $ProjectRoot 'src\AssistentePatinho.Export\AssistentePatinho.Export.csproj'
$Source = Join-Path $ProjectRoot 'src\AssistentePatinho.App\sample-cmms-data.json'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $ProjectRoot 'artifacts\power-bi'
}

dotnet run --project $Project -c Release -- $Source $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "A exportação Power BI terminou com o código $LASTEXITCODE."
}
