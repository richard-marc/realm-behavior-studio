# Verification results · 2026-10-10

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

**32 structural authoring checks** in `tests/Structure.Checks`:

- Phase rename with escaped/verbatim literal references, preserved IDs/unrelated entity source, duplicate/empty name rejection and atomic failure.
- Locked inbound reference protection and safe renames through parameter proposals.
- Nested phase/branch copies with unique names, fresh IDs, remapped internal references and preserved external targets.
- Reordering/copying/deleting identical actions with stable surviving identities.
- Reordered phase entry executed by the real runtime; deletion removes incoming references to every deleted nested phase.
- Single undo/redo restores source, references and identities; region/ancestor/descendant lock guards, root protection and failed candidate validation.
- Explicit rejection of grouped constructor argument edits and dynamic phase reference rewrites; project round-trip.
- Renamed, duplicated and deleted phase exports accepted by the original full server compiler.

Artifacts: `.artifacts/structure/results.json` and `organized.rbs`. This covers direct phase children and literal references; clipboard, drag/drop, cross-parent moves and arbitrary source identity reconciliation remain pending.

**30 selection authoring checks** in `tests/Selection.Checks`:

- Multi-node source order and stable identical-action IDs; cross-parent and nested-to-ancestor moves without restoring removed children.
- Separator comments survive moves/grouping; branch cycles, root moves, locked destinations/siblings and rejected candidates leave source/IDs unchanged.
- Clipboard version/constructor validation, fresh copied IDs, unique nested phase names, internal reference remapping and rejection of missing external destinations.
- Native nested phase entry, grouping/ungrouping, inbound-reference guards and atomic bulk deletion/undo.
- Moved, grouped and pasted exports compile against the original complete server source set. Artifacts: `.artifacts/selection/results.json`.

**23 transition analysis checks** in `tests/Analysis.Checks`:

- Filtered source-order priority, strict HP labels with C# numeric suffixes and condition modes for players/entities.
- Native first-match sibling order, exact HP boundary behavior, changed native destination after reorder and child-before-parent precedence.
- First-child parent-target descent, inherited ancestor transitions, unreachable siblings/orphans and finite traversal of conditional cycles.
- Self transitions remain advisories; dynamic targets, opaque factories, unknown actions and duplicate names suppress definitive unreachable claims.
- Missing-target errors retain transition IDs; broader HP/player-distance predicates and zero-time timers diagnose shadowed successors without treating nonzero stateful timers as stateless predicates.
- Authoring analysis stays separate from blocking runtime validation. Artifacts: `.artifacts/analysis/results.json`.

This is conservative structural analysis. It does not prove that a world/HP/timer condition will occur, detect general closed cycles or analyze custom/multiple destinations.

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

**61 native schema checks** in `tests/Schema.Checks` cover all eight Taunt overloads and actual field assignments, C# ambiguity, named/expanded/normal-array arguments, separate repeated field edits and identities, clipboard schema retention, optional defaults, casts/arithmetic and native Cooldown conversions. Invalid conversions, parameter names, required arguments and incorrect CycleBehavior children are rejected. Native execution checks temporary/permanent effects and inherited common-parent persistence. Lifecycle callbacks omit empty methods and retain source provenance. Qualified roots, named phase order, literal Root/empty-root targets and incomplete array visuals are checked. Independent compilation of the complete original .NET 8 server accepts all eight fixtures and rejects the ambiguous numeric/Cooldown call. Source-context expressions remain explicit limitations, and unrelated qualified types are never substituted for game types.

**Desktop smoke** built and launched WPF, selected Cave Pirate Brawler, changed Shoot.count to 7, executed 3.5 seconds, saved/reopened-compatible project data, wrote a trace and rendered the workspace. At capture: HP 975, six live bullets, eleven events. Warm encounter construction measured **71.3 ms** in the current core run; this excludes resource/catalog/schema initialization. Screenshot: `.artifacts/desktop/workspace.png`.

Nine native editor checks cover inherited callback navigation, overload-specific repeated fields, real constructor selection and array values, invalid-value disabling, source preview, commit and parent locks. The new `native-lifecycle.png` and `native-creation.png` captures were visually inspected.

**85 UI checks** passed after the workspace, weapon, player, enemy, structural authoring and transition review changes. Eighteen selection checks exercise Ctrl/Shift selection, clipboard boundary operations, actual WPF routed move/copy drag-over/drop handlers and before/after insertion, focused-versus-selected styling, grouping, collapse persistence and desktop reopening, hidden-node navigation, searchable commands and stale-drag rejection. Controlled drag events use the installed WPF constructor through reflection; the test replaces the clipboard boundary with an in-memory payload and does not claim physical mouse/OS clipboard testing. Ten transition-review checks exercise condition-row selection, native order summaries, reorder/undo diagnostic refresh, missing-target/unreachable/conflict navigation, lock guards and clearing stale diagnostics. Nine authoring checks exercise the real desktop rename, duplicate, reorder, delete, lock/unlock, selection and undo/redo controls. They cover actual client textures for Brawler, Ghost Ship, Wizard directional frames and live enemy/weapon projectiles; source-scale sprite/half-tile hit-plane alignment; distinct up/down frames and mirrored left movement; both walking frames and extended attack anchoring; player movement on every simulated 60 Hz frame; quarter/four-times playback timing; the dark workspace; real WPF GridSplitter drag handlers; console expansion/collapse/restoration; project layout persistence; event filtering; clearing without trace loss; immediate breakpoint changes; the custom playback popup; a usable 1120×800 window; weapon search, weapon preset persistence and firing in the live viewport. Nine enemy-motion checks cover the client linear blend, endpoint clamping, immediate spawn placement, movement on all 12 simulated 60 Hz frames, smaller movement across tick boundaries, paused pose freezing, catch-up endpoints, fractional quarter-speed frames and restart reset. `.artifacts/desktop/enemy-motion.json` records rendered and authoritative positions across those 12 frames. The loader resolved 110 client sheet registrations, with no missing textures in these sampled scenes. Frame scheduling checks use controlled elapsed times in the actual WPF playback/render-position methods, not measured hardware FPS. This does not certify every custom sprite or alternate skin, full client collision interpolation or live network behavior.

The real WPF dispatcher rendered `workspace.png`, `behavior.png`, `console-resized.png`, `console-expanded.png`, `compact.png`, `playback-dropdown.png`, `weapons-hitboxes.png`, `sprite-alignment.png`, `phase-authoring.png`, `transition-review.png` and `transition-priority.png` under `.artifacts/desktop`. The popup, compact layout, behavior view, weapon/hitbox scene and directional/attack alignment strip and structural authoring inspector were visually inspected. The transition review, selected condition/route, collapsed group and command-search screenshots were also inspected. `ui-checks.json` records check names and sprite coverage.

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
