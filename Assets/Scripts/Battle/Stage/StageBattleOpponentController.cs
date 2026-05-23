using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Deterministic SideB opponent controller for Stage battles.
    ///
    /// Ported from <c>DebugBattleSideBAutoController</c>.  The
    /// <c>DebugBattleScenario</c> dependency is eliminated: callers pass
    /// <see cref="SlotDefinition"/>[] and <see cref="LaneDefinition"/>[] directly
    /// instead of going through the debug scenario wrapper.
    ///
    /// Pure C# — no MonoBehaviour, no UnityEngine.Random, Time, Physics, or
    /// GameObject references.
    ///
    /// Per-tick rule (emits at most one SpawnDroneSquad per tick):
    ///   1. Choose target lane: prefer the lane with the most SideA entities.
    ///      Rotate by tick when lanes are tied or empty.
    ///   2. Rotate slot priority by (currentTick / SlotAlternatePeriod) % slotCount.
    ///   3. Emit SpawnDroneSquad for the first viable slot; skip if cooldown > 0,
    ///      pilot is deployed, or SideB energy is insufficient.
    ///   4. If no viable slot found, emit nothing.
    ///
    /// Only emits BattleSide.SideB commands. Never emits SideA commands.
    /// </summary>
    public sealed class StageBattleOpponentController
    {
        private const int SlotAlternatePeriod = 20;
        private const int LaneAlternatePeriod = 15;

        public bool IsEnabled { get; private set; }

        public StageBattleOpponentController(bool initiallyEnabled = true)
        {
            IsEnabled = initiallyEnabled;
        }

        public void Toggle()                     { IsEnabled = !IsEnabled; }
        public void SetEnabled(bool enabled)     { IsEnabled = enabled; }

        /// <summary>
        /// Evaluates the SideB action for the given tick.
        /// Returns a SpawnDroneSquad command, or null if no action is taken.
        /// All returned commands have <c>Side == BattleSide.SideB</c>.
        /// </summary>
        /// <param name="state">Current battle state snapshot.</param>
        /// <param name="sideBSlots">SideB slot definitions from the stage initial state.</param>
        /// <param name="lanes">Lane definitions from the stage config.</param>
        /// <param name="currentTick">Current simulator tick.</param>
        public BattleCommand Evaluate(
            BattleState      state,
            SlotDefinition[] sideBSlots,
            LaneDefinition[] lanes,
            int              currentTick)
        {
            if (!IsEnabled)                                    return null;
            if (state == null || state.IsTerminated)           return null;
            if (sideBSlots == null || sideBSlots.Length == 0)  return null;
            if (lanes      == null || lanes.Length      == 0)  return null;

            BattleSideState sideBState = GetSideState(state, BattleSide.SideB);
            if (sideBState == null)                            return null;

            string targetLane = ChooseLane(state, lanes, currentTick);
            if (string.IsNullOrEmpty(targetLane))              return null;

            int slotCount   = sideBSlots.Length;
            int startOffset = slotCount > 1
                ? (currentTick / SlotAlternatePeriod) % slotCount
                : 0;

            for (int attempt = 0; attempt < slotCount; attempt++)
            {
                int            idx     = (startOffset + attempt) % slotCount;
                SlotDefinition slotDef = sideBSlots[idx];

                SlotState slotState = FindSlotState(sideBState, slotDef.SlotIndex);
                if (slotState == null)                          continue;
                if (slotState.DroneCooldownTick > 0)            continue;
                if (slotState.IsPilotDeployed)                  continue;
                if (sideBState.Energy < slotDef.EnergyCost)     continue;

                return BattleCommand.SpawnDroneSquad(
                    currentTick, slotDef.SlotIndex, targetLane, BattleSide.SideB);
            }

            return null;
        }

        // ── Lane selection ────────────────────────────────────────────────────

        private static string ChooseLane(
            BattleState      state,
            LaneDefinition[] lanes,
            int              currentTick)
        {
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

            if (bestCount > 0) return bestId;

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
