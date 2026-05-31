using UnityEngine;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Renders the Victory or Defeat banner in the center of the world space.
    /// </summary>
    public sealed class ResultBannerView : BattleVisualView
    {
        [SerializeField] private TextMesh textMesh;

        protected override void Awake()
        {
            base.Awake();
            if (textMesh == null)
            {
                textMesh = GetComponentInChildren<TextMesh>();
            }
        }

        public void Show(string text, Color color)
        {
            if (textMesh != null)
            {
                textMesh.text = text;
                textMesh.color = color;
            }
            SetActiveVisual(true);
        }

        public void Hide()
        {
            SetActiveVisual(false);
        }
    }
}
