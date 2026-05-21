using System.Collections.Generic;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// A named, self-contained battle scenario used by the debug runner.
    ///
    /// Migrated to SideA/SideB API:
    ///   <see cref="ExpectedWinnerSide"/> replaces the old BattleOutcome? expectedOutcome.
    ///   <see cref="MatchesExpected"/> compares <see cref="BattleResult.WinnerSide"/>.
    /// </summary>
    public sealed class DebugBattleScenario
    {
        private readonly Dictionary<int, BattleCommand[]> _commandsByTick;

        /// <param name="expectedWinnerSide">
        /// Expected winning side. Null means no expectation (interactive sandbox).
        /// Phase 1 client policy: SideA = local player, SideB = opponent.
        /// </param>
        public DebugBattleScenario(
            string                           scenarioId,
            string                           displayName,
            BattleConfigSnapshot             config,
            BattleInitialState               initialState,
            Dictionary<int, BattleCommand[]> commandsByTick,
            BattleSide?                      expectedWinnerSide,
            BattleEndReason?                 expectedEndReason,
            int?                             expectedClearTimeTick)
        {
            ScenarioId            = scenarioId;
            DisplayName           = displayName;
            Config                = config;
            InitialState          = initialState;
            _commandsByTick       = commandsByTick;
            ExpectedWinnerSide    = expectedWinnerSide;
            ExpectedEndReason     = expectedEndReason;
            ExpectedClearTimeTick = expectedClearTimeTick;
        }

        public string               ScenarioId            { get; }
        public string               DisplayName           { get; }
        public BattleConfigSnapshot Config                { get; }
        public BattleInitialState   InitialState          { get; }
        /// <summary>Expected winner. Null = no expectation (interactive modes).</summary>
        public BattleSide?          ExpectedWinnerSide    { get; }
        public BattleEndReason?     ExpectedEndReason     { get; }
        public int?                 ExpectedClearTimeTick { get; }

        public IReadOnlyList<BattleCommand> GetFixtureCommandsForTick(int tick)
        {
            BattleCommand[] commands;
            return _commandsByTick.TryGetValue(tick, out commands)
                ? commands
                : EmptyCommands.Value;
        }

        /// <summary>
        /// Returns true when <paramref name="result"/> matches all non-null expectations.
        /// Uses <see cref="BattleResult.WinnerSide"/> instead of the removed BattleOutcome.
        /// </summary>
        public bool MatchesExpected(BattleResult result)
        {
            if (ExpectedWinnerSide.HasValue && result.WinnerSide != ExpectedWinnerSide.Value)
                return false;

            if (ExpectedEndReason.HasValue && result.EndReason != ExpectedEndReason.Value)
                return false;

            if (ExpectedClearTimeTick.HasValue && result.ClearTimeTick != ExpectedClearTimeTick.Value)
                return false;

            return true;
        }

        private static class EmptyCommands
        {
            public static readonly BattleCommand[] Value = new BattleCommand[0];
        }
    }
}
