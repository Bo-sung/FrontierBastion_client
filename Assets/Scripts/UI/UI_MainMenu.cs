using UnityEngine;
using UnityEngine.UI;
using FrontierBastion.Client.App;

namespace FrontierBastion.Client.UI
{
    /// <summary>
    /// Main menu screen controller (skeleton).
    /// Start button advances to the stage-select screen.
    /// </summary>
    public sealed class UI_MainMenu : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button quitButton;

        private void Start()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStart);
            if (quitButton  != null) quitButton.onClick.AddListener(OnQuit);
        }

        private void OnStart()
        {
            if (GameFlowManager.Instance != null)
                GameFlowManager.Instance.GoToStageSelect();
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
