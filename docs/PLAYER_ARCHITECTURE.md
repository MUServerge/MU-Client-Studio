# Player / Characters Architecture

Status: architecture contract for the first complete MU Client Studio feature.

Scope: Player / Characters only.

Source priority:

1. Main_EX603 — source of truth for EX603 Player/client semantics
2. xulek/muonline-bmd-viewer — implementation/rendering reference
3. Main5.2 — cross-check only

This document defines the implementation boundary before additional Player functionality is written.

---

## 1. Architectural decision

MU Client Studio is an **offline native Windows editor**, not an injected MU client and not a browser viewer port.

Main_EX603 is therefore used to recover the target client's semantics, model routing and Player behavior. Its hard-coded executable addresses, hooks, process patching and server-distribution custom systems are not part of Studio's architecture.

The Player pipeline is split into four responsibilities:

1. **Client data** — resolve and decode real MU client files without UI or renderer dependencies.
2. **Player domain/application** — resolve class/loadout semantics and assemble a deterministic character scene.
3. **Rendering** — consume already-resolved scene data and render it; it must not invent MU rules.
4. **WPF presentation** — MU Project OS V6 Compact shell, commands, inspector and user interaction.

No layer may skip this direction by reading arbitrary client files directly from XAML/code-behind or by embedding MU-specific path guesses inside the renderer.

---

## 2. Target solution structure

### `MUClientStudio.Models`

Pure .NET types. No WPF, file-system or GPU dependencies.

Owns:

- `PlayerClassId` and evolution/base-class identity
- `EquipmentSlotId`
- `ItemId` / group/index identity
- `PlayerLoadout`
- `CharacterDefinition`
- `CharacterAppearance`
- `CharacterAssembly`
- BMD raw/decoded contracts
- skeleton/bone contracts with stable `BmdBoneIndex`
- animation/action contracts
- texture/image contracts
- typed diagnostics/result contracts

Models must be serializable/testable without loading WPF.

### `MUClientStudio.Core`

Application orchestration and concrete client-data services. No WPF dependency.

Owns:

- workspace open/close lifecycle
- Data-root resolution
- client asset index
- case-insensitive MU path resolution
- BMD reader/decryption/validation
- texture decoders
- `Local/item.bmd` catalog loading
- source-backed Player data loaders required by the base client
- Player definition catalog
- Player session/document state
- character assembly coordinator
- cancellation/revision handling
- CPU asset caches
- diagnostics

Core references Models.

### `MUClientStudio.Rendering`

Renderer contracts and GPU-scene implementation. No Player business rules.

Owns:

- render-device lifecycle
- camera and viewport state
- GPU mesh/texture/skeleton resources
- skinning
- animation pose upload
- MU material/blend/effect implementation
- scene resource cache
- picking/debug overlays required by Player inspector

Rendering references Models, not Core.

The renderer receives resolved `CharacterAssembly`/render resources. It must not decide item groups, class paths, wing model names, attachment bones or fallback routing.

### `MUClientStudio.App`

WPF-only shell and presentation.

Owns:

- MU Project OS V6 Compact resource dictionaries
- `MainWindow` shell composition
- Player navigation
- Player workspace view
- viewport host
- inspector
- status/diagnostic presentation
- ViewModels and WPF commands

App references Core, Models and Rendering.

### Tests

Add focused test projects as implementation begins:

- `MUClientStudio.Models.Tests`
- `MUClientStudio.Core.Tests`

Rendering validation should use deterministic capture/reference tests once a renderer exists.

---

## 3. Native C++ decision

**Do not add a C++ project for parsing, Player rules, workspace logic or domain state.** Those stay in C#.

A C++ project is justified only for the final GPU backend if the WPF/.NET 8 renderer cannot preserve required MU rendering semantics cleanly.

The boundary is fixed now so the backend can change without changing Player logic:

`CharacterAssembly -> IPlayerRenderer -> GPU backend`

If a native backend is introduced, it must be a thin renderer behind a stable C ABI. C# remains authoritative for decoded assets and character assembly; native code receives immutable mesh/skeleton/texture/effect payloads and returns opaque resource handles.

Do not inject into or call the original `main.exe`.

---

## 4. Workspace and asset index

### Open Client

Input may be either:

- MU client root containing `Data`
- `Data` itself

`DataRootResolver` resolves one canonical full path.

### Asset index

Do not recursively count every file on the WPF UI thread.

Create a lazy/background `ClientAssetIndex` keyed by normalized relative paths:

- slash-normalized
- case-insensitive
- stores canonical actual path
- supports exact relative-path lookup first
- supports controlled basename/extension fallback only where the Player source contract allows it

The index must distinguish `Player/...` and `Item/...`; it cannot collapse them into one basename namespace.

### Workspace state

Persist only user/editor state such as selected client root. Never copy client assets into the application profile.

Opening another client invalidates all client-specific CPU/GPU caches and cancels stale Player builds.

---

## 5. BMD data pipeline

Pipeline:

`ClientAssetIndex -> BmdReader -> BmdDocument -> CharacterAssembler -> RenderResourceBuilder`

### `BmdReader`

Responsibilities:

- validate BMD header/version
- perform supported BMD decryption before parsing
- bounded reads with explicit count/size limits
- decode meshes, triangles, UVs, normals, bones and actions
- preserve raw BMD bone ordering
- preserve raw action/frame data needed for exact animation
- return typed parse failures, never partial undefined structures

No renderer object may be created during parsing.

### Stable bone identity

`BmdBone.Index` is always the original BMD index.

A renderer may internally add a synthetic skeleton root, but that renderer-local index is never exposed as BMD identity. Gameplay/source attachment references 33/42/47 always address `BmdBone.Index`.

### Coordinate conversion

Raw BMD data remains in MU coordinates.

Any MU-to-renderer coordinate-system conversion happens exactly once in the renderer/scene transform boundary.

Weapon/wing attachment local transforms stay:

- position = zero
- rotation = identity
- scale = one

Do not hide a renderer coordinate conversion inside attachment code.

---

## 6. Texture pipeline

Pipeline:

`texture reference -> AssetResolver -> TextureDecoder -> DecodedImage -> renderer texture cache`

Core owns format decoding and produces a renderer-neutral decoded image.

Player-required formats begin with the verified TGA/OZJ/OZT behavior in the project contract. Unsupported formats produce a diagnostic and must not be silently interpreted as another format.

Texture lookup is deterministic:

1. exact source path/reference
2. verified MU compatibility fallback
3. diagnostic failure

Do not perform an unrestricted recursive texture search every frame/build.

---

## 7. Item and model resolution

### Item catalog

`Data/Local/item.bmd` is the normal item metadata catalog for Player equipment selection.

UI selections carry `ItemId`, never an invented model filename.

### EX603 directory semantics

Main_EX603 establishes:

- groups 7–11 body equipment: `Data/Player` primary
- standard wings: `Data/Item` primary
- other items: `Data/Item` primary

Compatibility resolver:

- groups 7–11: Player -> Item
- other groups including standard wings: Item -> Player

Every fallback is observable through diagnostics so incorrect client data can be identified instead of hidden.

### Server-specific extensions are out of scope

`CustomWing`, `CUSTOM_WING_INFO`, custom wing effect tables and similar server-distribution additions are **not part of MU Client Studio Player scope**.

The editor targets the base client Player data and standard client equipment/wings. Do not add `CustomWingDefinition`, `main.premium/main.free` custom-wing decoding, or server-addon effect configuration to the Player pipeline unless the project scope is explicitly changed later.

---

## 8. Player class definitions

The Player feature must support all EX603 base classes:

- Dark Wizard
- Dark Knight
- Fairy Elf
- Magic Gladiator
- Dark Lord
- Summoner
- Rage Fighter

Do not use the current static `PlayerProfile.Classes` list as architecture authority.

`PlayerDefinitionCatalog` owns source-backed class definitions, model token/path rules and class-specific exceptions.

Evolution classes map to a base class/model identity through explicit data. Presentation names and model identity are separate concepts.

The current scaffold's direct string construction such as `Player/ArmorClass{Token}.bmd` must be replaced by this catalog/resolver. A filename convention is allowed only after it is validated against the target EX603 Data and represented in one source-backed resolver.

---

## 9. Character assembly

`CharacterAssembler` is the central Player pipeline.

Input:

- `PlayerClassId`
- `PlayerLoadout`
- current action/speed
- resolved item definitions
- decoded model assets

Output:

- immutable `CharacterAssembly`
- one authoritative body skeleton
- skinned body-part instances
- bone attachments
- animation sources
- material/effect descriptors
- diagnostics

### Assembly order

1. Resolve the selected class to its base Player definition.
2. Load the base torso/armor-class model and establish its skeleton as the authoritative body skeleton.
3. Load base Helm/Pants/Gloves/Boots class parts.
4. Rebind those skinned meshes to the authoritative skeleton.
5. Resolve equipped groups 7–11.
6. Replace the matching base body part and rebind equipped skinned meshes to the same skeleton.
7. Resolve left weapon and attach to BMD bone 33.
8. Resolve right weapon and attach to BMD bone 42.
9. Resolve standard wings and attach to BMD bone 47.
10. Bind the Player action bank to the body skeleton.
11. Create independent attachment animation players for animated weapon/wing models.
12. Submit one coherent assembly revision to the renderer.

A build is not partially swapped into the viewport. The previous valid character remains until the new assembly is ready, then the renderer atomically replaces the scene revision.

---

## 10. Body parts versus attachments

These are different mechanisms and must stay separate.

### Body equipment: groups 7–11

Body parts are skinned meshes that share/rebind to the base Player skeleton.

They are **not** attached to a single bone.

Equipping a body part hides/replaces its corresponding base class mesh.

### Weapons and wings

Weapons/wings retain their own model hierarchy and are child attachments of a specific base-character BMD bone.

- left weapon -> 33
- right weapon -> 42
- wings -> 47

They may have their own animation clips and animation lifetime.

This separation follows the proven Xulek assembly pattern while preserving the Main_EX603 item/model semantics.

---

## 11. Animation architecture

There are two animation domains.

### Player body animator

Owns:

- selected Player action
- current/prior pose if blending is implemented
- 24 FPS source timing
- base skeleton pose

Changing Player action changes only this animator.

### Attachment animators

Each animated attachment owns an independent playback clock/action instance.

Changing Player action must not recreate/reset attachment animation.

Changing global animation speed updates effective playback rate for both body and attachments without resetting playback phase.

### Action naming

Do not hardcode generic UI values such as `Idle`, `Walk`, `Run`, `Attack`, `Skill` unless a source-backed action catalog maps those names to actual EX603 action indices.

Until names are verified, expose stable action indices plus any verified names/metadata.

---

## 12. Rendering boundary

The renderer consumes MU-resolved data; it does not resolve MU data.

Minimum render features required for Player completion:

- indexed triangle meshes with verified BMD winding
- skeletal skinning
- body-part shared skeleton
- independent attachment transforms/animations
- texture sampling with correct UV orientation
- alpha/blend modes needed by Player assets
- item level/excellent/ancient rendering only when source behavior is verified
- standard client wing rendering required by the base Data
- orbit/pan/zoom camera
- deterministic viewport resize
- skeleton/bone debug overlay
- selected-bone/attachment diagnostics

Renderer debug tools are controlled through Player diagnostics, not exposed as unrelated application modules.

---

## 13. Threading, cancellation and lifetime

### WPF UI thread

Only:

- bindings
- commands
- view state
- viewport host sizing/input

No recursive client scan, BMD parse, texture decode or character build may block the UI thread.

### Worker tasks

File IO, parsing, decoding and assembly run off the UI thread where appropriate and accept `CancellationToken`.

### Revision rule

Every rebuild has a monotonically increasing `PlayerRevision`.

A completed task may publish only if its revision is still current. This prevents old class/equipment loads from overwriting a newer user selection.

### Renderer thread

GPU resource creation, mutation and destruction occur on the renderer's owning thread/context.

### Ownership

`PlayerSession` owns the current CPU-side assembly lifecycle.

The renderer owns GPU resource handles.

Workspace close/change and application close must deterministically dispose both sides.

No finalizer-only resource management.

---

## 14. Error model and diagnostics

Do not swallow exceptions to return `null` with no reason.

Use typed diagnostics such as:

- workspace/Data root invalid
- asset not found
- unsupported BMD version
- corrupt/truncated BMD
- texture decode failed
- item catalog invalid
- class model missing
- skeleton missing
- skeleton/body-part mismatch
- attachment bone missing
- renderer resource/device failure

Fatal errors prevent the new assembly revision from replacing the last valid scene.

Optional asset failures may degrade the assembly but remain visible in the inspector/status diagnostics.

---

## 15. WPF / V6 Compact presentation architecture

`MainWindow` is a shell, not the Player implementation.

Create reusable V6 Compact resources rather than styling controls ad hoc inside one XAML file:

- `Themes/V6Compact.Colors.xaml`
- `Themes/V6Compact.Metrics.xaml`
- `Themes/V6Compact.Typography.xaml`
- `Themes/V6Compact.Controls.xaml`

Player presentation is split into focused views/view-models:

- `PlayerWorkspaceView`
- `PlayerToolbarView`
- `PlayerViewportView`
- `PlayerEquipmentView`
- `PlayerAnimationView`
- `PlayerInspectorView`
- `PlayerDiagnosticsView`

The central viewport remains the dominant workspace. Equipment is presented as compact selectors/tool fields, not large inventory-shaped cards or dashboard tiles.

No Player state is populated by directly mutating TextBlocks/ComboBoxes from `MainWindow.xaml.cs` in the final architecture; the current code-behind remains an incremental scaffold until the ViewModel split is complete.

---

## 16. What from the current scaffold is rejected

The initial scaffold is useful only as bootstrap code. These parts are not approved architecture:

- Xulek listed as primary source
- direct `MainWindow.xaml.cs` ownership of Player state
- hardcoded `Idle / Walk / Run / Attack / Skill` list
- invented class model-path string construction in code-behind
- static `PlayerProfile` as the complete Player definition system
- missing Rage Fighter class
- synchronous recursive file count during workspace open/refresh
- catch-all workspace restore failure with no diagnostic
- placeholder renderer card as the long-term viewport implementation
- ad-hoc one-file WPF styling as full V6 Compact compliance

They should be replaced incrementally; there is no requirement to preserve their internal API.

---

## 17. Implementation phases

### Phase 0 — architecture lock

- this document
- correct source priority
- no renderer/functionality changes

### Phase 1 — foundation

- refactor workspace lifecycle/indexing off UI thread
- typed diagnostics and cancellation
- Player ViewModel/session boundary
- complete EX603 base-class catalog including Rage Fighter
- V6 Compact resource-dictionary shell refactor

### Phase 2 — data fidelity

- BMD reader + tests
- texture decoders + tests
- item catalog loader
- deterministic model resolver

### Phase 3 — character assembly without final effects

- base class skeleton
- base body parts
- equipped body-part replacement/rebinding
- left/right weapon attachment
- standard wing attachment
- stable BMD bone-index mapping

### Phase 4 — animation

- Player action bank
- exact source timing
- action selection
- independent attachment animation clocks
- speed control without attachment reset

### Phase 5 — renderer fidelity

- final GPU backend
- materials/blending
- item level/excellent/ancient rendering where verified
- standard client wing rendering
- camera and debug overlays

Introduce the optional native C++ renderer only here if fidelity/performance evidence justifies it.

### Phase 6 — Player UX completion

- complete V6 Compact Player workspace
- compact equipment selectors and filtering
- animation controls
- inspector and diagnostics
- refresh/reload behavior
- representative EX603 client validation set

### Player completion gate

Player is complete only when representative real EX603 client Data can reliably execute:

`Open Client -> choose every supported base class -> load base body -> equip armor -> equip both weapons -> equip standard wings -> switch actions -> inspect source data -> refresh/reopen client`

with no stale loads, no guessed source behavior and no UI-thread stalls.

No other editor module starts before this gate is met.
