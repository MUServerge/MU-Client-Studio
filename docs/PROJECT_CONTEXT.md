# MU Client Studio — Project Contract

## Product

MU Client Studio is a native Windows desktop editor for MU Online client data.

This repository is the clean rewrite of the abandoned Electron/Next.js version.

## Stack

- C# / .NET 8
- WPF
- Visual Studio solution (`MUClientStudio.sln`)
- Windows x64
- Optional C++ native project/DLL only where technically justified

## Scope lock

Only **Player / Characters** may be developed until Player is complete.

Do not add placeholder modules for Items, Skills, Gates, Warp, Maps, etc.

## UI contract

Visual reference: **MU Project OS — V6 Compact**.

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

1. `xulek/muonline-bmd-viewer` — primary Player implementation reference
2. Main_EX603 — legacy target-client validation
3. Main5.2 — cross-check only

Policy:

- source-backed exactness
- do not invent BMD semantics
- reuse proven MU logic
- no approximations when source behavior is available

## Verified Player facts

### Equipment groups

- 7 Helm
- 8 Armor
- 9 Pants
- 10 Gloves
- 11 Boots
- 0–6 weapons
- 12 wings

### Attachment bones

- left weapon: 33
- right weapon: 42
- wings: 47

Attachments use identity local position/rotation and scale 1. Do not invent a -90 degree transform.

### Animation

- 24 FPS
- clip timing follows `(frameCount - 1) / 24`
- attached items/wings maintain independent animation phase
- changing Player action must not reset attachment animation

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

## Model routing

Groups 7–11: Player-first, then Item, then fallback.

Other groups including wings: Item-first, then Player, then fallback.

## Custom wings

Main_EX603 proves custom wing behavior is configuration-driven. `ModelType`, glow behavior, static effects and dynamic effects must never be inferred from model names alone.

## First functional milestone

`Open Client -> Player -> class -> base character -> equipment -> weapon/wing attachments -> animation -> inspector -> refresh`

Only after this works correctly with representative real client Data should another module be considered.
