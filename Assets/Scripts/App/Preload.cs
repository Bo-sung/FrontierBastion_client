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

            var rootGO = new GameObject("GameFlowManager");
            var appRoot = rootGO.AddComponent<GameFlowManager>(); // GameFlowManager.Awake → CreateManagers (UIRoot, managers)
            appRoot.EnterStage();                         // load HUD from Resources, spawn under UIRoot
        }
    }
}
