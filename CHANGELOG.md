# Changelog

## 0.4.1 (2026-09-27)
- Shorter menu options: **Relocate** and **Swap** (previously "Relocate a stall here" and "Swap stall with...").
  - If you already ran 0.4.0, your config file keeps the old text. Edit `Text.RelocateHere` / `Text.SwapWith`, or delete the config file to get the new defaults.
- Tested: relocating and swapping **yard** stalls.

## 0.4.0 (2026-09-27): first public version
- Relocate a built merchant stall to an empty stall location, or swap two built stalls.
- The new plot is repaired as it would be by a normal build, and the old plot returns to its original ruined state. Net town quality is unchanged.
- Moves keep the same merchant, shop progress and stall tier, and never re-grant build rewards.
- Disabled in co-op games.
- Config: `Enabled`, menu texts, `VerboseLogging` (off by default).

## Development builds (not released)
- 0.3.0: return the vacated plot to ruins (un-repair).
- 0.2.0: repair the destination plot like a vanilla build.
- 0.1.0: first working move/swap (the plot was left un-repaired).
