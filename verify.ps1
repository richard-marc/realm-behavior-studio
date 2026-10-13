param([string]$ServerRoot = (Join-Path $PSScriptRoot '../raple-realms-7-server'), [switch]$OriginalRuntime, [switch]$ChecksOnly)
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
    & dotnet run --project tests/Schema.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/schema-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/schema-checks.log' -Tail 25; throw 'Native schema checks failed.' }
    Get-Content '.artifacts/schema-checks.log' -Tail 1
    & dotnet run --project tests/Argument.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/argument-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/argument-checks.log' -Tail 25; throw 'Grouped argument checks failed.' }
    Get-Content '.artifacts/argument-checks.log' -Tail 1
    & dotnet run --project tests/SourceContext.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/source-context-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/source-context-checks.log' -Tail 25; throw 'Source context checks failed.' }
    Get-Content '.artifacts/source-context-checks.log' -Tail 1
    & dotnet run --project tests/Definition.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/definition-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/definition-checks.log' -Tail 25; throw 'Source definition checks failed.' }
    Get-Content '.artifacts/definition-checks.log' -Tail 1
    & dotnet run --project tests/SourceSet.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/source-set-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/source-set-checks.log' -Tail 25; throw 'Multi-file source project checks failed.' }
    Get-Content '.artifacts/source-set-checks.log' -Tail 1
    & dotnet run --project tests/ExternalDefinition.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/external-definition-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/external-definition-checks.log' -Tail 25; throw 'Cross-file visual definition checks failed.' }
    Get-Content '.artifacts/external-definition-checks.log' -Tail 1
    & dotnet run --project tests/Origin.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/origin-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/origin-checks.log' -Tail 25; throw 'Native State provenance checks failed.' }
    Get-Content '.artifacts/origin-checks.log' -Tail 1
    & dotnet run --project tests/PhaseWorkspace.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/phase-workspace-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/phase-workspace-checks.log' -Tail 25; throw 'Coordinated phase workspace checks failed.' }
    Get-Content '.artifacts/phase-workspace-checks.log' -Tail 1
    & dotnet run --project tests/DeleteWorkspace.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/workspace-delete-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/workspace-delete-checks.log' -Tail 25; throw 'Coordinated workspace deletion checks failed.' }
    Get-Content '.artifacts/workspace-delete-checks.log' -Tail 1
    & dotnet run --project tests/GroupWorkspace.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/workspace-group-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/workspace-group-checks.log' -Tail 25; throw 'Coordinated workspace grouping checks failed.' }
    Get-Content '.artifacts/workspace-group-checks.log' -Tail 1
    & dotnet run --project tests/CopyWorkspace.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/workspace-copy-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/workspace-copy-checks.log' -Tail 25; throw 'Coordinated workspace copy checks failed.' }
    Get-Content '.artifacts/workspace-copy-checks.log' -Tail 1
    & dotnet run --project tests/Extraction.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/extraction-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/extraction-checks.log' -Tail 25; throw 'Source extraction checks failed.' }
    Get-Content '.artifacts/extraction-checks.log' -Tail 1
    & dotnet run --project tests/CollectionExtraction.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/collection-extraction-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/collection-extraction-checks.log' -Tail 25; throw 'Native collection extraction checks failed.' }
    Get-Content '.artifacts/collection-extraction-checks.log' -Tail 1
    & dotnet run --project tests/ScopedExtraction.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/scoped-extraction-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/scoped-extraction-checks.log' -Tail 25; throw 'File-scoped native helper extraction checks failed.' }
    Get-Content '.artifacts/scoped-extraction-checks.log' -Tail 1
    & dotnet run --project tests/SelectionExtraction.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/selection-extraction-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/selection-extraction-checks.log' -Tail 25; throw 'Native multi-expression helper extraction checks failed.' }
    Get-Content '.artifacts/selection-extraction-checks.log' -Tail 1
    & dotnet run --project tests/MemberInitializer.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/member-initializer-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/member-initializer-checks.log' -Tail 25; throw 'Native State member initializer checks failed.' }
    Get-Content '.artifacts/member-initializer-checks.log' -Tail 1
    & dotnet run --project tests/KeyedInitializer.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/keyed-initializer-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/keyed-initializer-checks.log' -Tail 25; throw 'Native keyed Add initializer checks failed.' }
    Get-Content '.artifacts/keyed-initializer-checks.log' -Tail 1
    & dotnet run --project tests/SharedEntry.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/shared-entry-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/shared-entry-checks.log' -Tail 25; throw 'Shared keyed entry workspace checks failed.' }
    Get-Content '.artifacts/shared-entry-checks.log' -Tail 1
    & dotnet run --project tests/CloneWorkspace.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/clone-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/clone-checks.log' -Tail 25; throw 'Independent definition clone checks failed.' }
    Get-Content '.artifacts/clone-checks.log' -Tail 1
    & dotnet run --project tests/ReferenceClone.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/reference-clone-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/reference-clone-checks.log' -Tail 25; throw 'Independent reference clone checks failed.' }
    Get-Content '.artifacts/reference-clone-checks.log' -Tail 1
    & dotnet run --project tests/Transformation.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/transformation-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/transformation-checks.log' -Tail 25; throw 'Native transformation checks failed.' }
    Get-Content '.artifacts/transformation-checks.log' -Tail 1
    & dotnet run --project tests/Replacement.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/replacement-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/replacement-checks.log' -Tail 25; throw 'Native entity replacement checks failed.' }
    Get-Content '.artifacts/replacement-checks.log' -Tail 1
    & dotnet run --project tests/OwnedEmitter.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/owned-emitter-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/owned-emitter-checks.log' -Tail 25; throw 'Owned emitter checks failed.' }
    Get-Content '.artifacts/owned-emitter-checks.log' -Tail 1
    & dotnet run --project tests/Effects.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/effect-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/effect-checks.log' -Tail 25; throw 'Native effect checks failed.' }
    Get-Content '.artifacts/effect-checks.log' -Tail 1
    & dotnet run --project tests/Choice.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/choice-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/choice-checks.log' -Tail 25; throw 'Native phase choice checks failed.' }
    Get-Content '.artifacts/choice-checks.log' -Tail 1
    & dotnet run --project tests/Health.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/health-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/health-checks.log' -Tail 25; throw 'Native health checks failed.' }
    Get-Content '.artifacts/health-checks.log' -Tail 1
    & dotnet run --project tests/Registration.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/registration-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/registration-checks.log' -Tail 25; throw 'Native registration checks failed.' }
    Get-Content '.artifacts/registration-checks.log' -Tail 1
    & dotnet run --project tests/Runtime.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/runtime-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/runtime-checks.log' -Tail 25; throw 'Compiled runtime checks failed.' }
    Get-Content '.artifacts/runtime-checks.log' -Tail 1
    & dotnet run --project tests/Coverage.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/coverage-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/coverage-checks.log' -Tail 25; throw 'Native attack preview checks failed.' }
    Get-Content '.artifacts/coverage-checks.log' -Tail 1
    & dotnet run --project tests/Fragment.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/fragment-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/fragment-checks.log' -Tail 25; throw 'Reusable fragment checks failed.' }
    Get-Content '.artifacts/fragment-checks.log' -Tail 1
    & dotnet run --project tests/Replay.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/replay-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/replay-checks.log' -Tail 25; throw 'Native replay checks failed.' }
    Get-Content '.artifacts/replay-checks.log' -Tail 1
    & dotnet run --project tests/Arena.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/arena-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/arena-checks.log' -Tail 25; throw 'Native arena checks failed.' }
    Get-Content '.artifacts/arena-checks.log' -Tail 1
    & dotnet run --project tests/Map.Checks "-p:ServerRoot=$ServerRoot" -- $ServerRoot *> '.artifacts/map-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/map-checks.log' -Tail 25; throw 'Native map checks failed.' }
    Get-Content '.artifacts/map-checks.log' -Tail 1
    & dotnet run --project tests/AI.Checks *> '.artifacts/ai-checks.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/ai-checks.log' -Tail 25; throw 'AI contract checks failed.' }
    Get-Content '.artifacts/ai-checks.log' -Tail 1
    if($ChecksOnly){return}
    & dotnet build src/Studio.Desktop "-p:ServerRoot=$ServerRoot" -v quiet *> '.artifacts/desktop-build.log'
    if ($LASTEXITCODE -ne 0) { Get-Content '.artifacts/desktop-build.log' -Tail 25; throw 'Desktop build failed.' }
    $studioExe = Join-Path $PSScriptRoot 'src/Studio.Desktop/bin/Debug/net10.0-windows/Studio.Desktop.exe'
    $studioProcess = Start-Process -FilePath $studioExe -ArgumentList @('--smoke', '--server', ('"' + [IO.Path]::GetFullPath($ServerRoot) + '"')) -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
    $studioDeadline = [Diagnostics.Stopwatch]::StartNew()
    while (!$studioProcess.WaitForExit(5000)) {
        # Full native authoring/history smoke measured 1556 seconds with 559 assertions.
        # This suite deadline does not alter native startup, tick or memory limits.
        if ($studioDeadline.Elapsed.TotalSeconds -gt 2400) { throw 'Desktop authoring/native-preview smoke suite did not finish within 2400 seconds.' }
    }
    if ($studioProcess.ExitCode -ne 0) { throw 'Desktop smoke test failed; see .artifacts/desktop-smoke-error.txt.' }
    Write-Host 'Desktop import/edit/play/save/trace/render smoke passed.'
} finally { Pop-Location }
