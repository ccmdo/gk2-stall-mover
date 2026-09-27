# How Stall Mover works

This file is for anyone reading or extending the code. It's based on Graveyard Keeper 2 on Unity 6000.3.9f1 (Mono) with BepInEx 5.4.23.5, verified in-game in September 2026.

## The game's stall model

- **A stall location** starts as a signboard world object (`WgoData`) whose definition has `interactionType == TownBuildingPlace`, for example `repair_sign_1208`.
  - The signboard carries a `TownBuildingWgoComponent`.
  - That component holds the location's **layout**, a `TownBuildingSceneConfiguration`:
    - route points for the merchant (`gdPointTent`, `gdPointHomeOutside`, `gdPointHomeInside`);
    - for each tier, where to spawn the tent, sign, yard and decorations.
- **Building a stall** is the craft `town_building_craft:<defId>`. Its end expressions (built in `GameBalance`) are `CreateTownBuilding()` followed by the stall definition's own `execute_on_building_finished` list. For every vendor stall that list is:
  ```
  RepairTownCluster() ; AddRep("village_REP", 2) ; AddInspiration("insp_build_shop", 1) ;
  AchTriggerCountable("market_stall_built", 1) ; LockTownBuilding("<id>")
  ```
- `TownSystem.CreateTownBuildingOnWgo`:
  - spawns the tent and pieces using the location's layout;
  - moves the `TownBuildingWgoComponent` onto the **tent**;
  - spawns the merchant character and links the two (`tent.LinkedFromTownBuildingUniqueId` ↔ `character.LinkedToTownBuildingUniqueId`);
  - **deletes the signboard**.
- **The merchant's routes** are looked up on demand through the linked tent's layout (`Flow_GetTownBuildingData`). So re-linking a merchant to a new tent is enough to give it the new routes.
- **Shop progress** (`VendorSystem.vendors`) is keyed by vendor id, not by location.
- **Stall families:** a stall fits a location if its tier-1 definition's `craftsIn` contains the location's signboard id.
- **`RepairTownCluster()`** runs `TownClusterRepairWgoComponent.DoRepairLogic()` on the signboard. It:
  - removes the "destroyed" (ruin) objects;
  - removes or house-repairs the ruined static objects;
  - spawns the "repaired" objects.

  It is **not idempotent**: running it twice creates duplicate objects.

## Finding things

- `Town.Scan(sceneId)` walks the save's world objects to find empty vendor signboards and built vendor tents, with their merchants.
- A built stall's location is identified by its layout's `gdPointTent`.
- `ContentIndex` reads the game's **original world content**:
  - it goes `GameSceneConfig.contentDataRefs` → `SceneWgoContentPart.Wgos/Wsos`, the same data the game's own save-fixer (`AddWgoFromContentOperation`) uses;
  - it maps `gdPointTent` to the signboard id;
  - it recreates original objects with `CreateDataFromMe(sceneGlobalPosition, sceneId, copySGuid: true)`.
- Copies are deep-cloned where the game's copy constructor would otherwise share the component objects with the content template.

## Move (stall at A → empty location B)

1. **Checks:** single-player; the stall fits B; no upgrade in progress; A's original signboard exists in content.
2. Remove A's current-tier pieces and tent.
3. Give B's signboard component the stall's building id and tier. Spawn the pieces at B the same way `CreateTownBuildingOnWgo` does.
4. Re-link the **same** merchant and copy `available_by_time`. Teleport the merchant to B's `gdPointTent`.
5. Run B's `DoRepairLogic()` (the only build side effect the mod replays). Remove B's signboard.
6. Restore A's original signboard from content, with its **original** repair config. Then **un-repair** A:
   - remove the objects the repair spawned (matched by id and exact position);
   - restore the removed ruin objects from content (original UniqueIds);
   - for house-repair clusters, `ResetAllStages()` and take back the town quality that was added.

Not replayed: reputation, inspiration, the achievement counter, `LockTownBuilding`, and the merchant's `OnCreate` event.

## Swap (A ↔ B)

1. Remove both stalls' pieces.
2. Exchange the building id and tier between the two location components.
3. Respawn each stall using the other location's layout, re-link both merchants and teleport them.

Both plots were already repaired, so plot state doesn't change.

## UI

- **Empty location:** a Harmony prefix on `TownBuildingPlaceInteractionHandler.Interact` shows the game's own `Bubble.ShowMultiAnswer` with *Relocate a stall here*, `hint_build` and `common_leave`. Choosing *Build* runs the original handler.
- **Merchant dialogue:** it's graph-driven (FlowCanvas `Flow_MultiAnswer` → `Bubble.ShowMultiAnswer`). A prefix inserts *Swap stall with...* before the leave answer. When chosen, the mod first calls the graph's own callback with the leave id, so the dialogue ends normally, and then opens the picker.
- The move itself runs behind `UIFade`, with player control taken for the duration.
