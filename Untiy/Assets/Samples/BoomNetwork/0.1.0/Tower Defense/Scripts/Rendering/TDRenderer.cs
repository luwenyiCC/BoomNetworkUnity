// BoomNetwork TowerDefense Demo — Renderer
//
// Top-down orthographic view. Color-coded Quads and Cubes, no external art assets.
//
// Attack Effects (pure visual, no simulation state):
//   Arrow  — 黄色细长条从塔飞向目标，8 帧
//   Cannon — 橙色爆炸球在命中点膨胀收缩，12 帧
//   Magic  — 紫色光环在目标位置脉冲，10 帧
//
// TryGetGridCell(): analytic ray/y=0 plane intersection for mouse click.

using UnityEngine;

namespace BoomNetwork.Samples.TowerDefense
{
    public class TDRenderer : MonoBehaviour
    {
        GameState _state;
        bool _initialized;
        Camera _cam;

        // ==================== Materials ====================
        Material _matGround, _matGridLine, _matBase;
        Material _matArrow, _matCannon, _matMagic;
        Material _matBasic, _matFast, _matTank, _matBasicSlow;
        Material _matFxArrow, _matFxCannon, _matFxMagic;

        // ==================== Tower pool ====================
        GameObject[] _towerObjs = new GameObject[GameState.GridSize];
        Renderer[]   _towerRend = new Renderer[GameState.GridSize];

        // ==================== Enemy pool ====================
        GameObject[] _enemyObjs = new GameObject[GameState.MaxEnemies];
        Renderer[]   _enemyRend = new Renderer[GameState.MaxEnemies];

        // ==================== Attack effects ====================
        // 纯视觉层，不影响模拟状态
        const int FxPoolSize = 64;

        struct AttackEffect
        {
            public bool      Active;
            public TowerType Type;
            public Vector3   Origin;   // 塔中心世界坐标
            public Vector3   Target;   // 命中点世界坐标
            public int       FramesTotal;
            public int       FramesLeft;
            public GameObject Obj;     // 主视觉体
        }

        AttackEffect[] _fx = new AttackEffect[FxPoolSize];

        // Shadow copy：检测塔开火时刻（cooldown 跳到最大值）
        int[] _prevCooldown = new int[GameState.GridSize];

        // ==================== Camera ====================
        const float CamHeight = 24f;
        static readonly Vector3 MapCenter = new Vector3(GameState.GridW * 0.5f, 0f, GameState.GridH * 0.5f);

        // ==================== Init ====================

        public void Init(GameState state)
        {
            _state = state;
            if (_initialized) return;
            _initialized = true;

            CreateMaterials();
            CreateCamera();
            CreateGround();
            CreateGridLines();
            CreateBase();
            CreateTowerPool();
            CreateEnemyPool();
            CreateFxPool();

            for (int i = 0; i < GameState.GridSize; i++) _prevCooldown[i] = 0;
        }

        // ==================== SyncVisuals ====================

        public void SyncVisuals()
        {
            if (!_initialized || _state == null) return;
            SyncTowers();
            SyncEnemies();
            TickFx();
            CaptureFrameShadow();
        }

        // ==================== SyncTowers ====================

        void SyncTowers()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                ref var t = ref _state.Grid[i];
                bool show = t.Type != TowerType.None;
                _towerObjs[i].SetActive(show);
                if (!show) continue;

                switch (t.Type)
                {
                    case TowerType.Arrow:  _towerRend[i].sharedMaterial = _matArrow;  break;
                    case TowerType.Cannon: _towerRend[i].sharedMaterial = _matCannon; break;
                    case TowerType.Magic:  _towerRend[i].sharedMaterial = _matMagic;  break;
                }

                // 检测开火：cooldown 本帧跳回满值 → 上一帧是 1，刚才打出了一发
                int maxCd = GameState.GetTowerCooldown(t.Type);
                if (t.CooldownFrames == maxCd && _prevCooldown[i] < maxCd)
                {
                    int cx = i % GameState.GridW;
                    int cy = i / GameState.GridW;
                    Vector3 origin = new Vector3(cx + 0.5f, 0.6f, cy + 0.5f);
                    Vector3 target = FindNearestEnemyPos(origin, GameState.GetTowerRange(t.Type).ToFloat());
                    SpawnFx(t.Type, origin, target);
                }
            }
        }

        // 在渲染层找最近存活敌人（只读，不改状态）
        Vector3 FindNearestEnemyPos(Vector3 towerPos, float range)
        {
            float rangeSq = range * range;
            float bestDist = float.MaxValue;
            Vector3 best = towerPos; // 没找到就在塔上原地闪
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref _state.Enemies[i];
                if (!e.IsAlive) continue;
                float ex = e.PosX.ToFloat(), ez = e.PosZ.ToFloat();
                float dx = ex - towerPos.x, dz = ez - towerPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq <= rangeSq && dSq < bestDist)
                {
                    bestDist = dSq;
                    best = new Vector3(ex, 0.3f, ez);
                }
            }
            return best;
        }

        // ==================== SyncEnemies ====================

        void SyncEnemies()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref _state.Enemies[i];
                bool show = e.IsAlive;
                _enemyObjs[i].SetActive(show);
                if (!show) continue;

                _enemyObjs[i].transform.position = new Vector3(e.PosX.ToFloat(), 0.3f, e.PosZ.ToFloat());

                switch (e.Type)
                {
                    case EnemyType.Basic:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matBasic;
                        _enemyObjs[i].transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                        break;
                    case EnemyType.Fast:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matFast;
                        _enemyObjs[i].transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
                        break;
                    case EnemyType.Tank:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matTank;
                        _enemyObjs[i].transform.localScale = new Vector3(0.75f, 0.85f, 0.75f);
                        break;
                }
            }
        }

        // ==================== Attack Effects ====================

        void SpawnFx(TowerType type, Vector3 origin, Vector3 target)
        {
            for (int i = 0; i < FxPoolSize; i++)
            {
                ref var f = ref _fx[i];
                if (f.Active) continue;

                f.Active      = true;
                f.Type        = type;
                f.Origin      = origin;
                f.Target      = target;

                switch (type)
                {
                    case TowerType.Arrow:
                        f.FramesTotal = 8;
                        f.Obj.GetComponent<Renderer>().sharedMaterial = _matFxArrow;
                        break;
                    case TowerType.Cannon:
                        f.FramesTotal = 12;
                        f.Obj.GetComponent<Renderer>().sharedMaterial = _matFxCannon;
                        break;
                    case TowerType.Magic:
                        f.FramesTotal = 10;
                        f.Obj.GetComponent<Renderer>().sharedMaterial = _matFxMagic;
                        break;
                }

                f.FramesLeft = f.FramesTotal;
                f.Obj.SetActive(true);
                return;
            }
        }

        void TickFx()
        {
            for (int i = 0; i < FxPoolSize; i++)
            {
                ref var f = ref _fx[i];
                if (!f.Active) continue;

                f.FramesLeft--;
                if (f.FramesLeft <= 0)
                {
                    f.Active = false;
                    f.Obj.SetActive(false);
                    continue;
                }

                float t = 1f - (float)f.FramesLeft / f.FramesTotal; // 0→1 进度

                switch (f.Type)
                {
                    case TowerType.Arrow:
                        UpdateArrowFx(ref f, t);
                        break;
                    case TowerType.Cannon:
                        UpdateCannonFx(ref f, t);
                        break;
                    case TowerType.Magic:
                        UpdateMagicFx(ref f, t);
                        break;
                }
            }
        }

        // Arrow：细长条从塔飞向目标，沿途拉伸，抵达目标后消失
        void UpdateArrowFx(ref AttackEffect f, float t)
        {
            // 当前位置 = 从 Origin 飞向 Target
            Vector3 pos = Vector3.Lerp(f.Origin, f.Target, t);
            f.Obj.transform.position = pos;

            // 朝向：指向 Target 方向
            Vector3 dir = f.Target - f.Origin;
            if (dir.sqrMagnitude > 0.001f)
                f.Obj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // 长度 = 飞行距离的 1/3（拖尾感），宽度固定
            float len = Mathf.Max(0.15f, dir.magnitude * 0.35f);
            f.Obj.transform.localScale = new Vector3(0.08f, 0.08f, len);
        }

        // Cannon：爆炸球在命中点膨胀 → 收缩
        // t: 0 = 开始, 1 = 结束
        void UpdateCannonFx(ref AttackEffect f, float t)
        {
            f.Obj.transform.position = f.Target;
            // 先膨胀到最大（t=0.4）再收缩
            float s;
            if (t < 0.4f)
                s = Mathf.Lerp(0f, 1.8f, t / 0.4f);
            else
                s = Mathf.Lerp(1.8f, 0f, (t - 0.4f) / 0.6f);
            f.Obj.transform.localScale = new Vector3(s, s * 0.4f, s);
        }

        // Magic：紫色光环在目标处脉冲，缩放 sin 波
        void UpdateMagicFx(ref AttackEffect f, float t)
        {
            f.Obj.transform.position = f.Target;
            float pulse = Mathf.Sin(t * Mathf.PI); // 0→1→0
            float s = 0.2f + pulse * 0.8f;
            f.Obj.transform.localScale = new Vector3(s, s, s);
        }

        void CaptureFrameShadow()
        {
            for (int i = 0; i < GameState.GridSize; i++)
                _prevCooldown[i] = _state.Grid[i].CooldownFrames;
        }

        // ==================== Mouse → Grid Cell ====================

        public bool TryGetGridCell(Vector2 screenPos, out int gx, out int gy)
        {
            gx = gy = -1;
            if (_cam == null) return false;

            Ray ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            Vector3 hit = ray.origin + t * ray.direction;

            gx = Mathf.FloorToInt(hit.x);
            gy = Mathf.FloorToInt(hit.z);

            if (!GameState.IsInBounds(gx, gy)) { gx = gy = -1; return false; }
            return true;
        }

        // ==================== Object Creation ====================

        void CreateMaterials()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _matGround    = new Material(sh) { color = new Color(0.13f, 0.14f, 0.16f) };
            _matGridLine  = new Material(sh) { color = new Color(0.22f, 0.24f, 0.28f) };
            _matBase      = new Material(sh) { color = new Color(0.9f, 0.7f, 0.1f) };
            _matArrow     = new Material(sh) { color = new Color(0.3f, 0.9f, 0.3f) };
            _matCannon    = new Material(sh) { color = new Color(0.9f, 0.5f, 0.1f) };
            _matMagic     = new Material(sh) { color = new Color(0.5f, 0.3f, 1.0f) };
            _matBasic     = new Material(sh) { color = new Color(0.9f, 0.2f, 0.2f) };
            _matFast      = new Material(sh) { color = new Color(1.0f, 0.6f, 0.1f) };
            _matTank      = new Material(sh) { color = new Color(0.5f, 0.1f, 0.1f) };
            _matBasicSlow = new Material(sh) { color = new Color(0.4f, 0.4f, 0.9f) };

            // 攻击特效材质
            _matFxArrow  = new Material(sh) { color = new Color(1.0f, 0.95f, 0.3f) }; // 明黄
            _matFxCannon = new Material(sh) { color = new Color(1.0f, 0.45f, 0.05f) }; // 橙红爆炸
            _matFxMagic  = new Material(sh) { color = new Color(0.75f, 0.3f, 1.0f) };  // 紫光
        }

        void CreateCamera()
        {
            var camObj = new GameObject("TD_Camera");
            camObj.transform.SetParent(transform);
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            _cam.orthographic = true;
            _cam.orthographicSize = 12f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.transform.position = new Vector3(MapCenter.x, CamHeight, MapCenter.z);
            _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var mainCam = Camera.main;
            if (mainCam != null && mainCam != _cam) mainCam.gameObject.SetActive(false);
        }

        void CreateGround()
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            obj.name = "TD_Ground";
            obj.transform.SetParent(transform);
            obj.transform.position = new Vector3(MapCenter.x, -0.01f, MapCenter.z);
            obj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            obj.transform.localScale = new Vector3(GameState.GridW, GameState.GridH, 1f);
            obj.GetComponent<Renderer>().sharedMaterial = _matGround;
            DestroyCollider(obj);
        }

        void CreateGridLines()
        {
            for (int x = 0; x <= GameState.GridW; x++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_VLine";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(x, 0f, MapCenter.z);
                obj.transform.localScale = new Vector3(0.02f, 0.01f, GameState.GridH);
                obj.GetComponent<Renderer>().sharedMaterial = _matGridLine;
                DestroyCollider(obj);
            }
            for (int z = 0; z <= GameState.GridH; z++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_HLine";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(MapCenter.x, 0f, z);
                obj.transform.localScale = new Vector3(GameState.GridW, 0.01f, 0.02f);
                obj.GetComponent<Renderer>().sharedMaterial = _matGridLine;
                DestroyCollider(obj);
            }
        }

        void CreateBase()
        {
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int cx = GameState.BaseCX + dx;
                    int cy = GameState.BaseCY + dy;
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    obj.name = "TD_Base";
                    obj.transform.SetParent(transform);
                    obj.transform.position = new Vector3(cx + 0.5f, 0.02f, cy + 0.5f);
                    obj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    obj.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                    obj.GetComponent<Renderer>().sharedMaterial = _matBase;
                    DestroyCollider(obj);
                }
            }
        }

        void CreateTowerPool()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                int cx = i % GameState.GridW;
                int cy = i / GameState.GridW;
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Tower";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(cx + 0.5f, 0.4f, cy + 0.5f);
                obj.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                _towerRend[i] = obj.GetComponent<Renderer>();
                _towerRend[i].sharedMaterial = _matArrow;
                DestroyCollider(obj);
                obj.SetActive(false);
                _towerObjs[i] = obj;
            }
        }

        void CreateEnemyPool()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Enemy";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                _enemyRend[i] = obj.GetComponent<Renderer>();
                _enemyRend[i].sharedMaterial = _matBasic;
                DestroyCollider(obj);
                obj.SetActive(false);
                _enemyObjs[i] = obj;
            }
        }

        void CreateFxPool()
        {
            for (int i = 0; i < FxPoolSize; i++)
            {
                // Arrow 用细长 Cube，Cannon/Magic 用 Sphere
                // 类型在 SpawnFx 时才确定，统一用 Cube（用途广）
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Fx";
                obj.transform.SetParent(transform);
                obj.transform.localScale = Vector3.one * 0.1f;
                DestroyCollider(obj);
                obj.SetActive(false);
                _fx[i] = new AttackEffect { Obj = obj };
            }
        }

        static void DestroyCollider(GameObject obj)
        {
            var col = obj.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        void OnDestroy()
        {
            Destroy(_matGround); Destroy(_matGridLine); Destroy(_matBase);
            Destroy(_matArrow); Destroy(_matCannon); Destroy(_matMagic);
            Destroy(_matBasic); Destroy(_matFast); Destroy(_matTank); Destroy(_matBasicSlow);
            Destroy(_matFxArrow); Destroy(_matFxCannon); Destroy(_matFxMagic);
        }
    }
}
