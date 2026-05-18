using System.Collections.Generic;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    public sealed class DebugBattleScenario
    {
        private readonly Dictionary<int, BattleCommand[]> _commandsByTick;

        public DebugBattleScenario(
            string scenarioId,
            string displayName,
            BattleConfigSnapshot config,
            BattleInitialState initialState,
            Dictionary<int, BattleCommand[]> commandsByTick,
            BattleOutcome? expectedOutcome,
            BattleEndReason? expectedEndReason,
            int? expectedClearTimeTick)
        {
            ScenarioId = scenarioId;
            DisplayName = displayName;
            Config = config;
            InitialState = initialState;
            _commandsByTick = commandsByTick;
            ExpectedOutcome = expectedOutcome;
            ExpectedEndReason = expectedEndReason;
            ExpectedClearTimeTick = expectedClearTimeTick;
        }

        public string ScenarioId { get; }
        public string DisplayName { get; }
        public BattleConfigSnapshot Config { get; }
        public BattleInitialState InitialState { get; }
        public BattleOutcome? ExpectedOutcome { get; }
        public BattleEndReason? ExpectedEndReason { get; }
        public int? ExpectedClearTimeTick { get; }

        public IReadOnlyList<BattleCommand> GetFixtureCommandsForTick(int tick)
        {
            BattleCommand[] commands;
            return _commandsByTick.TryGetValue(tick, out commands)
                ? commands
                : EmptyCommands.Value;
        }

        public bool MatchesExpected(BattleResult result)
        {
            if (ExpectedOutcome.HasValue && result.Outcome != ExpectedOutcome.Value)
            {
                return false;
            }

            if (ExpectedEndReason.HasValue && result.EndReason != ExpectedEndReason.Value)
            {
                return false;
            }

            if (ExpectedClearTimeTick.HasValue && result.ClearTimeTick != ExpectedClearTimeTick.Value)
            {
                return false;
            }

            return true;
        }

        private static class EmptyCommands
        {
            public static readonly BattleCommand[] Value = new BattleCommand[0];
        }
    }
}
