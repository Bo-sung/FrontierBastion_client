# uGUI Battle HUD Prefab Assembly Guide

This document outlines the detailed step-by-step instructions for assembling the Stage Battle uGUI HUD in the Unity Editor and connecting the C# MVP (Presenter + View) components.

---

## 1. GameObject Hierarchy Tree

Create the following hierarchy under your UI Canvas. The names must match the table below or be descriptively named for clarity.

```text
[Canvas] (Screen Space - Overlay)
 └── [BattleHUD] (Panel / RectTransform)
      ├── [TopPanel] (Horizontal Layout Group / Anchored Top-Stretch)
      │    ├── TickText (Text - "Tick: 0")
      │    ├── StatusText (Text - Status/Error Notifications)
      │    └── OpponentAutoText (Text - "AUTO OFF")
      │
      ├── [EnergyPanel] (Anchored Bottom-Left or Center)
      │    ├── SideAEnergyText (Text - "0.0")
      │    └── SideAEnergyBar (Slider - A's Energy Meter)
      │
      ├── [BaseHpPanel] (Anchored Top-Center / Stretch)
      │    ├── SideABaseHpBar (Slider - Player Base HP)
      │    └── SideBBaseHpBar (Slider - Opponent Base HP)
      │
      ├── [SelectionPanel] (Anchored Bottom-Center)
      │    ├── SelectedInfoText (Text - "Slot 1 / lane_ground_1")
      │    └── [LaneButtons]
      │         ├── LanePrevButton (Button - "<")
      │         └── LaneNextButton (Button - ">")
      │
      ├── [DeckPanel] (Horizontal Layout Group / Anchored Bottom-Stretch)
      │    ├── [Slot_0] (Button - Slot 1 Select)
      │    │    └── CooldownText_0 (Text - Cooldown / Cost overlay)
      │    ├── [Slot_1] (Button - Slot 2 Select)
      │    │    └── CooldownText_1 (Text - Cooldown / Cost overlay)
      │    ├── [Slot_2] (Button - Slot 3 Select)
      │    │    └── CooldownText_2 (Text - Cooldown / Cost overlay)
      │    └── [Slot_3] (Button - Slot 4 Select)
      │         └── CooldownText_3 (Text - Cooldown / Cost overlay)
      │
      ├── [ActionPanel] (Vertical/Horizontal / Anchored Bottom-Right)
      │    ├── SpawnButton (Button - "SPAWN DRONE")
      │    ├── DeployButton (Button - "DEPLOY PILOT")
      │    └── RecallButton (Button - "RECALL PILOT")
      │
      ├── [SystemPanel] (Vertical / Anchored Top-Right)
      │    ├── StartButton (Button - "START BATTLE")
      │    ├── PauseButton (Button - "PAUSE")
      │    ├── StepButton (Button - "STEP")
      │    └── AutoButton (Button - "AUTO AI")
      │
      └── [ResultPanel] (Panel / Full Screen Overlay / Initially Inactive)
           └── ResultText (Text - "VICTORY" or "DEFEAT")
```

---

## 2. Recommended RectTransform Settings

| GameObject Name | Anchor Presets | Pivot | Position / Size | Description |
| :--- | :--- | :--- | :--- | :--- |
| **BattleHUD** | Center-Stretch (Alt+Shift+Click) | (0.5, 0.5) | Rect = (0, 0, 0, 0) | Outer HUD Container panel |
| **TopPanel** | Top-Stretch | (0.5, 1.0) | PosY = -20, Height = 40 | Holds tick number, system warnings |
| **BaseHpPanel** | Top-Center | (0.5, 1.0) | PosY = -80, Width = 600, Height = 30 | Base HP Sliders comparing A vs B |
| **DeckPanel** | Bottom-Center | (0.5, 0.0) | PosY = 40, Width = 640, Height = 100 | Horizontal grid of 4 slot buttons |
| **ActionPanel** | Bottom-Right | (1.0, 0.0) | PosX = -40, PosY = 40, Width = 180, Height = 150 | Command dispatch panel |
| **SelectionPanel**| Bottom-Center | (0.5, 0.0) | PosY = 160, Width = 300, Height = 40 | Lane and slot selection display |
| **ResultPanel** | Whole-Stretch | (0.5, 0.5) | Rect = (0, 0, 0, 0) | Semi-transparent black overlays |

---

## 3. Component Setup & Bindings

### A. StageBattlePresenter Component
Attach `StageBattlePresenter` to a persistent GameObject in the scene (or the `AppRoot` instance which automatically attaches it during bootstrap).

*   **Battle Manager**: Assign the active scene's `StageBattleManager`. *(If left empty, will automatically auto-resolve using `AppRoot.Instance` or scene fallback).*
*   **View**: Assign the active scene's Canvas GameObject containing the `StageBattleHudView` component.

### B. StageBattleHudView Component
Attach `StageBattleHudView` to the `BattleHUD` panel GameObject under the Canvas. Wire the fields in the inspector 1:1 as follows:

| Inspector Field | Target GameObject / Component | Notes |
| :--- | :--- | :--- |
| **Presenter** | `StageBattlePresenter` component | Auto-resolves if left empty |
| **Tick Text** | `TickText` (Text) | Shows tick counter |
| **Status Text** | `StatusText` (Text) | Warning/error overlay text |
| **Side A Energy Bar** | `SideAEnergyBar` (Slider) | Max value should be set to 1.0 |
| **Side A Energy Text** | `SideAEnergyText` (Text) | Shows raw energy value |
| **Side A Base Hp Bar** | `SideABaseHpBar` (Slider) | Displays Player side base HP ratio |
| **Side B Base Hp Bar** | `SideBBaseHpBar` (Slider) | Displays Opponent side base HP ratio |
| **Opponent Auto Text** | `OpponentAutoText` (Text) | Toggles between "AUTO ON"/"AUTO OFF" |
| **Selected Info Text** | `SelectedInfoText` (Text) | Format: "Slot N / Lane_ID" |
| **Slot Select Buttons** | Array size 4: `Slot_0`, `Slot_1`, `Slot_2`, `Slot_3` | **Maintain 0 to 3 order** |
| **Slot Cooldown Texts**| Array size 4: `CooldownText_0`, `CooldownText_1`, ... | **Maintain 0 to 3 order** |
| **Spawn Button** | `SpawnButton` (Button) | Command: Spawn Drone Squad |
| **Deploy Button** | `DeployButton` (Button) | Command: Deploy Pilot |
| **Recall Button** | `RecallButton` (Button) | Command: Recall Pilot |
| **Lane Prev Button** | `LanePrevButton` (Button) | Cycle Lane Left ("<") |
| **Lane Next Button** | `LaneNextButton` (Button) | Cycle Lane Right (">") |
| **Pause Button** | `PauseButton` (Button) | Pause / Resume toggle |
| **Step Button** | `StepButton` (Button) | Manual Frame step |
| **Auto Button** | `AutoButton` (Button) | Opponent AI Auto toggle |
| **Start Button** | `StartButton` (Button) | Initialize / Restart Sandbox |
| **Result Panel** | `ResultPanel` (GameObject) | Set Active when battle is finished |
| **Result Text** | `ResultText` (Text) | Displays "VICTORY" / "DEFEAT" |

---

## 4. Test and Verification Procedure

Verify the MVP wiring by performing the following sequence in Play Mode:

1.  **Start Battle**: Click `START BATTLE` (or press `F6`). A new sandbox stage battle session should initialize, updating `TickText` and rendering the 3-lane visual board.
2.  **Lane Navigation**: Click the `<` or `>` buttons (or press `Q` / `E`). The `SelectedInfoText` should update, cycling the selected lane index.
3.  **Slot Selection**: Click Slot Select Buttons `1` through `4` (or press `1`..`4` on the keyboard). The selected button should highlight with a **Cyan color block** and update the selection text.
4.  **Issue Commands**:
    *   Click `SPAWN DRONE` (or press `W`). A drone squad should deploy in the selected lane.
    *   Click `DEPLOY PILOT` (or press `R`). The pilot should appear. If successful, the Slot Cooldown text updates to `PILOT`.
    *   If you attempt to invoke commands too quickly, the `StatusText` will flash with yellow rate-limit messages (`Same-tick command rejection`) or red error messages, decaying after 4 seconds.
5.  **Simulation Controls**:
    *   Press `Space` (or click `PAUSE`). The status indicator changes to "PAUSED" and the world freezes.
    *   Click `STEP` (or press `F8`) to advance the simulation exactly one tick.
    *   Click `AUTO AI` (or press `A`) to toggle the Side B bot's autonomous behavior.
6.  **Battle End**:
    *   Once a base HP bar reaches `0`, the `ResultPanel` should pop up displaying `VICTORY` in Green or `DEFEAT` in Red.
