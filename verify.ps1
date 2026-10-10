param([string]$ServerRoot = (Join-Path $PSScriptRoot '../raple-realms-7-server'), [switch]$OriginalRuntime)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path '.artifacts' -Force | Out-Null
    if ($OriginalRuntime) {
        $referenceArtifacts = Join-Path $ServerRoot '.artifacts/studio-reference'
        & dotnet build (Join-Path $ServerRoot 'tests/BossPatternSim/BossPatternSim.csproj') --artifacts-path $referenceArtifacts -v quiet *> '.artifacts/reference-build.log'
        if ($LASTEXITCODE -ne 0) { throw 'Original harness build failed.' }
        $referenceDll = Get-ChildItem -LiteralPath (Join-Path $referenceArtifacts 'bin') -Recurse -Filter BossPatternSim.dll | Select-Object -First 1 -ExpandProperty FullName
        $referenceHost = Join-Path $PSScriptRoot '../.tools/dotnet/dotnet.exe'
        if (!(Test-Path -LiteralPath $referenceHost)) { $referenceHost = 'dotnet' }
        & $referenceHost $referenceDll selftest --quick --root $ServerRoot *> '.artifacts/reference-selftest.log'
        if ($LASTEXITCODE -ne 0) { throw 'Original harness self-test failed.' }
        & $referenceHost $referenceDll run --root $ServerRoot --boss 'Cave Pirate Brawler' --map bosslab --duration 10000 --seed 7 --sample 20 --scenarios none --out '.artifacts/reference-brawler' *> '.artifacts/reference-brawler.log'
        if ($LASTEXITCODE -ne 0) { throw 'Original reference capture failed.' }
    }
    & dotnet run --project tests/Studio.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/core-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/core-checks.log' -Tail 25; throw 'Editor/runtime checks failed.' }
    Get-Content '.artifacts/core-checks.log' -Tail 1
    & dotnet run --project tests/Structure.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/structure-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/structure-checks.log' -Tail 25; throw 'Structural authoring checks failed.' }
    Get-Content '.artifacts/structure-checks.log' -Tail 1
    & dotnet run --project tests/Selection.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/selection-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/selection-checks.log' -Tail 25; throw 'Selection authoring checks failed.' }
    Get-Content '.artifacts/selection-checks.log' -Tail 1
    & dotnet run --project tests/Analysis.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/analysis-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/analysis-checks.log' -Tail 25; throw 'Transition analysis checks failed.' }
    Get-Content '.artifacts/analysis-checks.log' -Tail 1
    & dotnet run --project tests/Weapon.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/weapon-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/weapon-checks.log' -Tail 25; throw 'Collision/weapon checks failed.' }
    Get-Content '.artifacts/weapon-checks.log' -Tail 1
    & dotnet run --project tests/AI.Checks *> '.artifacts/ai-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/ai-checks.log' -Tail 25; throw 'AI contract checks failed.' }
    Get-Content '.artifacts/ai-checks.log' -Tail 1
    & dotnet build src/Studio.Desktop "-p:ServerRoot=$ServerRoot" -v quiet *> '.artifacts/desktop-build.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/desktop-build.log' -Tail 25; throw 'Desktop build failed.' }
    $studioExe = Join-Path $PSScriptRoot 'src/Studio.Desktop/bin/Debug/net10.0-windows/Studio.Desktop.exe'
    $studioProcess = Start-Process -FilePath $studioExe -ArgumentList @('--smoke', '--server', ('"' + [IO.Path]::GetFullPath($ServerRoot) + '"')) -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
    if (!$studioProcess.WaitForExit(30000)) { throw 'Desktop smoke test did not finish within 30 seconds.' }
    if ($studioProcess.ExitCode -ne 0) { throw 'Desktop smoke test failed; see .artifacts/desktop-smoke-error.txt.' }
    Write-Host 'Desktop import/edit/play/save/trace/render smoke passed.'
} finally { Pop-Location }
