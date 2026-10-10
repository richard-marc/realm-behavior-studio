# Milestone 3 completion audit

The milestone is incomplete. This checklist preserves the roadmap's full scope; passing existing checks does not establish completion of missing features. Current verified progress includes native C# constructor binding, constructor creation forms and source-derived lifecycle/inheritance controls, with 61 native schema checks and 85 desktop checks.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| Drag/drop, multi-selection, clipboard, cross-parent moves | BehaviorDocument.Selection; 30 selection checks and routed desktop drag/drop checks; literal branch/action identities, locks, references and undo verified | Extend to grouped constructor/factory schemas; additional physical pointer and OS clipboard session evidence |
| Grouping, collapse, navigation, command search | Native nested phase group/ungroup, source/phase navigation and Ctrl+K search; desktop save/reopen restores collapse | Basic direct-phase workflow verified; broader factory/grouped argument coverage depends on native schemas |
| Entry/ongoing/exit semantics and inheritance | Source-derived nonempty callback stages and inherited action rows; navigation to owner actions; native temporary/permanent effects and common-parent persistence checked | Broader callback wrappers/custom-controller editing and executable callback-order evidence |
| Custom/multiple transition targets, cycles, inherited conflicts, timers | Conservative literal analysis; self-edge notices | Correct native schemas and stateful diagnostics with conditional limitations and runtime evidence |
| Constructor overloads, factories/helpers, variables/counters, weighted choices/loops/events | Native overload/named/default/params/array binding; all eight Taunt fixtures pass the original compiler; helper constructors retained as values; source context limitations explicit | Grouped argument visuals and complete source-context/executable coverage of the remaining mechanisms |
| Custom primitives, owned emitters, multi-boss controllers, transformations, defenses, telegraphs | Small supported constructor list | Import/edit/execute/export scenarios using actual supplied primitives |
| Attack handles and pattern/trajectory tools | Numeric Shoot inspector | Direct viewport interaction and source-unit verification, resource/entity/projectile pickers/previews |
| Movement tools and attack/density/damage/safe-space previews | Generic parameters; encounter viewport | Source-unit movement controls and measured previews with visible assumptions |
| Reusable fragments/templates/sharing | Original example source fragment | Save/import/parameterize/reuse UI, reference reconciliation and round-trip checks |
| Editable arenas, obstacles/maps/spawns, multiple players/targets | Arena size and one test player | Actual native world editing and multi-target execution evidence |
| Executable event/phase timeline | Event console only | Runtime-backed timeline navigation/playback and source provenance checks |

After these requirements are implemented and verified, audit Milestone 3 again before changing its status. Continue with the remaining roadmap milestones and complete-product acceptance gate; their unfinished requirements remain in ROADMAP.md.

Next implementation focus: extend grouped constructor arguments and source-context helpers/factories, then broaden primitive execution and callback wrappers. Current binding resolves standalone C# constructor expressions; it does not reconstruct arbitrary factory calls, local variables or shared encounter events. Those limitations remain visible and source is retained.
