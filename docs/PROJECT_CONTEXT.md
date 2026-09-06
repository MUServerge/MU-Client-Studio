# MU Client Studio — Project Contract

## Product

MU Client Studio is a native Windows desktop editor for MU Online client data.

This repository is the clean rewrite of the abandoned Electron/Next.js version.

The long-term product is a unified client-data studio: Player/BMD viewing and editing first, then the verified client tables and editors exposed by the real client data set (for example Local tables and world data such as `Gate.bmd`). Server-emulator add-ons are not part of the base client editor unless they are explicitly added as a separate future scope.

## Stack

- C# / .NET 8
- WPF
- Visual Studio solution (`MUClientStudio.sln`)
- Windows x64
- Optional C++ native project/DLL only where technically justified

## Scope lock

Only **Player / Characters** may be developed until Player is complete.

Do not add placeholder modules for Items, Skills, Gates, Warp, Maps, etc. Their codecs/data contracts may be researched and tested when needed for the common editor foundation, but no competing UI module starts before the Player completion gate.

## UI contract

Visual reference: **MU Project OS — V6 Compact**.

This is the UI contract for the complete desktop shell and visual language, not loose inspiration.

Required shell:

- edge-to-edge desktop UI
- compact header
- left Player navigation
- large central Player workspace
- useful right inspector
- bottom status bar
- dark OS-like palette
- subtle borders and small radii
- dense professional layout

Forbidden:

- centered website container
- webpage outer margins/max-width
- giant dashboard cards
- decorative placeholder modules
- browser/PWA concepts

## Technical source hierarchy

1. **Main_EX603 — primary source of truth for Player/client semantics**
2. **xulek/muonline-bmd-viewer — implementation and rendering reference**
3. **Main5.2 — cross-check only**

Policy:

- Main_EX603 wins when references disagree about EX603 Player behavior
- Xulek may supply a proven implementation pattern where Main_EX603 only exposes legacy engine behavior through hooks/addresses
- Main5.2 is evidence for cross-checking legacy client behavior, never the primary design authority
- do not copy Main_EX603 executable addresses, injection hooks or process-patching architecture into Studio
- source-backed exactness
- do not invent BMD semantics
- reuse proven MU logic
- no approximations when source behavior is available

## Supported EX603 base classes

Player must cover the complete EX603/Season 6 base-class set before it is considered complete:

- Dark Wizard
- Dark Knight
- Fairy Elf
- Magic Gladiator
- Dark Lord
- Summoner
- Rage Fighter

Advanced/evolution class display states must resolve back to the correct base character model rather than creating a second rendering architecture.

## Verified Player facts

### Equipment groups

- 7 Helm
- 8 Armor
- 9 Pants
- 10 Gloves
- 11 Boots
- 0–6 weapon groups
- 12 is a mixed item group; only the verified standard-wing IDs are treated as wings by Player

Never classify the whole of group 12 as wings.

### Attachment bones

- left weapon: 33
- right weapon: 42
- wings: 47

Attachment bone numbers are **BMD bone indices**, not renderer-local indices. If a renderer inserts a synthetic armature/root bone, BMD index identity must still be preserved separately.

Attachments use identity local position/rotation and scale 1. Do not invent a -90 degree attachment transform. Any coordinate-system conversion belongs at the scene/renderer boundary, not on weapon/wing attachment transforms.

### Character assembly

The body is one shared animated character skeleton.

- base torso provides the authoritative character skeleton
- base helm/pants/gloves/boots meshes are rebound to that skeleton
- equipped groups 7–11 replace the corresponding base body mesh and are rebound to the same skeleton
- weapons and standard wings remain independent model hierarchies attached to BMD bones 33/42/47

### Animation

- 24 FPS source timing
- clip timing follows `(frameCount - 1) / 24`
- the Player skeleton owns the selected character action
- attached items/wings maintain independent animation playback/lifetime
- changing Player action must not reset attachment animation
- a common speed control may change playback rates without recreating/resetting attachments

### Geometry

- first triangle winding: `0,2,1`
- quad second triangle: `0,2,3`
- preserve the fourth quad vertex
- raw BMD rotations are radians

## Texture facts

### TGA

- type 2 uncompressed true-color
- type 10 RLE true-color
- 24/32 bit
- vertical origin bit `0x20`
- horizontal/right origin bit `0x10`
- RLE packet count `(packet & 0x7f) + 1`

### OZJ

- JPEG marker starts at byte 16
- top-down flag byte 17

### OZT

- width at 16
- height at 18
- depth byte 20
- data offset 22
- BGRA, bottom-up
- Xulek expands to next power-of-two canvas

Do not invent OZB support without source/data evidence.

## Model and asset routing

`Data/Local/item.bmd` supplies item metadata. It does **not** contain the 3D model filename/path, so Player model routing is a separate source-backed responsibility.

For the base client Player pipeline:

- groups 7–11 resolve body models from the verified Player model banks and use `Data/Player` as the primary body-model namespace
- standard wings and normal item attachments resolve from the verified base-client item model map and use `Data/Item` as their primary namespace
- compatibility fallback is allowed only when it is explicit and reported by diagnostics

Do not silently derive model filenames from item display names.

## Server-specific extensions are excluded

`CustomWing`, `CUSTOM_WING_INFO`, `main.premium/main.free`, server custom-item tables and server-added wing/effect systems are **not part of the base MU Client Studio Player scope**.

The Studio should edit and render the real client data first. Server-specific extensions may only be considered later as isolated optional integrations if the project scope is explicitly expanded.

## Player architecture authority

The exact component/data-flow contract is defined in `docs/PLAYER_ARCHITECTURE.md`.

Any implementation that bypasses that architecture needs an explicit source-backed reason before it is merged.

## First functional milestone

`Open Client -> Player -> class -> base character -> equipment -> weapon/standard-wing attachments -> animation -> inspector -> refresh`

Only after this works correctly with representative real EX603 client Data should another editor module be considered.
