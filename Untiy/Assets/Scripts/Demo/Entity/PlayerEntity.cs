using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 2D 玩家实体 — 彩色方块 + 头顶标签 + 方向指示
    /// </summary>
    public class PlayerEntity : MonoBehaviour
    {
        public int PlayerId { get; private set; }

        private SpriteRenderer _sprite;
        private SpriteRenderer _dirArrow;
        private TextMesh _label;
        private Vector2 _lastDir;

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
            return entity;
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

            // 屏幕环绕：超出边界从另一端出现
            WrapPosition();
        }

        private Camera _cachedCam;

        void WrapPosition()
        {
            if (_cachedCam == null) _cachedCam = Camera.main;
            var cam = _cachedCam;
            if (cam == null) return;

            // 正交摄像机的世界空间边界
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float cx = cam.transform.position.x;
            float cy = cam.transform.position.y;

            var pos = transform.position;

            if (pos.x > cx + halfW + 0.5f) pos.x = cx - halfW - 0.5f;
            else if (pos.x < cx - halfW - 0.5f) pos.x = cx + halfW + 0.5f;

            if (pos.y > cy + halfH + 0.5f) pos.y = cy - halfH - 0.5f;
            else if (pos.y < cy - halfH - 0.5f) pos.y = cy + halfH + 0.5f;

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
