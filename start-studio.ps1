param([string]$ServerRoot = (Join-Path $PSScriptRoot '../raple-realms-7-server'), [string]$ClientRoot = (Join-Path $PSScriptRoot '../raple-realms-7-client'), [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$studioProject = Join-Path $PSScriptRoot 'src/Studio.Desktop/Studio.Desktop.csproj'
$studioLog = Join-Path $PSScriptRoot '.artifacts/startup-build.log'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '.artifacts') -Force | Out-Null
# A source version gets its own output so an older open window never blocks rebuilding.
$studioSourceHash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    $studioSourceRoots = @((Join-Path $PSScriptRoot 'src'), (Join-Path $ServerRoot 'TKR.WorldServer'), (Join-Path $ServerRoot 'TKR.Shared'))
    $studioSourceFiles = @(Get-ChildItem -LiteralPath $studioSourceRoots -Recurse -File | Where-Object { $_.Extension -in '.cs', '.xaml', '.csproj' -and $_.FullName -notmatch '[\\/](obj|bin|\.artifacts)[\\/]' })
    $studioSourceFiles += Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props')
    $studioSourceFiles = $studioSourceFiles | Sort-Object FullName
    foreach ($studioFile in $studioSourceFiles) { $studioSourceHash.AppendData([Text.Encoding]::UTF8.GetBytes($studioFile.FullName)); $studioSourceHash.AppendData([IO.File]::ReadAllBytes($studioFile.FullName)) }
    $studioVersion = [BitConverter]::ToString($studioSourceHash.GetHashAndReset()).Replace('-', '').Substring(0,12).ToLowerInvariant()
} finally { $studioSourceHash.Dispose() }
$studioBuildRoot = Join-Path $PSScriptRoot ('.artifacts/app-' + $studioVersion)
Write-Host 'Preparing Realm Behavior Studio...'
& dotnet build $studioProject --configuration Release --artifacts-path $studioBuildRoot "-p:ServerRoot=$ServerRoot" --verbosity quiet *> $studioLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $studioLog -Tail 25; throw "Build failed. Details: $studioLog" }
$studioExe = Join-Path $studioBuildRoot 'bin/Studio.Desktop/release/Studio.Desktop.exe'
if ($BuildOnly) { Write-Host ('Built: ' + $studioExe); return }
Start-Process -FilePath $studioExe -ArgumentList @('--server', ('"' + [IO.Path]::GetFullPath($ServerRoot) + '"'), '--client', ('"' + [IO.Path]::GetFullPath($ClientRoot) + '"')) -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
