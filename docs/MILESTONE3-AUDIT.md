# Milestone 3 completion audit

The milestone is incomplete. This checklist preserves the roadmap's full scope; passing existing checks does not establish completion of missing features. Current progress includes compiler-bound constructor/factory arguments, multiple typed child slots, arrays/collection expressions/spreads/ordinary collection initializers, source-derived lifecycle controls, compiled native execution and visual Shoot editing. Current check totals and their scope are recorded in VERIFICATION.md.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| Drag/drop, multi-selection, clipboard, cross-parent moves | 30 selection and 53 grouped argument checks, source-context collection/multiple-slot checks and routed desktop moves; identities, locks, references and undo verified | Visually editing generated/shared definitions, multi-input Add/index/member initializers; physical pointer and OS clipboard session evidence |
| Grouping, collapse, navigation, command search | Native nested phase group/ungroup, source/phase navigation and Ctrl+K search; desktop save/reopen restores collapse | Basic direct-phase workflow verified; broader factory/grouped argument coverage depends on native schemas |
| Entry/ongoing/exit semantics and inheritance | Source-derived callbacks, inherited owner actions, effects/common-parent persistence; native Timed loop and OnDeathBehavior death-to-entry dispatch executed and represented in the editor | Broader custom controllers and callback-order scenarios |
| Custom/multiple transition targets, cycles, inherited conflicts, timers | Conservative literal analysis; self-edge notices | Correct native schemas and stateful diagnostics with conditional limitations and runtime evidence |
| Constructor overloads, factories/helpers, variables/counters, weighted choices/loops/events | Native overload/named/default/params binding, complete-source factory/property-root discovery and typed argument/collection editing; compiled native loading and actual custom dispatch checked | Generated/shared definitions and per-mechanism execution/provenance scenarios for counters/choices/loops/shared events |
| Custom primitives, owned emitters, multi-boss controllers, transformations, defenses, telegraphs | Restricted constructor list plus compiled native custom-class execution and bounded failure evidence; original embedded JS is retained | Broad import/edit/execute/export scenarios using the supplied complex primitives and real game-context prerequisites |
| Attack handles and pattern/trajectory tools | Native Shoot aim/spacing/search-radius handles; compiler-known geometry, actual sprites, atomic gestures, locks/revisions/undo/save and independent native packet comparison | Broader bursts/repetition/trajectories/owned emitters and source-supported origins; resource/entity/projectile pickers/previews |
| Movement tools and attack/density/damage/safe-space previews | Generic parameters; encounter viewport | Source-unit movement controls and measured previews with visible assumptions |
| Reusable fragments/templates/sharing | Original example source fragment | Save/import/parameterize/reuse UI, reference reconciliation and round-trip checks |
| Editable arenas, obstacles/maps/spawns, multiple players/targets | Arena size and one test player | Actual native world editing and multi-target execution evidence |
| Executable event/phase timeline | Event console only | Runtime-backed timeline navigation/playback and source provenance checks |

After these requirements are implemented and verified, audit Milestone 3 again before changing its status. Continue with the remaining roadmap milestones and complete-product acceptance gate; their unfinished requirements remain in ROADMAP.md.

Next implementation focus: native resource/projectile pickers and attack/movement tools, then broaden supplied custom primitives/controllers with callback and ownership provenance. Generated/shared structures still need safe visual editing across definitions. Comprehensive shared events, controllers, player/client prerequisites and per-mechanism fidelity remain unproven.
