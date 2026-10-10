# Realm Behavior Studio

A working Windows desktop encounter editor for the supplied Raple Realms source. Version 0.1 is an incremental implementation, not the full product described in the roadmap.

**This server uses C# behavior definitions, not XML behaviors.** XML defines entities, projectiles, tiles and equipment. Studio imports C# `.Init(...)` definitions, preserves the complete source, edits the same document that Play consumes, and compiles exports against the original server sources before writing them.

## Run

Install the .NET 10 SDK on Windows, then double-click **Start Studio.cmd**. The supplied workspace already has the SDK. This builds the app and opens its workspace. No Redis, game login, running server or AIR client is needed for Studio's current local test host.

The refreshed workspace uses a graphite theme, matching dark dropdowns, separate Encounter/Behavior/Source/Details views, and a full-width console. Drag the divider above Console to resize it; the two rightmost console buttons expand or collapse it. Panel sizes are saved with projects and remembered between launches. Parameter explanations are available on hover, with optional settings collapsed by default.

Entity thumbnails, enemies, the Wizard test character and projectiles use the supplied client's actual PNG sheets. Studio reads the client texture registrations and resolves the source XML's texture indices, including hex indices and animated character cells. Pixel art stays crisp while zooming. Sprites are loaded from the private client checkout, not bundled into the public application. Unsupported/missing texture registrations show a placeholder; alternate skins, procedural rendering and full client animation parity remain incomplete.

```powershell
./start-studio.ps1
# Another checkout:
./start-studio.ps1 -ServerRoot C:/path/to/raple-realms-7-server -ClientRoot C:/path/to/raple-realms-7-client
```

To build directly:

```powershell
dotnet build src/Studio.Desktop -c Release
dotnet run --project src/Studio.Desktop -- --server ../raple-realms-7-server
```

Build-time `-p:ServerRoot=...` controls references to private source. Run-time `--server ...` selects its resources and source catalog. They must refer to the same revision. Rebuild when changing private server source; restart Studio when changing resources. Private dependencies are intentionally outside this application directory.

The launcher builds into a directory identified by the application/server source version, allowing a refreshed build while an older window remains open. Save work in the older window before switching to the new one. Direct launch also accepts `--client <client folder>`; the sibling client is the default.

## First encounter

1. Search for **Cave Pirate Brawler** in the entity library. Its real behavior contains Follow, Wander and Shoot.
2. Open Behavior and select Shoot in the tree. Change `count` or `coolDown`, then Apply changes. Expand More supported parameters to add spacing, fixed angle or per-volley rotation.
3. Choose a game weapon in the Player tab, set base attack/dexterity, and Apply & restart. Move with WASD/arrows in the viewport; aim with the mouse and hold left click to fire real projectiles. Weapon thumbnails and projectile textures come from the client. Scroll to zoom and middle-drag to pan.
4. Use Play/Pause, step 100 ms, choose playback speed, inspect runtime timers under Details, or select a console event to inspect its behavior node. Right-click an unambiguous projectile to inspect its source action. The console supports filtering and clearing the visible log without deleting the saved trace; changing its breakpoint applies immediately.
5. Save a `.rbs` project. Open it to restore source, node IDs, brief, seed, arena size, player preset and locks. Recovery files are under `%LOCALAPPDATA%/RealmBehaviorStudio/Recovery`; Open project also opens `.rbs.recovery` files.
6. Export to a separate folder. Studio first checks the entire WorldServer source compilation with the edited file substituted. Review the exported C# file before replacing its corresponding server source and rebuilding the server.

The project holds the **complete imported C# file**; changing a selected entity retains other definitions and loot in that file. Switching files retains edits in memory and writes recovery snapshots. Save projects per source file; a multi-document project container is not implemented yet.

Advanced source edits apply atomically after syntax/reference checks. Invalid edits stay in the source pane while the last valid visual document remains active. Full compiler validation happens at export; source expressions outside the adapter's literal/constructor subset may export successfully while remaining unsimulatable.

## What Play currently means

Play reuses the actual `State`, `Entity.Tick`, movement, targeting, transition, Spawn, Shoot, projectile-position and defense implementations, through the upstream headless BossPatternSim host. Enemy ticks are always 100 ms. Collision/player samples are 5 ms. The viewport advances the player on each WPF display frame rather than waiting for an enemy tick. Enemy sprites, shadows, health bars and displayed hitboxes blend between tick positions on each frame, following the client's linear position-update formula. Enemy walking direction and cadence follow that segment's velocity. Playback speed changes scheduling, never the step sizes. Native player walking frames follow movement speed/direction; attack frames follow the equipped weapon's cadence, including extended weapon art.

The player remains an offline test actor, now equipped with a real XML weapon. The catalog contains 237 weapons, with 197 currently supported. Firing uses XML damage/range/spread/rate, the original normal attack/dexterity formulas and seeded damage generator, weapon stat bonuses, original OnShoot volley tracking, projectile replacement/count/spread cycles and supported temporary self boosts/statuses. Hits call the original `Player.EnemyHit`; kills run native death callbacks, while Boss Lab suppresses loot/progression. Unsupported special hit activations and cursor grenades are disabled with reasons. Armor/ring loadouts, abilities, MP, regeneration, special class systems and the full player lifecycle remain pending.

The hitbox overlay uses the same geometry as collision: inclusive one-tile target squares, circular `Oryx4HitRadius` exceptions and projectile **points**, independent of sprite size. World position is at the character's feet. Sprites use the client's scale (an 8px character at size 100 occupies 0.8 tiles), while projectile points and collision boundaries project half a tile above the ground, matching the client's projectile height. Mouse aiming uses the same projection. Hidden, paused, stasis and invincible targets are skipped; piercing projectiles hit each target once. Parametric projectile X parity follows the client where its formula differs from server validation.

Client collision is sampled against authoritative enemy tick positions; enemy presentation interpolation does not change collision results or attack origins. The displayed enemy hitbox follows its rendered pose and can trail the runtime position within a tick. Full client collision interpolation, protocol latency, client hit reporting and special trajectory families are not fully reproduced. Unsupported motions are omitted and reported. No encounter receives a blanket verified-accurate label. See [compatibility](docs/COMPATIBILITY.md).

## Examples and verification

```powershell
./verify.ps1
# Also capture the original .NET 8 harness and run its client-formula checks:
./verify.ps1 -OriginalRuntime
```

The original-harness option requires a .NET 8 runtime. This workspace's local runtime is used automatically. Verification creates:

- `.artifacts/verification/three-phase.rbs`: a runnable acceptance project using local private source/resources.
- `.artifacts/verification/roundtrip.rbs`: visually edited real enemy.
- `.artifacts/verification/*trace.json`: edited, imported and independently compiled reference traces.
- `.artifacts/reference-brawler/result.json`: captured original .NET 8 runtime evidence when requested.
- `.artifacts/desktop/workspace.png`: rendered desktop smoke-test screenshot.
- `.artifacts/desktop/ui-checks.json`: sprite, real splitter-drag, console, layout, popup and compact-window checks; additional rendered screenshots are in the same directory.
- `.artifacts/auth-results.json`: offline authentication/protocol checks, explicitly excluding live sign-in/inference.

The three-phase example moves and aims, spawns two helpers below 70% HP, enters rotating fans below 35%, and changes its nested state when nearby helpers disappear. Its source fragment is original Studio content in `examples/three-phase.fragment.cs.txt`; it names locally supplied resources. Generated `.rbs` examples contain private imported source and stay ignored. Equipment acceptance scenarios remain pending.

## Optional AI

The Assistant tab implements Continue with ChatGPT, separate account registrations, PKCE, state/nonce and signed ID-token validation, Windows DPAPI storage, serialized token renewal and session revocation. It drives a local `codex.exe app-server` over stdio using the documented ChatGPT-plan provider. Install a compatible Codex CLI and supply its executable path if it is not on PATH.

Enable selected-context sharing, enter a brief and send a message. Only the selected entity, supported constructor catalog, brief and requested traces are exposed through Studio's native tools. Proposals are validated with document revision/lock checks and require Preview/Apply; Apply is one undo. Bounded stationary simulations can test the current document or proposal. Built-in shell, browser, computer, connector and delegation capabilities are disabled in this integration; it uses its own isolated Codex home/workspace.

Live account sign-in and inference have **not** been verified in this delivery. Offline tests cover signatures, tampering, permissions, rotation locking, protected storage and streamed/canceled/failed protocol turns using a fake peer. Actual model entitlement and account usage are established by a real completed inference turn, not a model list. Sign-in adds no free allowance. Eligible plan usage and credits are governed by OpenAI's current policies.

Official references: [OSS registration](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [accounts and renewal](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions), [Codex provider configuration](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server), [preview restrictions](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).

## Architecture and next work

- `Studio.Core`: lossless syntax document, stable node metadata, revisions, atomic patches, undo/redo, project persistence.
- `Studio.Raple`: source catalog, typed constructor catalog, validation, restricted binding to real classes, local host, traces and original-source compiler verification.
- `Studio.Desktop`: WPF workspace, custom viewport/graph, inspector, player controls, source editor, proposals and account UI.
- `Studio.AI`: official public-client OAuth lifecycle and Codex app-server transport. It has no dependency on the game.
- `tests`: editor/runtime/differential checks, authentication contracts and desktop smoke automation.

Read [architecture](docs/ARCHITECTURE.md), [verification results](docs/VERIFICATION.md), [adapter guide](docs/ADAPTERS.md) and [complete roadmap](docs/ROADMAP.md).

Original application code is MIT licensed. Private server/client source and assets are not included or relicensed. Local binaries necessarily reference private server assemblies and must not be redistributed without checking their licenses. The build reports inherited BouncyCastle/System.Drawing.Common dependency advisories; upgrading those dependencies requires a separate server compatibility change.
