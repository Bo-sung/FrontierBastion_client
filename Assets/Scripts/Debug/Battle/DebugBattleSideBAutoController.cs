using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// Prototype SideB automatic opponent controller for the F3 Interactive Sandbox.
    ///
    /// Decision policy — deterministic, tick and BattleState based only:
    ///   No Unity Random, Physics, or Time is used for AI decisions.
    ///   Time.deltaTime / Time.time are intentionally excluded from this class.
    ///
    /// Per-tick rule (produces at most one SpawnDroneSquad per tick):
    ///   1. Get SideB energy and slot states from BattleState.
    ///   2. Choose target lane: prefer the lane with the most SideA entities (pressure response).
    ///      When no SideA entities are present on any lane, rotate by
    ///      (currentTick / LaneAlternatePeriod) % laneCount.
    ///      When multiple lanes share the same maximum SideA entity count, prefer the
    ///      one whose index equals (currentTick / LaneAlternatePeriod) % tiedCount.
    ///   3. Rotate slot priority by (currentTick / SlotAlternatePeriod) % slotCount.
    ///   4. For each candidate slot (in rotated priority order):
    ///      – skip if drone cooldown > 0
    ///      – skip if pilot is deployed
    ///      – skip if SideB energy < slot energy cost
    ///      – otherwise emit SpawnDroneSquad(currentTick, slotIndex, lane, SideB) and stop.
    ///   5. If no viable slot is found, emit no command this tick.
    ///
    /// Only emits BattleSide.SideB commands. Never emits SideA commands.
    /// </summary>
    public sealed class DebugBattleSideBAutoController
    {
        /// <summary>Ticks between slot priority rotations.</summary>
        private const int SlotAlternatePeriod = 20;

        /// <summary>Ticks between lane tie-break rotations.</summary>
        private const int LaneAlternatePeriod = 15;

        public bool IsEnabled { get; private set; }

        public DebugBattleSideBAutoController(bool initiallyEnabled)
        {
            IsEnabled = initiallyEnabled;
        }

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }

        /// <summary>
        /// Evaluates SideB action for the given tick.
        /// Returns a BattleCommand to submit, or null if no action is taken.
        /// All returned commands have Side == BattleSide.SideB.
        /// </summary>
        public BattleCommand Evaluate(
            BattleState         state,
            DebugBattleScenario scenario,
            int                 currentTick)
        {
            if (!IsEnabled)                                  return null;
            if (state == null || state.IsTerminated)         return null;
            if (scenario == null)                            return null;

            BattleSideState sideBState = GetSideState(state, BattleSide.SideB);
            if (sideBState == null)                          return null;

            SlotDefinition[] sideBSlots = scenario.InitialState?.SideB?.Slots;
            if (sideBSlots == null || sideBSlots.Length == 0) return null;

            LaneDefinition[] lanes = scenario.Config?.Lanes;
            if (lanes == null || lanes.Length == 0)          return null;

            // ── 1. Choose best target lane ────────────────────────────────────
            string targetLane = ChooseLane(state, lanes, currentTick);
            if (string.IsNullOrEmpty(targetLane))            return null;

            // ── 2. Rotate slot priority ───────────────────────────────────────
            int slotCount   = sideBSlots.Length;
            int startOffset = slotCount > 1
                ? (currentTick / SlotAlternatePeriod) % slotCount
                : 0;

            for (int attempt = 0; attempt < slotCount; attempt++)
            {
                int            idx     = (startOffset + attempt) % slotCount;
                SlotDefinition slotDef = sideBSlots[idx];

                SlotState slotState = FindSlotState(sideBState, slotDef.SlotIndex);
                if (slotState == null) continue;

                // Guard: slot must be ready and energy must be sufficient.
                if (slotState.DroneCooldownTick > 0)          continue;
                if (slotState.IsPilotDeployed)                continue;
                if (sideBState.Energy < slotDef.EnergyCost)   continue;

                return BattleCommand.SpawnDroneSquad(
                    currentTick, slotDef.SlotIndex, targetLane, BattleSide.SideB);
            }

            return null; // no viable slot this tick
        }

        // ── Lane selection ────────────────────────────────────────────────────

        private static string ChooseLane(
            BattleState      state,
            LaneDefinition[] lanes,
            int              currentTick)
        {
            // Count SideA entities per lane; track best.
            int    bestCount = -1;
            string bestId    = lanes[0].LaneId;

            for (int i = 0; i < lanes.Length; i++)
            {
                int count = CountEntitiesBySide(state, lanes[i].LaneId, BattleSide.SideA);
                if (count > bestCount)
                {
                    bestCount = count;
                    bestId    = lanes[i].LaneId;
                }
            }

            // If at least one SideA entity found, return that lane.
            if (bestCount > 0) return bestId;

            // No SideA pressure: rotate through all lanes by tick.
            int idx = (currentTick / LaneAlternatePeriod) % lanes.Length;
            return lanes[idx].LaneId;
        }

        // ── State lookup helpers ──────────────────────────────────────────────

        private static int CountEntitiesBySide(BattleState state, string laneId, BattleSide side)
        {
            if (state?.Lanes == null) return 0;
            for (int i = 0; i < state.Lanes.Count; i++)
            {
                LaneState lane = state.Lanes[i];
                if (lane.LaneId != laneId) continue;
                int count = 0;
                for (int j = 0; j < lane.Entities.Count; j++)
                    if (lane.Entities[j].Side == side) count++;
                return count;
            }
            return 0;
        }

        private static BattleSideState GetSideState(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return null;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i];
            return null;
        }

        private static SlotState FindSlotState(BattleSideState sideState, int slotIndex)
        {
            if (sideState?.Slots == null) return null;
            for (int i = 0; i < sideState.Slots.Count; i++)
                if (sideState.Slots[i].SlotIndex == slotIndex) return sideState.Slots[i];
            return null;
        }
    }
}
