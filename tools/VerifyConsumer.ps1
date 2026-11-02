param([string]$ArtifactDirectory, [switch]$Published)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactDirectory) {
    $ArtifactDirectory = (Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-verification.txt') -Raw).Trim()
}
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$referencePackage = Join-Path $ArtifactDirectory 'BenScr.Serialization.1.0.0.nupkg'
if (-not (Test-Path -LiteralPath $referencePackage)) { throw 'Reference package is missing.' }
$consumerRoot = Join-Path ([IO.Path]::GetTempPath()) ('benscr-serialization-consumer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $consumerRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'global.json') -Destination $consumerRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'Playground/Program.cs') -Destination $consumerRoot
$cache = Join-Path $consumerRoot 'packages'
$source = if ($Published) { 'nuget.org' } else { 'local verified artifact' }
if ($Published) {
    $configuration = '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>'
} else {
    $escapedDirectory = [Security.SecurityElement]::Escape($ArtifactDirectory)
    $configuration = '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="verified" value="' + $escapedDirectory + '"/><add key="dependencies" value="https://api.nuget.org/v3/index.json"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><packageSourceMapping><packageSource key="verified"><package pattern="BenScr.Serialization"/></packageSource><packageSource key="dependencies"><package pattern="MessagePack"/><package pattern="MessagePack.Annotations"/><package pattern="MessagePackAnalyzer"/><package pattern="Microsoft.NET.StringTools"/><package pattern="Microsoft.NETCore.App.*"/><package pattern="Microsoft.AspNetCore.App.*"/></packageSource></packageSourceMapping></configuration>'
}
Set-Content -LiteralPath (Join-Path $consumerRoot 'NuGet.Config') -Value $configuration -Encoding utf8
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>net9.0;net10.0</TargetFrameworks><Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings><RestoreFallbackFolders></RestoreFallbackFolders></PropertyGroup><ItemGroup><PackageReference Include="BenScr.Serialization" Version="[1.0.0]"/></ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $consumerRoot 'Consumer.csproj') -Value $project -Encoding utf8
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$env:NUGET_PACKAGES = $cache
$env:NUGET_HTTP_CACHE_PATH = Join-Path $consumerRoot 'http-cache'
Push-Location $consumerRoot
try {
    & dotnet restore Consumer.csproj --configfile NuGet.Config --no-cache
    if ($LASTEXITCODE -ne 0) { throw 'Isolated package restore failed.' }
    foreach ($framework in @('net9.0','net10.0')) {
        & dotnet run --project Consumer.csproj -c Release -f $framework --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Consumer failed: $framework" }
    }
    $installedPackage = Join-Path $cache 'benscr.serialization/1.0.0/benscr.serialization.1.0.0.nupkg'
    $reference = [IO.Compression.ZipFile]::OpenRead($referencePackage)
    $installed = [IO.Compression.ZipFile]::OpenRead($installedPackage)
    try {
        $entries = @($reference.Entries | Where-Object { $_.FullName -match '^lib/' -or $_.FullName -in @('README.md','LICENSE.txt','icon.png','BenScr.Serialization.nuspec') })
        if ($entries.Count -ne 8) { throw 'Unexpected package payload count.' }
        $hashes = @()
        foreach ($entry in $entries) {
            $actualEntry = $installed.GetEntry($entry.FullName)
            if (-not $actualEntry) { throw "Missing installed payload: $($entry.FullName)" }
            $expectedStream = $entry.Open()
            $actualStream = $actualEntry.Open()
            try {
                $expectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expectedStream)).ToLowerInvariant()
                $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actualStream)).ToLowerInvariant()
            } finally { $expectedStream.Dispose(); $actualStream.Dispose() }
            if ($expectedHash -ne $actualHash) { throw "Installed payload differs: $($entry.FullName)" }
            $hashes += @{entry=$entry.FullName; sha256=$actualHash}
        }
    } finally { $reference.Dispose(); $installed.Dispose() }
    $evidence = @{
        status = $(if ($Published) {'published_consumer_verified'} else {'local_consumer_verified'})
        verified_at_utc = [DateTime]::UtcNow.ToString('o')
        package_id = 'BenScr.Serialization'
        version = '1.0.0'
        package_source = $source
        frameworks = @('net9.0','net10.0')
        initially_empty_package_cache = $true
        project_references = $false
        payloads = $hashes
    }
    $name = if ($Published) { 'published-consumer.json' } else { 'local-consumer.json' }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory $name) -Encoding utf8
    Write-Output "Verified isolated consumer from $source."
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
}

