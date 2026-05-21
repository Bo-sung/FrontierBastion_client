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
        private const float TickSeconds = 0.05f;
        private const int MaxEventLines = 10;

        private readonly Queue<string> _eventLines = new Queue<string>();
        private readonly StringBuilder _guiBuilder = new StringBuilder(2048);

        private DebugBattleScenario _scenario;
        private BattleSimulator _simulator;
        private BattleState _lastState;
        private BattleResult _result;
        private bool _isPaused;
        private float _tickAccumulator;

        // Phase 1 client policy: local player = SideA, opponent = SideB.
        private const BattleSide LocalSide    = BattleSide.SideA;
        private const BattleSide OpponentSide = BattleSide.SideB;

        // Selection cursors: index into InitialState.SideA.Slots[] and Config.Lanes[].
        // Reset to 0 on every scenario load so selection is always valid.
        private int _selectedSlotCursor;
        private int _selectedLaneCursor;

        // World-space SpriteRenderer view — created at runtime, runs alongside IMGUI view.
        private DebugBattleWorldView _worldView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreateInDebugBuilds()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                return;
            }

            if (FindObjectOfType<DebugBattleRunner>() != null)
            {
                return;
            }

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
            {
                return;
            }

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
            GUILayout.BeginArea(new Rect(12, 12, 560, 700), GUI.skin.box);
            GUILayout.Label(BuildStatusText());
            GUILayout.EndArea();
            DebugBattleStageView.Draw(_scenario, _lastState, SelectedLaneId);
        }

        private void OnDrawGizmos()
        {
            if (_scenario == null || _lastState == null)
            {
                return;
            }

            for (int i = 0; i < _lastState.Lanes.Count; i++)
            {
                LaneState lane = _lastState.Lanes[i];
                long laneLength = GetLaneLength(lane.LaneId);
                float y = -1.5f * i;
                Vector3 start = new Vector3(-5f, y, 0f);
                Vector3 end = new Vector3(5f, y, 0f);

                Gizmos.color = lane.LaneId == SelectedLaneId ? Color.yellow : Color.gray;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireCube(start, Vector3.one * 0.2f);
                Gizmos.DrawWireCube(end, Vector3.one * 0.2f);

                foreach (BattleEntity entity in lane.Entities)
                {
                    float t = laneLength > 0 ? Mathf.Clamp01(entity.PositionMilli / (float)laneLength) : 0f;
                    Vector3 position = Vector3.Lerp(start, end, t);
                    Gizmos.color = entity.Side == BattleSide.SideA ? Color.cyan : Color.red;
                    Gizmos.DrawSphere(position, 0.14f);
                }
            }
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            // ── Scenario selection ──────────────────────────────────────────
            if (keyboard.f1Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeSideAVictory(), paused: false);
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeSideBVictory(), paused: false);
            }

            if (keyboard.f3Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateInteractiveSandbox(), paused: true);
            }

            if (keyboard.f4Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokeTimeoutSideBTiebreak(), paused: false);
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                ResetToScenario(_scenario ?? DebugBattleScenarioFactory.CreateSmokeSideAVictory(), paused: true);
            }

            // ── Tick controls ───────────────────────────────────────────────
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _isPaused = !_isPaused;
                AddEvent(_isPaused ? "Paused" : "Resumed");
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                RunOneTick();
            }

            // ── Slot / Lane selection ───────────────────────────────────────
            if (keyboard.qKey.wasPressedThisFrame)
            {
                CycleSlot(-1);
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                CycleSlot(+1);
            }

            if (keyboard.zKey.wasPressedThisFrame)
            {
                CycleLane(-1);
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                CycleLane(+1);
            }

            if (_simulator == null || _simulator.IsTerminated)
            {
                return;
            }

            // ── Battle commands (use selected slot/lane) ────────────────────
            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                int slotIdx = SelectedSlotIndex;
                string laneId = SelectedLaneId;

                if (!SlotExistsInScenario(slotIdx))
                {
                    AddEvent("SpawnDrone rejected: slot " + slotIdx + " not in scenario");
                }
                else if (!LaneExistsInConfig(laneId))
                {
                    AddEvent("SpawnDrone rejected: lane '" + laneId + "' not in scenario");
                }
                else
                {
                    SlotState slot1 = FindLastSlotState(slotIdx);
                    if (slot1 != null && slot1.DroneCooldownTick > 0)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " cooldown (" + slot1.DroneCooldownTick + " ticks)");
                    else if (slot1 != null && slot1.IsPilotDeployed)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " pilot deployed");
                    else if (_lastState != null && (GetLocalSideState()?.Energy ?? Fp.Zero) < FindSlotEnergyCost(slotIdx))
                        AddEvent("SpawnDrone rejected: energy " + (GetLocalSideState()?.Energy ?? Fp.Zero) + " < " + FindSlotEnergyCost(slotIdx));
                    else
                        TrySubmit(BattleCommand.SpawnDroneSquad(_simulator.CurrentTick, slotIdx, laneId, LocalSide));
                }
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                int slotIdx = SelectedSlotIndex;
                string laneId = SelectedLaneId;

                if (!SlotExistsInScenario(slotIdx))
                {
                    AddEvent("DeployPilot rejected: slot " + slotIdx + " not in scenario");
                }
                else if (!LaneExistsInConfig(laneId))
                {
                    AddEvent("DeployPilot rejected: lane '" + laneId + "' not in scenario");
                }
                else
                {
                    SlotState slot2 = FindLastSlotState(slotIdx);
                    if (slot2 != null && slot2.IsPilotDeployed)
                        AddEvent("DeployPilot rejected: slot " + slotIdx + " pilot already deployed");
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
                {
                    AddEvent("RecallPilot rejected: slot " + slotIdx + " not in scenario");
                }
                else
                {
                    SlotState slotR = FindLastSlotState(slotIdx);
                    if (slotR != null && !slotR.IsPilotDeployed)
                        AddEvent("RecallPilot rejected: slot " + slotIdx + " pilot not deployed");
                    else
                        TrySubmit(BattleCommand.RecallPilot(_simulator.CurrentTick, slotIdx, null, LocalSide));
                }
            }
        }

        private void ResetToScenario(DebugBattleScenario scenario, bool paused)
        {
            _scenario = scenario;
            _simulator = new BattleSimulator(scenario.Config, scenario.InitialState);
            _lastState = _simulator.GetState();
            _result = null;
            _isPaused = paused;
            _tickAccumulator = 0f;
            _selectedSlotCursor = 0;
            _selectedLaneCursor = 0;
            _eventLines.Clear();
            AddEvent("Loaded " + scenario.DisplayName + (paused ? " (paused)" : ""));
        }

        private void RunOneTick()
        {
            if (_simulator == null || _simulator.IsTerminated)
            {
                return;
            }

            SubmitFixtureCommandsForCurrentTick();

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
                bool matchesExpected = _scenario.MatchesExpected(_result);
                AddEvent("Result: " + FormatResult(_result) + " expected=" + matchesExpected);
                Debug.Log("[DebugBattleRunner] " + _scenario.DisplayName + " result: " + FormatResult(_result)
                    + " expected=" + matchesExpected);
            }
        }

        private void SubmitFixtureCommandsForCurrentTick()
        {
            IReadOnlyList<BattleCommand> commands = _scenario.GetFixtureCommandsForTick(_simulator.CurrentTick);
            for (int i = 0; i < commands.Count; i++)
            {
                TrySubmit(commands[i]);
            }
        }

        private void TrySubmit(BattleCommand command)
        {
            try
            {
                _simulator.SubmitCommand(command);
                AddEvent("Command: " + command.CommandType + " side=" + command.Side
                    + " slot=" + command.SlotIndex + " lane=" + command.LaneId + " tick=" + command.Tick);
            }
            catch (Exception ex)
            {
                AddEvent("Command rejected: " + command.CommandType + " - " + ex.Message);
            }
        }

        private string BuildStatusText()
        {
            _guiBuilder.Length = 0;
            _guiBuilder.AppendLine("Frontier Bastion - Debug Battle Runner");
            _guiBuilder.AppendLine("Scenario: " + (_scenario != null ? _scenario.DisplayName : "<none>"));
            _guiBuilder.AppendLine("Tick loop: 20 TPS / 1 tick = 0.05s / " + (_isPaused ? "Paused" : "Running"));
            _guiBuilder.AppendLine("Controls: F1 victory, F2 defeat, F3 interactive, F4 timeout, Backspace reset");
            _guiBuilder.AppendLine("Controls: Space pause, T step");
            _guiBuilder.AppendLine("Controls: Q/E cycle slot, Z/X cycle lane");
            _guiBuilder.AppendLine("Controls: 1 spawn drone, 2 deploy pilot, R recall  [uses selected slot/lane]");
            _guiBuilder.AppendLine();

            // Selected slot / lane summary
            int slotCount = _scenario?.InitialState?.SideA?.Slots?.Length ?? 0;
            int laneCount = _scenario?.Config?.Lanes?.Length ?? 0;
            _guiBuilder.AppendLine("Selected: slot=" + SelectedSlotIndex
                + " [" + (_selectedSlotCursor + 1) + "/" + slotCount + "]"
                + "  lane=" + SelectedLaneId
                + " [" + (_selectedLaneCursor + 1) + "/" + laneCount + "]");
            _guiBuilder.AppendLine();

            if (_lastState != null)
            {
                BattleSideState localState    = GetSideState(_lastState, LocalSide);
                BattleSideState opponentState = GetSideState(_lastState, OpponentSide);

                _guiBuilder.AppendLine("Tick: " + _lastState.CurrentTick);
                _guiBuilder.AppendLine("SideA Energy: " + (localState    != null ? localState.Energy.ToString()    : "-"));
                _guiBuilder.AppendLine("SideA Base HP: " + (localState   != null ? localState.BaseHp.ToString()    : "-"));
                _guiBuilder.AppendLine("SideB Base HP: " + (opponentState != null ? opponentState.BaseHp.ToString() : "-"));
                _guiBuilder.AppendLine("Terminated: " + _lastState.IsTerminated + " / " + _lastState.EndReason);

                if (localState != null)
                {
                    for (int i = 0; i < localState.Slots.Count; i++)
                    {
                        SlotState slot = localState.Slots[i];
                        _guiBuilder.AppendLine("Slot " + slot.SlotIndex
                            + (slot.SlotIndex == SelectedSlotIndex ? " [SEL]" : "")
                            + " drone_cd=" + slot.DroneCooldownTick
                            + " pilot_cd=" + slot.PilotCooldownTick
                            + " pilot_deployed=" + slot.IsPilotDeployed
                            + " pilot_ko=" + slot.IsPilotKnockedOut);
                    }
                }

                for (int i = 0; i < _lastState.Lanes.Count; i++)
                {
                    LaneState lane = _lastState.Lanes[i];
                    _guiBuilder.AppendLine("Lane " + lane.LaneId
                        + (lane.LaneId == SelectedLaneId ? " [SEL]" : "")
                        + " entities=" + lane.Entities.Count);
                    for (int j = 0; j < lane.Entities.Count; j++)
                    {
                        BattleEntity entity = lane.Entities[j];
                        _guiBuilder.AppendLine("  " + entity.EntityId
                            + " " + entity.Side
                            + " hp=" + entity.Hp
                            + " pos=" + entity.PositionMilli);
                    }
                }
            }

            if (_result != null)
            {
                _guiBuilder.AppendLine();
                _guiBuilder.AppendLine("Result: " + FormatResult(_result));
            }

            _guiBuilder.AppendLine();
            _guiBuilder.AppendLine("Events:");
            foreach (string line in _eventLines)
            {
                _guiBuilder.AppendLine("- " + line);
            }

            return _guiBuilder.ToString();
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
        /// LaneId of the currently selected lane, derived from <see cref="_selectedLaneCursor"/>.
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
                AddEvent("CycleSlot: no SideA slots in scenario");
                return;
            }
            int count = slots.Length;
            _selectedSlotCursor = (_selectedSlotCursor + direction + count) % count;
            AddEvent("Selected slot " + SelectedSlotIndex
                + " (" + (_selectedSlotCursor + 1) + "/" + count + ")");
        }

        private void CycleLane(int direction)
        {
            if (_scenario?.Config?.Lanes == null || _scenario.Config.Lanes.Length == 0)
            {
                AddEvent("CycleLane: no lanes in scenario");
                return;
            }
            int count = _scenario.Config.Lanes.Length;
            _selectedLaneCursor = (_selectedLaneCursor + direction + count) % count;
            AddEvent("Selected lane " + SelectedLaneId
                + " (" + (_selectedLaneCursor + 1) + "/" + count + ")");
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
            if (_scenario == null)
            {
                return 1L;
            }

            LaneDefinition[] lanes = _scenario.Config.Lanes;
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].LaneId == laneId)
                {
                    return lanes[i].LaneLengthMilli;
                }
            }

            return 1L;
        }

        private void AddEvent(string message)
        {
            if (_eventLines.Count >= MaxEventLines)
            {
                _eventLines.Dequeue();
            }

            _eventLines.Enqueue(message);
        }

        private static string FormatResult(BattleResult result)
        {
            return "winner=" + result.WinnerSide + " / " + result.EndReason
                + " clearTick=" + result.ClearTimeTick
                + " sideARatio=" + result.SideABaseHpRatio
                + " sideBRatio=" + result.SideBBaseHpRatio;
        }

        /// <summary>
        /// Returns the local-side (SideA) slot state for the given slotIndex from the last known
        /// state snapshot, or null if not found. Used for pre-validation before SubmitCommand.
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
        /// Returns the energy cost for the given slotIndex from the scenario's SideA slot definition,
        /// or Fp.Zero if not found.
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

        private BattleSideState GetLocalSideState()   => GetSideState(_lastState, LocalSide);
        private BattleSideState GetOpponentSideState() => GetSideState(_lastState, OpponentSide);
    }
}
