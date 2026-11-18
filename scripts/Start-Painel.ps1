[CmdletBinding()]
param(
    [ValidateSet('Sqlite', 'SqlServer')]
    [string]$Database = 'Sqlite',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Split-Path -Parent $PSScriptRoot

$sdks = & $Dotnet --list-sdks
if (-not ($sdks -match '^10\.')) {
    throw 'É necessário o SDK do .NET 10. Use -Dotnet com o caminho do SDK, se preciso.'
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__Provider = $Database
$env:Demo__Enabled = 'true'
$env:Demo__AllowHttp = 'true'
$env:Mobile__WebOrigin = 'http://localhost:8081'
$env:Demo__Password = if ($env:DEMO_PASSWORD) { $env:DEMO_PASSWORD } else { 'CampoLocal!2026' }

Push-Location -LiteralPath $repo
try {
    if ($Database -eq 'SqlServer') {
        if (-not $env:ConnectionStrings__SqlServer) {
            throw 'Configure ConnectionStrings__SqlServer com a identidade limitada de execução e o certificado, conforme o README.'
        }
    }

    & $Dotnet restore src/ManutencaoEmCampo.Web --configfile (Join-Path $repo 'NuGet.Config')
    & $Dotnet run --project src/ManutencaoEmCampo.Web --no-restore --no-launch-profile --urls 'http://localhost:5180'
}
finally {
    Pop-Location
}
