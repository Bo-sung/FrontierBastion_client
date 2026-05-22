using System;
using System.Collections.Generic;
using System.Text;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.Simulation;
using BattleSim.Core.State;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrontierBastion.Client.DebugBattle
{
    public sealed class DebugBattleRunner : MonoBehaviour
    {
        private const float TickSeconds   = 0.05f;
        private const int   MaxEventLines = 12;

        private readonly Queue<string>  _eventLines = new Queue<string>();
        private readonly StringBuilder  _guiBuilder = new StringBuilder(4096);

        private DebugBattleScenario _scenario;
        private BattleSimulator     _simulator;
        private BattleState         _lastState;
        private BattleResult        _result;
        private bool                _isPaused;
        private float               _tickAccumulator;

        // Phase 1 client policy: local player = SideA, opponent = SideB.
        private const BattleSide LocalSide    = BattleSide.SideA;
        private const BattleSide OpponentSide = BattleSide.SideB;

        // Selection cursors: index into InitialState.SideA.Slots[] and Config.Lanes[].
        // Reset to 0 on every scenario load so selection is always valid.
        private int _selectedSlotCursor;
        private int _selectedLaneCursor;

        // World-space SpriteRenderer view — created at runtime, runs alongside IMGUI view.
        private DebugBattleWorldView _worldView;

        // SideB auto controller — active by default in F3 Interactive Sandbox.
        private DebugBattleSideBAutoController _sideBAutoController;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreateInDebugBuilds()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return;

            if (FindObjectOfType<DebugBattleRunner>() != null)
                return;

            GameObject runnerObject = new GameObject("Debug Battle Runner");
            runnerObject.AddComponent<DebugBattleRunner>();
            DontDestroyOnLoad(runnerObject);
        }

        private void Awake()
        {
            _worldView = DebugBattleWorldView.GetOrCreate(gameObject);
            ResetToScenario(DebugBattleScenarioFactory.CreateSmokeSideAVictory(), paused: false);
        }

        private void Update()
        {
            HandleInput();

            if (_simulator == null || _simulator.IsTerminated || _isPaused)
                return;

            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= TickSeconds && !_simulator.IsTerminated)
            {
                RunOneTick();
                _tickAccumulator -= TickSeconds;
            }
        }

        private void LateUpdate()
        {
            // Refresh world-space SpriteRenderer view every frame after tick processing.
            if (_worldView != null)
                _worldView.Render(_scenario, _lastState, SelectedLaneId);
        }

        private void OnGUI()
        {
            // Panel height increased to accommodate 4-slot SideA + SideB status.
            GUILayout.BeginArea(new Rect(12, 12, 560, 860), GUI.skin.box);
            GUILayout.Label(BuildStatusText());
            GUILayout.EndArea();
            DebugBattleStageView.Draw(_scenario, _lastState, SelectedLaneId);
        }

        private void OnDrawGizmos()
        {
            if (_scenario == null || _lastState == null)
                return;

            for (int i = 0; i < _lastState.Lanes.Count; i++)
            {
                LaneState lane       = _lastState.Lanes[i];
                long      laneLength = GetLaneLength(lane.LaneId);
                float     y          = -1.5f * i;
                Vector3   start      = new Vector3(-5f, y, 0f);
                Vector3   end        = new Vector3(5f, y, 0f);

                Gizmos.color = lane.LaneId == SelectedLaneId ? Color.yellow : Color.gray;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireCube(start, Vector3.one * 0.2f);
                Gizmos.DrawWireCube(end, Vector3.one * 0.2f);

                foreach (BattleEntity entity in lane.Entities)
                {
                    float   t        = laneLength > 0 ? Mathf.Clamp01(entity.PositionMilli / (float)laneLength) : 0f;
                    Vector3 position = Vector3.Lerp(start, end, t);
                    Gizmos.color = entity.Side == BattleSide.SideA ? Color.cyan : Color.red;
                    Gizmos.DrawSphere(position, 0.14f);
                }
            }
        }

        // ------------------------------------------------------------------ input

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // ── Scenario selection ──────────────────────────────────────────
            if (keyboard.f1Key.wasPressedThisFrame)
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeSideAVictory(), paused: false);

            if (keyboard.f2Key.wasPressedThisFrame)
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeSideBVictory(), paused: false);

            if (keyboard.f3Key.wasPressedThisFrame)
                ResetToScenario(DebugBattleScenarioFactory.CreateInteractiveSandbox(), paused: true);

            if (keyboard.f4Key.wasPressedThisFrame)
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeTimeoutSideBTiebreak(), paused: false);

            if (keyboard.backspaceKey.wasPressedThisFrame)
                ResetToScenario(_scenario ?? DebugBattleScenarioFactory.CreateSmokeSideAVictory(), paused: true);

            // ── Tick controls ───────────────────────────────────────────────
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _isPaused = !_isPaused;
                AddEvent(_isPaused ? "Paused" : "Resumed");
            }

            if (keyboard.tKey.wasPressedThisFrame)
                RunOneTick();

            // ── Slot / Lane selection ───────────────────────────────────────
            if (keyboard.qKey.wasPressedThisFrame) CycleSlot(-1);
            if (keyboard.eKey.wasPressedThisFrame) CycleSlot(+1);
            if (keyboard.zKey.wasPressedThisFrame) CycleLane(-1);
            if (keyboard.xKey.wasPressedThisFrame) CycleLane(+1);

            if (_simulator == null || _simulator.IsTerminated) return;

            // ── Battle commands (use selected slot/lane) ────────────────────
            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                int    slotIdx = SelectedSlotIndex;
                string laneId  = SelectedLaneId;

                if (!SlotExistsInScenario(slotIdx))
                    AddEvent("SpawnDrone rejected: slot " + slotIdx + " not in scenario");
                else if (!LaneExistsInConfig(laneId))
                    AddEvent("SpawnDrone rejected: lane '" + laneId + "' not in scenario");
                else
                {
                    SlotState slot1 = FindLastSlotState(slotIdx);
                    if (slot1 != null && slot1.DroneCooldownTick > 0)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " cooldown (" + slot1.DroneCooldownTick + ")");
                    else if (slot1 != null && slot1.IsPilotDeployed)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " pilot deployed");
                    else if (_lastState != null && (GetLocalSideState()?.Energy ?? Fp.Zero) < FindSlotEnergyCost(slotIdx))
                        AddEvent("SpawnDrone rejected: E=" + FpDisplay(GetLocalSideState()?.Energy ?? Fp.Zero)
                            + " < cost=" + FpInt(FindSlotEnergyCost(slotIdx)));
                    else
                        TrySubmit(BattleCommand.SpawnDroneSquad(_simulator.CurrentTick, slotIdx, laneId, LocalSide));
                }
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                int    slotIdx = SelectedSlotIndex;
                string laneId  = SelectedLaneId;

                if (!SlotExistsInScenario(slotIdx))
                    AddEvent("DeployPilot rejected: slot " + slotIdx + " not in scenario");
                else if (!LaneExistsInConfig(laneId))
                    AddEvent("DeployPilot rejected: lane '" + laneId + "' not in scenario");
                else
                {
                    SlotState slot2 = FindLastSlotState(slotIdx);
                    if (slot2 != null && slot2.IsPilotDeployed)
                        AddEvent("DeployPilot rejected: slot " + slotIdx + " already deployed");
                    else if (slot2 != null && slot2.IsPilotKnockedOut)
                        AddEvent("DeployPilot rejected: slot " + slotIdx + " pilot KO");
                    else
                        TrySubmit(BattleCommand.DeployPilot(_simulator.CurrentTick, slotIdx, laneId, LocalSide));
                }
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                int slotIdx = SelectedSlotIndex;

                if (!SlotExistsInScenario(slotIdx))
                    AddEvent("RecallPilot rejected: slot " + slotIdx + " not in scenario");
                else
                {
                    SlotState slotR = FindLastSlotState(slotIdx);
                    if (slotR != null && !slotR.IsPilotDeployed)
                        AddEvent("RecallPilot rejected: slot " + slotIdx + " pilot not deployed");
                    else
                        TrySubmit(BattleCommand.RecallPilot(_simulator.CurrentTick, slotIdx, null, LocalSide));
                }
            }

            // ── SideB Auto toggle ────────────────────────────────────────────
            if (keyboard.aKey.wasPressedThisFrame && _sideBAutoController != null)
            {
                _sideBAutoController.Toggle();
                AddEvent("SideB Auto: " + (_sideBAutoController.IsEnabled ? "ON" : "OFF"));
            }
        }

        // ------------------------------------------------------------------ scenario management

        private void ResetToScenario(DebugBattleScenario scenario, bool paused)
        {
            _scenario        = scenario;
            _simulator       = new BattleSimulator(scenario.Config, scenario.InitialState);
            _lastState       = _simulator.GetState();
            _result          = null;
            _isPaused        = paused;
            _tickAccumulator = 0f;
            _selectedSlotCursor = 0;
            _selectedLaneCursor = 0;

            // SideB auto: on by default for Interactive Sandbox, off for smoke scenarios.
            bool isSandbox = scenario != null && scenario.ScenarioId == "interactive_sandbox";
            if (_sideBAutoController == null)
                _sideBAutoController = new DebugBattleSideBAutoController(isSandbox);
            else
                _sideBAutoController.SetEnabled(isSandbox);

            _eventLines.Clear();
            AddEvent("Loaded: " + scenario.DisplayName + (paused ? " [paused]" : ""));
            AddEvent("SideB Auto: " + (_sideBAutoController.IsEnabled ? "ON" : "OFF"));
        }

        // ------------------------------------------------------------------ tick

        private void RunOneTick()
        {
            if (_simulator == null || _simulator.IsTerminated) return;

            SubmitFixtureCommandsForCurrentTick();
            SubmitSideBAutoCommands();

            try
            {
                _simulator.AdvanceTick();
                _lastState = _simulator.GetState();
            }
            catch (Exception ex)
            {
                AddEvent("AdvanceTick failed: " + ex.Message);
                _isPaused = true;
                return;
            }

            if (_simulator.IsTerminated)
            {
                _result = _simulator.GetResult();
                bool ok = _scenario.MatchesExpected(_result);
                AddEvent("Result: " + FormatResult(_result) + "  expected=" + ok);
                Debug.Log("[DebugBattleRunner] " + _scenario.DisplayName
                    + " result: " + FormatResult(_result) + " expected=" + ok);
            }
        }

        private void SubmitFixtureCommandsForCurrentTick()
        {
            IReadOnlyList<BattleCommand> cmds = _scenario.GetFixtureCommandsForTick(_simulator.CurrentTick);
            for (int i = 0; i < cmds.Count; i++)
                TrySubmit(cmds[i]);
        }

        private void SubmitSideBAutoCommands()
        {
            if (_sideBAutoController == null || _lastState == null) return;

            // Defensive guard: skip auto if a fixture SideB command is already queued this tick.
            if (HasFixtureSideBCommandThisTick()) return;

            BattleCommand cmd = _sideBAutoController.Evaluate(
                _lastState, _scenario, _simulator.CurrentTick);
            if (cmd == null) return;
            try
            {
                _simulator.SubmitCommand(cmd);
                AddEvent("Auto-B " + cmd.CommandType + " slot=" + cmd.SlotIndex + " lane=" + cmd.LaneId);
            }
            catch (Exception ex)
            {
                AddEvent("Auto-B rejected: " + ex.Message);
            }
        }

        /// <summary>
        /// Returns true if the current tick's fixture commands include at least one
        /// SideB command. Prevents auto-controller double-submit.
        /// </summary>
        private bool HasFixtureSideBCommandThisTick()
        {
            IReadOnlyList<BattleCommand> cmds =
                _scenario.GetFixtureCommandsForTick(_simulator.CurrentTick);
            for (int i = 0; i < cmds.Count; i++)
                if (cmds[i].Side == BattleSide.SideB) return true;
            return false;
        }

        private void TrySubmit(BattleCommand command)
        {
            try
            {
                _simulator.SubmitCommand(command);
                AddEvent("Cmd " + command.Side + " " + command.CommandType
                    + " slot=" + command.SlotIndex + " lane=" + command.LaneId);
            }
            catch (Exception ex)
            {
                AddEvent("Cmd rejected: " + command.CommandType + " - " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ IMGUI status

        private string BuildStatusText()
        {
            _guiBuilder.Length = 0;

            // ── Header ──────────────────────────────────────────────────────
            _guiBuilder.AppendLine("── Frontier Bastion  Debug Battle ─────────────────");
            _guiBuilder.AppendLine("Scenario : " + (_scenario != null ? _scenario.DisplayName : "<none>"));

            if (_lastState != null)
            {
                string tickLine = "Tick     : " + _lastState.CurrentTick
                    + "   " + (_isPaused ? "[PAUSED]" : "[running]");
                if (_lastState.IsTerminated)
                    tickLine += "   TERMINATED / " + _lastState.EndReason;
                _guiBuilder.AppendLine(tickLine);
            }
            else
            {
                _guiBuilder.AppendLine("           " + (_isPaused ? "[PAUSED]" : "[running]"));
            }
            _guiBuilder.AppendLine();

            // ── Controls ────────────────────────────────────────────────────
            _guiBuilder.AppendLine("── Controls ────────────────────────────────────────");
            _guiBuilder.AppendLine(" F1 SideA Victory  F2 SideB Victory  F3 Interactive  F4 Timeout");
            _guiBuilder.AppendLine(" Backspace : reset (paused)");
            _guiBuilder.AppendLine(" Space : pause/resume   T : manual step");
            _guiBuilder.AppendLine(" Q / E : prev / next slot   Z / X : prev / next lane");
            _guiBuilder.AppendLine(" 1 SpawnDrone   2 DeployPilot   R Recall  [selected slot+lane]");
            _guiBuilder.AppendLine(" A : SideB Auto ON/OFF  [F3 default: ON]");
            _guiBuilder.AppendLine();

            if (_lastState != null)
            {
                BattleSideState sideAState = GetSideState(_lastState, BattleSide.SideA);
                BattleSideState sideBState = GetSideState(_lastState, BattleSide.SideB);
                SlotDefinition[] sideADefs = _scenario?.InitialState?.SideA?.Slots;
                SlotDefinition[] sideBDefs = _scenario?.InitialState?.SideB?.Slots;

                // ── SideA ────────────────────────────────────────────────────
                _guiBuilder.Append("── SideA (player)");
                if (sideAState != null)
                {
                    Fp maxE = _scenario?.Config?.SideA?.MaxEnergy ?? Fp.Zero;
                    _guiBuilder.Append("  E:" + FpDisplay(sideAState.Energy) + "/" + FpInt(maxE));
                    _guiBuilder.Append("  BaseHP:" + FpDisplay(sideAState.BaseHp));
                }
                _guiBuilder.AppendLine(" ─────────────────────");

                // Column header
                _guiBuilder.AppendLine("    [#] Role       Cost  Cooldown  Pilot");

                if (sideAState != null)
                {
                    for (int i = 0; i < sideAState.Slots.Count; i++)
                    {
                        SlotState      ss  = sideAState.Slots[i];
                        SlotDefinition def = FindSlotDef(sideADefs, ss.SlotIndex);
                        bool           sel = ss.SlotIndex == SelectedSlotIndex;
                        AppendSlotLine(_guiBuilder, ss, def, sel);
                    }
                }
                _guiBuilder.AppendLine();

                // ── Lanes ─────────────────────────────────────────────────────
                int laneCount = _scenario?.Config?.Lanes?.Length ?? 0;
                _guiBuilder.AppendLine(
                    "── Lanes [Z/X]  (" + (_selectedLaneCursor + 1) + "/" + laneCount + ") ──────────────────────");

                for (int i = 0; i < _lastState.Lanes.Count; i++)
                {
                    LaneState lane    = _lastState.Lanes[i];
                    bool      laneSel = lane.LaneId == SelectedLaneId;
                    _guiBuilder.Append(laneSel ? " >> " : "    ");
                    _guiBuilder.Append(lane.LaneId);
                    if (laneSel) _guiBuilder.Append(" [SEL]");
                    _guiBuilder.AppendLine("  entities=" + lane.Entities.Count);

                    for (int j = 0; j < lane.Entities.Count; j++)
                    {
                        BattleEntity e = lane.Entities[j];
                        _guiBuilder.AppendLine("      " + e.EntityId
                            + "  " + (e.Side == BattleSide.SideA ? "A" : "B")
                            + "  hp=" + FpInt(e.Hp)
                            + "  pos=" + e.PositionMilli);
                    }
                }
                _guiBuilder.AppendLine();

                // ── SideB ────────────────────────────────────────────────────
                string autoTag = _sideBAutoController != null
                    ? (_sideBAutoController.IsEnabled ? "Auto:ON" : "Auto:OFF")
                    : "Auto:n/a";
                _guiBuilder.Append("── SideB (opponent)  " + autoTag);
                if (sideBState != null)
                {
                    Fp maxE = _scenario?.Config?.SideB?.MaxEnergy ?? Fp.Zero;
                    _guiBuilder.Append("  E:" + FpDisplay(sideBState.Energy) + "/" + FpInt(maxE));
                    _guiBuilder.Append("  BaseHP:" + FpDisplay(sideBState.BaseHp));
                }
                _guiBuilder.AppendLine(" ──────────────────");

                // Column header
                _guiBuilder.AppendLine("    [#] Role       Cost  Cooldown  Pilot");

                if (sideBState != null)
                {
                    for (int i = 0; i < sideBState.Slots.Count; i++)
                    {
                        SlotState      ss  = sideBState.Slots[i];
                        SlotDefinition def = FindSlotDef(sideBDefs, ss.SlotIndex);
                        AppendSlotLine(_guiBuilder, ss, def, false);
                    }
                }
            }

            // ── Result ──────────────────────────────────────────────────────
            if (_result != null)
            {
                _guiBuilder.AppendLine();
                _guiBuilder.AppendLine("── Result ──────────────────────────────────────────");
                _guiBuilder.AppendLine("  " + FormatResult(_result));
            }

            // ── Events ──────────────────────────────────────────────────────
            _guiBuilder.AppendLine();
            _guiBuilder.AppendLine("── Events ──────────────────────────────────────────");
            foreach (string line in _eventLines)
                _guiBuilder.AppendLine("  " + line);

            return _guiBuilder.ToString();
        }

        // ------------------------------------------------------------------ slot display helpers

        /// <summary>
        /// Appends one slot status line.
        /// Format:  " >> [0] Tank       E:15  cd:  0  pilot:ON"
        /// Only SideA slots show the ">>" selection marker (<paramref name="isSelected"/>).
        /// Display-only — reads SlotState and SlotDefinition, never writes simulation state.
        /// </summary>
        private static void AppendSlotLine(
            StringBuilder  sb,
            SlotState      ss,
            SlotDefinition def,
            bool           isSelected)
        {
            sb.Append(isSelected ? " >> " : "    ");
            sb.Append("[" + ss.SlotIndex + "] ");

            string name = def != null ? GetSlotRoleName(def.PilotId) : ("Slot" + ss.SlotIndex);
            sb.Append(name.PadRight(10));

            if (def != null)
            {
                sb.Append("  E:");
                sb.Append(FpInt(def.EnergyCost).ToString().PadLeft(2));
            }
            else
            {
                sb.Append("  E: -");
            }

            sb.Append("  cd:");
            sb.Append(ss.DroneCooldownTick.ToString().PadLeft(3));

            if (ss.IsPilotDeployed)
                sb.Append("  pilot:ON");
            else if (ss.IsPilotKnockedOut)
                sb.Append("  pilot:KO");
            else if (ss.PilotCooldownTick > 0)
                sb.Append("  pcd:" + ss.PilotCooldownTick.ToString().PadLeft(3));

            sb.AppendLine();
        }

        /// <summary>
        /// Derives a human-readable role name from a pilot id string.
        /// Extracts the last underscore-delimited token and capitalises it.
        /// Examples: "pilot_a_tank" → "Tank", "pilot_b_bruiser" → "Bruiser",
        ///           "pilot_high" → "High", "placeholder" → "Placeholder".
        /// Display-only.
        /// </summary>
        private static string GetSlotRoleName(string pilotId)
        {
            if (string.IsNullOrEmpty(pilotId)) return "Unknown";
            int lastUnderscore = pilotId.LastIndexOf('_');
            string token = (lastUnderscore >= 0 && lastUnderscore < pilotId.Length - 1)
                ? pilotId.Substring(lastUnderscore + 1)
                : pilotId;
            if (token.Length == 0) return pilotId;
            return char.ToUpper(token[0]) + (token.Length > 1 ? token.Substring(1) : string.Empty);
        }

        /// <summary>Finds the SlotDefinition for <paramref name="slotIndex"/> in <paramref name="defs"/>.</summary>
        private static SlotDefinition FindSlotDef(SlotDefinition[] defs, int slotIndex)
        {
            if (defs == null) return null;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].SlotIndex == slotIndex) return defs[i];
            return null;
        }

        /// <summary>
        /// Returns Fp as a "INT.D" string (1 decimal digit).
        /// E.g. energy 28.5 → "28.5", HP 300 → "300.0".
        /// Display-only — uses Fp.Raw and Fp.Scale directly.
        /// </summary>
        private static string FpDisplay(Fp fp)
        {
            if (fp.Raw <= 0L) return "0";
            long intPart  = fp.Raw / Fp.Scale;
            long fracPart = (fp.Raw % Fp.Scale) / (Fp.Scale / 10); // single decimal digit
            return intPart + "." + fracPart;
        }

        /// <summary>Returns the integer (floor) part of a Fp value. Display-only.</summary>
        private static int FpInt(Fp fp)
        {
            if (fp.Raw <= 0L) return 0;
            return (int)(fp.Raw / Fp.Scale);
        }

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// SlotIndex of the currently selected local-side (SideA) slot.
        /// Returns 0 if no slots are available.
        /// </summary>
        private int SelectedSlotIndex
        {
            get
            {
                SlotDefinition[] slots = _scenario?.InitialState?.SideA?.Slots;
                if (slots == null || slots.Length == 0) return 0;
                int cursor = Mathf.Clamp(_selectedSlotCursor, 0, slots.Length - 1);
                return slots[cursor].SlotIndex;
            }
        }

        /// <summary>
        /// LaneId of the currently selected lane.
        /// Returns empty string if no lanes are available.
        /// </summary>
        private string SelectedLaneId
        {
            get
            {
                if (_scenario?.Config?.Lanes == null || _scenario.Config.Lanes.Length == 0)
                    return string.Empty;
                int cursor = Mathf.Clamp(_selectedLaneCursor, 0, _scenario.Config.Lanes.Length - 1);
                return _scenario.Config.Lanes[cursor].LaneId;
            }
        }

        private void CycleSlot(int direction)
        {
            SlotDefinition[] slots = _scenario?.InitialState?.SideA?.Slots;
            if (slots == null || slots.Length == 0)
            {
                AddEvent("CycleSlot: no SideA slots");
                return;
            }
            int count = slots.Length;
            _selectedSlotCursor = (_selectedSlotCursor + direction + count) % count;
            AddEvent("Slot >> " + SelectedSlotIndex
                + " [" + GetSlotRoleName(_scenario.InitialState.SideA.Slots[_selectedSlotCursor].PilotId) + "]"
                + "  (" + (_selectedSlotCursor + 1) + "/" + count + ")");
        }

        private void CycleLane(int direction)
        {
            if (_scenario?.Config?.Lanes == null || _scenario.Config.Lanes.Length == 0)
            {
                AddEvent("CycleLane: no lanes");
                return;
            }
            int count = _scenario.Config.Lanes.Length;
            _selectedLaneCursor = (_selectedLaneCursor + direction + count) % count;
            AddEvent("Lane >> " + SelectedLaneId
                + "  (" + (_selectedLaneCursor + 1) + "/" + count + ")");
        }

        // ------------------------------------------------------------------ validation

        private bool SlotExistsInScenario(int slotIndex)
        {
            SlotDefinition[] slots = _scenario?.InitialState?.SideA?.Slots;
            if (slots == null) return false;
            foreach (SlotDefinition def in slots)
                if (def.SlotIndex == slotIndex) return true;
            return false;
        }

        private bool LaneExistsInConfig(string laneId)
        {
            if (string.IsNullOrEmpty(laneId) || _scenario?.Config?.Lanes == null)
                return false;
            foreach (LaneDefinition lane in _scenario.Config.Lanes)
                if (lane.LaneId == laneId) return true;
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private long GetLaneLength(string laneId)
        {
            if (_scenario == null) return 1L;
            LaneDefinition[] lanes = _scenario.Config.Lanes;
            for (int i = 0; i < lanes.Length; i++)
                if (lanes[i].LaneId == laneId) return lanes[i].LaneLengthMilli;
            return 1L;
        }

        private void AddEvent(string message)
        {
            if (_eventLines.Count >= MaxEventLines)
                _eventLines.Dequeue();
            _eventLines.Enqueue(message);
        }

        private static string FormatResult(BattleResult result)
        {
            return "winner=" + result.WinnerSide
                + " / " + result.EndReason
                + "  tick=" + result.ClearTimeTick
                + "  A=" + FpDisplay(result.SideABaseHpRatio)
                + "  B=" + FpDisplay(result.SideBBaseHpRatio);
        }

        /// <summary>
        /// Returns the local-side (SideA) slot state for <paramref name="slotIndex"/>
        /// from the last known state snapshot. Used for pre-validation before SubmitCommand.
        /// </summary>
        private SlotState FindLastSlotState(int slotIndex)
        {
            BattleSideState localState = GetSideState(_lastState, LocalSide);
            if (localState == null) return null;
            for (int i = 0; i < localState.Slots.Count; i++)
                if (localState.Slots[i].SlotIndex == slotIndex)
                    return localState.Slots[i];
            return null;
        }

        /// <summary>
        /// Returns the energy cost for the given slotIndex from the scenario's SideA slot
        /// definition, or Fp.Zero if not found.
        /// </summary>
        private Fp FindSlotEnergyCost(int slotIndex)
        {
            SlotDefinition[] slots = _scenario?.InitialState?.SideA?.Slots;
            if (slots == null) return Fp.Zero;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].SlotIndex == slotIndex)
                    return slots[i].EnergyCost;
            return Fp.Zero;
        }

        // ── Side state helpers ────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="BattleSideState"/> for <paramref name="side"/> from
        /// <paramref name="state"/>, or null if not found.
        /// Read-only — never used for combat judgment.
        /// </summary>
        private static BattleSideState GetSideState(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return null;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i];
            return null;
        }

        private BattleSideState GetLocalSideState()    => GetSideState(_lastState, LocalSide);
        private BattleSideState GetOpponentSideState() => GetSideState(_lastState, OpponentSide);
    }
}
