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
$env:Demo__Password = if ($env:DEMO_PASSWORD) { $env:DEMO_PASSWORD } else { 'CampoLocal!2026' }

Push-Location -LiteralPath $repo
try {
    if ($Database -eq 'SqlServer') {
        if (-not $env:MSSQL_SA_PASSWORD) {
            throw 'Defina MSSQL_SA_PASSWORD no ambiente antes de iniciar o SQL Server.'
        }

        docker compose up -d --wait
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection.ConnectionString = 'Server=localhost,14333;Database=ManutencaoEmCampo;User ID=sa;Encrypt=True;TrustServerCertificate=True'
        $connection['Password'] = $env:MSSQL_SA_PASSWORD
        $env:ConnectionStrings__SqlServer = $connection.ConnectionString
    }

    & $Dotnet restore src/ManutencaoEmCampo.Web --configfile (Join-Path $repo 'NuGet.Config')
    & $Dotnet run --project src/ManutencaoEmCampo.Web --no-restore --no-launch-profile --urls 'http://localhost:5180'
}
finally {
    Pop-Location
}
