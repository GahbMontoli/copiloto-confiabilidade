[CmdletBinding()]
param()

$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$ProjectFile = Join-Path $ProjectRoot 'src\AssistentePatinho.App\AssistentePatinho.App.csproj'
$ValidationScript = Join-Path $ProjectRoot 'Validar-AssistentePatinho.ps1'
$ArtifactsRoot = Join-Path $ProjectRoot 'artifacts'
$PublishRoot = Join-Path $ArtifactsRoot 'win-x64'
$DistRoot = Join-Path $ProjectRoot 'dist'
$ArchivePath = Join-Path $DistRoot 'Copiloto-Confiabilidade-win-x64.zip'

& $ValidationScript
if ($LASTEXITCODE -ne 0) {
    throw "A validação falhou com o código $LASTEXITCODE."
}

if (Test-Path -LiteralPath $PublishRoot) {
    Remove-Item -LiteralPath $PublishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DistRoot -Force | Out-Null

dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $PublishRoot `
    -v minimal

if ($LASTEXITCODE -ne 0) {
    throw "A publicação falhou com o código $LASTEXITCODE."
}

if (Test-Path -LiteralPath $ArchivePath) {
    Remove-Item -LiteralPath $ArchivePath -Force
}

Compress-Archive -Path (Join-Path $PublishRoot '*') -DestinationPath $ArchivePath -CompressionLevel Optimal
Write-Host "Pacote criado em: $ArchivePath" -ForegroundColor Green
