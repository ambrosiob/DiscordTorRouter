[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\DiscordTorRouter\DiscordTorRouter.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\DiscordTorRouter'
$installerScript = Join-Path $PSScriptRoot 'DiscordTorRouter.iss'

$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$compiler = $compilerCandidates | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6 não encontrado. Instale-o em https://jrsoftware.org/isdl.php e execute este script novamente.'
}

Write-Host 'Publicando o Discord Tor Router e suas dependências...'
dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    --output $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo.' }

Write-Host 'Criando o instalador...'
& $compiler $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }

$setupPath = Join-Path $PSScriptRoot 'output\DiscordTorRouter-Setup.exe'
Write-Host "Instalador criado em: $setupPath"
