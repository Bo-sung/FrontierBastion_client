using FrontierBastion.Client.Stage;
using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Provides stage definition and deck data for the Stage prototype pipeline.
    /// Currently backed by <see cref="StagePrototypeCatalog"/>; replace with
    /// asset-based loading when the data pipeline is ready.
    /// </summary>
    public sealed class StageDataManager : MonoBehaviour
    {
        public StageDefinition  GetPrototypeStage()     => StagePrototypeCatalog.CreateInteractiveSandboxStage();
        public TroopCardData[]  GetPrototypeSideADeck() => StagePrototypeCatalog.CreateSideAPrototypeDeck();
    }
}
