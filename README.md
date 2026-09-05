# MU Client Studio

Native Windows MU Online client editor.

## Stack

- C# / .NET 8
- WPF
- Visual Studio solution
- Windows x64
- Optional native C++ layer only when source-backed behavior requires it

## Current scope

Only **Player / Characters** is in scope until it is complete.

## Build

Open `MUClientStudio.sln` in Visual Studio 2022 and build the solution, or run:

```powershell
dotnet build MUClientStudio.sln -c Debug
```

Release publish:

```powershell
dotnet publish src/MUClientStudio.App/MUClientStudio.App.csproj -c Release -r win-x64 --self-contained true
```

See `docs/PROJECT_CONTEXT.md` for the product contract and source-backed Player rules.
