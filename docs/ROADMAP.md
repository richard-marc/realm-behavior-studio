# Product roadmap and milestone ledger

This ledger covers the complete requested scope. Status is implementation/verification status, not a promise of runtime fidelity. The current deliverable is a runnable **0.1 vertical slice**. Milestone 1 is complete for the supplied checkouts; subsequent milestones have partial work and explicit gates.

## Milestone 1 · inspected source and architecture — complete

Delivered source compatibility report, server/client revisions, XML fingerprint, C# architecture decision, component boundaries, literal DSL import strategy and source-aware verification strategy. No required game source path is missing. The smallest complete authoring loop is Cave Pirate Brawler → Shoot parameters → real enemy executor with a damageable test target → compiler-gated C# export.

Run the desktop and core checks using the README. Verification includes a captured original .NET 8 harness result and upstream client-formula checks. This establishes a narrow basis for reuse, not perfect game emulation.

## Milestone 2 · first workflow — working slice, fidelity gate incomplete

Implemented:

- Native C# desktop application, entity/source catalog and resource descriptors.
- Lossless import, stable editor IDs, typed literal parameter editing including optional parameters, validation, source preview and atomic undo/redo.
- Edited document binding to real runtime State/behavior classes and local enemy simulation.
- Play/pause/stop/restart/100 ms step, 0.25×–4× playback, seed and basic arena size.
- Damageable HP/speed/defense/attack/dexterity test player, XML weapon selection and firing, original weapon volley procs, client hit geometry, spectator and visible invulnerability override.
- Save/open/recovery, source export compiled with the original server source set, isolated private dependencies.
- Core round-trip, stale patch, lock, unsupported-feature and differential checks.

Required before calling milestone 2 source-faithful gameplay complete:

- Bootstrap a real Player, StatsManager, class/equipment data and local protocol boundary instead of the copied test actor.
- Finish player ability/death/regeneration/effect mechanisms, armor/ring loadouts and unsupported weapon special hit/grenade activations. The ray has been removed; supported weapons already use real XML projectiles and native enemy hit/death paths.
- Execute client-relevant collision/interpolation/hit reporting with original-client independent evidence.
- Broaden parser/executor coverage beyond the literal constructor subset.
- Persist source/build/client asset provenance and invalidate all verified artifacts when it changes.

## Milestone 3 · full encounter authoring — partial

Implemented hierarchy tree and state overview, transition links, ordered source retention, parameter inspector, constructor search, child-state/basic-action creation, forced-phase test, minion spawning/HP gates and the three-phase acceptance example. The structural authoring slice now adds phase rename with literal transition-reference updates; phase/branch/action duplication with unique copied phase names and remapped internal references; source-order reordering; and branch deletion with removal of incoming literal transitions to deleted phases. These operations preserve surviving node identities, use candidate validation, respect region/ancestor/descendant locks, persist in projects, and support atomic undo/redo. The inspector exposes Phase name and a collapsed Organize section, follows tree/graph selection, and disables locked controls immediately. Named phase changes from parameter proposals use the same safe rename path.

Transition cards now show native condition labels, destinations and numbered priorities; selecting a condition opens its parameters and draws its route. Organize controls reorder the actual source transitions. A collapsed Phase review provides clickable missing-target errors, conservative unreachable-phase notices, sibling priority conflicts and self-transition advisories. Navigation follows the shared node identities, and locked transitions cannot be reordered. Native checks confirm first-match sibling order, strict HP thresholds and child-before-parent precedence. Dynamic/custom/opaque content or duplicate names suppress definitive reachability notices.

Bulk authoring now adds Ctrl/Shift multi-selection, source-ordered cross-parent drag/drop, Ctrl-drag copies, clipboard paste with independent identities and remapped internal phase references, nested phase grouping/ungrouping, graph/tree collapse, project collapse persistence and Ctrl+K command/phase/source navigation. Core operations are atomic and preserve separator comments, locks and node identities; the desktop routes actions through those operations.

Remaining:

- Extend structural editing beyond direct phase children to grouped constructor argument schemas and dynamic/opaque phase references. These still require source editing. Renames remain separate atomic operations from other parameter edits. Hardware pointer/OS clipboard testing across sessions remains additional verification work.
- First-class entry/ongoing/exit action editing tied to each actual primitive's semantics. Actual parent action inheritance must remain visible.
- Extend transition analysis to custom/multiple targets, general cycles and conditional reachability; resolve conflicts across inherited conditions and analyze stateful timers using actual runtime state. Current conflict analysis handles broader preceding HP/player-distance conditions and zero-time timers; reachability is an overestimate of literal destinations and first-child entry. It does not prove that conditional transitions will occur.
- Runtime constructor overload resolution, factory/helper expressions, variables, counters, weighted choices, loops and shared encounter events where supported.
- Comprehensive custom primitives, owned minion emitters, multi-boss controllers, transformations, defenses and source-supported telegraphs.
- Attack visual handles for aims/origins/arcs, spacing/burst/repetition/trajectory editors, resource/entity/projectile pickers and sprite previews.
- Movement paths/orbits/home/range tools with exact source units, attack combination previews, density/damage/safe-space analysis with visible assumptions.
- Reusable fragments/parameterized templates/local sharing, editable test arenas, obstacles/tiles/maps/spawn positions, multiple players/targets.
- An executable event/phase timeline rather than an independent animation approximation.

## Milestone 4 · runtime verification and debugging — partial

Implemented input/event/shot traces, fixed stepping, searchable event timeline, basic transition/attack/hit/spawn/death breakpoints, timer inspection, camera zoom/pan, sampled hitbox overlay and event-to-node selection where provenance is unambiguous. Warm host measured under the 500 ms target on the recorded machine.

Remaining:

- Faithful replay seeking, snapshots, checkpoints, checkpoint initialization, recorded-path import/playback controls and persisted replay compatibility stamps.
- Complete runtime state/timer/cooldown/variable introspection and selectable entities/projectiles with reliable provenance for inherited/grouped/custom actions.
- Exact hitboxes, path/target/collision overlays, health/damage/phase UI, camera follow/fixed views, breakpoint stepping through callback order.
- Edits applied at documented safe runtime boundaries or checkpoint restarts; currently every edit discards the run and the next Play starts fresh.
- Differential scenarios for all movement, collision, effects, damage, death, spawn, transformations, networking and client trajectory families.
- Independent original-runtime traces for complex bosses and multiple players, explicit numerical tolerances, per-feature coverage certification.
- Thread isolation/runtime process host, bounded execution protection, unseeded RNG provenance, explicit real-world nondeterminism handling.
- Cold/warm startup distributions, rendering/hosting profiling and resource caching independent of gameplay timing.

## Milestone 5 · official ChatGPT/Codex integration — implemented paths, live gate incomplete

Implemented official OSS dynamic registration, persistent host ID, per-registration issued client IDs, PKCE/state/nonce and signed identity validation, permission checking, DPAPI storage, renewal locking, account picker, cancel/sign-out/revocation. Implemented Codex child stdio initialization, thread creation/resumption, deltas, completion/failure/interruption and token-change restart/resume.

Implemented selected-context control and namespaced local tools for inspection, constructor search, revision/lock-checked parameter proposals, bounded candidate/current simulations and traces. Apply uses core validation and one undo. No hosted connectors or product-only tool capabilities are assumed.

Remaining verification:

- Live browser registration/returning sign-in, eligible and ineligible accounts/workspaces, user-declined permissions, session cancellation, account switching and remote revocation.
- Live model inference with actual account limits/errors; model catalog must not be treated as entitlement.
- Long-running renewal/restart/resume, process crashes, revoked grants, network retries and concurrent application instances under actual services.
- Acceptance test that a real AI proposal survives preview → apply → save/reopen → original compiler/runtime load.

Remaining product features:

- Drawn directions, viewport annotations, reference patterns and recorded intent paths attached to selected regions.
- Structured encounter briefs beyond current free text, parameter-level locks, change subsets, richer diff/runnable-preview UI.
- Full document operation tools for adding/reordering phases/actions/transitions/player/equipment presets, version comparisons and bounded multi-scenario analysis.
- Conversation persistence/UI history, granular data-sharing selection and explainable source/trace evidence.

## Milestone 6 · maintainability, polish and packaging — partial foundations

Implemented separated modules, original MIT license, no bundled game assets, build/launch/verify scripts, v1 readable project JSON, crash recovery snapshots, reference docs and original source template. The redesigned desktop provides consistent dark controls, a quieter inspector, dedicated workspace views, an actually resizable full-width console with expand/collapse, saved panel dimensions, and runtime loading of actual client sprites.

Remaining:

- Portable adapter contract and capability manifests, deliberate support for additional variants, richer schema migration and readable project diffs.
- Persist/resume all documents and complete workspace state, freely dockable/floating panels, keyboard command palette and improved accessibility. Current panel dimensions and workspace view are persisted; panels are resizable within the fixed layout.
- Exact numerical inputs plus more sliders/handles, beginner-oriented labels/default explanations, richer searchable preview catalogs and onboarding. Entity sprite previews and parameter tooltips are implemented; complete sprite/animation/alternate-texture coverage remains pending.
- Automatic source/resource cache and fidelity-result invalidation, private-content audit before packaging, external runtime installation/binding workflow.
- Installer/signing/update process, distributable open-source core with user-supplied licensed adapter assemblies/assets, portable UI/OS credential vault implementations.
- Automated full acceptance suite and performance regression reporting in CI without distributing private game material.

## Acceptance gate for the complete product

The local example currently executes three health phases with movement, aimed fans, rotating shots, helper spawning and a helper-presence transition. Checks force HP/removal visibly to exercise state boundaries. Four speed/defense recorded-path combinations repeat. Weapon scenarios now cover bow/sword/staff range, spread, cadence, bonuses, cycling procs, temporary effects, deterministic damage and native enemy deaths; full loadout scenarios remain pending.

Full acceptance requires native equipment/abilities/regen/player death, complete recorded/replayed movement, independent original-client/server traces, and all encounter features supported by the adapter. Manual and live-AI edits must persist, exports must compile **and load** in an isolated original runtime, and transitions/movement/projectiles/hits/damage/deaths must meet documented tolerances. A compiler-success label or a successful import cannot substitute for these gates.
