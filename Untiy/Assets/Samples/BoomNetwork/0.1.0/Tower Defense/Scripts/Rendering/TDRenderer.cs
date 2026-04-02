// BoomNetwork TowerDefense Demo — Renderer
//
// Top-down orthographic view. Color-coded Quads and Cubes, no external art assets.
//
// 攻击特效（纯视觉层，不影响模拟状态）：
//   弓箭塔 — 黄色细长 Cube 从塔飞向目标，8 帧
//   炮台   — 两阶段：橙色 Sphere 炮弹飞行（前 10 帧）+ 橙红爆炸球膨胀（后 8 帧），共 18 帧
//   魔法塔 — 紫色 Sphere 在目标处 sin 脉冲，10 帧
//
// 三个独立 FX 池，避免几何体混用。

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
        Material _matFxArrow, _matFxBall, _matFxExplosion, _matFxMagic;

        // ==================== Tower / Enemy pools ====================
        GameObject[] _towerObjs = new GameObject[GameState.GridSize];
        Renderer[]   _towerRend = new Renderer[GameState.GridSize];
        GameObject[] _enemyObjs = new GameObject[GameState.MaxEnemies];
        Renderer[]   _enemyRend = new Renderer[GameState.MaxEnemies];

        // ==================== 弓箭塔 FX（Cube） ====================
        const int ArrowFxPool = 24;

        struct ArrowEffect
        {
            public bool      Active;
            public Vector3   Origin, Target;
            public int       FramesLeft;
            public GameObject Obj;
        }

        ArrowEffect[] _arrowFx = new ArrowEffect[ArrowFxPool];

        // ==================== 炮台 FX（Sphere ×2） ====================
        // 飞行弹 + 爆炸球各自独立生命周期
        const int CannonFxPool = 12;

        struct CannonBall
        {
            public bool      Active;
            public Vector3   Origin, Target;
            public int       FramesTotal, FramesLeft;
            public GameObject Ball;       // 飞行炮弹 Sphere
            public GameObject Explosion;  // 爆炸球 Sphere
            public bool      Exploded;    // 是否已进入爆炸阶段
        }

        CannonBall[] _cannonFx = new CannonBall[CannonFxPool];

        // ==================== 魔法塔 FX（Sphere） ====================
        const int MagicFxPool = 16;

        struct MagicEffect
        {
            public bool      Active;
            public Vector3   Target;
            public int       FramesTotal, FramesLeft;
            public GameObject Obj;
        }

        MagicEffect[] _magicFx = new MagicEffect[MagicFxPool];

        // ==================== Shadow copy ====================
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
            CreateArrowFxPool();
            CreateCannonFxPool();
            CreateMagicFxPool();

            for (int i = 0; i < GameState.GridSize; i++) _prevCooldown[i] = 0;
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

                // 开火检测：cooldown 本帧刚跳回最大值
                int maxCd = GameState.GetTowerCooldown(t.Type);
                if (t.CooldownFrames == maxCd && _prevCooldown[i] < maxCd)
                {
                    int cx = i % GameState.GridW;
                    int cy = i / GameState.GridW;
                    Vector3 origin = new Vector3(cx + 0.5f, 0.8f, cy + 0.5f);
                    Vector3 target = FindNearestEnemyPos(origin, GameState.GetTowerRange(t.Type).ToFloat());
                    SpawnFx(t.Type, origin, target);
                }
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

        void SpawnFx(TowerType type, Vector3 origin, Vector3 target)
        {
            switch (type)
            {
                case TowerType.Arrow:  SpawnArrow(origin, target);  break;
                case TowerType.Cannon: SpawnCannon(origin, target); break;
                case TowerType.Magic:  SpawnMagic(target);          break;
            }
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

        // ==================== 弓箭塔特效 ====================

        void SpawnArrow(Vector3 origin, Vector3 target)
        {
            for (int i = 0; i < ArrowFxPool; i++)
            {
                ref var f = ref _arrowFx[i];
                if (f.Active) continue;
                f.Active     = true;
                f.Origin     = origin;
                f.Target     = target;
                f.FramesLeft = 8;
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
                if (f.FramesLeft <= 0)
                {
                    f.Active = false;
                    f.Obj.SetActive(false);
                    continue;
                }

                float t   = 1f - f.FramesLeft / 8f; // 0→1
                Vector3 pos = Vector3.Lerp(f.Origin, f.Target, t);
                f.Obj.transform.position = pos;

                Vector3 dir = f.Target - f.Origin;
                if (dir.sqrMagnitude > 0.001f)
                    f.Obj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

                float len = Mathf.Max(0.2f, dir.magnitude * 0.4f);
                f.Obj.transform.localScale = new Vector3(0.1f, 0.1f, len);
            }
        }

        // ==================== 炮台特效 ====================
        // 两阶段：飞行炮弹（10 帧）→ 爆炸膨胀（8 帧）

        const int CannonTravelFrames    = 10;
        const int CannonExplosionFrames = 8;

        void SpawnCannon(Vector3 origin, Vector3 target)
        {
            for (int i = 0; i < CannonFxPool; i++)
            {
                ref var f = ref _cannonFx[i];
                if (f.Active) continue;
                f.Active      = true;
                f.Origin      = origin;
                f.Target      = target;
                f.FramesTotal = CannonTravelFrames;
                f.FramesLeft  = CannonTravelFrames;
                f.Exploded    = false;
                f.Ball.SetActive(true);
                f.Explosion.SetActive(false);
                f.Ball.transform.position   = origin;
                f.Ball.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
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
                    // 飞行阶段：炮弹从 Origin 飞向 Target
                    float t = 1f - (float)f.FramesLeft / f.FramesTotal;
                    f.Ball.transform.position = Vector3.Lerp(f.Origin, f.Target, t);

                    if (f.FramesLeft <= 0)
                    {
                        // 切换到爆炸阶段
                        f.Exploded    = true;
                        f.FramesTotal = CannonExplosionFrames;
                        f.FramesLeft  = CannonExplosionFrames;
                        f.Ball.SetActive(false);
                        f.Explosion.transform.position   = f.Target;
                        f.Explosion.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                        f.Explosion.SetActive(true);
                    }
                }
                else
                {
                    // 爆炸阶段：球体快速膨胀再收缩
                    float t = 1f - (float)f.FramesLeft / f.FramesTotal; // 0→1
                    float s;
                    if (t < 0.45f)
                        s = Mathf.Lerp(0.3f, 3.5f, t / 0.45f);   // 膨胀
                    else
                        s = Mathf.Lerp(3.5f, 0.1f, (t - 0.45f) / 0.55f); // 收缩
                    f.Explosion.transform.localScale = new Vector3(s, s * 0.5f, s);

                    if (f.FramesLeft <= 0)
                    {
                        f.Active = false;
                        f.Explosion.SetActive(false);
                    }
                }
            }
        }

        // ==================== 魔法塔特效 ====================

        void SpawnMagic(Vector3 target)
        {
            for (int i = 0; i < MagicFxPool; i++)
            {
                ref var f = ref _magicFx[i];
                if (f.Active) continue;
                f.Active      = true;
                f.Target      = target;
                f.FramesTotal = 10;
                f.FramesLeft  = 10;
                f.Obj.SetActive(true);
                f.Obj.transform.position = target;
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
                if (f.FramesLeft <= 0)
                {
                    f.Active = false;
                    f.Obj.SetActive(false);
                    continue;
                }

                float t     = 1f - (float)f.FramesLeft / f.FramesTotal;
                float pulse = Mathf.Sin(t * Mathf.PI); // 0→1→0
                float s     = 0.15f + pulse * 1.0f;
                f.Obj.transform.localScale = new Vector3(s, s * 0.3f, s); // 扁平光环
            }
        }

        // ==================== Shadow copy ====================

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

            _matFxArrow     = new Material(sh) { color = new Color(1.0f, 0.95f, 0.3f) };  // 明黄箭矢
            _matFxBall      = new Material(sh) { color = new Color(0.2f, 0.2f, 0.2f) };   // 深色炮弹
            _matFxExplosion = new Material(sh) { color = new Color(1.0f, 0.40f, 0.05f) }; // 橙红爆炸
            _matFxMagic     = new Material(sh) { color = new Color(0.75f, 0.3f, 1.0f) };  // 紫光
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
                _arrowFx[i] = new ArrowEffect { Obj = obj };
            }
        }

        void CreateCannonFxPool()
        {
            for (int i = 0; i < CannonFxPool; i++)
            {
                // 炮弹球
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "TD_CannonBall";
                ball.transform.SetParent(transform);
                ball.GetComponent<Renderer>().sharedMaterial = _matFxBall;
                DestroyCollider(ball);
                ball.SetActive(false);

                // 爆炸球（单独实例，比炮弹大得多）
                var expl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                expl.name = "TD_CannonExpl";
                expl.transform.SetParent(transform);
                expl.GetComponent<Renderer>().sharedMaterial = _matFxExplosion;
                DestroyCollider(expl);
                expl.SetActive(false);

                _cannonFx[i] = new CannonBall { Ball = ball, Explosion = expl };
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
                _magicFx[i] = new MagicEffect { Obj = obj };
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
            Destroy(_matFxArrow); Destroy(_matFxBall); Destroy(_matFxExplosion); Destroy(_matFxMagic);
        }
    }
}
