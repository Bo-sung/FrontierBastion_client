using System.Collections.Generic;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using FrontierBastion.Client.Stage;

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
        /// Interactive sandbox: 2 lanes, 4 SideA slots, SideB driven by auto controller.
        /// SideB has no scripted fixture commands — spawning is handled entirely by
        /// <see cref="DebugBattleSideBAutoController"/> in the runner (A key to toggle).
        /// SideA controlled manually (1=drone, 2=pilot, R=recall).
        ///
        /// Stage config and both decks are now sourced from the Stage data model:
        ///   <see cref="StagePrototypeCatalog.CreateInteractiveSandboxStage"/>
        ///   <see cref="StagePrototypeCatalog.CreateSideAPrototypeDeck"/>
        ///   <see cref="StageBattleDefinitionBuilder"/>
        /// </summary>
        public static DebugBattleScenario CreateInteractiveSandbox()
        {
            StageDefinition  stage     = StagePrototypeCatalog.CreateInteractiveSandboxStage();
            TroopCardData[]  sideADeck = StagePrototypeCatalog.CreateSideAPrototypeDeck();

            BattleConfigSnapshot config  = StageBattleDefinitionBuilder.BuildConfig(stage);
            BattleInitialState   initial = StageBattleDefinitionBuilder.BuildInitialState(stage, sideADeck);

            // No SideB scripted fixture commands — the auto controller handles SideB spawning.
            // SideA fixture commands could be added here for guided tutorial scenarios.
            var commandsByTick = new Dictionary<int, BattleCommand[]>();

            // NOTE: The scenarioId "interactive_sandbox" is the debug-runner's detection key
            // (DebugBattleRunner.ResetToScenario checks this string to enable SideB Auto).
            // stage.StageId ("debug_interactive") is the simulation seed identifier used by
            // BattleInitialState, and is intentionally different — do not conflate the two.
            return new DebugBattleScenario(
                "interactive_sandbox",
                stage.DisplayName,
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
                lanes: new[] { new LaneDefinition(LaneGround, LaneType.Ground, 100000, 0L, 0L, 100000L, 0L) },
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
            return new[] { new LaneDefinition(LaneGround, LaneType.Ground, 1000, 0L, 0L, 1000L, 0L) };
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
                droneDefense:         Fp.Zero,
                droneRangeMilli:      500,
                droneSpeedMilliPerTick: 2000,
                droneAttackPeriodTick: 1,
                droneAttackKind:      AttackKind.Melee,
                droneProjectileSpeedMilliPerTick: 0L,
                pilotHp:              Fp.FromInt(200),
                pilotAttack:          Fp.FromInt(20),
                pilotDefense:         Fp.Zero,
                pilotRangeMilli:      1000,
                pilotSpeedMilliPerTick: 300,
                pilotAttackPeriodTick: 1,
                pilotAttackKind:      AttackKind.Melee,
                pilotProjectileSpeedMilliPerTick: 0L);
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
                droneDefense:         Fp.Zero,
                droneRangeMilli:      0,
                droneSpeedMilliPerTick: 0,
                droneAttackPeriodTick: 1,
                droneAttackKind:      AttackKind.Melee,
                droneProjectileSpeedMilliPerTick: 0L,
                pilotHp:              Fp.FromInt(1),
                pilotAttack:          Fp.Zero,
                pilotDefense:         Fp.Zero,
                pilotRangeMilli:      0,
                pilotSpeedMilliPerTick: 0,
                pilotAttackPeriodTick: 1,
                pilotAttackKind:      AttackKind.Melee,
                pilotProjectileSpeedMilliPerTick: 0L);
        }

    }
}
