using UnityEngine;
using System.Collections;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using BattleSim.Core.Events;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Renders a Battle Entity (Hero or Minion) in world space.
    /// Manages Side sprites/colors, HP bars, knockback/movement interpolations, and attack/damage visuals.
    /// </summary>
    public sealed class Entity_Unit : Entity_BaseView
    {
        [SerializeField] private Sprite sideASprite;
        [SerializeField] private Sprite sideBSprite;
        [SerializeField] private SpriteRenderer body;

        // Health bar elements
        [SerializeField] private Transform hpBarRoot;
        [SerializeField] private SpriteRenderer hpBarFill;

        public string LaneId { get; set; }

        private float _hpShowTimer = 0f;
        private bool _isSelected = false;
        private float _maxHp = -1f;

        // HP bar fill base transform (captured once) for left-anchored scaling.
        private float _hpFillBaseScaleX = 1f;
        private float _hpFillBaseLocalX = 0f;
        private bool _hpFillCaptured = false;

        // Visual states
        private enum VisualState { Normal, Dying, Recalling }
        private VisualState _state = VisualState.Normal;
        private float _stateTimer = 0f;
        private Color _baseColor;

        // Knockback / Interpolation
        private Vector3 _currentPos;
        private Vector3 _targetPos;
        private float _interpTimer = -1f;
        private float _interpDuration = 0f;

        // Scale Pulse
        private float _pulseTimer = 0f;
        private float _initialScale = 0.28f;

        protected override void Awake()
        {
            base.Awake();
            if (body == null)
            {
                body = spriteRenderer;
            }
            if (body == null)
            {
                body = GetComponentInChildren<SpriteRenderer>();
            }
            EnsureSprite(body);
            EnsureSprite(hpBarFill);

            // Auto-discover components if references are empty
            if (hpBarRoot == null)
            {
                var hpBarTrans = transform.Find("HpBar");
                if (hpBarTrans != null)
                {
                    hpBarRoot = hpBarTrans;
                    var hpBarFillTrans = hpBarTrans.Find("HpBarFill");
                    if (hpBarFillTrans != null)
                    {
                        hpBarFill = hpBarFillTrans.GetComponent<SpriteRenderer>();
                    }
                }
            }

            _initialScale = transform.localScale.x;
            if (_initialScale <= 0f)
            {
                _initialScale = 0.28f;
            }
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            _state = VisualState.Normal;
            _hpShowTimer = 0f;
            _isSelected = false;
            _interpTimer = -1f;
            _pulseTimer = 0f;
            _maxHp = -1f;
            transform.localScale = new Vector3(_initialScale, _initialScale, 1f);
            if (hpBarRoot != null)
            {
                hpBarRoot.gameObject.SetActive(false);
            }
        }

        public override void OnDespawn()
        {
            base.OnDespawn();
        }

        public void SetSide(BattleSide side)
        {
            _baseColor = side == BattleSide.SideA
                ? new Color(0.1f, 0.8f, 0.9f, 1f)  // Cyan
                : new Color(0.9f, 0.6f, 0.1f, 1f); // Orange

            if (body != null)
            {
                body.color = _baseColor;
                Sprite sideSprite = side == BattleSide.SideA ? sideASprite : sideBSprite;
                if (sideSprite != null)
                {
                    body.sprite = sideSprite;
                }
            }
        }

        public void SetHp(float currentHp)
        {
            if (_maxHp <= 0f)
            {
                _maxHp = currentHp;
            }
            if (hpBarFill != null)
            {
                if (!_hpFillCaptured)
                {
                    _hpFillBaseScaleX = hpBarFill.transform.localScale.x;
                    if (_hpFillBaseScaleX <= 0f) _hpFillBaseScaleX = 1f;
                    _hpFillBaseLocalX = hpBarFill.transform.localPosition.x;
                    _hpFillCaptured = true;
                }

                float ratio = _maxHp > 0f ? Mathf.Clamp01(currentHp / _maxHp) : 0f;

                // Left-anchored shrink: scale down width and shift center left so the
                // left edge stays fixed (sprite uses a centered pivot).
                var ls = hpBarFill.transform.localScale;
                ls.x = _hpFillBaseScaleX * ratio;
                hpBarFill.transform.localScale = ls;

                float halfBase = _hpFillBaseScaleX * 0.5f;
                var lp = hpBarFill.transform.localPosition;
                lp.x = _hpFillBaseLocalX - halfBase * (1f - ratio);
                hpBarFill.transform.localPosition = lp;
            }
        }

        public void ShowHpBar(bool show)
        {
            if (hpBarRoot != null)
            {
                hpBarRoot.gameObject.SetActive(show);
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            UpdateHpBarVisibility();
        }

        public void OnHit()
        {
            _hpShowTimer = 2.0f; // Show health bar for 2 seconds after hit
            UpdateHpBarVisibility();
            PlayHitFlash();
        }

        private void UpdateHpBarVisibility()
        {
            bool show = _isSelected || _hpShowTimer > 0f;
            ShowHpBar(show);
        }

        // Animations / Effects
        public void PlayAttackPulse()
        {
            _pulseTimer = 0.1f;
        }

        public void PlayHitFlash()
        {
            StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            if (body != null)
            {
                body.color = _baseColor + new Color(0.3f, 0.3f, 0.3f, 0f);
                yield return new WaitForSeconds(0.08f);
                body.color = _baseColor;
            }
        }

        public void PlayDeathFade()
        {
            _state = VisualState.Dying;
            _stateTimer = 0.3f;
        }

        public void PlayRecallFade()
        {
            _state = VisualState.Recalling;
            _stateTimer = 0.2f;
        }

        // Interpolations
        public void SetPositionImmediate(Vector3 pos)
        {
            transform.position = pos;
            _currentPos = pos;
            _targetPos = pos;
            _interpTimer = -1f;
        }

        public void KnockbackTo(Vector3 from, Vector3 to, float duration)
        {
            _currentPos = from;
            _targetPos = to;
            _interpTimer = 0f;
            _interpDuration = duration;
        }

        public void MoveTo(Vector3 target)
        {
            _currentPos = transform.position;
            _targetPos = target;
            _interpTimer = 0f;
            _interpDuration = Time.deltaTime; // Interpolate within one frame
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // HP Bar timer update
            if (_hpShowTimer > 0f)
            {
                _hpShowTimer -= dt;
                UpdateHpBarVisibility();
            }

            // Attack pulse scale
            if (_pulseTimer > 0f)
            {
                _pulseTimer -= dt;
                float scaleFactor = _pulseTimer > 0f ? 1.4f : 1f;
                transform.localScale = new Vector3(_initialScale * scaleFactor, _initialScale * scaleFactor, 1f);
            }

            // Interpolation update
            if (_interpTimer >= 0f)
            {
                _interpTimer += dt;
                float t = _interpDuration > 0f ? Mathf.Clamp01(_interpTimer / _interpDuration) : 1f;
                transform.position = Vector3.Lerp(_currentPos, _targetPos, t);
                if (t >= 1f)
                {
                    _interpTimer = -1f;
                    _currentPos = _targetPos;
                }
            }

            // Dying and Recalling fade out
            if (_state == VisualState.Dying)
            {
                _stateTimer -= dt;
                float progress = Mathf.Clamp01(_stateTimer / 0.3f);
                Color grayCol = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                Color finalCol = Color.Lerp(new Color(0.3f, 0.3f, 0.3f, 0f), Color.Lerp(grayCol, _baseColor, progress), progress);
                if (body != null)
                {
                    body.color = finalCol;
                }

                if (_stateTimer <= 0f)
                {
                    SetActiveVisual(false);
                }
            }
            else if (_state == VisualState.Recalling)
            {
                _stateTimer -= dt;
                float progress = Mathf.Clamp01(_stateTimer / 0.2f);
                Color finalCol = new Color(_baseColor.r, _baseColor.g, _baseColor.b, progress);
                if (body != null)
                {
                    body.color = finalCol;
                }

                if (_stateTimer <= 0f)
                {
                    SetActiveVisual(false);
                }
            }
        }
    }
}
