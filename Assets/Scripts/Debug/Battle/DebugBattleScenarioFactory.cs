using System.Collections.Generic;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    public static class DebugBattleScenarioFactory
    {
        public const string LaneGround = "lane_ground";

        public static DebugBattleScenario CreateSmokePlayerVictory()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "smoke_v1",
                initialEnergy: Fp.FromInt(20),
                maxEnergy: Fp.FromInt(100),
                energyRegenPerTick: Fp.Zero,
                pilotDeployCooldownTick: 200,
                pilotReturnCooldownTick: 100,
                pilotKnockoutDroneResumeTick: 50,
                playerBaseInitialHp: Fp.FromInt(1000),
                enemyBaseInitialHp: Fp.FromInt(1),
                maxBattleTick: 10,
                lanes: CreateSmokeLanes(),
                enemySpawnSchedule: new EnemySpawnDefinition[0]);

            BattleInitialState initial = new BattleInitialState(
                stageId: "smoke_victory",
                rngSeed: 1,
                slots: new[] { CreateVictorySlot() });

            Dictionary<int, BattleCommand[]> commandsByTick = new Dictionary<int, BattleCommand[]>
            {
                { 0, new[] { BattleCommand.SpawnDroneSquad(0, 0, LaneGround) } },
            };

            return new DebugBattleScenario(
                "smoke_player_victory",
                "Smoke Player Victory",
                config,
                initial,
                commandsByTick,
                BattleOutcome.Victory,
                BattleEndReason.EnemyBaseDestroyed,
                1);
        }

        public static DebugBattleScenario CreateSmokePlayerDefeat()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "smoke_v1",
                initialEnergy: Fp.Zero,
                maxEnergy: Fp.FromInt(100),
                energyRegenPerTick: Fp.Zero,
                pilotDeployCooldownTick: 200,
                pilotReturnCooldownTick: 100,
                pilotKnockoutDroneResumeTick: 50,
                playerBaseInitialHp: Fp.FromInt(1),
                enemyBaseInitialHp: Fp.FromInt(1000),
                maxBattleTick: 10,
                lanes: CreateSmokeLanes(),
                enemySpawnSchedule: new[]
                {
                    new EnemySpawnDefinition(
                        spawnTick: 1,
                        laneId: LaneGround,
                        hp: Fp.FromInt(200),
                        attack: Fp.FromInt(50),
                        rangeMilli: 500,
                        speedMilliPerTick: 2000),
                });

            BattleInitialState initial = new BattleInitialState(
                stageId: "smoke_defeat",
                rngSeed: 2,
                slots: new[] { CreateDefeatSlot() });

            return new DebugBattleScenario(
                "smoke_player_defeat",
                "Smoke Player Defeat",
                config,
                initial,
                new Dictionary<int, BattleCommand[]>(),
                BattleOutcome.Defeat,
                BattleEndReason.PlayerBaseDestroyed,
                1);
        }

        public static DebugBattleScenario CreateInteractiveSandbox()
        {
            BattleConfigSnapshot config = new BattleConfigSnapshot(
                configVersion: "debug_interactive_v1",
                initialEnergy: Fp.FromInt(60),
                maxEnergy: Fp.FromInt(100),
                energyRegenPerTick: Fp.FromFraction(5, 10),
                pilotDeployCooldownTick: 200,
                pilotReturnCooldownTick: 100,
                pilotKnockoutDroneResumeTick: 50,
                playerBaseInitialHp: Fp.FromInt(300),
                enemyBaseInitialHp: Fp.FromInt(300),
                maxBattleTick: 600,
                lanes: new[]
                {
                    new LaneDefinition(LaneGround, LaneType.Ground, 5000),
                },
                enemySpawnSchedule: new[]
                {
                    new EnemySpawnDefinition(20, LaneGround, Fp.FromInt(80), Fp.FromInt(10), 400, 120),
                    new EnemySpawnDefinition(80, LaneGround, Fp.FromInt(80), Fp.FromInt(10), 400, 120),
                    new EnemySpawnDefinition(140, LaneGround, Fp.FromInt(120), Fp.FromInt(15), 450, 100),
                });

            BattleInitialState initial = new BattleInitialState(
                stageId: "debug_interactive",
                rngSeed: 99,
                slots: new[] { CreateInteractiveSlot() });

            return new DebugBattleScenario(
                "interactive_sandbox",
                "Interactive Sandbox",
                config,
                initial,
                new Dictionary<int, BattleCommand[]>(),
                null,
                null,
                null);
        }

        private static LaneDefinition[] CreateSmokeLanes()
        {
            return new[]
            {
                new LaneDefinition(LaneGround, LaneType.Ground, 1000),
            };
        }

        private static SlotDefinition CreateVictorySlot()
        {
            return new SlotDefinition(
                slotIndex: 0,
                pilotId: "pilot_a",
                droneSquadId: "drone_a",
                energyCost: Fp.FromInt(20),
                cooldownTick: 5,
                droneHp: Fp.FromInt(100),
                droneAttack: Fp.FromInt(50),
                droneRangeMilli: 500,
                droneSpeedMilliPerTick: 2000,
                pilotHp: Fp.FromInt(200),
                pilotAttack: Fp.FromInt(20),
                pilotRangeMilli: 1000,
                pilotSpeedMilliPerTick: 300);
        }

        private static SlotDefinition CreateDefeatSlot()
        {
            return new SlotDefinition(
                slotIndex: 0,
                pilotId: "pilot_a",
                droneSquadId: "drone_a",
                energyCost: Fp.FromInt(20),
                cooldownTick: 5,
                droneHp: Fp.FromInt(100),
                droneAttack: Fp.FromInt(10),
                droneRangeMilli: 500,
                droneSpeedMilliPerTick: 500,
                pilotHp: Fp.FromInt(200),
                pilotAttack: Fp.FromInt(20),
                pilotRangeMilli: 1000,
                pilotSpeedMilliPerTick: 300);
        }

        private static SlotDefinition CreateInteractiveSlot()
        {
            return new SlotDefinition(
                slotIndex: 0,
                pilotId: "pilot_debug",
                droneSquadId: "drone_debug",
                energyCost: Fp.FromInt(20),
                cooldownTick: 15,
                droneHp: Fp.FromInt(80),
                droneAttack: Fp.FromInt(18),
                droneRangeMilli: 350,
                droneSpeedMilliPerTick: 180,
                pilotHp: Fp.FromInt(180),
                pilotAttack: Fp.FromInt(25),
                pilotRangeMilli: 600,
                pilotSpeedMilliPerTick: 220);
        }
    }
}
