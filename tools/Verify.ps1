param([string]$ReleaseTag, [switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($args -join ' ')" }
}
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
    if (-not $AllowDirty -and (& git status --porcelain)) { throw 'Verification requires a clean checkout.' }
    if ($ReleaseTag) {
        $tagCommit = & git rev-list -n 1 $ReleaseTag
        if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $commit -or $ReleaseTag -ne 'packages/BenScr.Serialization/1.0.0') { throw 'Release tag mismatch.' }
    }
    if (-not (Test-Path -LiteralPath docs/PublicApi.txt)) { throw 'Reviewed public API baseline is missing.' }
    $output = Join-Path $projectRoot ('artifacts/verification-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output | Out-Null
    foreach ($project in @('Serializer/Serializer.csproj','tests/BenScr.Serialization.Tests.csproj','Playground/Playground.csproj','tools/PackageAudit/PackageAudit.csproj')) {
        Invoke-DotNet restore $project --locked-mode
        Invoke-DotNet build $project -c Release --no-restore
        Invoke-DotNet format whitespace $project --no-restore --verify-no-changes
        Invoke-DotNet format style $project --no-restore --verify-no-changes --severity error
    }
    foreach ($framework in @('net9.0','net10.0')) {
        Invoke-DotNet test --project tests/BenScr.Serialization.Tests.csproj -c Release -f $framework --no-build --no-restore --report-xunit-trx --report-xunit-trx-filename "tests_$framework.trx" --results-directory $output
        Invoke-DotNet run --project tools/PackageAudit -c Release --no-build -- "Serializer/bin/Release/$framework/BenScr.Serialization.dll" docs/PublicApi.txt
        Invoke-DotNet run --project Playground/Playground.csproj -c Release -f $framework --no-build --no-restore
    }
    Invoke-DotNet pack Serializer/Serializer.csproj -c Release --no-build --no-restore -o $output
    $package = Join-Path $output 'BenScr.Serialization.1.0.0.nupkg'
    $symbols = Join-Path $output 'BenScr.Serialization.1.0.0.snupkg'
    Invoke-DotNet run --project tools/PackageAudit -c Release --no-build -- package $package $symbols $commit $projectRoot
    & (Join-Path $PSScriptRoot 'VerifyConsumer.ps1') -ArtifactDirectory $output
    $testReports = @(Get-ChildItem -LiteralPath $output -Filter '*.trx' | ForEach-Object {
        [xml]$report = Get-Content -LiteralPath $_.FullName -Raw
        $counters = $report.TestRun.ResultSummary.Counters
        if ([int]$counters.total -lt 1 -or [int]$counters.passed -ne [int]$counters.total) { throw 'Incomplete test execution.' }
        @{file=$_.Name; total=[int]$counters.total; passed=[int]$counters.passed; failed=[int]$counters.failed}
    })
    if ($testReports.Count -ne 2) { throw 'Expected two framework reports.' }
    $audit = Get-Content -LiteralPath (Join-Path $output 'package-audit.json') -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        schema_version = 1
        status = $(if ($AllowDirty) {'tested_local_dirty'} elseif ($ReleaseTag) {'ready_to_publish'} else {'tested_local'})
        verified_at_utc = [DateTime]::UtcNow.ToString('o')
        repository = 'https://github.com/Ben-Scr/BenScr.Serialization'
        commit = $commit
        release_tag = $ReleaseTag
        package_id = $audit.package_id
        version = $audit.version
        package_file = 'BenScr.Serialization.1.0.0.nupkg'
        package_sha256 = $audit.package_sha256
        symbols_file = 'BenScr.Serialization.1.0.0.snupkg'
        symbols_sha256 = $audit.symbols_sha256
        tests = $testReports
        frameworks = @('net9.0','net10.0')
        direct_dependencies = @('MessagePack 3.1.12')
        local_consumer_verified = $true
        archive_manifest_checked = $true
        published = $false
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
    $output | Set-Content -LiteralPath artifacts/latest-verification.txt -Encoding utf8
    Write-Output "Verified artifacts: $output"
} finally { Pop-Location }
