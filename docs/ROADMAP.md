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

Native constructor binding now uses C# overload resolution against signature mirrors of the actual runtime, including named/optional/expanded arguments, native arrays, enum constants, casts and Cooldown conversions. The mirrors are never emitted or executed. The inspector follows the selected overload and gives repeated params separate field keys. Action creation exposes real overloads with validated values and a source preview. A collapsed lifecycle inspector derives nonempty entry/ongoing/exit/death callbacks from supplied source, shows inherited parent actions and opens the actual action for editing. ConditionEffectBehavior now uses its genuine entry/exit semantics in local execution. Native execution and independent original-server compiler fixtures verify this subset; factories and source variables are preserved with explicit context limitations.

Grouped constructor authoring now exposes explicit/inferred native child arrays as typed lists in the hierarchy. Expanded params and scalar wrapper slots use the same validated structural operations. Reorder, duplicate, copy/paste, cross-parent moves, delete and phase grouping preserve native array syntax, subtype restrictions, scalar cardinality, comments, identities, references and undo. Phase graph/analysis/lifecycle views flatten only native State array boundaries. Older opaque-array IDs and locks upgrade into the newly visible lists. Timed and OnDeathBehavior execute their real callbacks: Timed decrements once per child; OnDeathBehavior calls the child's entry callback at death. Wrapped-child lifecycle views reflect that dispatch. New Prioritize wrappers start with an editable native child.

Compiled source preview now runs factory/helper/variable arguments and custom classes inside imported literal State roots through an edited complete original WorldServer assembly. Play selects this path automatically; asynchronous preparation/ticks keep the desktop available. Native embedded JavaScript resources and assembly identity are retained. Source/build/reference/resource/runtime fingerprints, bounded process initialization/ticks, memory checks, immutable presentation/event snapshots, cancellation, disposal and parent-exit shutdown are verified. Bounded local application-tool simulations use the same path. Desktop exports now require original compilation and native behavior-database loading, with provenance sidecars. Literal quick previews retain the existing in-process host. Broader game-context fidelity and visual representation of generated constructs remain incomplete.

Source-context authoring now discovers native State factory/property roots and exposes helper arguments, actual overloads/defaults/repeated params, constants/variables and single typed child arrays. Binding uses the complete original source compilation at each expression's position without executing helpers/getters. Compiler errors retain the last valid revision; source context, IDs, locks and undo survive save/reopen and legacy opaque-node upgrades. The graph shows a source encounter card for generated roots. Compiled workers receive bindings from the same compiler that emitted their assembly, preserving bounded native initialization without recompiling in the worker. Generated contents and hidden references remain explicit source regions.

Factory calls and shared references inside typed child slots now support structural operations. Destination-scoped insertion/copy retains actual helper overloads, constants, nested child arrays and argument comments; moves preserve identities and copies receive fresh editor identities. Shared-reference copies continue to refer to the original shared value. Missing destination helpers and incompatible native types reject atomically. Desktop Organize and clipboard actions use the same core operations; compiled execution verifies that duplicated helper calls produce two native attacks. Encounter roots remain fixed, and copying/renaming named phases that could contain hidden helper references still requires source editing.

Helpers and source constructors with multiple child parameters now have independently selectable named containers for explicit/inferred arrays, scalar children, omitted optional slots and expanded params. Required scalar slots support atomic replacement; optional slots support removal, creation and cross-slot moves. Optional null/missing arrays are visibly unassigned and become genuinely typed arrays when populated. Rewrites preserve formal binding when positional arguments are removed, including escaped keyword parameters and existing argument comments. Flat legacy IDs/locks and saved mixed slot layouts are verified. Runtime bindings deduplicate the shared physical call span; compiled checks execute both helper-generated Timed children and a source-defined constructor with deliberately reversed tick dispatch. The desktop exposes slot choosers, replacement, optional removal/creation and routed cross-slot drag/drop. Shared/generated collections retain an explicit source boundary.

Native array-targeted collection expressions now expose typed lists with retained brackets, comments, target types, IDs, locks and undo. Explicit children and spread inputs support structural operations; spreads are copied/moved between collection lists without expanding helpers/getters during authoring. The complete destination compiler validates their source binding and element conversions. Source spreads use compiled preview, and reopened native traces verify real expansion order and shared cooldown state. Literal child/value collections use actual native constructors in quick playback. Spread nodes suppress guessed phase-reference/reachability conclusions. Other initializer forms and visually expanding shared/generated contents remain pending.

Source-bound collection inputs now also expose actual IEnumerable/IReadOnlyList/List and custom CollectionBuilder targets in helpers and custom controller constructors. Child collections have their own cardinality and declared target type; they are not scalar slots. Creation fills omitted/null collections using target-typed brackets, while edits preserve existing arrays/brackets, spread comments, binding, IDs, locks, undo and save/reopen. Compiler rejection protects unconstructible collection targets and incompatible native element types. Compiled traces execute actual helper/builder dispatch and a controller with reversed list tick order. Shared/generated collection definitions remain source regions; the editor displays source input order rather than claiming runtime callback order.

Explicit and target-typed collection initializers now expose editable single-input child lists. Structural edits retain constructor arguments, braces, comments, concrete element restrictions, IDs, locks, undo and project persistence. Empty braces remain editable after C# reparses them, and empty object initializers on scalar/expanded behaviors remain genuine behavior nodes. Compiled traces verify the real collection constructors and custom Add methods, including reversed enumeration. Multi-argument Add initializers remain source regions and execute through their actual overloads. Native behavior/State object initializers retain a visible source region, select compiled playback and protect hidden child/reference edits and suppress definitive reachability notices; Open Source selects their exact expression. Native State member additions are verified in core and desktop playback.

Remaining:

- Extend structural editing to factory-generated/shared argument-list contents, multi-argument/index/member initializers and dynamic/opaque phase references. Native array and source-bound non-array collection expressions (including retained spread inputs), ordinary/target-typed single-input collection initializers, literal constructor arrays, single/multiple typed child slots, scalar replacement, optional slots, expanded params and factory/reference nodes in those slots now have structural editing. Visually expanding shared/generated contents and safe phase-reference rewriting across helper definitions remain pending. Hardware pointer/OS clipboard testing across sessions remains additional verification work.
- Extend lifecycle editing beyond the verified Prioritize/Timed/OnDeathBehavior dispatch to custom controllers and other source-supported wrappers, with executable callback-order evidence. The editor does not relocate a primitive's fixed callback behavior to arbitrary stages.
- Extend transition analysis to custom/multiple targets, general cycles and conditional reachability; resolve conflicts across inherited conditions and analyze stateful timers using actual runtime state. Current conflict analysis handles broader preceding HP/player-distance conditions and zero-time timers; reachability is an overestimate of literal destinations and first-child entry. It does not prove that conditional transitions will occur.
- Extend visual/native schemas to counters, weighted choices, loops, shared encounter events, generated/shared collections, dynamic entity IDs and InitMany discovery. Source-context binding and native State factory/property roots now have dedicated checks; comprehensive per-mechanism/controller coverage remains pending.
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
- Extend the verified compiled process host to all preview paths, source instrumentation and complete state introspection. Add OS-level resource enforcement, source-preparation budgets, complete unseeded RNG provenance and explicit real-world nondeterminism handling. Current process time limits and sampled working-set checks are failure containment, not a security sandbox.
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
