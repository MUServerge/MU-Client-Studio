# MU Client Studio — Client Editor Architecture

## Product goal

MU Client Studio is a native Windows editor for the MU client data set. Player / Characters is the first completion gate, not the final scope of the product.

The Studio must eventually replace the fragmented workflow of legacy utilities such as PentiumTools with one coherent editor shell, one asset workspace, shared codecs, shared diagnostics, and source-backed file semantics.

## Reference hierarchy

For target behavior and file semantics:

1. Main_EX603 — primary target semantics where the source contains the relevant subsystem.
2. Actual opened client files — authoritative evidence for the concrete client being edited.
3. Xulek BMD viewer — implementation / rendering reference for 3D BMD handling.
4. Main5.2 — source cross-check and format reference when Main_EX603 does not contain the original subsystem implementation.
5. PentiumTools — functional/editor-coverage reference only; its compiled binary is not the binary-format authority.

No editor may invent a layout merely because a file uses the `.bmd` extension.

## Critical format boundary

MU uses `.bmd` for more than one unrelated binary family. They must remain separate in code and UI.

### 1. Model BMD

Examples:

- `Data/Player/Player.bmd`
- `Data/Player/ArmorClass01.bmd`
- `Data/Item/Sword01.bmd`

Contains 3D meshes, UVs, skeletons, bones, actions and animation keys.

Namespace target:

`MUClientStudio.Core.Formats.ModelBmd`

### 2. Local/table BMD

Examples:

- `Data/Local/Item.bmd`
- `Data/Local/ItemAddOption.bmd`
- `Data/Local/ItemSetType.bmd`
- `Data/Local/Eng/Skill_eng.bmd`

Contains packed tables/configuration/text rather than 3D geometry.

Namespace target:

`MUClientStudio.Core.Formats.LocalBmd`

Every Local BMD receives its own verified typed codec on top of shared container primitives.

## Shared Local BMD foundation

The Local editor layer owns reusable primitives rather than duplicating crypt/checksum code in every editor:

- `BuxCodec` — repeating `FC CF AB` symmetric byte transform.
- `MuChecksum2` — original `GenerateCheckSum2` compatible checksum.
- `FixedRecordLocalTableCodec` — fixed-size records + BUX + trailing checksum container.
- typed codecs such as `ItemBmdCodec` — field layout, validation and round-trip writing.

Unknown/reserved bytes must be preserved where possible so opening and saving a file does not destroy data outside the currently understood schema.

## Verified Item.bmd contract

The supplied EX603 client `Local/Item.bmd` is not the previously assumed variable-size/model-path table.

Verified layout:

- 8192 (`0x2000`) records
- 84 bytes per record
- 688,128-byte encrypted payload
- 4-byte checksum trailer
- total file size 688,132 bytes
- BUX transform is reset for each 84-byte record
- checksum key: `0xE2F1`
- group = `flatIndex / 512`
- id = `flatIndex % 512`

The 84-byte record matches the EX603 `ITEM_ATTRIBUTE` layout: name, two-hand flag, level, inventory slot, skill, dimensions, combat values, requirements, class restrictions and resistances.

`Item.bmd` does **not** contain the Player/Item model filename. Model routing must therefore be a separate client-model catalog/resolver backed by client source and actual asset files.

## Local.zip inventory and editor targets

The supplied Local data proves the following editor families are relevant.

### Core tables

- Item
- Item Add Option
- Item Set Type
- Item Struct
- Macro Config
- Map Characters
- Master Skill Tree Data
- Mix
- Monster Skill
- NPC Dialogue
- Pet
- Pet Data
- Quest Progress
- Server List
- Credit
- Filter / Filter Name

### Locale-specific tables

English, Portuguese and Spanish folders contain variants of:

- Buff Effect
- Dialog
- Item
- Item Level Tooltip
- Item Set Option
- Item Tooltip
- Item Tooltip Text
- Jewel of Harmony Option
- Jewel of Harmony Smelt
- Master Skill Tooltip
- Move Requirement
- Quest
- Quest Words
- Skill
- Slide
- Socket Item
- Text

The locale is part of the document identity. Editors must not silently mix records from different locale directories.

### Local visual assets

`ImgsMapName` and locale roots include OZT map-name textures. They belong to the same client workspace but use the texture/image pipeline rather than Local-table parsing.

## Editor module contract

Each future editor module should expose the same lifecycle:

`Open -> Decode -> Validate -> Present -> Edit -> Validate -> Save/Save As -> Verify round-trip`

Required behaviors:

- typed fields rather than raw hex for verified schema;
- raw/offset inspector for diagnostics;
- preserve unknown bytes;
- explicit dirty state;
- undo/redo boundary at document level;
- automatic backup before in-place save;
- save to temporary file then atomic replacement where possible;
- post-save reopen/validation;
- source file path and locale always visible;
- no hidden conversion to a different client version.

## Application shell

The V6 Compact shell remains the common workspace. Modules plug into the same shell instead of becoming separate executables.

Long-term navigation groups:

- Player / Characters
- 3D BMD
- Items
- Skills
- Sets / Options
- Harmony
- Quests / Dialog
- Pets
- Mix
- Local Text / Locale
- Textures
- Diagnostics

Navigation entries must be enabled only when the corresponding editor is implemented and verified.

## Player relationship

Player stays the first completion gate because it exercises the widest shared surface:

- Model BMD reader
- texture decoding
- skeleton and animation
- item catalog
- equipment model routing
- attachments
- asset lookup
- renderer

However, Player must consume the shared client-data layer rather than embedding format logic in the UI or renderer. Fixes made for Item/Local BMD must therefore benefit future Item and Local editors directly.

## Current correction

The earlier Studio `ItemBmdReader` treated `Local/Item.bmd` as a different modern structure containing model folder/name strings. That assumption is invalid for the supplied EX603 client and must not remain architectural authority.

The correct direction is:

`Local/Item.bmd -> ItemBmdCodec -> typed item metadata`

and independently:

`group/id -> PlayerItemModelResolver -> actual Data/Player or Data/Item BMD`

This separation is mandatory for both Player fidelity and the future Item editor.
