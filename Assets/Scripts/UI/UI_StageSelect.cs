using UnityEngine;
using UnityEngine.UI;
using FrontierBastion.Client.App;

namespace FrontierBastion.Client.UI
{
    /// <summary>
    /// Stage-select screen controller (skeleton).
    /// One prototype-stage button enters Battle; Back returns to the main menu.
    /// A real stage list replaces the single button later.
    /// </summary>
    public sealed class UI_StageSelect : MonoBehaviour
    {
        [SerializeField] private Button prototypeStageButton;
        [SerializeField] private Button backButton;

        // Prototype stage id (matches StagePrototypeCatalog's interactive sandbox).
        private const string PrototypeStageId = "debug_interactive";

        private void Start()
        {
            if (prototypeStageButton != null) prototypeStageButton.onClick.AddListener(OnSelectPrototype);
            if (backButton           != null) backButton.onClick.AddListener(OnBack);
        }

        private void OnSelectPrototype()
        {
            if (GameFlowManager.Instance != null)
                GameFlowManager.Instance.GoToBattle(PrototypeStageId);
        }

        private void OnBack()
        {
            if (GameFlowManager.Instance != null)
                GameFlowManager.Instance.GoToMainMenu();
        }
    }
}
