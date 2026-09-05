# Architecture

## Solution

`MUClientStudio.sln`

- `MUClientStudio.App` — WPF shell, window composition, interaction and presentation
- `MUClientStudio.Core` — local client workspace, file access, profiles, future safe writers
- `MUClientStudio.Models` — BMD/player domain types, skeleton/animation/model contracts
- `MuNative` — optional future C++ project only when native reuse is justified

## Dependency direction

`App -> Core`

`App -> Models`

Core and Models must remain independent from WPF so they can be tested without UI.

## Player-first rule

The application exposes only Player functionality until the Player pipeline is complete and validated against real client Data.

## Native workspace

The user selects either the MU client root or its `Data` folder. The Core resolves the Data root, indexes files from disk, and persists only the selected root in `%LOCALAPPDATA%/MUClientStudio/workspace.json`.

Raw client data is never uploaded anywhere.

## Renderer boundary

Do not choose a rendering backend by convenience alone. The first renderer implementation must preserve Xulek/Main_EX603 model, skeleton, attachment and texture semantics. A native C++/Direct3D layer can be introduced later if it materially improves fidelity or source reuse.
