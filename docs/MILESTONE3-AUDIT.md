# Milestone 3 completion audit

The milestone is incomplete. This checklist preserves the roadmap's full scope; passing existing checks does not establish completion of missing features. Current verified progress includes native C# constructor binding, grouped constructor/array editing, source-derived lifecycle/inheritance/wrapper controls and isolated compiled source execution, with 61 native schema checks, 41 grouped argument checks, 33 compiled runtime checks and 110 desktop checks.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| Drag/drop, multi-selection, clipboard, cross-parent moves | 30 selection and 41 grouped argument checks; routed desktop moves through typed arrays; identities, locks, references and undo verified | Factory/shared/multiple-child-slot schemas; additional physical pointer and OS clipboard session evidence |
| Grouping, collapse, navigation, command search | Native nested phase group/ungroup, source/phase navigation and Ctrl+K search; desktop save/reopen restores collapse | Basic direct-phase workflow verified; broader factory/grouped argument coverage depends on native schemas |
| Entry/ongoing/exit semantics and inheritance | Source-derived callbacks, inherited owner actions, effects/common-parent persistence; native Timed loop and OnDeathBehavior death-to-entry dispatch executed and represented in the editor | Broader custom controllers and callback-order scenarios |
| Custom/multiple transition targets, cycles, inherited conflicts, timers | Conservative literal analysis; self-edge notices | Correct native schemas and stateful diagnostics with conditional limitations and runtime evidence |
| Constructor overloads, factories/helpers, variables/counters, weighted choices/loops/events | Native overload/named/default/params/array binding and structural array editing; compiled complete original assembly executes source constants/helper/factory arguments inside imported State roots; native loading is checked | Source-context visual schemas, factory-root discovery and per-mechanism execution/provenance scenarios for remaining counters/choices/loops/shared events |
| Custom primitives, owned emitters, multi-boss controllers, transformations, defenses, telegraphs | Restricted constructor list plus compiled native custom-class execution and bounded failure evidence; original embedded JS is retained | Broad import/edit/execute/export scenarios using the supplied complex primitives and real game-context prerequisites |
| Attack handles and pattern/trajectory tools | Numeric Shoot inspector | Direct viewport interaction and source-unit verification, resource/entity/projectile pickers/previews |
| Movement tools and attack/density/damage/safe-space previews | Generic parameters; encounter viewport | Source-unit movement controls and measured previews with visible assumptions |
| Reusable fragments/templates/sharing | Original example source fragment | Save/import/parameterize/reuse UI, reference reconciliation and round-trip checks |
| Editable arenas, obstacles/maps/spawns, multiple players/targets | Arena size and one test player | Actual native world editing and multi-target execution evidence |
| Executable event/phase timeline | Event console only | Runtime-backed timeline navigation/playback and source provenance checks |

After these requirements are implemented and verified, audit Milestone 3 again before changing its status. Continue with the remaining roadmap milestones and complete-product acceptance gate; their unfinished requirements remain in ROADMAP.md.

Next implementation focus: source-context visual schemas and factory-root discovery, then broaden supplied custom primitives/controllers with native callback and ownership provenance. Compiled execution now supplies the real surrounding C# context for arguments inside imported State roots; generated/shared structures are still opaque for visual editing. Comprehensive shared events, controllers, player/client prerequisites and per-mechanism fidelity remain unproven.
