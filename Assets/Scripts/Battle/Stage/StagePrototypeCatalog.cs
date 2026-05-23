using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Hardcoded prototype stage definitions and troop decks used by the Debug
    /// Battle runner during the Stage Data Bridge prototype phase.
    ///
    /// All numbers in this file are temporary prototype values. Final balance and
    /// stage configuration will come from a data-driven source in a later phase.
    ///
    /// This type is pure C# — no MonoBehaviour, no UnityEngine dependency.
    /// </summary>
    public static class StagePrototypeCatalog
    {
        // Lane ID constants — must match DebugBattleScenarioFactory.Lane* constants.
        public const string LaneGround = "lane_ground";
        public const string LaneAir    = "lane_air";

        /// <summary>
        /// Maps a deck slot index to its default lane ID.
        /// Slots 0, 1, 2 default to lane_ground, slot 3 defaults to lane_air.
        /// </summary>
        public static string GetDefaultLaneId(int slotIndex)
        {
            return slotIndex == 3 ? LaneAir : LaneGround;
        }

        // ── Stage definitions ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="StageDefinition"/> for the F3 Interactive Sandbox.
        /// Includes all stage-level parameters and the embedded SideB (opponent) deck.
        /// The SideA (player) deck is not included — pass it separately to
        /// <see cref="StageBattleDefinitionBuilder.BuildInitialState"/>.
        /// </summary>
        public static StageDefinition CreateInteractiveSandboxStage()
        {
            return new StageDefinition
            {
                StageId       = "debug_interactive",
                DisplayName   = "Interactive Sandbox",
                ConfigVersion = "debug_interactive_v3",
                RngSeed       = 99,
                MaxBattleTick = 600,

                Lanes = new[]
                {
                    new LaneDefinition(LaneGround, LaneType.Ground, 5000, 0L),
                    new LaneDefinition(LaneAir,    LaneType.Air,    3000, 1500L),
                },

                SideAConfig = new StageSideConfig
                {
                    BaseInitialHp      = Fp.FromInt(300),
                    InitialEnergy      = Fp.FromInt(70),   // enough to try most slots immediately
                    MaxEnergy          = Fp.FromInt(120),  // raised so expensive combos are viable
                    EnergyRegenPerTick = Fp.FromFraction(6, 10),
                },

                SideBConfig = new StageSideConfig
                {
                    BaseInitialHp      = Fp.FromInt(300),
                    InitialEnergy      = Fp.FromInt(40),   // quick first burst, then steady-state
                    MaxEnergy          = Fp.FromInt(100),
                    EnergyRegenPerTick = Fp.FromFraction(5, 10),
                },

                PilotDeployCooldownTick      = 200,
                PilotReturnCooldownTick      = 100,
                PilotKnockoutDroneResumeTick = 50,

                TimeOutTieWinnerSide = BattleSide.SideB,

                SideBDeck = CreateSideBPrototypeDeck(),
            };
        }

        // ── Deck definitions ─────────────────────────────────────────────────

        /// <summary>
        /// SideA prototype deck for the F3 Interactive Sandbox.
        /// Four slots: Balanced Melee / Fragile Ranged / Melee Tank / Air Ranged.
        /// </summary>
        public static TroopCardData[] CreateSideAPrototypeDeck()
        {
            return new[]
            {
                // Slot 0 — Balanced Melee (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 0,
                    PilotId                = "pilot_a_melee_balanced",
                    DroneSquadId           = "drone_a_melee_balanced",
                    EnergyCost             = Fp.FromInt(15),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(40),
                    DroneAttack            = Fp.FromInt(10),
                    DroneDefense           = Fp.FromInt(5),
                    DroneRangeMilli        = 1000,
                    DroneSpeedMilliPerTick = 200,
                    DroneAttackPeriodTick  = 4,
                    PilotHp                = Fp.FromInt(120),
                    PilotAttack            = Fp.FromInt(15),
                    PilotDefense           = Fp.FromInt(5),
                    PilotRangeMilli        = 1000,
                    PilotSpeedMilliPerTick = 220,
                    PilotAttackPeriodTick  = 3,
                },
                // Slot 1 — Fragile Ranged (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 1,
                    PilotId                = "pilot_a_ranged_fragile",
                    DroneSquadId           = "drone_a_ranged_fragile",
                    EnergyCost             = Fp.FromInt(20),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(30),
                    DroneAttack            = Fp.FromInt(8),
                    DroneDefense           = Fp.FromInt(3),
                    DroneRangeMilli        = 4000,
                    DroneSpeedMilliPerTick = 180,
                    DroneAttackPeriodTick  = 3,
                    PilotHp                = Fp.FromInt(80),
                    PilotAttack            = Fp.FromInt(10),
                    PilotDefense           = Fp.FromInt(5),
                    PilotRangeMilli        = 5000,
                    PilotSpeedMilliPerTick = 200,
                    PilotAttackPeriodTick  = 2,
                },
                // Slot 2 — Melee Tank (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 2,
                    PilotId                = "pilot_a_melee_tank",
                    DroneSquadId           = "drone_a_melee_tank",
                    EnergyCost             = Fp.FromInt(18),
                    CooldownTick           = 25,
                    DroneHp                = Fp.FromInt(40),
                    DroneAttack            = Fp.FromInt(7),
                    DroneDefense           = Fp.FromInt(8),
                    DroneRangeMilli        = 1000,
                    DroneSpeedMilliPerTick = 100,
                    DroneAttackPeriodTick  = 4,
                    PilotHp                = Fp.FromInt(150),
                    PilotAttack            = Fp.FromInt(7),
                    PilotDefense           = Fp.FromInt(10),
                    PilotRangeMilli        = 1000,
                    PilotSpeedMilliPerTick = 110,
                    PilotAttackPeriodTick  = 4,
                },
                // Slot 3 — Air Ranged (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 3,
                    PilotId                = "pilot_a_air_ranged",
                    DroneSquadId           = "drone_a_air_ranged",
                    EnergyCost             = Fp.FromInt(22),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(30),
                    DroneAttack            = Fp.FromInt(8),
                    DroneDefense           = Fp.FromInt(1),
                    DroneRangeMilli        = 6000,
                    DroneSpeedMilliPerTick = 220,
                    DroneAttackPeriodTick  = 3,
                    PilotHp                = Fp.FromInt(80),
                    PilotAttack            = Fp.FromInt(10),
                    PilotDefense           = Fp.FromInt(2),
                    PilotRangeMilli        = 6000,
                    PilotSpeedMilliPerTick = 240,
                    PilotAttackPeriodTick  = 2,
                },
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// SideB prototype deck for the F3 Interactive Sandbox (auto-opponent).
        /// Symmetrically mirrors the SideA deck.
        /// </summary>
        private static TroopCardData[] CreateSideBPrototypeDeck()
        {
            return new[]
            {
                // Slot 0 — Balanced Melee (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 0,
                    PilotId                = "pilot_b_melee_balanced",
                    DroneSquadId           = "drone_b_melee_balanced",
                    EnergyCost             = Fp.FromInt(15),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(40),
                    DroneAttack            = Fp.FromInt(10),
                    DroneDefense           = Fp.FromInt(5),
                    DroneRangeMilli        = 1000,
                    DroneSpeedMilliPerTick = 200,
                    DroneAttackPeriodTick  = 4,
                    PilotHp                = Fp.FromInt(120),
                    PilotAttack            = Fp.FromInt(15),
                    PilotDefense           = Fp.FromInt(5),
                    PilotRangeMilli        = 1000,
                    PilotSpeedMilliPerTick = 220,
                    PilotAttackPeriodTick  = 3,
                },
                // Slot 1 — Fragile Ranged (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 1,
                    PilotId                = "pilot_b_ranged_fragile",
                    DroneSquadId           = "drone_b_ranged_fragile",
                    EnergyCost             = Fp.FromInt(20),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(30),
                    DroneAttack            = Fp.FromInt(8),
                    DroneDefense           = Fp.FromInt(3),
                    DroneRangeMilli        = 4000,
                    DroneSpeedMilliPerTick = 180,
                    DroneAttackPeriodTick  = 3,
                    PilotHp                = Fp.FromInt(80),
                    PilotAttack            = Fp.FromInt(10),
                    PilotDefense           = Fp.FromInt(5),
                    PilotRangeMilli        = 5000,
                    PilotSpeedMilliPerTick = 200,
                    PilotAttackPeriodTick  = 2,
                },
                // Slot 2 — Melee Tank (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 2,
                    PilotId                = "pilot_b_melee_tank",
                    DroneSquadId           = "drone_b_melee_tank",
                    EnergyCost             = Fp.FromInt(18),
                    CooldownTick           = 25,
                    DroneHp                = Fp.FromInt(40),
                    DroneAttack            = Fp.FromInt(7),
                    DroneDefense           = Fp.FromInt(8),
                    DroneRangeMilli        = 1000,
                    DroneSpeedMilliPerTick = 100,
                    DroneAttackPeriodTick  = 4,
                    PilotHp                = Fp.FromInt(150),
                    PilotAttack            = Fp.FromInt(7),
                    PilotDefense           = Fp.FromInt(10),
                    PilotRangeMilli        = 1000,
                    PilotSpeedMilliPerTick = 110,
                    PilotAttackPeriodTick  = 4,
                },
                // Slot 3 — Air Ranged (PROTOTYPE: 사용자 조정 대기)
                new TroopCardData
                {
                    SlotIndex              = 3,
                    PilotId                = "pilot_b_air_ranged",
                    DroneSquadId           = "drone_b_air_ranged",
                    EnergyCost             = Fp.FromInt(22),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(30),
                    DroneAttack            = Fp.FromInt(8),
                    DroneDefense           = Fp.FromInt(1),
                    DroneRangeMilli        = 6000,
                    DroneSpeedMilliPerTick = 220,
                    DroneAttackPeriodTick  = 3,
                    PilotHp                = Fp.FromInt(80),
                    PilotAttack            = Fp.FromInt(10),
                    PilotDefense           = Fp.FromInt(2),
                    PilotRangeMilli        = 6000,
                    PilotSpeedMilliPerTick = 240,
                    PilotAttackPeriodTick  = 2,
                },
            };
        }
    }
}
