# Stall Mover — a Graveyard Keeper 2 mod

Move a built merchant stall to another stall location in town, or swap two stalls, without losing the merchant or their shop progress.

In the base game, once a stall such as the Baker is built, it stays on that plot for good. This mod lets you rearrange the market.

## Features

- **Relocate a stall.** At an **empty** stall location, choose **Relocate**, then pick one of your built stalls. The stall moves to that spot.
- **Swap two stalls.** Talk to a built stall's merchant, choose **Swap**, then pick another built stall. The two stalls trade places.
- **What moves with the stall:**
  - the same merchant, with their shop level, stock and orders;
  - the stall's upgrade tier.
- **Plots behave like vanilla:**
  - The **new** plot is tidied and repaired exactly as it would be by building there normally.
  - The **old** plot returns to its original ruined state with an empty signboard, ready to build on again.
- **Town quality stays the same.** The repair bonus moves with the stall.
- **No free rewards.** Moving never re-grants build rewards (reputation, inspiration, achievement progress).
- The game's normal stall rules still apply. "House" stalls fit house plots and "yard" stalls fit yard plots, and the lists only offer compatible stalls.

## Requirements

- Graveyard Keeper 2 (Windows, Steam).
- [BepInEx 5.4.23.5 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). The game has to be launched once after installing BepInEx.

This mod is standalone. It does **not** require GK2 Mod Framework, but it works alongside it.

## Installation

1. Install BepInEx 5.4.23.5 x64 into the game folder and run the game once.
2. Download `GK2.StallMover-<version>.zip` from [Releases](../../releases).
3. Extract it into the game folder so that you end up with:
   ```
   Graveyard Keeper 2\BepInEx\plugins\GK2.StallMover.dll
   ```
4. Launch the game. `BepInEx\LogOutput.log` should contain `Stall Mover <version> loaded`.

**Back up your saves before first use.** They are in `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`. Each save slot is a `.dat` file plus an `.info` file.

## Usage

| Where | What you'll see |
|---|---|
| Interacting with an **empty** stall location, when a compatible stall is built elsewhere | **Relocate** / Build / Leave |
| Talking to a **built stall's merchant**, when a compatible stall to swap with exists | **Swap** added before *Leave* |

The screen fades briefly while the stall is moved.

A stall can't be moved while it's being upgraded. The options are also hidden in **co-op** games, because the mod is single-player only.

## Configuration

`BepInEx\config\steven.gk2.stallmover.cfg` is created on first launch.

| Setting | Default | Purpose |
|---|---|---|
| `General.Enabled` | `true` | Turn the mod's options on or off. |
| `Text.RelocateHere` | `Relocate` | Menu text. You can change it, for example to translate it. |
| `Text.SwapWith` | `Swap` | Menu text. |
| `Text.Cancel` | `Cancel` | Menu text. |
| `Debug.VerboseLogging` | `false` | Detailed diagnostics in the BepInEx log. |

## Known limitations

- **Single-player only.** The options are disabled in co-op.
- **Tested so far:**
  - relocating and swapping **house** and **yard** stalls at **tier 1**;
  - plot repair and un-repair;
  - save and reload.
- **Not yet tested in-game:** upgraded (tier 2/3) stalls. They use the same code path.
- **The menu text is English only.** You can change it in the config file.
- **Edge cases the mod doesn't handle:**
  - if two plots share the same ruin objects, un-repairing one could affect the other;
  - items that ruin objects start with in a new game are not recreated.
- Game updates may break the mod. Every operation is wrapped in error handling and writes errors to the BepInEx log.

## How it works

See [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md) for the technical details: which game systems are involved, and how moves, swaps and plot repair are done.

## Building from source

Requirements: the .NET SDK (6 or later), plus the game with BepInEx installed.

```
cd src/GK2.StallMover
dotnet build -c Release
```

The project references the game's DLLs straight from your install, so no game files are included in this repository. If the game isn't in the default Steam folder, pass its location:

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Graveyard Keeper 2"
```

The output is `src/GK2.StallMover/bin/Release/GK2.StallMover.dll`.

## Credits and disclaimer

Made by Steven Hamilton, with development assistance from Claude (Anthropic).

This is an unofficial fan mod. It is not affiliated with or endorsed by Lazy Bear Games or the game's publisher. Graveyard Keeper 2 and its assets belong to their respective owners. This repository contains only original mod code and no game files or decompiled game code.

Licensed under the [MIT License](LICENSE).
