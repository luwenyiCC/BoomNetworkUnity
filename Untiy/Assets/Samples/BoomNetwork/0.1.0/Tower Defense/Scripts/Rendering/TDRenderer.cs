// BoomNetwork TowerDefense Demo — Renderer (Visual Upgrade)
//
// 摄像机：70° 斜视角，正交，可见塔高和 3D 感
// 塔外形：弓箭塔=细高圆柱 / 炮台=宽矮圆柱+球 / 魔法塔=胶囊+顶球
// 敌人：Basic=方块 / Fast=旋转45°菱形 / Tank=宽圆柱；HP 越低越缩小
// 基地：金色光标脉动
// 攻击特效：独立三池（Arrow/Cannon/Magic）

using UnityEngine;

namespace BoomNetwork.Samples.TowerDefense
{
    public class TDRenderer : MonoBehaviour
    {
        GameState _state;
        bool _initialized;
        Camera _cam;

        // ==================== Materials ====================
        Material _matGround, _matGridEven, _matGridOdd, _matBase, _matBaseBeacon;
        // Tower materials: [type index 1-3][level 1-3] — pre-tinted per level
        Material[,] _matTowerBase = new Material[4, 4]; // [TowerType, level]
        Material[,] _matTowerDeco = new Material[4, 4];
        Material _matBasic, _matFast, _matTank, _matSlowed;
        Material _matFxArrow, _matFxBall, _matFxExplosion, _matFxMagic;
        // ==================== Cell highlight ====================
        GameObject _cellHighlight;
        Renderer   _cellHighlightRend;
        Material   _matCellHighlight;

        // ==================== Tower pool ====================
        // 每格：底座 + 顶部装饰（各有自己的 Renderer）
        GameObject[] _towerBase = new GameObject[GameState.GridSize];
        GameObject[] _towerDeco = new GameObject[GameState.GridSize]; // 炮球/魔法顶球/箭头
        Renderer[]   _towerBaseRend = new Renderer[GameState.GridSize];
        Renderer[]   _towerDecoRend = new Renderer[GameState.GridSize];

        // 塔攻击"后坐"缩放（纯视觉）
        float[] _towerPunchTimer = new float[GameState.GridSize];
        int[]   _prevCooldown    = new int[GameState.GridSize];

        // ==================== Enemy pool ====================
        GameObject[] _enemyObjs = new GameObject[GameState.MaxEnemies];
        Renderer[]   _enemyRend = new Renderer[GameState.MaxEnemies];
        int[]        _prevEnemyHp = new int[GameState.MaxEnemies]; // HP 变化检测

        // ==================== Base beacon ====================
        GameObject _baseBeacon;
        Renderer   _baseBeaconRend;

        // ==================== Arrow FX ====================
        const int ArrowFxPool = 24;
        struct ArrowFx { public bool Active; public Vector3 Origin, Target; public int FramesLeft; public GameObject Obj; }
        ArrowFx[] _arrowFx = new ArrowFx[ArrowFxPool];

        // ==================== Cannon FX ====================
        const int CannonFxPool = 12;
        const int CannonTravelFrames    = 15;
        const int CannonExplosionFrames = 10;
        struct CannonFx
        {
            public bool Active, Exploded;
            public Vector3 Origin, Target;
            public int FramesTotal, FramesLeft;
            public GameObject Ball, Explosion;
        }
        CannonFx[] _cannonFx = new CannonFx[CannonFxPool];

        // ==================== Magic FX ====================
        const int MagicFxPool = 16;
        struct MagicFx { public bool Active; public Vector3 Target; public int FramesTotal, FramesLeft; public GameObject Obj; }
        MagicFx[] _magicFx = new MagicFx[MagicFxPool];

        // ==================== Camera ====================
        // 70° 仰角斜视，能看到塔高度
        const float CamTiltX   = 70f;
        const float CamHeight  = 26f;
        const float CamOrtho   = 13f;
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
            CreateBase();
            CreateTowerPool();
            CreateEnemyPool();
            CreateArrowFxPool();
            CreateCannonFxPool();
            CreateMagicFxPool();
            CreateCellHighlight();

            for (int i = 0; i < GameState.GridSize; i++) _prevCooldown[i] = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++) _prevEnemyHp[i] = 0;
        }

        // ==================== SyncVisuals ====================

        public void SyncVisuals()
        {
            if (!_initialized || _state == null) return;
            SyncTowers();
            SyncEnemies();
            TickArrowFx();
            TickCannonFx();
            TickMagicFx();
            CaptureFrameShadow();
        }

        // ==================== Update（纯视觉动画） ====================

        void Update()
        {
            if (!_initialized || _state == null) return;
            AnimateBase();
            AnimateTowerPunch();
        }

        void AnimateBase()
        {
            if (_baseBeacon == null) return;
            float hp = _state.BaseHp;
            // 脉动：基地血量越低，脉动越快
            float speed = 1.5f + (3 - hp) * 1.2f;
            float pulse = Mathf.Sin(Time.time * speed) * 0.5f + 0.5f; // 0→1
            float s = Mathf.Lerp(0.6f, 1.1f, pulse);
            _baseBeacon.transform.localScale = new Vector3(s, s * 0.3f, s);
            // 颜色随血量：满血金色 → 半血橙色 → 低血红色
            Color baseColor = hp >= 3
                ? Color.Lerp(new Color(0.9f, 0.7f, 0.1f), new Color(1f, 0.9f, 0.3f), pulse)
                : hp == 2
                    ? Color.Lerp(new Color(0.9f, 0.4f, 0.1f), new Color(1f, 0.6f, 0.1f), pulse)
                    : Color.Lerp(new Color(0.8f, 0.1f, 0.1f), new Color(1f, 0.2f, 0.2f), pulse);
            _baseBeaconRend.material.color = baseColor;
        }

        void AnimateTowerPunch()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < GameState.GridSize; i++)
            {
                if (_towerPunchTimer[i] <= 0f) continue;
                _towerPunchTimer[i] -= dt * 12f; // 快速衰减
                if (_towerPunchTimer[i] < 0f) _towerPunchTimer[i] = 0f;
                // 后坐：先缩再弹回
                float punch = Mathf.Sin(_towerPunchTimer[i] * Mathf.PI);
                float s = 1f + punch * 0.25f;
                if (_towerBase[i].activeSelf)
                {
                    ref var gt = ref _state.Grid[i];
                    _towerBase[i].transform.localScale = GetTowerBaseScale(gt.Type, gt.Level) * s;
                }
            }
        }

        // ==================== SyncTowers ====================

        void SyncTowers()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                ref var t = ref _state.Grid[i];
                bool show = t.Type != TowerType.None;
                _towerBase[i].SetActive(show);
                _towerDeco[i].SetActive(show);
                if (!show) continue;

                // Scale by level (punch animation may override momentarily)
                if (_towerPunchTimer[i] <= 0f)
                    _towerBase[i].transform.localScale = GetTowerBaseScale(t.Type, t.Level);

                int lv = Mathf.Clamp(t.Level, 1, 3);
                _towerBaseRend[i].sharedMaterial = _matTowerBase[(int)t.Type, lv];
                _towerDecoRend[i].sharedMaterial = _matTowerDeco[(int)t.Type, lv];

                // 开火检测
                int maxCd = GameState.GetTowerCooldown(t.Type, t.Level);
                if (t.CooldownFrames == maxCd && _prevCooldown[i] < maxCd)
                {
                    _towerPunchTimer[i] = 1f; // 触发后坐动画
                    int cx = i % GameState.GridW;
                    int cy = i / GameState.GridW;
                    Vector3 origin = new Vector3(cx + 0.5f, 1.0f, cy + 0.5f);
                    Vector3 target = FindNearestEnemyPos(origin, GameState.GetTowerRange(t.Type, t.Level).ToFloat());
                    SpawnFx(t.Type, origin, target);
                }
            }
        }

        Vector3 GetTowerBaseScale(TowerType type, int level = 1)
        {
            float s = 1f + (level - 1) * 0.22f; // L2: ×1.22, L3: ×1.44
            switch (type)
            {
                case TowerType.Arrow:  return new Vector3(0.32f * s, 1.3f  * s, 0.32f * s);
                case TowerType.Cannon: return new Vector3(0.75f * s, 0.45f * s, 0.75f * s);
                default:               return new Vector3(0.38f * s, 0.95f * s, 0.38f * s);
            }
        }

        Vector3 FindNearestEnemyPos(Vector3 towerPos, float range)
        {
            float rangeSq  = range * range;
            float bestDist = float.MaxValue;
            Vector3 best   = towerPos;
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

                float x = e.PosX.ToFloat(), z = e.PosZ.ToFloat();

                // HP 比例决定尺寸（血越少越小，有濒死感）
                int maxHp = GameState.GetEnemyHp(e.Type);
                float hpRatio = Mathf.Clamp01((float)e.Hp / maxHp);
                float shrink = Mathf.Lerp(0.55f, 1.0f, hpRatio);

                bool slowed = e.SlowFrames > 0;

                switch (e.Type)
                {
                    case EnemyType.Basic:
                        _enemyRend[i].sharedMaterial = slowed ? _matSlowed : _matBasic;
                        _enemyObjs[i].transform.position   = new Vector3(x, 0.25f * shrink, z);
                        _enemyObjs[i].transform.localScale  = new Vector3(0.5f * shrink, 0.5f * shrink, 0.5f * shrink);
                        break;
                    case EnemyType.Fast:
                        _enemyRend[i].sharedMaterial = slowed ? _matSlowed : _matFast;
                        // 旋转 45° 让它从俯视看是菱形
                        _enemyObjs[i].transform.rotation    = Quaternion.Euler(0f, 45f, 0f);
                        _enemyObjs[i].transform.position    = new Vector3(x, 0.2f * shrink, z);
                        _enemyObjs[i].transform.localScale  = new Vector3(0.45f * shrink, 0.35f * shrink, 0.45f * shrink);
                        break;
                    case EnemyType.Tank:
                        _enemyRend[i].sharedMaterial = slowed ? _matSlowed : _matTank;
                        _enemyObjs[i].transform.position   = new Vector3(x, 0.2f * shrink, z);
                        _enemyObjs[i].transform.localScale  = new Vector3(0.8f * shrink, 0.35f, 0.8f * shrink); // 宽矮
                        break;
                }
            }
        }

        void SpawnFx(TowerType type, Vector3 origin, Vector3 target)
        {
            switch (type)
            {
                case TowerType.Arrow:  SpawnArrow(origin, target);  break;
                case TowerType.Cannon: SpawnCannon(origin, target); break;
                case TowerType.Magic:  SpawnMagic(target);          break;
            }
        }

        // ==================== Arrow FX ====================

        void SpawnArrow(Vector3 origin, Vector3 target)
        {
            for (int i = 0; i < ArrowFxPool; i++)
            {
                ref var f = ref _arrowFx[i];
                if (f.Active) continue;
                f.Active = true; f.Origin = origin; f.Target = target; f.FramesLeft = 8;
                f.Obj.SetActive(true);
                return;
            }
        }

        void TickArrowFx()
        {
            for (int i = 0; i < ArrowFxPool; i++)
            {
                ref var f = ref _arrowFx[i];
                if (!f.Active) continue;
                f.FramesLeft--;
                if (f.FramesLeft <= 0) { f.Active = false; f.Obj.SetActive(false); continue; }

                float t   = 1f - f.FramesLeft / 8f;
                Vector3 pos = Vector3.Lerp(f.Origin, f.Target, t);
                f.Obj.transform.position = pos;
                Vector3 dir = f.Target - f.Origin;
                if (dir.sqrMagnitude > 0.001f)
                    f.Obj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                float len = Mathf.Max(0.2f, dir.magnitude * 0.4f);
                f.Obj.transform.localScale = new Vector3(0.1f, 0.1f, len);
            }
        }

        // ==================== Cannon FX ====================

        void SpawnCannon(Vector3 origin, Vector3 target)
        {
            for (int i = 0; i < CannonFxPool; i++)
            {
                ref var f = ref _cannonFx[i];
                if (f.Active) continue;
                f.Active = true; f.Exploded = false;
                f.Origin = origin; f.Target = target;
                f.FramesTotal = CannonTravelFrames; f.FramesLeft = CannonTravelFrames;
                f.Ball.transform.position   = new Vector3(origin.x, 1.5f, origin.z);
                f.Ball.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                f.Ball.SetActive(true);
                f.Explosion.SetActive(false);
                return;
            }
        }

        void TickCannonFx()
        {
            for (int i = 0; i < CannonFxPool; i++)
            {
                ref var f = ref _cannonFx[i];
                if (!f.Active) continue;
                f.FramesLeft--;

                if (!f.Exploded)
                {
                    float t = 1f - (float)f.FramesLeft / f.FramesTotal;
                    float px = Mathf.Lerp(f.Origin.x, f.Target.x, t);
                    float pz = Mathf.Lerp(f.Origin.z, f.Target.z, t);
                    float arcY = Mathf.Lerp(1.5f, 0.5f, t) + Mathf.Sin(t * Mathf.PI) * 1.5f;
                    f.Ball.transform.position   = new Vector3(px, arcY, pz);
                    float s = Mathf.Lerp(0.35f, 0.75f, t);
                    f.Ball.transform.localScale = new Vector3(s, s, s);

                    if (f.FramesLeft <= 0)
                    {
                        f.Exploded = true;
                        f.FramesTotal = CannonExplosionFrames; f.FramesLeft = CannonExplosionFrames;
                        f.Ball.SetActive(false);
                        f.Explosion.transform.position   = new Vector3(f.Target.x, 0.5f, f.Target.z);
                        f.Explosion.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                        f.Explosion.SetActive(true);
                    }
                }
                else
                {
                    float t = 1f - (float)f.FramesLeft / f.FramesTotal;
                    float s = t < 0.4f
                        ? Mathf.Lerp(0.5f, 4.5f, t / 0.4f)
                        : Mathf.Lerp(4.5f, 0.05f, (t - 0.4f) / 0.6f);
                    f.Explosion.transform.localScale = new Vector3(s, s * 0.3f, s);
                    if (f.FramesLeft <= 0) { f.Active = false; f.Explosion.SetActive(false); }
                }
            }
        }

        // ==================== Magic FX ====================

        void SpawnMagic(Vector3 target)
        {
            for (int i = 0; i < MagicFxPool; i++)
            {
                ref var f = ref _magicFx[i];
                if (f.Active) continue;
                f.Active = true; f.Target = target; f.FramesTotal = 12; f.FramesLeft = 12;
                f.Obj.transform.position = target;
                f.Obj.SetActive(true);
                return;
            }
        }

        void TickMagicFx()
        {
            for (int i = 0; i < MagicFxPool; i++)
            {
                ref var f = ref _magicFx[i];
                if (!f.Active) continue;
                f.FramesLeft--;
                if (f.FramesLeft <= 0) { f.Active = false; f.Obj.SetActive(false); continue; }
                float t     = 1f - (float)f.FramesLeft / f.FramesTotal;
                float pulse = Mathf.Sin(t * Mathf.PI);
                float s     = 0.15f + pulse * 1.2f;
                f.Obj.transform.localScale = new Vector3(s, s * 0.25f, s);
            }
        }

        // ==================== Shadow Copy ====================

        void CaptureFrameShadow()
        {
            for (int i = 0; i < GameState.GridSize; i++)   _prevCooldown[i]  = _state.Grid[i].CooldownFrames;
            for (int i = 0; i < GameState.MaxEnemies; i++) _prevEnemyHp[i]   = _state.Enemies[i].Hp;
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

        public void SetCellHighlight(int gx, int gy)
        {
            if (_cellHighlight == null) return;
            if (gx < 0 || !GameState.IsInBounds(gx, gy))
            {
                _cellHighlight.SetActive(false);
                return;
            }
            _cellHighlight.transform.position = new Vector3(gx + 0.5f, 0.03f, gy + 0.5f);
            _cellHighlight.SetActive(true);
        }

        public Vector2 GetCellScreenCenter(int gx, int gy)
        {
            if (_cam == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 world  = new Vector3(gx + 0.5f, 0f, gy + 0.5f);
            Vector3 screen = _cam.WorldToScreenPoint(world);
            return new Vector2(screen.x, screen.y);
        }

        // ==================== Object Creation ====================

        void CreateMaterials()
        {
            // 优先 URP Lit，回退 Standard
            var sh  = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var unl = Shader.Find("Universal Render Pipeline/Unlit") ?? sh;

            // 地面与格子（棋盘格增强层次感）
            _matGround   = new Material(sh)  { color = new Color(0.10f, 0.11f, 0.14f) };
            _matGridEven = new Material(unl) { color = new Color(0.14f, 0.16f, 0.20f) };
            _matGridOdd  = new Material(unl) { color = new Color(0.17f, 0.19f, 0.24f) };

            // 基地
            _matBase       = new Material(sh) { color = new Color(0.80f, 0.60f, 0.10f) };
            _matBaseBeacon = new Material(sh) { color = new Color(1.00f, 0.85f, 0.20f) };

            // 塔：预生成 3 个等级的颜色（每级亮 25%）
            Color[] baseColors = { Color.clear,
                new Color(0.15f, 0.75f, 0.30f),  // Arrow base
                new Color(0.75f, 0.35f, 0.05f),  // Cannon base
                new Color(0.45f, 0.15f, 0.85f),  // Magic base
            };
            Color[] decoColors = { Color.clear,
                new Color(0.80f, 1.00f, 0.60f),  // Arrow deco
                new Color(0.25f, 0.25f, 0.30f),  // Cannon deco
                new Color(0.85f, 0.55f, 1.00f),  // Magic deco
            };
            for (int ti = 1; ti <= 3; ti++)
            {
                for (int lv = 1; lv <= 3; lv++)
                {
                    float t = 1f + (lv - 1) * 0.25f;
                    _matTowerBase[ti, lv] = new Material(sh) { color = baseColors[ti] * t };
                    _matTowerDeco[ti, lv] = new Material(sh) { color = decoColors[ti] * t };
                }
            }

            // 敌人
            _matBasic  = new Material(sh) { color = new Color(0.90f, 0.15f, 0.15f) }; // 红
            _matFast   = new Material(sh) { color = new Color(1.00f, 0.55f, 0.05f) }; // 橙
            _matTank   = new Material(sh) { color = new Color(0.40f, 0.05f, 0.05f) }; // 暗红
            _matSlowed = new Material(sh) { color = new Color(0.35f, 0.35f, 0.90f) }; // 减速蓝

            // FX
            _matFxArrow     = new Material(sh) { color = new Color(1.00f, 0.95f, 0.30f) };
            _matFxBall      = new Material(sh) { color = new Color(1.00f, 0.85f, 0.10f) };
            _matFxExplosion = new Material(sh) { color = new Color(1.00f, 0.35f, 0.00f) };
            _matFxMagic     = new Material(sh) { color = new Color(0.80f, 0.30f, 1.00f) };
        }

        void CreateCamera()
        {
            var camObj = new GameObject("TD_Camera");
            camObj.transform.SetParent(transform);
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f);
            _cam.orthographic = true;
            _cam.orthographicSize = CamOrtho;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 80f;

            // 70° 斜视角：摄像机在地图中心正上方偏后方
            float tiltRad = CamTiltX * Mathf.Deg2Rad;
            float camY    = CamHeight;
            float camBack = camY / Mathf.Tan(tiltRad); // 向后退以对准地图
            camObj.transform.position = new Vector3(MapCenter.x, camY, MapCenter.z - camBack);
            camObj.transform.rotation = Quaternion.Euler(CamTiltX, 0f, 0f);

            var mainCam = Camera.main;
            if (mainCam != null && mainCam != _cam) mainCam.gameObject.SetActive(false);
        }

        void CreateGround()
        {
            // 棋盘格：每格单独 Quad
            for (int cy = 0; cy < GameState.GridH; cy++)
            {
                for (int cx = 0; cx < GameState.GridW; cx++)
                {
                    var cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    cell.name = "TD_Cell";
                    cell.transform.SetParent(transform);
                    cell.transform.position = new Vector3(cx + 0.5f, 0f, cy + 0.5f);
                    cell.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    cell.transform.localScale = new Vector3(0.98f, 0.98f, 1f);
                    bool even = (cx + cy) % 2 == 0;
                    cell.GetComponent<Renderer>().sharedMaterial = even ? _matGridEven : _matGridOdd;
                    DestroyCollider(cell);
                }
            }
        }

        void CreateBase()
        {
            // 2×2 基地底板
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int cx = GameState.BaseCX + dx, cy = GameState.BaseCY + dy;
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    obj.name = "TD_BaseTile";
                    obj.transform.SetParent(transform);
                    obj.transform.position  = new Vector3(cx + 0.5f, 0.01f, cy + 0.5f);
                    obj.transform.rotation  = Quaternion.Euler(90f, 0f, 0f);
                    obj.transform.localScale = new Vector3(0.97f, 0.97f, 1f);
                    obj.GetComponent<Renderer>().sharedMaterial = _matBase;
                    DestroyCollider(obj);
                }
            }

            // 中心脉动光标（Sphere 扁平化）
            _baseBeacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _baseBeacon.name = "TD_BaseBeacon";
            _baseBeacon.transform.SetParent(transform);
            _baseBeacon.transform.position   = new Vector3(GameState.BaseCX + 1.0f, 0.2f, GameState.BaseCY + 1.0f);
            _baseBeacon.transform.localScale  = new Vector3(0.8f, 0.2f, 0.8f);
            _baseBeaconRend = _baseBeacon.GetComponent<Renderer>();
            _baseBeaconRend.material = new Material(_matBaseBeacon);
            DestroyCollider(_baseBeacon);
        }

        void CreateTowerPool()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                int cx = i % GameState.GridW, cy = i / GameState.GridW;
                float wx = cx + 0.5f, wz = cy + 0.5f;

                // 底座（弓箭=Cylinder，炮台=Cylinder，魔法=Cylinder）
                var tBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tBase.name = "TD_TowerBase";
                tBase.transform.SetParent(transform);
                tBase.transform.position   = new Vector3(wx, 0.4f, wz);
                tBase.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
                _towerBaseRend[i] = tBase.GetComponent<Renderer>();
                _towerBaseRend[i].sharedMaterial = _matTowerBase[1, 1];
                DestroyCollider(tBase);
                tBase.SetActive(false);
                _towerBase[i] = tBase;

                // 顶部装饰（弓箭=Cube尖顶，炮台=Sphere炮球，魔法=Sphere水晶球）
                var tDeco = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tDeco.name = "TD_TowerDeco";
                tDeco.transform.SetParent(transform);
                tDeco.transform.position   = new Vector3(wx, 1.1f, wz);
                tDeco.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                _towerDecoRend[i] = tDeco.GetComponent<Renderer>();
                _towerDecoRend[i].sharedMaterial = _matTowerDeco[1, 1];
                DestroyCollider(tDeco);
                tDeco.SetActive(false);
                _towerDeco[i] = tDeco;
            }
        }

        void CreateEnemyPool()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                // Basic/Tank 用 Cube，Fast 用 Cube（旋转 45°）
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Enemy";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                _enemyRend[i] = obj.GetComponent<Renderer>();
                _enemyRend[i].sharedMaterial = _matBasic;
                DestroyCollider(obj);
                obj.SetActive(false);
                _enemyObjs[i] = obj;
            }
        }

        void CreateArrowFxPool()
        {
            for (int i = 0; i < ArrowFxPool; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_ArrowFx";
                obj.transform.SetParent(transform);
                obj.GetComponent<Renderer>().sharedMaterial = _matFxArrow;
                DestroyCollider(obj);
                obj.SetActive(false);
                _arrowFx[i] = new ArrowFx { Obj = obj };
            }
        }

        void CreateCannonFxPool()
        {
            for (int i = 0; i < CannonFxPool; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "TD_CannonBall";
                ball.transform.SetParent(transform);
                ball.GetComponent<Renderer>().sharedMaterial = _matFxBall;
                DestroyCollider(ball);
                ball.SetActive(false);

                var expl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                expl.name = "TD_CannonExpl";
                expl.transform.SetParent(transform);
                expl.GetComponent<Renderer>().sharedMaterial = _matFxExplosion;
                DestroyCollider(expl);
                expl.SetActive(false);

                _cannonFx[i] = new CannonFx { Ball = ball, Explosion = expl };
            }
        }

        void CreateMagicFxPool()
        {
            for (int i = 0; i < MagicFxPool; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                obj.name = "TD_MagicFx";
                obj.transform.SetParent(transform);
                obj.GetComponent<Renderer>().sharedMaterial = _matFxMagic;
                DestroyCollider(obj);
                obj.SetActive(false);
                _magicFx[i] = new MagicFx { Obj = obj };
            }
        }

        void CreateCellHighlight()
        {
            _cellHighlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _cellHighlight.name = "TD_CellHighlight";
            _cellHighlight.transform.SetParent(transform);
            _cellHighlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _cellHighlight.transform.localScale = new Vector3(0.95f, 0.95f, 1f);
            _matCellHighlight = new Material(
                Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"))
            {
                color = new Color(1f, 0.92f, 0.2f, 0.55f)
            };
            // Enable transparency
            _matCellHighlight.SetFloat("_Surface", 1f);
            _matCellHighlight.renderQueue = 3000;
            _cellHighlightRend = _cellHighlight.GetComponent<Renderer>();
            _cellHighlightRend.material = _matCellHighlight;
            DestroyCollider(_cellHighlight);
            _cellHighlight.SetActive(false);
        }

        static void DestroyCollider(GameObject obj)
        {
            var col = obj.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        void OnDestroy()
        {
            Destroy(_matGround); Destroy(_matGridEven); Destroy(_matGridOdd);
            Destroy(_matBase); Destroy(_matBaseBeacon);
            Destroy(_matBasic); Destroy(_matFast); Destroy(_matTank); Destroy(_matSlowed);
            Destroy(_matFxArrow); Destroy(_matFxBall); Destroy(_matFxExplosion); Destroy(_matFxMagic);
            Destroy(_matCellHighlight);
            for (int ti = 1; ti <= 3; ti++)
                for (int lv = 1; lv <= 3; lv++)
                {
                    Destroy(_matTowerBase[ti, lv]);
                    Destroy(_matTowerDeco[ti, lv]);
                }
        }
    }
}
