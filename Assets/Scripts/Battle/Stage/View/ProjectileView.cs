using UnityEngine;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using BattleSim.Core.Events;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Renders a combat projectile in world space.
    /// Supports side color assignment and hit burst / miss fade visual sequences.
    /// </summary>
    public sealed class ProjectileView : BattleVisualView
    {
        private Color _baseColor;
        private bool _isDying = false;
        private float _fadeTimer = 0f;
        private float _initialScale = 0.18f;

        protected override void Awake()
        {
            base.Awake();
            _initialScale = transform.localScale.x;
            if (_initialScale <= 0f)
            {
                _initialScale = 0.18f;
            }
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            _isDying = false;
            _fadeTimer = 0f;
            transform.localScale = new Vector3(_initialScale, _initialScale, 1f);
        }

        public void SetSide(BattleSide side)
        {
            _baseColor = side == BattleSide.SideA
                ? new Color(0.4f, 0.95f, 1.0f, 1f)  // Light Cyan
                : new Color(1.0f, 0.75f, 0.3f, 1f); // Light Orange

            if (spriteRenderer != null)
            {
                spriteRenderer.color = _baseColor;
            }
        }

        public void PlayHitBurst()
        {
            _isDying = true;
            _fadeTimer = 0.1f;
        }

        public void PlayMissFade()
        {
            _isDying = true;
            _fadeTimer = 0.1f;
        }

        private void Update()
        {
            if (_isDying)
            {
                float dt = Time.deltaTime;
                _fadeTimer -= dt;
                float progress = Mathf.Clamp01(_fadeTimer / 0.1f);
                if (spriteRenderer != null)
                {
                    spriteRenderer.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, progress);
                }

                float scaleFactor = 1f + (1f - progress) * 0.5f;
                transform.localScale = new Vector3(_initialScale * scaleFactor, _initialScale * scaleFactor, 1f);

                if (_fadeTimer <= 0f)
                {
                    SetActiveVisual(false);
                }
            }
        }
    }
}
