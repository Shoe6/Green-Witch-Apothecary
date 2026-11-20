# The Green Witch's Apothecary (Phase 1)

**Studio:** Leaf & Logic Studios  
**Target Platform:** Android (Google Play)  
**Tech Stack:** Unity 3D (URP), Firebase (Auth, Realtime Database, Cloud Functions), AdMob, Google Play Billing, Figma

## Repository Layout

- `firebase/` — Firebase Realtime Database rules (`database.rules.json`), `nosql_schemas.json` (7 NoSQL collections seed), `reagents.json`, `recipes.json`, and Cloud Functions (`functions/index.js`) for AFK Epoch timestamp growth and workstation `completion_timestamp` preservation math.
- `unity-client/Assets/Scripts/` — Core Sprint 1 C# scripts:
  - `GameStateManager.cs` — 6:00 AM to 12:00 AM day/night cycle, Midnight Pass-Out (10:00 AM wake-up + stamina penalty + bedside note), and zero-cost well drink protection.
  - `HomesteadGridManager.cs` — 3x3 open-concept homestead grid generator with micro-climate lighting and soil pH metadata.
  - `SwipePanCameraController.cs` — Mobile touch swipe-to-pan camera controller with Editor mouse fallback.
  - `VirtualJoystick.cs` — Touchscreen virtual joystick for Third-Person movement.
  - `WellInteraction.cs` — Zero-stamina well interaction hook.
  - `ReagentModels.cs` & `FirebaseDatabaseManager.cs` — C# schemas and RTDB sync hooks for the 7 NoSQL collections (`users`, `world_state`, `inventories`, `garden_plots`, `workstations`, `companions`, `economy_and_karma`) including AFK `completion_timestamp` preservation logic.
- `figma-wireframes/` — Diegetic UI layouts (Grandmother's Tome, Radial Tool Wheel, Rucksack, Guild Ad Flyer).
