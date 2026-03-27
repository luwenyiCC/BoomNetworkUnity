// BoomNetwork VampireSurvivors Demo — Master 3D Renderer
//
// Creates all visual objects programmatically (zero external assets).
// Object pools for enemies, projectiles, and gems.
// Reads GameState each frame and drives Transform positions.

using UnityEngine;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public class VSRenderer : MonoBehaviour
    {
        GameState _state;
        int _localSlot;
        bool _initialized;

        // Camera
        Camera _cam;
        const float CamHeight = 25f;
        const float CamAngle = 55f;

        // Materials (shared, created once)
        Material _matPlayer;
        Material _matEnemy;
        Material _matKnife;
        Material _matGem;
        Material _matGround;
        Material _matPlayerHit; // flash on invincibility

        // Player pool
        GameObject[] _playerObjs = new GameObject[GameState.MaxPlayers];
        Material[] _playerMats = new Material[GameState.MaxPlayers];

        // Enemy pool
        GameObject[] _enemyPool = new GameObject[GameState.MaxEnemies];

        // Projectile pool
        GameObject[] _projPool = new GameObject[GameState.MaxProjectiles];

        // Gem pool
        GameObject[] _gemPool = new GameObject[GameState.MaxGems];

        // Ground
        GameObject _ground;

        // Player colors
        static readonly Color[] PlayerColors =
        {
            new Color(0.2f, 0.6f, 1f),
            new Color(0.2f, 1f, 0.4f),
            new Color(1f, 0.9f, 0.2f),
            new Color(1f, 0.4f, 0.9f),
        };

        public void Init(GameState state, int localSlot)
        {
            _state = state;
            _localSlot = localSlot;

            if (_initialized) return;
            _initialized = true;

            CreateMaterials();
            CreateGround();
            CreateCamera();
            CreatePlayerPool();
            CreateEnemyPool();
            CreateProjectilePool();
            CreateGemPool();
            CreateLight();
        }

        void CreateMaterials()
        {
            // Try URP first, fallback to Standard
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            _matPlayer = new Material(shader) { color = PlayerColors[0] };
            _matEnemy = new Material(shader) { color = new Color(0.9f, 0.2f, 0.15f) };
            _matKnife = new Material(shader) { color = Color.white };
            _matGem = new Material(shader) { color = new Color(0.3f, 1f, 0.5f) };
            _matGround = new Material(shader) { color = new Color(0.15f, 0.15f, 0.2f) };
            _matPlayerHit = new Material(shader) { color = new Color(1f, 0.5f, 0.5f, 0.7f) };
        }

        void CreateCamera()
        {
            var camObj = new GameObject("VS_Camera");
            camObj.transform.SetParent(transform);
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 100f;
            // Position will be updated in SyncVisuals to follow local player

            // Disable any existing main camera
            var mainCam = Camera.main;
            if (mainCam != null && mainCam != _cam)
                mainCam.gameObject.SetActive(false);
        }

        void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ground.name = "VS_Ground";
            _ground.transform.SetParent(transform);
            float size = GameState.ArenaHalfSize * 2f;
            _ground.transform.localScale = new Vector3(size, size, 1f);
            _ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _ground.transform.position = new Vector3(0f, -0.01f, 0f);
            _ground.GetComponent<Renderer>().sharedMaterial = _matGround;
            // Remove collider
            var col = _ground.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Arena border lines (thin cubes)
            float hs = GameState.ArenaHalfSize;
            CreateBorderLine(0f, 0f, hs, size + 0.2f, 0.1f, 0.05f);      // top
            CreateBorderLine(0f, 0f, -hs, size + 0.2f, 0.1f, 0.05f);     // bottom
            CreateBorderLine(-hs, 0f, 0f, 0.05f, 0.1f, size + 0.2f);     // left
            CreateBorderLine(hs, 0f, 0f, 0.05f, 0.1f, size + 0.2f);      // right
        }

        void CreateBorderLine(float x, float y, float z, float sx, float sy, float sz)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "VS_Border";
            line.transform.SetParent(transform);
            line.transform.position = new Vector3(x, y, z);
            line.transform.localScale = new Vector3(sx, sy, sz);
            line.GetComponent<Renderer>().sharedMaterial = _matGem; // green border
            var col = line.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        void CreatePlayerPool()
        {
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.name = $"VS_Player_{i}";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
                var mat = new Material(_matPlayer) { color = PlayerColors[i] };
                obj.GetComponent<Renderer>().sharedMaterial = mat;
                _playerMats[i] = mat;
                var col = obj.GetComponent<Collider>();
                if (col != null) Destroy(col);
                obj.SetActive(false);
                _playerObjs[i] = obj;
            }
        }

        void CreateEnemyPool()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "VS_Enemy";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                obj.GetComponent<Renderer>().sharedMaterial = _matEnemy;
                var col = obj.GetComponent<Collider>();
                if (col != null) Destroy(col);
                obj.SetActive(false);
                _enemyPool[i] = obj;
            }
        }

        void CreateProjectilePool()
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "VS_Proj";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.1f, 0.1f, 0.35f);
                obj.GetComponent<Renderer>().sharedMaterial = _matKnife;
                var col = obj.GetComponent<Collider>();
                if (col != null) Destroy(col);
                obj.SetActive(false);
                _projPool[i] = obj;
            }
        }

        void CreateGemPool()
        {
            for (int i = 0; i < GameState.MaxGems; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                obj.name = "VS_Gem";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                obj.GetComponent<Renderer>().sharedMaterial = _matGem;
                var col = obj.GetComponent<Collider>();
                if (col != null) Destroy(col);
                obj.SetActive(false);
                _gemPool[i] = obj;
            }
        }

        void CreateLight()
        {
            var lightObj = new GameObject("VS_Light");
            lightObj.transform.SetParent(transform);
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
        }

        public void SyncVisuals()
        {
            if (!_initialized || _state == null) return;

            // --- Players ---
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref _state.Players[i];
                bool show = p.IsActive && p.IsAlive;
                _playerObjs[i].SetActive(show);
                if (!show) continue;

                _playerObjs[i].transform.position = new Vector3(p.PosX, 0.5f, p.PosZ);

                // Face movement direction
                if (p.FacingX != 0f || p.FacingZ != 0f)
                {
                    float angle = Mathf.Atan2(p.FacingX, p.FacingZ) * Mathf.Rad2Deg;
                    _playerObjs[i].transform.rotation = Quaternion.Euler(0f, angle, 0f);
                }

                // Flash on invincibility
                var rend = _playerObjs[i].GetComponent<Renderer>();
                rend.sharedMaterial = p.InvincibilityFrames > 0 && (_state.FrameNumber % 4 < 2)
                    ? _matPlayerHit
                    : _playerMats[i];
            }

            // --- Enemies ---
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref _state.Enemies[i];
                bool show = e.IsAlive;
                _enemyPool[i].SetActive(show);
                if (show)
                    _enemyPool[i].transform.position = new Vector3(e.PosX, 0.45f, e.PosZ);
            }

            // --- Projectiles ---
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var p = ref _state.Projectiles[i];
                bool show = p.IsAlive;
                _projPool[i].SetActive(show);
                if (!show) continue;

                _projPool[i].transform.position = new Vector3(p.PosX, 0.5f, p.PosZ);
                // Rotate knife to face travel direction
                if (p.DirX != 0f || p.DirZ != 0f)
                {
                    float angle = Mathf.Atan2(p.DirX, p.DirZ) * Mathf.Rad2Deg;
                    _projPool[i].transform.rotation = Quaternion.Euler(0f, angle, 0f);
                }
            }

            // --- Gems ---
            for (int i = 0; i < GameState.MaxGems; i++)
            {
                ref var g = ref _state.Gems[i];
                bool show = g.IsAlive;
                _gemPool[i].SetActive(show);
                if (show)
                {
                    // Slight float/bob animation
                    float bob = Mathf.Sin((_state.FrameNumber + i * 7) * 0.15f) * 0.1f;
                    _gemPool[i].transform.position = new Vector3(g.PosX, 0.2f + bob, g.PosZ);
                }
            }

            // --- Camera follow local player ---
            UpdateCamera();
        }

        void UpdateCamera()
        {
            if (_cam == null) return;

            Vector3 target = Vector3.zero;
            if (_localSlot >= 0 && _localSlot < GameState.MaxPlayers)
            {
                ref var p = ref _state.Players[_localSlot];
                if (p.IsActive)
                    target = new Vector3(p.PosX, 0f, p.PosZ);
            }

            // Position camera behind and above the player
            float rad = CamAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(0f, CamHeight * Mathf.Sin(rad), -CamHeight * Mathf.Cos(rad));
            Vector3 desired = target + offset;

            // Smooth follow
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desired, 0.1f);
            _cam.transform.LookAt(target + Vector3.up * 0.5f);
        }

        void OnDestroy()
        {
            // Cleanup materials
            if (_matPlayer != null) Destroy(_matPlayer);
            if (_matEnemy != null) Destroy(_matEnemy);
            if (_matKnife != null) Destroy(_matKnife);
            if (_matGem != null) Destroy(_matGem);
            if (_matGround != null) Destroy(_matGround);
            if (_matPlayerHit != null) Destroy(_matPlayerHit);
            for (int i = 0; i < _playerMats.Length; i++)
                if (_playerMats[i] != null) Destroy(_playerMats[i]);
        }
    }
}
