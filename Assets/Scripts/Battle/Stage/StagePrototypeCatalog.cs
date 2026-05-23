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
        private const string LaneGround = "lane_ground";
        private const string LaneAir    = "lane_air";

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
                    new LaneDefinition(LaneGround, LaneType.Ground, 5000),
                    new LaneDefinition(LaneAir,    LaneType.Air,    3000),
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
        /// Four slots: Tank / Striker / Ranger / Runner.
        /// All values are identical to the previous hardcoded slots in
        /// <c>DebugBattleScenarioFactory</c>.
        /// </summary>
        public static TroopCardData[] CreateSideAPrototypeDeck()
        {
            return new[]
            {
                // Slot 0 — Tank: high HP, low attack, slow, cheap.
                // Designed for sustained front-line presence on ground lanes.
                new TroopCardData
                {
                    SlotIndex              = 0,
                    PilotId                = "pilot_a_tank",
                    DroneSquadId           = "drone_a_tank",
                    EnergyCost             = Fp.FromInt(15),
                    CooldownTick           = 12,
                    DroneHp                = Fp.FromInt(180),
                    DroneAttack            = Fp.FromInt(18),
                    DroneRangeMilli        = 400,
                    DroneSpeedMilliPerTick = 130,
                    PilotHp                = Fp.FromInt(360),
                    PilotAttack            = Fp.FromInt(28),
                    PilotRangeMilli        = 500,
                    PilotSpeedMilliPerTick = 160,
                },
                // Slot 1 — Striker: low HP, high attack, fast, expensive.
                // Glass-cannon burst; melts quickly if met by durable enemies.
                new TroopCardData
                {
                    SlotIndex              = 1,
                    PilotId                = "pilot_a_striker",
                    DroneSquadId           = "drone_a_striker",
                    EnergyCost             = Fp.FromInt(25),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(60),
                    DroneAttack            = Fp.FromInt(45),
                    DroneRangeMilli        = 380,
                    DroneSpeedMilliPerTick = 350,
                    PilotHp                = Fp.FromInt(140),
                    PilotAttack            = Fp.FromInt(60),
                    PilotRangeMilli        = 480,
                    PilotSpeedMilliPerTick = 330,
                },
                // Slot 2 — Ranger: long range, medium speed, medium cost.
                // Engages from a safe distance; strong counter to slow enemies.
                new TroopCardData
                {
                    SlotIndex              = 2,
                    PilotId                = "pilot_a_ranger",
                    DroneSquadId           = "drone_a_ranger",
                    EnergyCost             = Fp.FromInt(22),
                    CooldownTick           = 20,
                    DroneHp                = Fp.FromInt(80),
                    DroneAttack            = Fp.FromInt(28),
                    DroneRangeMilli        = 1800,
                    DroneSpeedMilliPerTick = 180,
                    PilotHp                = Fp.FromInt(160),
                    PilotAttack            = Fp.FromInt(35),
                    PilotRangeMilli        = 2200,
                    PilotSpeedMilliPerTick = 200,
                },
                // Slot 3 — Runner: very fast, very cheap, very weak.
                // Rushes past combat to deal base damage; highly expendable.
                new TroopCardData
                {
                    SlotIndex              = 3,
                    PilotId                = "pilot_a_runner",
                    DroneSquadId           = "drone_a_runner",
                    EnergyCost             = Fp.FromInt(10),
                    CooldownTick           = 10,
                    DroneHp                = Fp.FromInt(50),
                    DroneAttack            = Fp.FromInt(10),
                    DroneRangeMilli        = 300,
                    DroneSpeedMilliPerTick = 450,
                    PilotHp                = Fp.FromInt(100),
                    PilotAttack            = Fp.FromInt(15),
                    PilotRangeMilli        = 380,
                    PilotSpeedMilliPerTick = 420,
                },
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// SideB prototype deck for the F3 Interactive Sandbox (auto-opponent).
        /// Four slots: Bruiser / Shooter / Swarm / Raider.
        /// All values are identical to the previous hardcoded slots in
        /// <c>DebugBattleScenarioFactory</c>.
        /// </summary>
        private static TroopCardData[] CreateSideBPrototypeDeck()
        {
            return new[]
            {
                // Slot 0 — Bruiser: high HP, medium attack, slow.
                // Durable front-line pressure; soaks SideA hits while grinding forward.
                new TroopCardData
                {
                    SlotIndex              = 0,
                    PilotId                = "pilot_b_bruiser",
                    DroneSquadId           = "drone_b_bruiser",
                    EnergyCost             = Fp.FromInt(14),
                    CooldownTick           = 45,
                    DroneHp                = Fp.FromInt(160),
                    DroneAttack            = Fp.FromInt(20),
                    DroneRangeMilli        = 380,
                    DroneSpeedMilliPerTick = 110,
                    PilotHp                = Fp.FromInt(280),
                    PilotAttack            = Fp.FromInt(28),
                    PilotRangeMilli        = 480,
                    PilotSpeedMilliPerTick = 130,
                },
                // Slot 1 — Shooter: low-mid HP, high attack, very long range, medium speed.
                // Engages SideA units before they close to melee range.
                new TroopCardData
                {
                    SlotIndex              = 1,
                    PilotId                = "pilot_b_shooter",
                    DroneSquadId           = "drone_b_shooter",
                    EnergyCost             = Fp.FromInt(20),
                    CooldownTick           = 55,
                    DroneHp                = Fp.FromInt(80),
                    DroneAttack            = Fp.FromInt(30),
                    DroneRangeMilli        = 2000,
                    DroneSpeedMilliPerTick = 160,
                    PilotHp                = Fp.FromInt(140),
                    PilotAttack            = Fp.FromInt(38),
                    PilotRangeMilli        = 2400,
                    PilotSpeedMilliPerTick = 180,
                },
                // Slot 2 — Swarm: very low HP and attack, fast, very cheap.
                // Numbers game; overwhelms through quantity when energy permits.
                new TroopCardData
                {
                    SlotIndex              = 2,
                    PilotId                = "pilot_b_swarm",
                    DroneSquadId           = "drone_b_swarm",
                    EnergyCost             = Fp.FromInt(9),
                    CooldownTick           = 25,
                    DroneHp                = Fp.FromInt(45),
                    DroneAttack            = Fp.FromInt(8),
                    DroneRangeMilli        = 300,
                    DroneSpeedMilliPerTick = 380,
                    PilotHp                = Fp.FromInt(80),
                    PilotAttack            = Fp.FromInt(12),
                    PilotRangeMilli        = 380,
                    PilotSpeedMilliPerTick = 360,
                },
                // Slot 3 — Raider: low-mid HP, medium attack, very fast.
                // Breakthrough rush; aims to reach the SideA base before interception.
                new TroopCardData
                {
                    SlotIndex              = 3,
                    PilotId                = "pilot_b_raider",
                    DroneSquadId           = "drone_b_raider",
                    EnergyCost             = Fp.FromInt(16),
                    CooldownTick           = 40,
                    DroneHp                = Fp.FromInt(90),
                    DroneAttack            = Fp.FromInt(22),
                    DroneRangeMilli        = 420,
                    DroneSpeedMilliPerTick = 320,
                    PilotHp                = Fp.FromInt(160),
                    PilotAttack            = Fp.FromInt(30),
                    PilotRangeMilli        = 500,
                    PilotSpeedMilliPerTick = 300,
                },
            };
        }
    }
}
