using System;
using System.Collections.Generic;
using System.Text;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.Simulation;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Stage-level battle session.  Owns the BattleSimulator, the SideB opponent
    /// AI, the 20 TPS tick accumulator, and the same-tick command guard.
    ///
    /// Pure C# — no MonoBehaviour, no UnityEngine dependency.
    /// Not thread-safe; call all methods from the same thread.
    ///
    /// Typical usage from a MonoBehaviour:
    /// <code>
    ///   // Create:
    ///   _session = new StageBattleSession(stage, sideADeck);
    ///
    ///   // Time-based update (pass Unity's Time.deltaTime):
    ///   _session.Tick(Time.deltaTime);
    ///
    ///   // Or manual single-step:
    ///   _session.ManualStep();
    ///
    ///   // Player command:
    ///   string err = _session.SubmitSpawnDroneSquad(slotIndex, laneId);
    ///   if (err != null) { /* handle rejection */ }
    /// </code>
    ///
    /// Side assignment:
    ///   Local player → SideA (<see cref="BattleSide.SideA"/>)
    ///   AI opponent  → SideB (<see cref="BattleSide.SideB"/>)
    /// </summary>
    public sealed class StageBattleSession
    {
        // 20 TPS: one tick every 0.05 seconds.
        private const float TickSeconds = 1f / 20f;

        private const BattleSide LocalSide    = BattleSide.SideA;
        private const BattleSide OpponentSide = BattleSide.SideB;

        private readonly StageDefinition      _stage;
        private readonly BattleConfigSnapshot  _config;
        private readonly BattleInitialState   _initialState;
        private readonly BattleSimulator      _simulator;

        private BattleState  _lastState;
        private BattleResult _result;
        private float        _tickAccumulator;
        private bool         _isFaulted;
        private string       _lastFailureDump;

        // Same-tick submitted-command tracking.
        // Mirrors DebugBattleRunner's guard: prevents stale commands from a failed
        // AdvanceTick surviving into the next attempt on the same tick number.
        private int                          _submittedCommandTick      = -1;
        private readonly List<BattleCommand> _submittedCommandsThisTick = new List<BattleCommand>(16);

        // ── Public surface ────────────────────────────────────────────────────

        public BattleState  LastState       => _lastState;
        public BattleResult Result          => _result;
        public bool         IsTerminated    => _simulator?.IsTerminated ?? false;
        public int          CurrentTick     => _simulator?.CurrentTick  ?? 0;
        public string       StageId         => _stage?.StageId;
        public BattleConfigSnapshot Config  => _config;

        /// <summary>
        /// True after AdvanceTick threw an unhandled exception.
        /// When faulted, <see cref="Tick"/> and <see cref="ManualStep"/> are no-ops.
        /// Inspect <see cref="LastFailureDump"/> for diagnostics.
        /// </summary>
        public bool   IsFaulted       => _isFaulted;

        /// <summary>
        /// Plain-text diagnostic block populated when AdvanceTick throws.
        /// Null until the first fault. Safe to copy-paste into a bug report.
        /// </summary>
        public string LastFailureDump => _lastFailureDump;

        /// <summary>
        /// SideB AI controller.  Enabled by default.
        /// Callers may toggle or inspect via this reference.
        /// </summary>
        public StageBattleOpponentController OpponentController { get; }

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new battle session from a stage definition and the local
        /// player's (SideA) deck.
        /// </summary>
        /// <param name="stage">Stage definition including the embedded SideB deck.</param>
        /// <param name="sideADeck">Local player's troop cards for this session.</param>
        public StageBattleSession(StageDefinition stage, TroopCardData[] sideADeck)
        {
            if (stage     == null) throw new ArgumentNullException(nameof(stage));
            if (sideADeck == null) throw new ArgumentNullException(nameof(sideADeck));

            _stage        = stage;
            _config       = StageBattleDefinitionBuilder.BuildConfig(stage);
            _initialState = StageBattleDefinitionBuilder.BuildInitialState(stage, sideADeck);
            _simulator    = new BattleSimulator(_config, _initialState);
            _lastState    = _simulator.GetState();

            OpponentController = new StageBattleOpponentController(initiallyEnabled: true);
        }

        // ── Tick driving ──────────────────────────────────────────────────────

        /// <summary>
        /// Advances the simulation by accumulated elapsed time at 20 TPS.
        /// Designed to receive Unity's <c>Time.deltaTime</c> — this class
        /// never calls <c>Time.deltaTime</c> itself.
        /// No-op when the battle is terminated or faulted.
        /// </summary>
        public void Tick(float elapsedSeconds)
        {
            if (IsTerminated || _isFaulted) return;

            _tickAccumulator += elapsedSeconds;
            while (_tickAccumulator >= TickSeconds && !IsTerminated && !_isFaulted)
            {
                RunOneTick();
                _tickAccumulator -= TickSeconds;
            }
        }

        /// <summary>
        /// Advances the simulation by exactly one tick regardless of elapsed time.
        /// No-op when the battle is terminated or faulted.
        /// </summary>
        public void ManualStep()
        {
            if (!IsTerminated && !_isFaulted)
                RunOneTick();
        }

        // ── Player (SideA) command submission ────────────────────────────────

        /// <summary>
        /// Submits a SpawnDroneSquad command for the local player (SideA).
        /// Returns null on success, or a rejection reason string on failure.
        /// </summary>
        public string SubmitSpawnDroneSquad(int slotIndex, string laneId) =>
            TrySubmitCommand(BattleCommand.SpawnDroneSquad(
                _simulator.CurrentTick, slotIndex, laneId, LocalSide));

        /// <summary>
        /// Submits a DeployPilot command for the local player (SideA).
        /// Returns null on success, or a rejection reason string on failure.
        /// </summary>
        public string SubmitDeployPilot(int slotIndex, string laneId) =>
            TrySubmitCommand(BattleCommand.DeployPilot(
                _simulator.CurrentTick, slotIndex, laneId, LocalSide));

        /// <summary>
        /// Submits a RecallPilot command for the local player (SideA).
        /// Returns null on success, or a rejection reason string on failure.
        /// </summary>
        public string SubmitRecallPilot(int slotIndex) =>
            TrySubmitCommand(BattleCommand.RecallPilot(
                _simulator.CurrentTick, slotIndex, null, LocalSide));

        /// <summary>
        /// Submits an arbitrary BattleCommand.
        /// The caller is responsible for the correct tick, side, and slot values.
        /// Returns null on success, or a rejection reason string on failure.
        /// </summary>
        public string SubmitCommand(BattleCommand command) =>
            TrySubmitCommand(command);

        // ── Core tick ────────────────────────────────────────────────────────

        private void RunOneTick()
        {
            if (_simulator == null || _simulator.IsTerminated) return;

            ResetSubmittedCommandsIfTickChanged();
            SubmitOpponentCommands();

            try
            {
                _simulator.AdvanceTick();
                _lastState = _simulator.GetState();
                if (_simulator.IsTerminated)
                    _result = _simulator.GetResult();
            }
            catch (Exception ex)
            {
                _lastFailureDump = BuildFailureDump(ex);
                _isFaulted       = true;
            }
        }

        private void SubmitOpponentCommands()
        {
            if (_lastState == null) return;

            SlotDefinition[] sideBSlots = _initialState?.SideB?.Slots;
            LaneDefinition[] lanes      = _config?.Lanes;

            BattleCommand cmd = OpponentController.Evaluate(
                _lastState, sideBSlots, lanes, _simulator.CurrentTick);
            if (cmd == null) return;

            ResetSubmittedCommandsIfTickChanged();
            if (HasSubmittedSameTickSlotCommand(cmd)) return;

            // Pre-validate against current state.  Without this, an invalid AI
            // command (e.g. cooldown not respected) would queue successfully and
            // then crash AdvanceTick, faulting the entire session.
            if (PrevalidateApplicable(cmd) != null) return;

            try
            {
                _simulator.SubmitCommand(cmd);
                RecordSubmittedCommand(cmd);
            }
            catch
            {
                // Opponent rejections are silently absorbed; the AI will adapt next tick.
            }
        }

        // ── Command submission ────────────────────────────────────────────────

        private string TrySubmitCommand(BattleCommand command)
        {
            if (command == null)
                return "Command is null";
            if (IsTerminated)
                return "Battle is already terminated";
            if (_isFaulted)
                return "Session faulted — check LastFailureDump";

            ResetSubmittedCommandsIfTickChanged();

            if (HasSubmittedSameTickSlotCommand(command))
                return "Same-tick slot guard: "
                    + command.Side + " slot=" + command.SlotIndex
                    + " already submitted this tick";

            // Pre-validate against current state (cooldown / pilot deployed /
            // pilot knocked out / energy).  Without this, the command would queue
            // and crash AdvanceTick, faulting the entire session — every subsequent
            // input would be rejected with "Session faulted".
            string prevalErr = PrevalidateApplicable(command);
            if (prevalErr != null) return prevalErr;

            try
            {
                _simulator.SubmitCommand(command);
                RecordSubmittedCommand(command);
                return null; // success
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ── Pre-validation (mirrors BattleSimulator.ApplyCommand invariants) ──

        /// <summary>
        /// Returns null if the command can be applied next AdvanceTick, or a
        /// rejection reason string otherwise.  Mirrors the checks performed by
        /// BattleSimulator.ApplySpawnDroneSquad / ApplyDeployPilot / ApplyRecallPilot,
        /// using <see cref="_lastState"/> for live slot state and
        /// <see cref="_initialState"/> for slot definitions.
        ///
        /// Energy validation also accounts for already-queued spawn commands this
        /// tick on the same side ("tentative" deduction), so two same-tick spawns
        /// whose combined cost exceeds available energy are rejected up front.
        /// </summary>
        private string PrevalidateApplicable(BattleCommand command)
        {
            // First tick (no state yet) — let the simulator handle it.
            if (_lastState == null) return null;

            BattleSideState sideState = FindSideStateLocal(_lastState, command.Side);
            if (sideState == null) return "Side state not found: " + command.Side;

            SlotState slot = FindSlotStateLocal(sideState, command.SlotIndex);

            BattleSideInitialState sideInit = command.Side == BattleSide.SideA
                ? _initialState?.SideA : _initialState?.SideB;
            SlotDefinition def = FindSlotDefinitionLocal(sideInit, command.SlotIndex);

            switch (command.CommandType)
            {
                case BattleCommandType.SpawnDroneSquad:
                    if (slot == null) return "Slot " + command.SlotIndex + " not found";
                    if (def  == null) return "Slot definition " + command.SlotIndex + " not found";
                    if (slot.DroneCooldownTick > 0)
                        return "Slot " + command.SlotIndex + " drone on cooldown ("
                             + slot.DroneCooldownTick + " ticks remaining)";
                    if (slot.IsPilotDeployed)
                        return "Slot " + command.SlotIndex + " pilot deployed; cannot spawn drone";
                    Fp tentative = SumTentativeSpawnEnergy(command.Side);
                    Fp available = sideState.Energy - tentative;
                    if (available < def.EnergyCost)
                        return "Insufficient energy: need " + def.EnergyCost
                             + ", have " + available
                             + (tentative > Fp.Zero ? " (after pending this tick)" : "");
                    return null;

                case BattleCommandType.DeployPilot:
                    if (slot == null) return "Slot " + command.SlotIndex + " not found";
                    if (slot.IsPilotDeployed)
                        return "Slot " + command.SlotIndex + " pilot already deployed";
                    if (slot.IsPilotKnockedOut)
                        return "Slot " + command.SlotIndex + " pilot knocked out";
                    return null;

                case BattleCommandType.RecallPilot:
                    if (slot == null) return "Slot " + command.SlotIndex + " not found";
                    if (!slot.IsPilotDeployed)
                        return "Slot " + command.SlotIndex + " pilot not deployed; cannot recall";
                    return null;

                default:
                    return null;
            }
        }

        private Fp SumTentativeSpawnEnergy(BattleSide side)
        {
            BattleSideInitialState sideInit = side == BattleSide.SideA
                ? _initialState?.SideA : _initialState?.SideB;
            if (sideInit?.Slots == null) return Fp.Zero;

            Fp total = Fp.Zero;
            for (int i = 0; i < _submittedCommandsThisTick.Count; i++)
            {
                BattleCommand c = _submittedCommandsThisTick[i];
                if (c.Side != side) continue;
                if (c.CommandType != BattleCommandType.SpawnDroneSquad) continue;
                SlotDefinition def = FindSlotDefinitionLocal(sideInit, c.SlotIndex);
                if (def != null) total = total + def.EnergyCost;
            }
            return total;
        }

        private static BattleSideState FindSideStateLocal(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return null;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i];
            return null;
        }

        private static SlotState FindSlotStateLocal(BattleSideState side, int slotIndex)
        {
            if (side?.Slots == null) return null;
            for (int i = 0; i < side.Slots.Count; i++)
                if (side.Slots[i].SlotIndex == slotIndex) return side.Slots[i];
            return null;
        }

        private static SlotDefinition FindSlotDefinitionLocal(BattleSideInitialState sideInit, int slotIndex)
        {
            if (sideInit?.Slots == null) return null;
            for (int i = 0; i < sideInit.Slots.Length; i++)
                if (sideInit.Slots[i].SlotIndex == slotIndex) return sideInit.Slots[i];
            return null;
        }

        // ── Same-tick command tracking ────────────────────────────────────────

        private void ResetSubmittedCommandsIfTickChanged()
        {
            if (_simulator == null) return;
            int currentTick = _simulator.CurrentTick;
            if (currentTick == _submittedCommandTick) return;
            _submittedCommandsThisTick.Clear();
            _submittedCommandTick = currentTick;
        }

        private void RecordSubmittedCommand(BattleCommand cmd)
        {
            _submittedCommandsThisTick.Add(cmd);
        }

        /// <summary>
        /// Returns true when another command for the same Side+Slot was already
        /// submitted this tick, blocking the duplicate.
        ///
        /// SlotIndex &lt; 0 bypasses the guard — RecallPilot carries pilot
        /// location internally and is safe to re-submit.
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

        // ── Failure dump ──────────────────────────────────────────────────────

        private string BuildFailureDump(Exception ex)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== StageBattle AdvanceTick Failure Dump ===");
            sb.AppendLine("Stage       : "
                + (_stage != null
                    ? _stage.DisplayName + " [" + _stage.StageId + "]"
                    : "<none>"));
            sb.AppendLine("Tick        : "
                + (_simulator != null ? _simulator.CurrentTick.ToString() : "?"));
            sb.AppendLine("OpponentAI  : "
                + (OpponentController != null
                    ? OpponentController.IsEnabled.ToString()
                    : "n/a"));
            sb.AppendLine();

            // Submitted commands this tick
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

            // Side states
            if (_lastState != null)
            {
                AppendSideDump(sb, _lastState, BattleSide.SideA, _initialState?.SideA?.Slots);
                AppendSideDump(sb, _lastState, BattleSide.SideB, _initialState?.SideB?.Slots);
                AppendLaneDump(sb, _lastState);
            }
            else
            {
                sb.AppendLine("-- LastState: null --");
                sb.AppendLine();
            }

            // Exception
            sb.AppendLine("-- Exception --");
            sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine("=== End Dump ===");

            return sb.ToString();
        }

        private void AppendSideDump(
            StringBuilder   sb,
            BattleState     state,
            BattleSide      side,
            SlotDefinition[] defs)
        {
            BattleSideState sideState = GetSideState(state, side);
            string          label     = side == BattleSide.SideA ? "SideA" : "SideB";
            sb.AppendLine("-- " + label + " state --");
            if (sideState == null)
            {
                sb.AppendLine("  (null)");
                sb.AppendLine();
                return;
            }
            sb.AppendLine("  Energy : " + sideState.Energy);
            sb.AppendLine("  BaseHP : " + sideState.BaseHp);
            sb.AppendLine("  Slots  : " + sideState.Slots.Count);
            for (int i = 0; i < sideState.Slots.Count; i++)
            {
                SlotState ss   = sideState.Slots[i];
                string    name = FindSlotPilotId(defs, ss.SlotIndex);
                sb.AppendLine("    [" + ss.SlotIndex + "] " + name
                    + "  drone_cd=" + ss.DroneCooldownTick
                    + "  pilot=" + (ss.IsPilotDeployed
                        ? "ON" : ss.IsPilotKnockedOut ? "KO" : "off")
                    + (ss.PilotCooldownTick > 0 ? "  pcd=" + ss.PilotCooldownTick : ""));
            }
            sb.AppendLine();
        }

        private static void AppendLaneDump(StringBuilder sb, BattleState state)
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

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FindSlotPilotId(SlotDefinition[] defs, int slotIndex)
        {
            if (defs == null) return "(unknown)";
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].SlotIndex == slotIndex) return defs[i].PilotId;
            return "(unknown)";
        }

        private static BattleSideState GetSideState(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return null;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i];
            return null;
        }
    }
}
