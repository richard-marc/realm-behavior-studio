# Architecture decision · 2026-10-09

## Language and desktop stack

C# is the appropriate choice: TKR.WorldServer and TKR.Shared already implement the source-authoritative behavior and gameplay logic in C#/.NET 8. The existing BossPatternSim test host provides a no-network bootstrap for the real engine. Rust would require an interprocess C# host without a demonstrated advantage for this first workflow.

The first desktop implementation uses WPF on Windows and .NET 10, both available in this workspace. WPF provides native desktop rendering, keyboard input, split panes and a straightforward custom viewport. Core document code stays separate and portable. Desktop/credential storage are Windows specific. Arbitrary docking and portable rendering are later milestones.

The engine assemblies remain .NET 8 targets. The current desktop executes their IL on .NET 10. One independent original .NET 8 capture is compared with Studio; that does not certify all cross-runtime numerical behavior. A .NET 8 runtime child host is the recommended follow-up if broader differential evidence finds runtime-version differences.

## Shared document and fidelity boundary

The adapter established that behaviors are **C# DSL expressions**, not XML. The complete source text and Roslyn syntax tree form the document. Graphs, inspector, source editor, AI patches, runtime binding and export all reference that document. Editor IDs/locks/layout metadata live in `.rbs`, never in unsupported server attributes.

Import finds literal `.Init("entity", new State(...))` calls. It retains everything else losslessly: comments, loot, custom expressions, helper methods and unrelated entity definitions. Runtime factory expressions inside a State become opaque visual nodes. Visual edits replace argument spans or append supported named parameters; they do not regenerate the whole file.

IDs are persisted with structural paths and expression signatures. Parameter edits and append operations retain IDs. Unique source expressions retain IDs when moved. Identical duplicated expressions and arbitrary source restructuring remain an identity reconciliation limitation; robust semantic reconciliation is on the roadmap.

Revision checks, locks and undo are in Studio.Core. A patch validates a candidate before committing. Failed edits preserve the last valid document. Full source replacement requires unlocked regions. Undo history is session-local. Source syntax and reference checks are immediate; compilation against all original server files is the export gate.

## Runtime hosting

Studio.Raple references the actual private server project and links its unchanged `tests/BossPatternSim/SimEngine.cs`. This bootstrap bypasses account/network startup, creates a real BossLabWorld, initializes BehaviorDb and captures EnemyShoot packets at the existing player broadcast boundary.

A restricted interpreter binds literal constructor expressions to actual runtime classes. It never compiles and executes arbitrary user code. `State.Resolve`/`ResolveChildren` run from the supplied assembly. Edited definitions are installed only in the local host. A fresh BehaviorDb per run prevents mutable Shoot rotation fields from leaking across runs.

The real enemy executor runs in 100 ms steps; sampled client/player interaction runs in 5 ms steps. CompositionTarget.Rendering advances the accumulated whole 5 ms samples on each display frame; enemy ticks remain independently aligned to 100 ms boundaries. This removes the former 100 ms player movement bursts without interpolation changing collision positions. Playback speed changes accumulated time, never gameplay step sizes. Simulation is currently single-thread confined because Behavior/Transition RNG and BehaviorDb.InitDb are thread-local/global initialization assumptions.

Viewport character bounds follow Camera.as/TextureRedrawer's source scale and bottom padding. Projectile points and target overlays share the client's z=0.5 projection, with mouse aim inverted through that same plane. ClientSprites follows AnimatedChar's direction rows, two-frame walk/attack sequences, seven-column symmetric walk fallback and centered extended weapon composites. Player animation uses actual sampled movement speed and the last weapon volley's attack period.

The test actor has an independent real ConditionEffectManager, native Inventory with one XML weapon, and original StatsManager/BaseStatManager/BoostStatManager instances backed by offline stat arrays rather than a database character. GameWeapon reuses ClientRandom, OnShootProcTracker and ActivateBoost; projectile hits call Player.EnemyHit and native enemy deaths. ClientCollision shares XML target geometry with the viewport; ClientTrajectory supplies the client's parametric parity while reusing other supported server motion formulas. Client interpolation, networking, special hit activations, grenades, abilities and the complete player lifecycle remain absent. This boundary is visible in the UI and every trace.

AI simulations stop an active interactive run, bind a candidate document and run a bounded stationary scenario. This avoids introducing a second run into the shared RNG context. Entity/projectile/time limits are checked at simulation boundaries. Replay inputs include their sample duration (default 100 ms for older records) so display-frame input boundaries can be reproduced; UI seeking/snapshots/checkpoints are pending.

## AI

Studio.AI owns its own persistent host ID and account registrations; tokens are Windows DPAPI protected. Existing registrations retain their issued OAuth client ID. Refresh and sign-out serialize using an exclusive per-registration file lock and reread the latest stored rotating credentials.

Codex is a local child process over stdio. The app supplies ACCESS_TOKEN, uses the official ChatGPT-plan Responses provider, and owns renewal. When the token changes, the process is replaced and its saved thread resumed. No developer API key or credentials from another application are used.

Tools are namespaced application functions, matching the generated Codex 0.160.1 experimental protocol schema. Tools inspect selected context, search constructors, propose parameter patches, run bounded current/proposed simulations and inspect traces. Mutations remain proposals until user Apply. Core validation and atomic undo apply to manual and AI operations equally.

Live authentication/inference is unverified. The offline protocol peer is explicitly test-only. No mocked response is displayed as live AI. The UI permits editing, save, export and Play independently of AI availability.
