# AGENTS.md

## Project overview

This repository contains the Unity implementation of **Yuukei**, a desktop mascot runtime focused on a “character living on the desktop” experience.

The current target is a **Windows MVP** implemented in **Unity 6000.3.6f1 (URP)**.
The Unity project is already set up with:

- **UniTask**
- **UniVRM**
- **CoplayDev/unity-mcp**

Your job is to implement, extend, and refactor the Unity project in a way that matches the project specifications exactly, while preserving future portability to macOS / Android / iOS / Linux where practical.

---

## Source of truth

Always follow this priority order:

1. Files under `docs/yuukei_spec/`
2. Files under `docs/daihon_spec/` **if and only if** your task touches Daihon parsing, runtime behavior, event/function resolution, alias handling, or bridge contracts
3. Example assets / example packages / example `.daihon` files under `examples/` if present
4. Existing code structure in the repository

If code and spec disagree, **the spec wins**.
If two spec files appear to disagree, prefer the more specific file and preserve the documented intent.
Do not silently invent behavior that contradicts the written specs.

---

## Required reading before coding

Before making changes, read these files when they exist:

### Always read
- `docs/yuukei_spec/00_README.md`
- `docs/yuukei_spec/01_*.md`
- `docs/yuukei_spec/02_*.md`
- `docs/yuukei_spec/03_*.md`
- `docs/yuukei_spec/04_*.md`
- `docs/yuukei_spec/05_*.md`
- `docs/yuukei_spec/06_*.md`
- `docs/yuukei_spec/07_*.md`

### Read only when touching Daihon-related systems
- `docs/daihon_spec/01-基本ルール.md`
- `docs/daihon_spec/02-台本の構造.md`
- `docs/daihon_spec/03-変数.md`
- `docs/daihon_spec/04-条件式.md`
- `docs/daihon_spec/05-関数呼び出し.md`
- `docs/daihon_spec/06-セリフとブロック.md`
- `docs/daihon_spec/07-制御構文.md`
- `docs/daihon_spec/付録.md`

If the repo uses different filenames but equivalent content, locate and read the matching files before editing.

---

## Mission boundaries

### You should implement
- Windows MVP behavior for Yuukei
- Unity-side runtime systems
- Transparent desktop mascot behavior
- Package loading and switching
- Persistence
- Daihon bridge integration
- Alias resolution layer for events and functions
- Settings UI skeleton required by the spec
- Safe DLL loading flow with explicit warnings
- Input/context monitoring required by the spec
- Minimal default UX needed for MVP completion

### You may implement when needed
- Small internal helpers
- Editor utilities that improve maintainability
- Lightweight abstractions for future multi-platform support
- Stub adapters / placeholder services for features explicitly marked as future work

### You must not do unless explicitly requested by the user/spec
- Redesign the product concept
- Replace the canonical naming scheme
- Remove alias support
- Add speculative features outside the MVP scope
- Build full marketplace functionality
- Build full cloud sync functionality
- Build large plugin ecosystems
- Change Daihon language semantics on your own
- Introduce heavy architectural complexity without a clear payoff

---

## Core product rules that must not be violated

Keep these invariant unless the user explicitly changes the spec:

- The mascot is the primary experience, not the settings window
- Clicking the character must **not** be repurposed as a settings shortcut
- Settings and exit actions must come from tray/menu/shortcut flows
- Behavior should be driven primarily by Daihon scripts
- The Unity app is the execution substrate: event sources, rendering, function binding, persistence, safety, package handling
- The mascot should feel present but not intrusive
- Busy situations should reduce movement / visibility pressure
- Fullscreen displays and blocked-app displays must be avoided in MVP
- Only one speech bubble may be shown at a time
- New speech overwrites previous speech bubble content
- The MVP target platform is Windows
- Canonical internal event/function names remain English identifiers
- Non-English aliases must still be resolvable through the alias layer

---

## Daihon integration policy

If your task touches Daihon, follow these rules strictly:

### Canonical naming
- Internal processing, logs, persistence, and code paths should use canonical English names
- User-facing Daihon event/function names must be normalized through alias registries

### Alias handling
Implement or preserve:
- `EventAliasRegistry`
- `FunctionAliasRegistry`
- package-provided alias registration
- built-in alias registration
- fallback acceptance of canonical names directly

Alias resolution rules:
- resolve `alias -> canonical`
- trim surrounding whitespace
- ASCII comparisons may be case-insensitive
- non-ASCII comparisons should remain exact unless the spec says otherwise
- package aliases override built-in aliases
- collisions should be logged with warnings

### Event context contract
Event context values exposed to Daihon must follow the `_event_*` naming contract from the spec.
Do not rename them arbitrarily.

### Execution model
Preserve the MVP execution model:
- one event at a time
- FIFO queue for pending events
- `periodic_tick` may be coalesced to the newest entry
- `show_choices(...)` is awaitable and returns a value
- cancellation rules must follow the spec

If Daihon runtime behavior is undefined in code but defined in the spec, implement the spec.
If Daihon runtime behavior is undefined in both code and spec, choose the smallest behavior that preserves the documented intent and mention it in your summary.

---

## Unity implementation guidance

Prefer the following responsibilities, or stay close to them if the project already has equivalents:

- `ResidentAppController`
- `MascotRuntime`
- `DaihonBridge`
- `InputContextMonitor`
- `PackageManager`
- `PersistenceStore`
- `SettingsWindow`
- `PluginLoader`
- `TutorialBootstrap`
- `AliasRegistry`

You may refactor naming or split internals, but do not destroy the separation of concerns.

### Prefer these design habits
- Keep MonoBehaviour logic thin where possible
- Push stateful logic into testable C# classes when practical
- Use UniTask consistently for async flows
- Isolate Windows-specific behavior behind interfaces or adapters when reasonable
- Keep package parsing, persistence, and Daihon binding decoupled from rendering concerns
- Avoid giant “god objects” unless the current codebase already forces that structure

### Avoid
- Massive hidden coupling between UI and runtime internals
- Hardcoding package content paths outside the spec-defined rules
- Deep inheritance trees when composition is simpler
- Inventing parallel naming systems for the same concept
- Embedding alias knowledge in random classes instead of a centralized registry or resolver path

---

## Package and persistence rules

Respect the spec-defined formats.
Do not casually change JSON shape.

### Package rules
- install under `Application.persistentDataPath/package/{creator}-{version}-{guid}/`
- reserve `manifest.json`
- reserve `character.vrm`
- skip broken elements individually instead of failing the whole package by default
- do not auto-load DLLs

### Persistence rules
- use `Application.persistentDataPath/save.json`
- keep API keys in OS-secure storage, not plain JSON
- do not persist transient runtime-only state unless the spec requires it
- do not persist character position unless the spec is changed later

If a migration is required because the existing code disagrees with the spec, implement the least risky migration path and describe it in your summary.

---

## UI rules

The settings UI is important, but it is not the product centerpiece.
Implement the skeleton required by the spec without overbuilding it.

Must preserve:
- single settings window with sidebar navigation
- categories required by the spec
- package browsing / switching / local import flow
- integration/API key area
- behavior/settings area
- about page
- DLL warning / confirmation UI
- simple choice UI for `show_choices(...)`

Do not turn the MVP into a polished enterprise dashboard.
Favor clarity, correctness, and spec compliance over visual flourish.

---

## Working style

When given a non-trivial task, work in this order:

1. Read the relevant spec files
2. Inspect the current codebase structure
3. Produce or update a concise implementation plan
4. Make the smallest correct change set that satisfies the task
5. Run relevant validation steps
6. Summarize what changed, what remains, and any assumptions

For large tasks, prefer incremental progress over a giant speculative rewrite.

---

## Validation expectations

Before considering a task done, do as many of these as the environment allows:

- ensure code compiles or is at least structurally compile-ready
- check for obvious C# syntax errors
- check namespace/type consistency
- verify no broken references were introduced intentionally
- verify new files are included in the expected project structure
- verify JSON examples / loaders match the documented schema
- verify alias registration paths still reach canonical execution
- verify async flows respect cancellation tokens where relevant

If you cannot run a validation step, say so explicitly.
Do not claim something was tested if it was not.

---

## Output expectations

When you finish a task, provide:

1. What you changed
2. Why those changes were necessary
3. Any assumptions you made
4. Any unresolved risks or follow-up work
5. Any files the user should review first

For spec-sensitive tasks, explicitly mention which spec files governed your decision.

---

## When requirements are ambiguous

Do not stop at the first ambiguity.
Use this fallback order:

1. More specific spec file
2. Existing repository convention, if it does not conflict with spec
3. Smallest implementation that preserves the project intent

When ambiguity remains, make a conservative choice and document it in your summary.
Avoid broad speculative feature work.

---

## Definition of success for this repository

A successful contribution makes the Unity project more compliant with the Yuukei specs, more maintainable, and closer to a working Windows MVP.

The best changes are:
- spec-accurate
- incremental
- testable
- easy to review
- respectful of the Daihon bridge contract
- respectful of the canonical-name + alias-resolution architecture

