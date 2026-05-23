using BattleSim.Core.Config;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Converts a <see cref="StageDefinition"/> and a SideA player deck into the
    /// Core config types consumed by
    /// <see cref="BattleSim.Core.Simulation.BattleSimulator"/>.
    ///
    /// Responsibilities:
    ///   • <see cref="BuildConfig"/>  — assembles <see cref="BattleConfigSnapshot"/>
    ///     from the stage's energy, lane, and timing parameters.
    ///   • <see cref="BuildInitialState"/> — assembles <see cref="BattleInitialState"/>
    ///     from the stage identity, the SideA player deck, and the embedded SideB deck.
    ///
    /// Side assignments:
    ///   • Local player  → <see cref="BattleSide.SideA"/>
    ///   • AI opponent   → <see cref="BattleSide.SideB"/>
    ///
    /// No combat logic lives here — this is pure data assembly.
    /// This type is pure C# — no MonoBehaviour, no UnityEngine dependency.
    /// </summary>
    public static class StageBattleDefinitionBuilder
    {
        /// <summary>
        /// Builds a <see cref="BattleConfigSnapshot"/> from the stage definition.
        /// The returned snapshot is immutable and ready to hand to
        /// <see cref="BattleSim.Core.Simulation.BattleSimulator"/>.
        /// </summary>
        public static BattleConfigSnapshot BuildConfig(StageDefinition stage)
        {
            return new BattleConfigSnapshot(
                configVersion: stage.ConfigVersion,
                sideA: new BattleSideConfig(
                    BattleSide.SideA,
                    baseInitialHp:      stage.SideAConfig.BaseInitialHp,
                    initialEnergy:      stage.SideAConfig.InitialEnergy,
                    maxEnergy:          stage.SideAConfig.MaxEnergy,
                    energyRegenPerTick: stage.SideAConfig.EnergyRegenPerTick),
                sideB: new BattleSideConfig(
                    BattleSide.SideB,
                    baseInitialHp:      stage.SideBConfig.BaseInitialHp,
                    initialEnergy:      stage.SideBConfig.InitialEnergy,
                    maxEnergy:          stage.SideBConfig.MaxEnergy,
                    energyRegenPerTick: stage.SideBConfig.EnergyRegenPerTick),
                pilotDeployCooldownTick:      stage.PilotDeployCooldownTick,
                pilotReturnCooldownTick:      stage.PilotReturnCooldownTick,
                pilotKnockoutDroneResumeTick: stage.PilotKnockoutDroneResumeTick,
                maxBattleTick:               stage.MaxBattleTick,
                lanes:                       stage.Lanes,
                timeOutTieWinnerSide:        stage.TimeOutTieWinnerSide);
        }

        /// <summary>
        /// Builds a <see cref="BattleInitialState"/> from the stage definition and the
        /// SideA player deck.  Each <see cref="TroopCardData"/> in both decks is
        /// converted to a <see cref="SlotDefinition"/> via
        /// <see cref="TroopCardData.ToSlotDefinition"/>.
        /// </summary>
        /// <param name="stage">Stage definition (includes the embedded SideB deck).</param>
        /// <param name="sideADeck">Player's SideA deck for this battle session.</param>
        public static BattleInitialState BuildInitialState(
            StageDefinition stage,
            TroopCardData[] sideADeck)
        {
            var sideASlots = new SlotDefinition[sideADeck.Length];
            for (int i = 0; i < sideADeck.Length; i++)
                sideASlots[i] = sideADeck[i].ToSlotDefinition();

            var sideBSlots = new SlotDefinition[stage.SideBDeck.Length];
            for (int i = 0; i < stage.SideBDeck.Length; i++)
                sideBSlots[i] = stage.SideBDeck[i].ToSlotDefinition();

            return new BattleInitialState(
                stageId: stage.StageId,
                rngSeed: stage.RngSeed,
                sideA:   new BattleSideInitialState(BattleSide.SideA, sideASlots),
                sideB:   new BattleSideInitialState(BattleSide.SideB, sideBSlots));
        }
    }
}
