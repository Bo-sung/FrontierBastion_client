using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Stage-level data container for a single deck slot (troop card).
    /// Stores pilot + drone squad stats as plain C# fields.
    ///
    /// Call <see cref="ToSlotDefinition"/> to convert to the
    /// <see cref="SlotDefinition"/> type consumed by
    /// <see cref="BattleSim.Core.Simulation.BattleSimulator"/>.
    ///
    /// This type is pure C# — no MonoBehaviour, no UnityEngine dependency.
    /// </summary>
    public sealed class TroopCardData
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public int    SlotIndex;
        public string PilotId;
        public string DroneSquadId;

        // ── Drone squad out-parameters ────────────────────────────────────────
        public Fp     EnergyCost;
        public int    CooldownTick;

        // ── Drone entity stats ────────────────────────────────────────────────
        public Fp   DroneHp;
        public Fp   DroneAttack;
        public long DroneRangeMilli;
        public long DroneSpeedMilliPerTick;

        // ── Pilot entity stats ────────────────────────────────────────────────
        public Fp   PilotHp;
        public Fp   PilotAttack;
        public long PilotRangeMilli;
        public long PilotSpeedMilliPerTick;

        // ── Conversion ────────────────────────────────────────────────────────

        /// <summary>
        /// Converts this troop card data into the <see cref="SlotDefinition"/>
        /// consumed by <see cref="BattleSim.Core.Simulation.BattleSimulator"/>.
        /// </summary>
        public SlotDefinition ToSlotDefinition()
        {
            return new SlotDefinition(
                slotIndex:              SlotIndex,
                pilotId:                PilotId,
                droneSquadId:           DroneSquadId,
                energyCost:             EnergyCost,
                cooldownTick:           CooldownTick,
                droneHp:                DroneHp,
                droneAttack:            DroneAttack,
                droneRangeMilli:        DroneRangeMilli,
                droneSpeedMilliPerTick: DroneSpeedMilliPerTick,
                pilotHp:                PilotHp,
                pilotAttack:            PilotAttack,
                pilotRangeMilli:        PilotRangeMilli,
                pilotSpeedMilliPerTick: PilotSpeedMilliPerTick);
        }
    }
}
