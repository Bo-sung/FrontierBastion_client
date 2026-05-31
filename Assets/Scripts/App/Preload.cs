using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Explicit app entry point.  Attach this to a GameObject in the Preload scene.
    ///
    /// Awake bootstraps <see cref="AppRoot"/> if it does not already exist.
    /// This class does not use RuntimeInitializeOnLoadMethod and is never
    /// auto-created — it must be placed in a scene manually.
    /// No battle logic lives here.
    /// </summary>
    public sealed class Preload : MonoBehaviour
    {
        private void Awake()
        {
            if (AppRoot.Instance != null) return;

            var rootGO = new GameObject("AppRoot");
            var appRoot = rootGO.AddComponent<AppRoot>(); // AppRoot.Awake → CreateManagers (UIRoot, managers)
            appRoot.EnterStage();                         // load HUD from Resources, spawn under UIRoot
        }
    }
}
