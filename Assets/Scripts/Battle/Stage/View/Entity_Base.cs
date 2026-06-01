using UnityEngine;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Renders a Base pillar.
    /// Supports dynamic HP-based color transition and hit damage flash highlights.
    /// </summary>
    public sealed class Entity_Base : Entity_BaseView
    {
        private Color _currentColor;
        private float _flashTimer = 0f;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _flashTimer = 0f;
        }

        public void SetHpColor(float ratio01, Color full, Color empty)
        {
            _currentColor = Color.Lerp(empty, full, ratio01);
            UpdateVisualColor();
        }

        public void PlayDamageFlash()
        {
            _flashTimer = 0.15f;
            UpdateVisualColor();
        }

        private void Update()
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                UpdateVisualColor();
            }
        }

        private void UpdateVisualColor()
        {
            if (spriteRenderer != null)
            {
                Color c = _currentColor;
                if (_flashTimer > 0f)
                {
                    c += new Color(0.3f, 0.3f, 0.3f, 0f);
                }
                spriteRenderer.color = c;
            }
        }
    }
}
