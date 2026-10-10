# Verification results · 2026-10-09

## Environment

Windows 11 x64; AMD Ryzen 9 7950X3D; approximately 64 GB installed memory. .NET SDK 10.0.400, desktop/runtime host 10.0.11, original captured harness .NET 8.0.31. Commits and resource fingerprint are recorded in COMPATIBILITY.md.

## Passed checks

**33 editor/runtime checks** in `tests/Studio.Checks`:

- Real C# behavior import, lossless source retention, existing/optional typed edits and stable IDs.
- Atomic undo/redo, invalid-source retention, value/type errors, unknown identifiers, locked nodes/ancestors and stale proposal rejection.
- Save/reopen preserving edits and identities; unsupported content retained while simulation refuses it.
- Edited attack count appears in actual runtime packets; sampled player takes damage through the original defense helper.
- Forced-phase labeling and repeatable seed/input traces.
- Three-phase strict health threshold, actual minion spawning, inherited parent attack and minion-presence transition.
- Recorded paths repeat at speed 20/75 and defense 0/40.
- Imported Brawler emission trace exactly equals a separately initialized **compiled production BehaviorDb tree** on the current host.
- Imported Brawler matches a captured original **.NET 8 BossPatternSim** run for packet time/count/damage, origin position and angle.
- Edited export is accepted by a C# 12 compilation and assembly emission of **527 original WorldServer source files**, substituting the exported file and using .NET 8 reference assemblies plus the server's restored compile dependencies. A negative check rejects APIs introduced after .NET 8. The game's behavior parser is the C# compiler.

**34 collision/weapon checks** in `tests/Weapon.Checks`:

- Inclusive square/circle boundaries, actual Oryx IV XML radii, stable ties, Heart priority, unhittable targets and MultiHit exclusions.
- Parametric X parity against the client formula, distinct from server validation.
- Real weapon slot catalog, three-pellet bow spread/damage/muzzle offset, sword range rejection and shots aimed away missing the target.
- Native Player.EnemyHit damage/death paths, seeded weapon replay, attack/dexterity/rate formulas and weapon IncrementStat bonuses.
- Original OnShoot cycling staff replacement projectiles/count/spread, sixth-volley damage changes, self defense boost expiration and Speedy movement.
- Weapon/attack/dexterity persistence, backward-compatible defaults, explicit grenade rejection.
- Player movement within a 15 ms frame, exact movement/weapon equivalence across display-frame partitions, unchanged enemy emissions/hits/effects at independent 100 ms ticks, and replay input duration reproduction.

Artifacts: `.artifacts/weapons/catalog.json`, `staff-trace.json`, `cycling-trace.json`, `preset.rbs`. Catalog: 237 weapons, 197 enabled. These checks cover specified source rules and selected weapons; they do not certify every item or an original live-client encounter.

**18 authentication/protocol checks** in `tests/AI.Checks`:

- Persistent host identity, dynamic/returning registration parameters and PKCE S256.
- Callback state rejection and RS256 identity signature, audience, nonce and expiration checks; tampering rejected.
- Windows DPAPI ciphertext storage.
- Concurrent refresh serializes and preserves the rotated credential set and grant.
- Session revocation and local token clearing while retaining registration.
- Codex initialization, thread creation, streaming, native tool response, failure and interruption using a test-only fake process.
- Exact documented ChatGPT-plan provider configuration.

**41 original-harness checks** passed with `BossPatternSim selftest --quick` on .NET 8. They include existing client-formula/parity checks and captured Gordo production-load reproduction. These are upstream independent evidence for selected functions, not complete new Studio encounter certification.

**Desktop smoke** built and launched WPF, selected Cave Pirate Brawler, changed Shoot.count to 7, executed 3.5 seconds, saved/reopened-compatible project data, wrote a trace and rendered the workspace. At capture: HP 975, six live bullets, eleven events. Warm encounter construction was about **18 ms**. Screenshot: `.artifacts/desktop/workspace.png`.

**39 UI checks** passed after the workspace, weapon, player and enemy rendering changes. They cover actual client textures for Brawler, Ghost Ship, Wizard directional frames and live enemy/weapon projectiles; source-scale sprite/half-tile hit-plane alignment; distinct up/down frames and mirrored left movement; both walking frames and extended attack anchoring; player movement on every simulated 60 Hz frame; quarter/four-times playback timing; the dark workspace; real WPF GridSplitter drag handlers; console expansion/collapse/restoration; project layout persistence; event filtering; clearing without trace loss; immediate breakpoint changes; the custom playback popup; a usable 1120×800 window; weapon search, weapon preset persistence and firing in the live viewport. Nine enemy-motion checks cover the client linear blend, endpoint clamping, immediate spawn placement, movement on all 12 simulated 60 Hz frames, smaller movement across tick boundaries, paused pose freezing, catch-up endpoints, fractional quarter-speed frames and restart reset. `.artifacts/desktop/enemy-motion.json` records rendered and authoritative positions across those 12 frames. The loader resolved 110 client sheet registrations, with no missing textures in these sampled scenes. Frame scheduling checks use controlled elapsed times in the actual WPF playback/render-position methods, not measured hardware FPS. This does not certify every custom sprite or alternate skin, full client collision interpolation or live network behavior.

The real WPF dispatcher rendered `workspace.png`, `behavior.png`, `console-resized.png`, `console-expanded.png`, `compact.png`, `playback-dropdown.png`, `weapons-hitboxes.png` and `sprite-alignment.png` under `.artifacts/desktop`. The popup, compact layout, behavior view, weapon/hitbox scene and directional/attack alignment strip were visually inspected. `ui-checks.json` records check names and sprite coverage.

The application primes runtime initialization before offering warm Play. Core first-run initialization in repeated Debug checks measured approximately **0.48–0.52 seconds**; subsequent small encounter starts roughly **18 ms**. These measurements cover encounter construction after resource catalog setup, not process startup, rendering, login, network or large bosses. They are single-machine samples, not a performance distribution or universal guarantee.

## Differential evidence and tolerance

`.artifacts/reference-brawler/result.json` is captured from the original compiled .NET 8 harness, stationary player at (70.5,64.5), enemy at (64.5,64.5), seed 7, 100 ms enemy tick/20 ms sample, 10 seconds. It contains nine production attack packets. Its report rounds coordinates/angles to three decimal places, so comparison tolerance is **0.00051 tiles/degrees**, while packet time/count/damage must match exactly.

`.artifacts/verification/compiled-original-trace.json` and `imported-original-trace.json` compare full-precision raw packet emissions on the current CLR with zero difference. The compiled production tree is independent of Studio's syntax interpreter, but both use the same headless host. This tests tree binding and execution; it cannot prove the shared host's client/network/player assumptions.

Resource SHA-256, check names and cold/warm construction times are in `.artifacts/verification/results.json`. Generated trace/project files may contain private source or game data and remain ignored.

## Not established

- Live ChatGPT registration, renewal/revocation and actual Codex inference/model entitlement have not been exercised. The fake peer is only protocol evidence.
- Equipment, abilities, regen, custom stats/classes, native attacks, complete player death/procs and all status interactions are not accepted.
- Full original-client execution/interpolation/network hit reporting is not compared with the application.
- Replay seeking/checkpoints, large encounter performance, arbitrary constructor/factory expressions, custom controllers/JS and all projectile families remain incomplete.
- Compilation does not establish runtime registration success for every custom behavior or loot side effect. The narrow imported tree is separately executed, but a general isolated original-runtime loader gate remains future work.
- No tested encounter receives a blanket verified-accurate label.

Builds pass with inherited private-server package advisories and compatibility/compiler warnings. No tracked private server/client source was edited. Original harness logging can create `unset/<date>` runtime logs alongside the server; these are generated output, not behavior modifications.
