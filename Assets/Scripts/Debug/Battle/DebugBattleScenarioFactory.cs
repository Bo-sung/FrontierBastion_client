using System.Collections.Generic;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// Builds <see cref="DebugBattleScenario"/> instances for the debug runner.
    ///
    /// Migrated to SideA/SideB symmetric API:
    ///   • No EnemySpawnSchedule. SideB actions are BattleCommand entries in commandsByTick.
    ///   • BattleConfigSnapshot uses per-side BattleSideConfig.
    ///   • BattleInitialState uses per-side BattleSideInitialState.
    ///   • All BattleCommand factories require a BattleSide argument.
    ///   • ExpectedWinnerSide replaces BattleOutcome.
    ///
    /// Phase 1 client policy: SideA = local player, SideB = AI opponent.
    /// </summary>
    public static class DebugBattleScenarioFactory
    {
        public const string LaneGround = "lane_ground";
        public const string LaneAir    = "lane_air";

        // ── F1 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Smoke: SideA drone destroys SideB base at tick 1.
        /// Expected: WinnerSide=SideA, EndReason=SideBBaseDestroyed, clearTick=1.
        /// </summary>
        public static DebugBattleScenario CreateSmokeSideAVictory()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "smoke_v2",
                sideA: new BattleSideConfig(
                    BattleSide.SideA,
                    baseInitialHp:      Fp.FromInt(1000),
                    initialEnergy:      Fp.FromInt(20),
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                sideB: new BattleSideConfig(
                    BattleSide.SideB,
                    baseInitialHp:      Fp.FromInt(1),    // fragile target
                    initialEnergy:      Fp.Zero,
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                pilotDeployCooldownTick:      200,
                pilotReturnCooldownTick:      100,
                pilotKnockoutDroneResumeTick: 50,
                maxBattleTick: 10,
                lanes: CreateSmokeLanes());

            BattleInitialState initial = new BattleInitialState(
                stageId: "smoke_side_a_victory",
                rngSeed: 1,
                sideA: new BattleSideInitialState(BattleSide.SideA, new[] { CreateHighAttackSlot(0) }),
                sideB: new BattleSideInitialState(BattleSide.SideB, new[] { CreatePlaceholderSlot(0) }));

            // SideA drone reaches SideB base (1000 milli) in 1 tick at speed 2000.
            var commandsByTick = new Dictionary<int, BattleCommand[]>
            {
                { 0, new[] { BattleCommand.SpawnDroneSquad(0, 0, LaneGround, BattleSide.SideA) } },
            };

            return new DebugBattleScenario(
                "smoke_side_a_victory",
                "Smoke SideA Victory",
                config, initial, commandsByTick,
                expectedWinnerSide:    BattleSide.SideA,
                expectedEndReason:     BattleEndReason.SideBBaseDestroyed,
                expectedClearTimeTick: 1);
        }

        // ── F2 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Smoke: SideB drone destroys SideA base at tick 1.
        /// Expected: WinnerSide=SideB, EndReason=SideABaseDestroyed, clearTick=1.
        /// </summary>
        public static DebugBattleScenario CreateSmokeSideBVictory()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "smoke_v2",
                sideA: new BattleSideConfig(
                    BattleSide.SideA,
                    baseInitialHp:      Fp.FromInt(1),    // fragile target
                    initialEnergy:      Fp.Zero,
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                sideB: new BattleSideConfig(
                    BattleSide.SideB,
                    baseInitialHp:      Fp.FromInt(1000),
                    initialEnergy:      Fp.FromInt(20),
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                pilotDeployCooldownTick:      200,
                pilotReturnCooldownTick:      100,
                pilotKnockoutDroneResumeTick: 50,
                maxBattleTick: 10,
                lanes: CreateSmokeLanes());

            BattleInitialState initial = new BattleInitialState(
                stageId: "smoke_side_b_victory",
                rngSeed: 2,
                sideA: new BattleSideInitialState(BattleSide.SideA, new[] { CreatePlaceholderSlot(0) }),
                sideB: new BattleSideInitialState(BattleSide.SideB, new[] { CreateHighAttackSlot(0) }));

            // SideB drone travels from laneLength back to SideA base, reaching it in 1 tick.
            var commandsByTick = new Dictionary<int, BattleCommand[]>
            {
                { 0, new[] { BattleCommand.SpawnDroneSquad(0, 0, LaneGround, BattleSide.SideB) } },
            };

            return new DebugBattleScenario(
                "smoke_side_b_victory",
                "Smoke SideB Victory",
                config, initial, commandsByTick,
                expectedWinnerSide:    BattleSide.SideB,
                expectedEndReason:     BattleEndReason.SideABaseDestroyed,
                expectedClearTimeTick: 1);
        }

        // ── F3 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Interactive sandbox: 2 lanes, 2 SideA slots, SideB driven by auto controller.
        /// SideB has no scripted fixture commands — spawning is handled entirely by
        /// <see cref="DebugBattleSideBAutoController"/> in the runner (A key to toggle).
        /// SideA controlled manually (1=drone, 2=pilot, R=recall).
        /// </summary>
        public static DebugBattleScenario CreateInteractiveSandbox()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "debug_interactive_v3",
                sideA: new BattleSideConfig(
                    BattleSide.SideA,
                    baseInitialHp:      Fp.FromInt(300),
                    initialEnergy:      Fp.FromInt(70),   // enough to try most slots immediately
                    maxEnergy:          Fp.FromInt(120),  // raised so expensive combos are viable
                    energyRegenPerTick: Fp.FromFraction(6, 10)),
                sideB: new BattleSideConfig(
                    BattleSide.SideB,
                    baseInitialHp:      Fp.FromInt(300),
                    initialEnergy:      Fp.FromInt(40),   // quick first burst then steady-state
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.FromFraction(5, 10)),
                pilotDeployCooldownTick:      200,
                pilotReturnCooldownTick:      100,
                pilotKnockoutDroneResumeTick: 50,
                maxBattleTick: 600,
                lanes: new[]
                {
                    new LaneDefinition(LaneGround, LaneType.Ground, 5000),
                    new LaneDefinition(LaneAir,    LaneType.Air,    3000),
                });

            BattleInitialState initial = new BattleInitialState(
                stageId: "debug_interactive",
                rngSeed: 99,
                sideA: new BattleSideInitialState(BattleSide.SideA, new[]
                {
                    CreateSideATankSlot(),     // slot 0 — Tank:    durable, slow, cheap
                    CreateSideAStrikerSlot(),  // slot 1 — Striker: fragile, fast, high damage
                    CreateSideARangerSlot(),   // slot 2 — Ranger:  long range, medium speed
                    CreateSideARunnerSlot(),   // slot 3 — Runner:  very fast, very cheap, weak
                }),
                sideB: new BattleSideInitialState(BattleSide.SideB, new[]
                {
                    CreateSideBBruiserSlot(),  // slot 0 — Bruiser: durable pressure
                    CreateSideBShooterSlot(),  // slot 1 — Shooter: long-range pressure
                    CreateSideBSwarmSlot(),    // slot 2 — Swarm:   cheap, fast, expendable
                    CreateSideBRaiderSlot(),   // slot 3 — Raider:  fast breakthrough
                }));

            // No SideB scripted fixture commands — the auto controller handles SideB spawning.
            // SideA fixture commands could be added here for guided tutorial scenarios.
            var commandsByTick = new Dictionary<int, BattleCommand[]>();

            return new DebugBattleScenario(
                "interactive_sandbox",
                "Interactive Sandbox",
                config, initial, commandsByTick,
                expectedWinnerSide:    null,   // no expectation — interactive
                expectedEndReason:     null,
                expectedClearTimeTick: null);
        }

        // ── F4 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Smoke: timeout with equal HP — SideB wins via TimeOutTieWinnerSide = SideB.
        /// Expected: WinnerSide=SideB, EndReason=TimeOut, clearTick=3.
        /// </summary>
        public static DebugBattleScenario CreateSmokeTimeoutSideBTiebreak()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "smoke_v2",
                sideA: new BattleSideConfig(
                    BattleSide.SideA,
                    baseInitialHp:      Fp.FromInt(1000),
                    initialEnergy:      Fp.Zero,
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                sideB: new BattleSideConfig(
                    BattleSide.SideB,
                    baseInitialHp:      Fp.FromInt(1000),
                    initialEnergy:      Fp.Zero,
                    maxEnergy:          Fp.FromInt(100),
                    energyRegenPerTick: Fp.Zero),
                pilotDeployCooldownTick:      200,
                pilotReturnCooldownTick:      100,
                pilotKnockoutDroneResumeTick: 50,
                maxBattleTick: 3,
                lanes: new[] { new LaneDefinition(LaneGround, LaneType.Ground, 100000) },
                timeOutTieWinnerSide: BattleSide.SideB);   // SideB wins on equal-HP tie

            BattleInitialState initial = new BattleInitialState(
                stageId: "smoke_timeout_sideb_tiebreak",
                rngSeed: 3,
                sideA: new BattleSideInitialState(BattleSide.SideA, new[] { CreatePlaceholderSlot(0) }),
                sideB: new BattleSideInitialState(BattleSide.SideB, new[] { CreatePlaceholderSlot(0) }));

            return new DebugBattleScenario(
                "smoke_timeout_sideb_tiebreak",
                "Smoke Timeout SideB Tiebreak",
                config, initial,
                commandsByTick:        new Dictionary<int, BattleCommand[]>(),
                expectedWinnerSide:    BattleSide.SideB,
                expectedEndReason:     BattleEndReason.TimeOut,
                expectedClearTimeTick: 3);
        }

        // ── Shared lane helpers ───────────────────────────────────────────────

        private static LaneDefinition[] CreateSmokeLanes()
        {
            return new[] { new LaneDefinition(LaneGround, LaneType.Ground, 1000) };
        }

        // ── SideA slot helpers ────────────────────────────────────────────────

        /// <summary>High-attack drone that destroys the target base in 1 tick on a 1000-milli lane.</summary>
        private static SlotDefinition CreateHighAttackSlot(int slotIndex)
        {
            return new SlotDefinition(
                slotIndex:            slotIndex,
                pilotId:              "pilot_high",
                droneSquadId:         "drone_high",
                energyCost:           Fp.FromInt(20),
                cooldownTick:         5,
                droneHp:              Fp.FromInt(100),
                droneAttack:          Fp.FromInt(50),
                droneRangeMilli:      500,
                droneSpeedMilliPerTick: 2000,
                pilotHp:              Fp.FromInt(200),
                pilotAttack:          Fp.FromInt(20),
                pilotRangeMilli:      1000,
                pilotSpeedMilliPerTick: 300);
        }

        /// <summary>Placeholder slot with zero-attack stats. Used for sides that don't spawn units.</summary>
        private static SlotDefinition CreatePlaceholderSlot(int slotIndex)
        {
            return new SlotDefinition(
                slotIndex:            slotIndex,
                pilotId:              "placeholder",
                droneSquadId:         "placeholder",
                energyCost:           Fp.FromInt(99),   // too expensive to spawn accidentally
                cooldownTick:         999,
                droneHp:              Fp.FromInt(1),
                droneAttack:          Fp.Zero,
                droneRangeMilli:      0,
                droneSpeedMilliPerTick: 0,
                pilotHp:              Fp.FromInt(1),
                pilotAttack:          Fp.Zero,
                pilotRangeMilli:      0,
                pilotSpeedMilliPerTick: 0);
        }

        // ── SideA slot helpers (F3) ──────────────────────────────────────────

        /// <summary>
        /// F3 SideA Slot 0 — Tank.
        /// High HP, low attack, short range, slow, cheap.
        /// Designed for sustained front-line presence on ground lanes.
        /// </summary>
        private static SlotDefinition CreateSideATankSlot()
        {
            return new SlotDefinition(
                slotIndex:              0,
                pilotId:                "pilot_a_tank",
                droneSquadId:           "drone_a_tank",
                energyCost:             Fp.FromInt(15),
                cooldownTick:           12,
                droneHp:                Fp.FromInt(180),
                droneAttack:            Fp.FromInt(18),
                droneRangeMilli:        400,
                droneSpeedMilliPerTick: 130,
                pilotHp:                Fp.FromInt(360),
                pilotAttack:            Fp.FromInt(28),
                pilotRangeMilli:        500,
                pilotSpeedMilliPerTick: 160);
        }

        /// <summary>
        /// F3 SideA Slot 1 — Striker.
        /// Low HP, high attack, medium range, fast, expensive.
        /// Glass-cannon burst; melts quickly if met by durable enemies.
        /// </summary>
        private static SlotDefinition CreateSideAStrikerSlot()
        {
            return new SlotDefinition(
                slotIndex:              1,
                pilotId:                "pilot_a_striker",
                droneSquadId:           "drone_a_striker",
                energyCost:             Fp.FromInt(25),
                cooldownTick:           20,
                droneHp:                Fp.FromInt(60),
                droneAttack:            Fp.FromInt(45),
                droneRangeMilli:        380,
                droneSpeedMilliPerTick: 350,
                pilotHp:                Fp.FromInt(140),
                pilotAttack:            Fp.FromInt(60),
                pilotRangeMilli:        480,
                pilotSpeedMilliPerTick: 330);
        }

        /// <summary>
        /// F3 SideA Slot 2 — Ranger.
        /// Low-mid HP, medium attack, very long range, medium speed, medium cost.
        /// Engages from a safe distance; strong counter to slow enemies.
        /// </summary>
        private static SlotDefinition CreateSideARangerSlot()
        {
            return new SlotDefinition(
                slotIndex:              2,
                pilotId:                "pilot_a_ranger",
                droneSquadId:           "drone_a_ranger",
                energyCost:             Fp.FromInt(22),
                cooldownTick:           20,
                droneHp:                Fp.FromInt(80),
                droneAttack:            Fp.FromInt(28),
                droneRangeMilli:        1800,
                droneSpeedMilliPerTick: 180,
                pilotHp:                Fp.FromInt(160),
                pilotAttack:            Fp.FromInt(35),
                pilotRangeMilli:        2200,
                pilotSpeedMilliPerTick: 200);
        }

        /// <summary>
        /// F3 SideA Slot 3 — Runner.
        /// Very low HP and attack, short range, very fast, cheap.
        /// Rushes past combat to deal base damage; highly expendable.
        /// </summary>
        private static SlotDefinition CreateSideARunnerSlot()
        {
            return new SlotDefinition(
                slotIndex:              3,
                pilotId:                "pilot_a_runner",
                droneSquadId:           "drone_a_runner",
                energyCost:             Fp.FromInt(10),
                cooldownTick:           10,
                droneHp:                Fp.FromInt(50),
                droneAttack:            Fp.FromInt(10),
                droneRangeMilli:        300,
                droneSpeedMilliPerTick: 450,
                pilotHp:                Fp.FromInt(100),
                pilotAttack:            Fp.FromInt(15),
                pilotRangeMilli:        380,
                pilotSpeedMilliPerTick: 420);
        }

        // ── SideB slot helpers (F3) ──────────────────────────────────────────

        /// <summary>
        /// F3 SideB Slot 0 — Bruiser.
        /// High HP, medium attack, short range, slow.
        /// Durable front-line pressure; soaks SideA hits while grinding forward.
        /// </summary>
        private static SlotDefinition CreateSideBBruiserSlot()
        {
            return new SlotDefinition(
                slotIndex:              0,
                pilotId:                "pilot_b_bruiser",
                droneSquadId:           "drone_b_bruiser",
                energyCost:             Fp.FromInt(14),
                cooldownTick:           45,
                droneHp:                Fp.FromInt(160),
                droneAttack:            Fp.FromInt(20),
                droneRangeMilli:        380,
                droneSpeedMilliPerTick: 110,
                pilotHp:                Fp.FromInt(280),
                pilotAttack:            Fp.FromInt(28),
                pilotRangeMilli:        480,
                pilotSpeedMilliPerTick: 130);
        }

        /// <summary>
        /// F3 SideB Slot 1 — Shooter.
        /// Low-mid HP, medium-high attack, very long range, medium speed.
        /// Engages SideA units before they close to melee range.
        /// </summary>
        private static SlotDefinition CreateSideBShooterSlot()
        {
            return new SlotDefinition(
                slotIndex:              1,
                pilotId:                "pilot_b_shooter",
                droneSquadId:           "drone_b_shooter",
                energyCost:             Fp.FromInt(20),
                cooldownTick:           55,
                droneHp:                Fp.FromInt(80),
                droneAttack:            Fp.FromInt(30),
                droneRangeMilli:        2000,
                droneSpeedMilliPerTick: 160,
                pilotHp:                Fp.FromInt(140),
                pilotAttack:            Fp.FromInt(38),
                pilotRangeMilli:        2400,
                pilotSpeedMilliPerTick: 180);
        }

        /// <summary>
        /// F3 SideB Slot 2 — Swarm.
        /// Very low HP and attack, short range, fast, very cheap.
        /// Numbers game; overwhelms through quantity when energy permits.
        /// </summary>
        private static SlotDefinition CreateSideBSwarmSlot()
        {
            return new SlotDefinition(
                slotIndex:              2,
                pilotId:                "pilot_b_swarm",
                droneSquadId:           "drone_b_swarm",
                energyCost:             Fp.FromInt(9),
                cooldownTick:           25,
                droneHp:                Fp.FromInt(45),
                droneAttack:            Fp.FromInt(8),
                droneRangeMilli:        300,
                droneSpeedMilliPerTick: 380,
                pilotHp:                Fp.FromInt(80),
                pilotAttack:            Fp.FromInt(12),
                pilotRangeMilli:        380,
                pilotSpeedMilliPerTick: 360);
        }

        /// <summary>
        /// F3 SideB Slot 3 — Raider.
        /// Low-mid HP, medium attack, short-mid range, very fast.
        /// Breakthrough rush; aims to reach the SideA base before interception.
        /// </summary>
        private static SlotDefinition CreateSideBRaiderSlot()
        {
            return new SlotDefinition(
                slotIndex:              3,
                pilotId:                "pilot_b_raider",
                droneSquadId:           "drone_b_raider",
                energyCost:             Fp.FromInt(16),
                cooldownTick:           40,
                droneHp:                Fp.FromInt(90),
                droneAttack:            Fp.FromInt(22),
                droneRangeMilli:        420,
                droneSpeedMilliPerTick: 320,
                pilotHp:                Fp.FromInt(160),
                pilotAttack:            Fp.FromInt(30),
                pilotRangeMilli:        500,
                pilotSpeedMilliPerTick: 300);
        }
    }
}
