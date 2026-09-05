# Architecture

## Solution

`MUClientStudio.sln`

Target projects:

- `MUClientStudio.App` — WPF shell, V6 Compact presentation, views/view-models and viewport host
- `MUClientStudio.Core` — workspace, MU client-data readers/resolvers, Player session and character assembly orchestration
- `MUClientStudio.Models` — WPF-independent BMD/Player/skeleton/animation/domain contracts
- `MUClientStudio.Rendering` — renderer contracts and GPU-scene implementation; no MU item/class/path business rules
- optional native renderer project — only if final Player rendering fidelity/performance justifies a C++ backend

Tests are added beside the managed projects as their implementation begins.

## Dependency direction

- `Models` depends on no application/UI project
- `Core -> Models`
- `Rendering -> Models`
- `App -> Core + Models + Rendering`

Core, Models and renderer-domain contracts must remain independent from WPF so Player behavior can be tested without UI.

## Player-first rule

The application exposes only Player functionality until the Player pipeline is complete and validated against representative real EX603 client Data.

## Source hierarchy

1. Main_EX603 — primary source of truth
2. xulek/muonline-bmd-viewer — implementation/rendering reference
3. Main5.2 — cross-check only

Main_EX603's executable addresses, injection hooks and process patching are evidence about legacy behavior, not Studio architecture.

## Native workspace

The user selects either the MU client root or its `Data` folder. Core resolves one canonical Data root and builds a case-insensitive MU-relative asset index without blocking the WPF UI thread.

Persist only editor/workspace state under `%LOCALAPPDATA%/MUClientStudio`. Raw client data stays on disk and is never uploaded by Studio.

## Renderer boundary

Player semantics are resolved before rendering.

`Client Data -> Player Domain/Character Assembly -> IPlayerRenderer -> GPU backend`

The renderer does not decide class paths, item groups, wing model names, attachment bones or compatibility fallback routing.

Do not add C++ for parsing or Player business rules. A thin native GPU backend is allowed later only when measured fidelity/performance requirements justify it.

## Authoritative Player architecture

See `docs/PLAYER_ARCHITECTURE.md` for the exact Player data flow, component boundaries, assembly rules, threading/lifetime model, error handling and implementation phases.
