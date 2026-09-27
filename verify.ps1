# Windows PowerShell 5.1 / PowerShell 7. No modifica fuentes ni necesita tokens.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') termino con codigo $LASTEXITCODE."
    }
}

function Test-PackageAudit {
    param([string]$Project)

    $json = & dotnet list $Project package --vulnerable --include-transitive --format json --output-version 1
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo completar la auditoria de $Project (codigo $LASTEXITCODE)."
    }

    $report = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    $logs = $report.PSObject.Properties['logs']
    if ($null -ne $logs) {
        foreach ($entry in $logs.Value) {
            if ($entry.level -in @('error', 'warning')) {
                throw "Auditoria incompleta: $($entry.message)"
            }
        }
    }

    $projects = $report.PSObject.Properties['projects']
    if ($null -eq $projects -or @($projects.Value).Count -eq 0) {
        throw "NuGet no devolvio resultados para $Project."
    }

    foreach ($projectResult in $projects.Value) {
        $frameworks = $projectResult.PSObject.Properties['frameworks']
        if ($null -eq $frameworks) { continue }
        foreach ($framework in $frameworks.Value) {
            foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
                $packages = $framework.PSObject.Properties[$kind]
                if ($null -eq $packages) { continue }
                foreach ($package in $packages.Value) {
                    $vulnerabilities = $package.PSObject.Properties['vulnerabilities']
                    if ($null -ne $vulnerabilities -and @($vulnerabilities.Value).Count -gt 0) {
                        $advisories = $vulnerabilities.Value | ForEach-Object { "$($_.severity): $($_.advisoryurl)" }
                        throw "Paquete vulnerable: $($package.id) $($package.resolvedVersion). $($advisories -join '; ')"
                    }
                }
            }
        }
    }
    Write-Host "OK: auditoria sin vulnerabilidades conocidas ($Project)."
}

try {
    if ($env:OS -ne 'Windows_NT') { throw 'La validacion WinUI requiere Windows.' }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^9\.') {
        throw 'Selecciona el SDK .NET 9 documentado para este proyecto.'
    }

    $app = Join-Path $PSScriptRoot 'TrelloNotifier\TrelloNotifier.csproj'
    $tests = Join-Path $PSScriptRoot 'TrelloNotifier.Tests\TrelloNotifier.Tests.csproj'
    foreach ($project in @($app, $tests)) {
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
            throw "No existe el proyecto esperado: $project"
        }
    }

    Write-Host 'RESTORE: proyectos y datos de auditoria NuGet'
    foreach ($project in @($app, $tests)) {
        Invoke-DotNet @('restore', $project, '--force', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all',
            '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904')
    }

    Write-Host 'FORMAT: C# (sin reescribir archivos)'
    foreach ($project in @($app, $tests)) {
        Invoke-DotNet @('format', 'whitespace', $project, '--verify-no-changes', '--no-restore')
    }

    Write-Host 'LINT: imports C# (IDE0005)'
    foreach ($project in @($app, $tests)) {
        Invoke-DotNet @('format', 'style', $project, '--diagnostics', 'IDE0005', '--severity', 'warn',
            '--verify-no-changes', '--no-restore')
    }

    Write-Host 'STATIC ANALYSIS + BUILD: SDK analyzers, Windows x64 y runner'
    $appOutput = Join-Path $PSScriptRoot 'artifacts\verify\app'
    Invoke-DotNet @('build', $app, '--no-restore', '-c', 'Debug', '-p:Platform=x64',
        '-p:RuntimeIdentifier=win10-x64', '--output', $appOutput, '-warnaserror')
    Invoke-DotNet @('build', $tests, '--no-restore', '-c', 'Debug', '-warnaserror')

    Write-Host 'TESTS: regresiones funcionales y arquitectura'
    Invoke-DotNet @('run', '--project', $tests, '--no-build', '--no-restore', '-c', 'Debug')

    Write-Host 'SECURITY: auditoria de paquetes directos y transitivos'
    Test-PackageAudit $app
    Test-PackageAudit $tests

    Write-Host 'OK: verificacion automatica completa. Completar revision manual segun .ai/review-checklist.md.'
    exit 0
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}