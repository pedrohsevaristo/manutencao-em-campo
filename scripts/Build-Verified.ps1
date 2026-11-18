[CmdletBinding()]
param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$reports = (New-Item -ItemType Directory -Path (Join-Path $root '.local/build-verification') -Force).FullName
$npm = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
$configuration = Join-Path $root 'NuGet.Config'
$solution = Join-Path $root 'ManutencaoEmCampo.slnx'

Push-Location -LiteralPath $root
try {
    $lock = Get-Content -LiteralPath 'mobile/package-lock.json' -Raw | ConvertFrom-Json -AsHashtable
    if ($lock.lockfileVersion -lt 3 -or $lock.packages.Count -lt 2) {
        throw 'O build exige o lockfile npm completo.'
    }
    foreach ($key in $lock.packages.Keys.Where({ $_ -ne '' })) {
        $entry = $lock.packages[$key]
        $uri = $null
        if (-not [Uri]::TryCreate($entry.resolved, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -ne 'https' -or $uri.Host -ne 'registry.npmjs.org' -or
            -not $uri.IsDefaultPort -or $uri.UserInfo -or
            $entry.integrity -cnotmatch '^sha512-[A-Za-z0-9+/]{86}==$') {
            throw "Dependência npm sem origem ou integridade esperada: $key"
        }
    }

    & $Dotnet restore $solution --locked-mode --force --no-http-cache --configfile $configuration -m:1
    & $Dotnet package list --project $solution --no-restore --include-transitive --format json |
        Set-Content -LiteralPath (Join-Path $reports 'nuget-packages.json') -Encoding utf8
    & $Dotnet package list --project $solution --no-restore --include-transitive --vulnerable --config $configuration --format json |
        Set-Content -LiteralPath (Join-Path $reports 'nuget-audit.json') -Encoding utf8

    Push-Location -LiteralPath (Join-Path $root 'mobile')
    try {
        $audit = Join-Path $reports 'npm-audit.json'
        try {
            & $npm audit --registry=https://registry.npmjs.org/ --audit-level=high --json |
                Set-Content -LiteralPath $audit -Encoding utf8
        }
        catch {
            throw "Auditoria npm bloqueou o build. Detalhes em $audit. $($_.Exception.Message)"
        }
        $metadata = (Get-Content -LiteralPath $audit -Raw | ConvertFrom-Json).metadata.vulnerabilities
        if ($null -eq $metadata) { throw "O registro npm não retornou uma auditoria utilizável. Consulte $audit." }
        if ($metadata.total -gt 0) {
            Write-Warning "Entradas npm abaixo do limiar de bloqueio: $($metadata.total). Consulte $audit."
        }
        & $npm ci --ignore-scripts --no-audit --fund=false --registry=https://registry.npmjs.org/
        & $npm sbom --package-lock-only --sbom-format=cyclonedx |
            Set-Content -LiteralPath (Join-Path $reports 'npm-sbom.cdx.json') -Encoding utf8
        & $npm run typecheck
        & $npm run build:web -- --max-workers 2
    }
    finally {
        Pop-Location
    }

    & $Dotnet build $solution -c Release --no-restore -m:1
    $previousDotnet = $env:DOTNET_EXE
    try {
        $env:DOTNET_EXE = (Get-Command $Dotnet -CommandType Application).Source
        node --experimental-strip-types --test scripts/test-security.mjs
    }
    finally {
        $env:DOTNET_EXE = $previousDotnet
    }
    Write-Output "Relatórios de dependências em $reports"
}
finally {
    Pop-Location
}
