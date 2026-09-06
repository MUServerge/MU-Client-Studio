# Gate.bmd format

`Gate.bmd` is a client/world table stored at `Data/Gate.bmd`; it is not part of `Data/Local`.

## Source-backed contract

Main5.2/GameServer exports a fixed array of `MAX_GATES = 1024` `GATE_ATTRIBUTE` records using `PackFileEncrypt(..., Key=0, WriteMax=false, CheckSum=false)`.

The native record is 14 bytes:

- offset 0: `Flag` (`BYTE`)
- offset 1: `Map` (`BYTE`)
- offset 2: `X1` (`BYTE`)
- offset 3: `Y1` (`BYTE`)
- offset 4: `X2` (`BYTE`)
- offset 5: `Y2` (`BYTE`)
- offsets 6-7: `TargetGate` (`WORD`)
- offset 8: `Angle` (`BYTE`)
- offset 9: native alignment/padding byte
- offsets 10-11: `MinLevel` (`WORD`)
- offsets 12-13: `MaxLevel` (`WORD`)

Container rules:

- 1024 records
- 14 bytes per record
- 14,336 bytes total
- no count header
- no checksum trailer
- `FC CF AB` BUX transform resets for each record
- padding/unknown bytes must be preserved during round-trip editing

## Studio direction

`GateBmdCodec` and `GateBmdDocument` provide the shared read/write layer. The eventual editor belongs under a World / Maps / Gates module and should integrate with map metadata and move requirements rather than becoming an isolated raw table editor.
