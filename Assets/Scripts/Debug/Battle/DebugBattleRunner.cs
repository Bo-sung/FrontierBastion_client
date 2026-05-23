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
    /// <summary>
    /// Debug battle runner — tick loop, input, scenario management.
    ///
    /// Responsibilities:
    ///   • Owns the simulation loop (BattleSimulator, tick accumulator).
    ///   • Handles keyboard input and dispatches BattleCommands.
    ///   • Drives SideB auto controller.
    ///   • Feeds read-only context to <see cref="DebugBattleStatusTextBuilder"/>
    ///     for IMGUI display.
    ///   • Drives <see cref="DebugBattleWorldView"/> and
    ///     <see cref="DebugBattleStageView"/> for visual output.
    ///
    /// Status text formatting is fully delegated to
    /// <see cref="DebugBattleStatusTextBuilder"/>; this class does not build
    /// display strings directly.
    /// </summary>
    public sealed class DebugBattleRunner : MonoBehaviour
    {
        private const float TickSeconds   = 0.05f;
        private const int   MaxEventLines = 12;

        private readonly Queue<string> _eventLines = new Queue<string>();

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

        // Same-tick submitted-command tracking.
        // Prevents duplicate re-submission after a failed AdvanceTick (Core does not
        // clear _pendingCommands when it throws, so stale commands survive to the next
        // attempt).  Reset whenever the tick number changes.
        private int                    _submittedCommandTick  = -1;
        private readonly List<BattleCommand> _submittedCommandsThisTick = new List<BattleCommand>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreateInDebugBuilds()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return;

            if (FindFirstObjectByType<DebugBattleRunner>() != null)
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
            if (_worldView != null)
                _worldView.Render(_scenario, _lastState, SelectedLaneId);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 560, 860), GUI.skin.box);
            GUILayout.Label(DebugBattleStatusTextBuilder.Build(BuildStatusContext()));
            GUILayout.EndArea();
            DebugBattleStageView.Draw(_scenario, _lastState, SelectedLaneId);
        }

        private void OnDrawGizmos()
        {
            if (_scenario == null || _lastState == null) return;

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

        // ------------------------------------------------------------------ context

        /// <summary>
        /// Assembles the read-only <see cref="DebugBattleStatusContext"/> passed to
        /// <see cref="DebugBattleStatusTextBuilder"/>. Called once per <c>OnGUI</c> frame.
        /// </summary>
        private DebugBattleStatusContext BuildStatusContext()
        {
            return new DebugBattleStatusContext
            {
                Scenario           = _scenario,
                LastState          = _lastState,
                Result             = _result,
                IsPaused           = _isPaused,
                SelectedSlotIndex  = SelectedSlotIndex,
                SelectedSlotCursor = _selectedSlotCursor,
                SelectedLaneId     = SelectedLaneId,
                SelectedLaneCursor = _selectedLaneCursor,
                SideBAutoEnabled   = _sideBAutoController?.IsEnabled ?? false,
                SideBAutoAvailable = _sideBAutoController != null,
                EventLines         = _eventLines,
            };
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
                    SlotState ss = FindLastSlotState(slotIdx);
                    if (ss != null && ss.DroneCooldownTick > 0)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " cooldown (" + ss.DroneCooldownTick + ")");
                    else if (ss != null && ss.IsPilotDeployed)
                        AddEvent("SpawnDrone rejected: slot " + slotIdx + " pilot deployed");
                    else if (_lastState != null && (GetLocalSideState()?.Energy ?? Fp.Zero) < FindSlotEnergyCost(slotIdx))
                        AddEvent("SpawnDrone rejected: E="
                            + DebugBattleStatusTextBuilder.FpDisplay(GetLocalSideState()?.Energy ?? Fp.Zero)
                            + " < cost=" + DebugBattleStatusTextBuilder.FpInt(FindSlotEnergyCost(slotIdx)));
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
                    SlotState ss = FindLastSlotState(slotIdx);
                    if (ss != null && ss.IsPilotDeployed)
                        AddEvent("DeployPilot rejected: slot " + slotIdx + " already deployed");
                    else if (ss != null && ss.IsPilotKnockedOut)
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
                    SlotState ss = FindLastSlotState(slotIdx);
                    if (ss != null && !ss.IsPilotDeployed)
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

            _submittedCommandsThisTick.Clear();
            _submittedCommandTick = -1;

            _eventLines.Clear();
            AddEvent("Loaded: " + scenario.DisplayName + (paused ? " [paused]" : ""));
            AddEvent("SideB Auto: " + (_sideBAutoController.IsEnabled ? "ON" : "OFF"));
        }

        // ------------------------------------------------------------------ tick

        private void RunOneTick()
        {
            if (_simulator == null || _simulator.IsTerminated) return;

            ResetSubmittedCommandsIfTickChanged();
            SubmitFixtureCommandsForCurrentTick();
            SubmitSideBAutoCommands();

            try
            {
                _simulator.AdvanceTick();
                _lastState = _simulator.GetState();
            }
            catch (Exception ex)
            {
                Debug.LogError("[DebugBattle] AdvanceTick failed.\n" + BuildFailureDump(ex));
                AddEvent("AdvanceTick failed: " + ex.Message + " (dump in Console)");
                _isPaused = true;
                return;
            }

            if (_simulator.IsTerminated)
            {
                _result = _simulator.GetResult();
                bool ok = _scenario.MatchesExpected(_result);
                string resultStr = DebugBattleStatusTextBuilder.FormatResult(_result);
                AddEvent("Result: " + resultStr + "  expected=" + ok);
                Debug.Log("[DebugBattleRunner] " + _scenario.DisplayName
                    + " result: " + resultStr + " expected=" + ok);
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

            // Duplicate guard: if any command for the same side+slot was already submitted
            // this tick (e.g. from a previous failed AdvanceTick whose stale commands
            // remain in Core's queue), skip re-submission.
            ResetSubmittedCommandsIfTickChanged();
            if (HasSubmittedSameTickSlotCommand(cmd))
            {
                AddEvent("Auto-B skipped: pending same-tick SideB slot=" + cmd.SlotIndex);
                return;
            }

            try
            {
                _simulator.SubmitCommand(cmd);
                RecordSubmittedCommand(cmd);
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
            ResetSubmittedCommandsIfTickChanged();

            // Same-tick slot guard — applies to both SideA manual input and fixture commands.
            // Any pending command on the same side+slot blocks further commands this tick
            // because _lastState does not reflect pending effects; Core will fail on the
            // second operation targeting the same slot.
            if (HasSubmittedSameTickSlotCommand(command))
            {
                AddEvent("Cmd skipped: pending same-tick " + command.Side + " slot=" + command.SlotIndex);
                return;
            }

            try
            {
                _simulator.SubmitCommand(command);
                RecordSubmittedCommand(command);
                AddEvent("Cmd " + command.Side + " " + command.CommandType
                    + " slot=" + command.SlotIndex + " lane=" + command.LaneId);
            }
            catch (Exception ex)
            {
                AddEvent("Cmd rejected: " + command.CommandType + " - " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ submitted-command tracking

        /// <summary>
        /// Clears the submitted-command list when the simulator tick has advanced past
        /// the tick we last recorded.  Safe to call multiple times per tick — it is a
        /// no-op when the tick has not changed.
        /// </summary>
        private void ResetSubmittedCommandsIfTickChanged()
        {
            if (_simulator == null) return;
            int currentTick = _simulator.CurrentTick;
            if (currentTick == _submittedCommandTick) return;
            _submittedCommandsThisTick.Clear();
            _submittedCommandTick = currentTick;
        }

        /// <summary>Records that <paramref name="cmd"/> was successfully submitted this tick.</summary>
        private void RecordSubmittedCommand(BattleCommand cmd)
        {
            _submittedCommandsThisTick.Add(cmd);
        }

        /// <summary>
        /// Returns true if any command for the same <c>Side</c> and <c>SlotIndex</c>
        /// was already submitted for the current tick.
        ///
        /// <c>CommandType</c> is intentionally excluded from the comparison.
        /// <c>_lastState</c> does not reflect pending-command effects, so Core's slot
        /// state after the first pending command is unknown here.  Any second command
        /// targeting the same slot in the same tick risks a cooldown / pilot-state
        /// violation inside <c>AdvanceTick</c>.  Blocking the entire slot is the safe
        /// and simple policy.
        ///
        /// A <c>SlotIndex</c> &lt; 0 (e.g. a RecallPilot with no lane) is not guarded —
        /// RecallPilot carries the pilot's current lane internally and is safe to re-try.
        /// </summary>
        private bool HasSubmittedSameTickSlotCommand(BattleCommand cmd)
        {
            if (cmd.SlotIndex < 0) return false;
            for (int i = 0; i < _submittedCommandsThisTick.Count; i++)
            {
                BattleCommand existing = _submittedCommandsThisTick[i];
                if (existing.Side      == cmd.Side
                 && existing.SlotIndex == cmd.SlotIndex)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ failure dump

        /// <summary>
        /// Builds a copy-friendly plain-text diagnostic block written to
        /// <c>Debug.LogError</c> when <c>AdvanceTick</c> throws.
        /// Includes scenario metadata, tick, submitted commands, slot states,
        /// lane entity counts, recent events, and the full exception.
        /// </summary>
        private string BuildFailureDump(Exception ex)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("=== DebugBattle AdvanceTick Failure Dump ===");
            sb.AppendLine("Scenario    : " + (_scenario != null ? _scenario.DisplayName + " [" + _scenario.ScenarioId + "]" : "<none>"));
            sb.AppendLine("Tick        : " + (_simulator != null ? _simulator.CurrentTick.ToString() : "?"));
            sb.AppendLine("IsPaused    : " + _isPaused);
            sb.AppendLine("SideBAuto   : " + (_sideBAutoController != null ? _sideBAutoController.IsEnabled.ToString() : "n/a"));
            sb.AppendLine();

            // ── Submitted commands this tick ──────────────────────────────────
            sb.AppendLine("-- Submitted this tick (" + _submittedCommandsThisTick.Count + ") --");
            if (_submittedCommandsThisTick.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                for (int i = 0; i < _submittedCommandsThisTick.Count; i++)
                {
                    BattleCommand c = _submittedCommandsThisTick[i];
                    sb.AppendLine("  [" + i + "] " + c.Side + " " + c.CommandType
                        + " slot=" + c.SlotIndex + " lane=" + c.LaneId);
                }
            }
            sb.AppendLine();

            // ── Fixture commands this tick ────────────────────────────────────
            if (_scenario != null && _simulator != null)
            {
                IReadOnlyList<BattleCommand> fixture =
                    _scenario.GetFixtureCommandsForTick(_simulator.CurrentTick);
                sb.AppendLine("-- Fixture commands this tick (" + fixture.Count + ") --");
                if (fixture.Count == 0)
                {
                    sb.AppendLine("  (none)");
                }
                else
                {
                    for (int i = 0; i < fixture.Count; i++)
                    {
                        BattleCommand c = fixture[i];
                        sb.AppendLine("  [" + i + "] " + c.Side + " " + c.CommandType
                            + " slot=" + c.SlotIndex + " lane=" + c.LaneId);
                    }
                }
                sb.AppendLine();
            }

            // ── Side states ───────────────────────────────────────────────────
            if (_lastState != null && _scenario != null)
            {
                AppendSideDump(sb, _lastState, BattleSide.SideA,
                    _scenario.InitialState?.SideA?.Slots);
                AppendSideDump(sb, _lastState, BattleSide.SideB,
                    _scenario.InitialState?.SideB?.Slots);
                AppendLaneDump(sb, _lastState);
            }
            else
            {
                sb.AppendLine("-- LastState: null --");
                sb.AppendLine();
            }

            // ── Recent events ─────────────────────────────────────────────────
            sb.AppendLine("-- Recent events --");
            foreach (string line in _eventLines)
                sb.AppendLine("  " + line);
            sb.AppendLine();

            // ── Exception ─────────────────────────────────────────────────────
            sb.AppendLine("-- Exception --");
            sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine("=== End Dump ===");

            return sb.ToString();
        }

        private void AppendSideDump(StringBuilder sb, BattleState state, BattleSide side, SlotDefinition[] defs)
        {
            BattleSideState sideState = GetSideState(state, side);
            string label = side == BattleSide.SideA ? "SideA" : "SideB";
            sb.AppendLine("-- " + label + " state --");
            if (sideState == null)
            {
                sb.AppendLine("  (null)");
                sb.AppendLine();
                return;
            }
            sb.AppendLine("  Energy   : " + DebugBattleStatusTextBuilder.FpDisplay(sideState.Energy));
            sb.AppendLine("  BaseHP   : " + DebugBattleStatusTextBuilder.FpDisplay(sideState.BaseHp));
            sb.AppendLine("  Slots    : " + sideState.Slots.Count);
            for (int i = 0; i < sideState.Slots.Count; i++)
            {
                SlotState ss  = sideState.Slots[i];
                string    name = "(unknown)";
                if (defs != null)
                {
                    for (int d = 0; d < defs.Length; d++)
                        if (defs[d].SlotIndex == ss.SlotIndex)
                        { name = DebugBattleStatusTextBuilder.GetSlotRoleName(defs[d].PilotId); break; }
                }
                sb.AppendLine("    [" + ss.SlotIndex + "] " + name
                    + "  drone_cd=" + ss.DroneCooldownTick
                    + "  pilot=" + (ss.IsPilotDeployed ? "ON" : ss.IsPilotKnockedOut ? "KO" : "off")
                    + (ss.PilotCooldownTick > 0 ? "  pcd=" + ss.PilotCooldownTick : ""));
            }
            sb.AppendLine();
        }

        private void AppendLaneDump(StringBuilder sb, BattleState state)
        {
            sb.AppendLine("-- Lanes --");
            if (state?.Lanes == null)
            {
                sb.AppendLine("  (null)");
                sb.AppendLine();
                return;
            }
            for (int i = 0; i < state.Lanes.Count; i++)
            {
                LaneState lane = state.Lanes[i];
                sb.Append("  " + lane.LaneId + "  entities=" + lane.Entities.Count);
                int a = 0, b = 0;
                for (int j = 0; j < lane.Entities.Count; j++)
                {
                    if (lane.Entities[j].Side == BattleSide.SideA) a++;
                    else b++;
                }
                sb.AppendLine("  (A=" + a + " B=" + b + ")");
            }
            sb.AppendLine();
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
                + " [" + DebugBattleStatusTextBuilder.GetSlotRoleName(
                    _scenario.InitialState.SideA.Slots[_selectedSlotCursor].PilotId) + "]"
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

        /// <summary>
        /// Returns the local-side (SideA) slot state for <paramref name="slotIndex"/>
        /// from the last known state snapshot. Used for command pre-validation.
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
        /// Returns the energy cost for <paramref name="slotIndex"/> from the scenario's
        /// SideA slot definition, or <c>Fp.Zero</c> if not found.
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

        private BattleSideState GetLocalSideState() => GetSideState(_lastState, LocalSide);
    }
}
