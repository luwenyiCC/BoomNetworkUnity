using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 2D 玩家实体 — 彩色方块 + 头顶标签 + 方向指示
    /// </summary>
    public class PlayerEntity : MonoBehaviour
    {
        public int PlayerId { get; private set; }
        public bool IsOffline { get; private set; }

        private SpriteRenderer _sprite;
        private SpriteRenderer _dirArrow;
        private TextMesh _label;
        private Vector2 _lastDir;
        private Color _baseColor;
        private string _baseLabel;

        public static PlayerEntity Spawn(int playerId, Color color, string label)
        {
            // 主体：彩色方块
            var go = new GameObject($"{label} (P{playerId})");
            // 初始位置在原点附近，按序号小幅偏移避免重叠
            float offset = (playerId % 4 - 1.5f) * 0.8f;
            go.transform.position = new Vector3(offset, 0, 0);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite();
            sr.color = color;
            sr.sortingOrder = 1;
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1);

            // 方向箭头（小三角）
            var arrowGo = new GameObject("Arrow");
            arrowGo.transform.SetParent(go.transform);
            arrowGo.transform.localPosition = new Vector3(0.5f, 0, 0);
            arrowGo.transform.localScale = new Vector3(0.4f, 0.4f, 1);
            var arrowSr = arrowGo.AddComponent<SpriteRenderer>();
            arrowSr.sprite = CreateTriangleSprite();
            arrowSr.color = Color.white;
            arrowSr.sortingOrder = 2;

            // 标签
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform);
            labelGo.transform.localPosition = new Vector3(0, 0.7f, 0);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = $"{label}\nP{playerId}";
            tm.characterSize = 0.15f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;

            var entity = go.AddComponent<PlayerEntity>();
            entity.PlayerId = playerId;
            entity._sprite = sr;
            entity._dirArrow = arrowSr;
            entity._label = tm;
            entity._baseColor = color;
            entity._baseLabel = $"{label}\nP{playerId}";
            return entity;
        }

        public void SetOffline()
        {
            if (IsOffline) return;
            IsOffline = true;
            if (_sprite != null) _sprite.color = new Color(_baseColor.r * 0.3f, _baseColor.g * 0.3f, _baseColor.b * 0.3f, 0.5f);
            if (_label != null) _label.text = $"{_baseLabel}\n<color=red>OFFLINE</color>";
            if (_label != null) _label.color = Color.red;
        }

        public void SetOnline()
        {
            if (!IsOffline) return;
            IsOffline = false;
            if (_sprite != null) _sprite.color = _baseColor;
            if (_label != null) _label.text = _baseLabel;
            if (_label != null) _label.color = _baseColor;
        }

        /// <summary>
        /// 固定世界边界（半宽/半高），所有客户端一致，不依赖摄像机/分辨率
        /// </summary>
        public static float WorldHalfWidth = 8f;
        public static float WorldHalfHeight = 5f;

        /// <summary>当前朝向角度（度），用于快照序列化</summary>
        public float FacingAngle => _lastDir.sqrMagnitude > 0.01f
            ? Mathf.Atan2(_lastDir.y, _lastDir.x) * Mathf.Rad2Deg
            : transform.eulerAngles.z;

        /// <summary>从快照恢复朝向</summary>
        public void SetFacing(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            _lastDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            transform.rotation = Quaternion.Euler(0, 0, angleDeg);
        }

        public void ApplyMove(Vector2 dir, float delta)
        {
            transform.position += new Vector3(dir.x, dir.y, 0) * delta;

            // 方向箭头
            if (dir.sqrMagnitude > 0.01f)
            {
                _lastDir = dir.normalized;
                float angle = Mathf.Atan2(_lastDir.y, _lastDir.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0, 0, angle);
            }

            // 世界边界环绕：固定大小，所有客户端一致
            WrapPosition();
        }

        void WrapPosition()
        {
            var pos = transform.position;
            float w = WorldHalfWidth + 0.5f;
            float h = WorldHalfHeight + 0.5f;

            if (pos.x > w) pos.x = -w;
            else if (pos.x < -w) pos.x = w;

            if (pos.y > h) pos.y = -h;
            else if (pos.y < -h) pos.y = h;

            transform.position = pos;
        }

        public void UpdateLabel(string text)
        {
            if (_label != null)
                _label.text = text;
        }

        // 生成 1x1 白色方块 Sprite（运行时创建，不依赖资源）
        static Sprite _squareSprite;
        static Sprite CreateSquareSprite()
        {
            if (_squareSprite != null) return _squareSprite;
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _squareSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            return _squareSprite;
        }

        // 生成三角形 Sprite（朝右的箭头）
        static Sprite _triSprite;
        static Sprite CreateTriangleSprite()
        {
            if (_triSprite != null) return _triSprite;
            int size = 16;
            var tex = new Texture2D(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 简单三角形：x > |y - center| * 2
                    float cy = size / 2f;
                    bool inside = x > Mathf.Abs(y - cy) * 2f;
                    pixels[y * size + x] = inside ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _triSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _triSprite;
        }
    }
}
