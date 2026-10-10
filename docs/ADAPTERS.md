# Adapter development guide

The first adapter deliberately targets the recorded Raple Realms revision. It is not a universal XML behavior editor.

## Boundaries

Studio.Core owns the common document, stable editor metadata, project persistence, revisions, locks and undoable operations. It knows nothing about TKR, networking or OpenAI.

Studio.Raple owns dialect discovery/import, constructor metadata, source/resource validation, restricted binding, the runtime host and export verification. Studio.Desktop renders that model. Studio.AI exposes validated application-native operations through Codex. Avoid putting gameplay physics in the viewport or creating a second AI-only document format.

## Adding a primitive

1. Read its actual constructor overloads, defaults, units, entry/exit/tick/death behavior and state storage. Trace any server/client/resource dependencies.
2. Let the reflection catalog expose its constructor metadata for browsing. Browsable does not imply simulatable.
3. Add a deliberate binding strategy. The current interpreter accepts C# constants, casts, enum expressions, native arrays, Cooldown conversions and whitelisted `new` expressions; method calls, factories, variables and arbitrary code remain opaque.
4. Supply ChildArgument slot types/cardinality/runtime minimums and callback dispatch from actual source. Explicit arrays keep their concrete element restriction, and view containers must never become runtime primitives. Add reference/type/range validation that preserves runtime units and meaningful defaults. Preserve input order and opaque source.
5. Check headless host support for every dependency, including networking, initialized player/account systems, visibility and client hit reporting. Do not whitelist a primitive whose required host systems do not exist.
6. Add independent compiled original-tree/event evidence, timing boundaries and relevant client/formula results. Record tolerance, runtime revisions and resource versions.
7. Update the five separate capability indicators: import, visual edit, export, simulate, verify. Never promote the final two based solely on syntax import or compiler success.

Runtime types must be resolved inside the IStateChildren/logic type family. Names such as Spawn also occur in packet classes, so a simple assembly-wide name lookup is unsafe.

## Adding another server variant

Inspect its source before implementing an adapter. It may use XML, JSON, C#, scripts, a different state executor or entirely different client collision responsibilities. Keep lossless opaque constructs and source mappings. Select runtime reuse over a convenient invented schema.

A future general adapter interface should provide resource/provenance manifests, an import/export dialect service, typed capabilities/parameter metadata, a document validator, runtime session boundaries and differential scenario execution. Do not flatten nested states/concurrent ancestor actions into sequential graph animations.

## Testing/export

The current C# export gate replaces the imported file in an in-memory compilation of the original WorldServer source set. No private server source file is overwritten to validate an export. Original compiler dependencies are discovered from the application runtime and local referenced assemblies.

The narrow runtime comparison creates one production BehaviorDb tree and one interpreter-bound document tree, runs equivalent recorded scenarios, and compares raw packet fields. An additional captured .NET 8 result checks cross-CLR emission behavior at report-rounding tolerance. Extend this to movement samples, timer/state transitions, collision reports, damage and lifecycle events before certifying another feature.

The current bootstrap depends on private field names via reflection, matching upstream test harness conventions. Treat a source update as a compatibility change: rebuild, re-inspect and rerun tests. Future adapters should negotiate a stable runtime-host contract rather than silently accepting broken reflection bindings.

## Licensing/distribution

Keep private checkouts and assets external. Studio's MIT license covers original application code only. Linked upstream host source and referenced server/client assemblies retain their licenses. Do not publish generated `.rbs` examples containing private imported source, game resource copies, token files or private binaries without explicit redistribution rights.
