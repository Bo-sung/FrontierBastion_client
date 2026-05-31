using UnityEngine;
using System;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Renders floating text indicators (e.g. damage values) in world space.
    /// Animates upward movement and alpha fade before returning itself to the pool.
    /// </summary>
    public sealed class FloatingTextView : BattleVisualView
    {
        [SerializeField] private TextMesh textMesh;
        private float _timer = 0f;
        private Vector3 _startPos;
        private Action<FloatingTextView> _onCompleteCallback;

        protected override void Awake()
        {
            base.Awake();
            if (textMesh == null)
            {
                textMesh = GetComponentInChildren<TextMesh>();
            }
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            _timer = 0f;
        }

        public void Show(string text, Color color, Vector3 startPos, Action<FloatingTextView> onComplete = null)
        {
            if (textMesh != null)
            {
                textMesh.text = text;
                textMesh.color = color;
            }
            _startPos = startPos;
            transform.position = startPos;
            _onCompleteCallback = onComplete;
            SetActiveVisual(true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _timer += dt;
            float progress = Mathf.Clamp01(_timer / 0.6f);

            transform.position = _startPos + Vector3.up * (0.4f + progress * 0.8f);

            if (textMesh != null)
            {
                textMesh.color = new Color(textMesh.color.r, textMesh.color.g, textMesh.color.b, 1f - progress);
            }

            if (_timer >= 0.6f)
            {
                SetActiveVisual(false);
                _onCompleteCallback?.Invoke(this);
            }
        }
    }
}
