# Milestone 3 completion audit

The milestone is incomplete. This checklist preserves the roadmap's full scope; passing existing checks does not establish completion of missing features. The previous goal turn made verified progress: commit `1e4baae` added transition priorities/review with 23 analysis checks and 58 desktop checks.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| Drag/drop, multi-selection, clipboard, cross-parent moves | BehaviorDocument.Selection; 30 selection checks and routed desktop drag/drop checks; literal branch/action identities, locks, references and undo verified | Extend to grouped constructor/factory schemas; additional physical pointer and OS clipboard session evidence |
| Grouping, collapse, navigation, command search | Native nested phase group/ungroup, source/phase navigation and Ctrl+K search; desktop save/reopen restores collapse | Basic direct-phase workflow verified; broader factory/grouped argument coverage depends on native schemas |
| Entry/ongoing/exit semantics and inheritance | Native runtime execution; generic constructor inspector | Source-derived lifecycle descriptions and editing, visible inherited actions, callback evidence |
| Custom/multiple transition targets, cycles, inherited conflicts, timers | Conservative literal analysis; self-edge notices | Correct native schemas and stateful diagnostics with conditional limitations and runtime evidence |
| Constructor overloads, factories/helpers, variables/counters, weighted choices/loops/events | Literal constructors; unsupported expressions preserved | Source/native schema integration and executable coverage of each supported mechanism |
| Custom primitives, owned emitters, multi-boss controllers, transformations, defenses, telegraphs | Small supported constructor list | Import/edit/execute/export scenarios using actual supplied primitives |
| Attack handles and pattern/trajectory tools | Numeric Shoot inspector | Direct viewport interaction and source-unit verification, resource/entity/projectile pickers/previews |
| Movement tools and attack/density/damage/safe-space previews | Generic parameters; encounter viewport | Source-unit movement controls and measured previews with visible assumptions |
| Reusable fragments/templates/sharing | Original example source fragment | Save/import/parameterize/reuse UI, reference reconciliation and round-trip checks |
| Editable arenas, obstacles/maps/spawns, multiple players/targets | Arena size and one test player | Actual native world editing and multi-target execution evidence |
| Executable event/phase timeline | Event console only | Runtime-backed timeline navigation/playback and source provenance checks |

After these requirements are implemented and verified, audit Milestone 3 again before changing its status. Continue with the remaining roadmap milestones and complete-product acceptance gate; their unfinished requirements remain in ROADMAP.md.

Next implementation focus: resolve actual native constructor overloads and derive lifecycle schemas from supplied source. The current longest-constructor binding is insufficient for primitives such as Taunt, and the generic inspector does not establish entry/ongoing/exit semantics. This foundation is required before claiming comprehensive native primitive authoring.
