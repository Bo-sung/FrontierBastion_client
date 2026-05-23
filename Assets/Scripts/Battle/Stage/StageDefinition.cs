using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Stage-level data container.  Pure C# — no MonoBehaviour, no UnityEngine.
    ///
    /// Holds all parameters needed to assemble a
    /// <see cref="BattleSim.Core.Config.BattleConfigSnapshot"/> and a
    /// <see cref="BattleSim.Core.State.BattleInitialState"/> for one battle session.
    ///
    /// Use <see cref="StageBattleDefinitionBuilder"/> to convert this data into
    /// Core types that <see cref="BattleSim.Core.Simulation.BattleSimulator"/> accepts.
    ///
    /// The local player (SideA) deck is NOT stored here — it varies per player and is
    /// passed separately to <see cref="StageBattleDefinitionBuilder.BuildInitialState"/>.
    /// The opponent (SideB) deck is embedded in <see cref="SideBDeck"/> because it is
    /// fully determined by the stage definition.
    /// </summary>
    public sealed class StageDefinition
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string StageId;
        public string DisplayName;
        public string ConfigVersion;

        // ── Simulation seed ───────────────────────────────────────────────────
        public long RngSeed;
        public int  MaxBattleTick;

        // ── Lane layout ───────────────────────────────────────────────────────
        public LaneDefinition[] Lanes;

        // ── Per-side energy economy and base HP ───────────────────────────────
        public StageSideConfig SideAConfig;
        public StageSideConfig SideBConfig;

        // ── Pilot timing (tick units, shared by both sides) ───────────────────
        public int PilotDeployCooldownTick;
        public int PilotReturnCooldownTick;
        public int PilotKnockoutDroneResumeTick;

        // ── Timeout tie-break ─────────────────────────────────────────────────
        public BattleSide TimeOutTieWinnerSide;

        // ── Opponent deck ─────────────────────────────────────────────────────
        /// <summary>
        /// SideB (opponent) troop deck baked into the stage definition.
        /// The SideA (player) deck is supplied at runtime via
        /// <see cref="StageBattleDefinitionBuilder.BuildInitialState"/>.
        /// </summary>
        public TroopCardData[] SideBDeck;
    }

    /// <summary>
    /// Per-side energy economy and base HP stored inside a <see cref="StageDefinition"/>.
    /// Mirrors the fields of <see cref="BattleSim.Core.Config.BattleSideConfig"/> but
    /// lives at the stage-data layer so stage files do not directly depend on the
    /// constructor signature of the Core config type.
    /// </summary>
    public sealed class StageSideConfig
    {
        public Fp BaseInitialHp;
        public Fp InitialEnergy;
        public Fp MaxEnergy;
        public Fp EnergyRegenPerTick;
    }
}
