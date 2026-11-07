[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [ValidateSet('synthetic', 'official_raw', 'lab_observed', 'field_observed', 'derived')]
    [string]$EvidenceClass,
    [string]$CacheDirectory
)

$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$Project = Join-Path $ProjectRoot 'src\AssistentePatinho.Analysis\AssistentePatinho.Analysis.csproj'
$ResolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$Arguments = @($ResolvedPath)
if ($EvidenceClass) {
    $Arguments += @('--evidence-class', $EvidenceClass)
}
if ($CacheDirectory) {
    $Arguments += @('--cache', [System.IO.Path]::GetFullPath($CacheDirectory))
}

dotnet run --project $Project -c Release -- @Arguments
exit $LASTEXITCODE
