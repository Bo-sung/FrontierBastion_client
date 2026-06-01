using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Explicit app entry point.  Attach this to a GameObject in the Preload scene.
    ///
    /// Awake bootstraps <see cref="GameFlowManager"/> if it does not already exist.
    /// This class does not use RuntimeInitializeOnLoadMethod and is never
    /// auto-created — it must be placed in a scene manually.
    /// No battle logic lives here.
    /// </summary>
    public sealed class Preload : MonoBehaviour
    {
        private void Awake()
        {
            if (GameFlowManager.Instance != null) return;

            // GameFlowManager.Awake creates managers + UIRoot and subscribes to
            // sceneLoaded; once this Preload scene finishes loading it auto-advances
            // to MainMenu. No further calls needed here.
            var rootGO = new GameObject("GameFlowManager");
            rootGO.AddComponent<GameFlowManager>();
        }
    }
}
