// BoomNetwork TowerDefense Demo — Network Manager
//
// DESIGN PRINCIPLE — Deterministic paths (GameState mutation):
//   All GameState mutations go through one path driven by FrameData:
//   OnFrame → TDSimulation.Tick → ApplyInputs
//   No direct GameState mutation outside frame processing.
//
// SILENT WHEN IDLE (Red Line #5):
//   Input is only sent when the player clicks a cell to place/sell a tower.
//   Idle frames send nothing. Tower placement is immediate (Red Line #4).
//
// BANDWIDTH CARVE-OUT:
//   100 or 200 enemies on screen = zero extra bandwidth. Only tower
//   placement commands traverse the wire. Capture this in OnGUI.

using UnityEngine;
using BoomNetwork.Client.FrameSync;
using BoomNetwork.Core.FrameSync;
using BoomNetwork.Unity;

namespace BoomNetwork.Samples.TowerDefense
{
    [RequireComponent(typeof(BoomNetworkManager))]
    public class TDNetworkManager : MonoBehaviour
    {
        BoomNetworkManager _network;
        TDSimulation _sim;
        TDRenderer _renderer;

        readonly byte[] _inputBuf = new byte[TDInput.InputSize];
        bool _syncing;
        bool _snapshotLoaded;
        bool _desyncDetected;
        uint _desyncFrame;

        // Tower placement UI state
        TowerType _selectedTower = TowerType.Arrow;
        bool _sellMode;

        // Cached GUIStyles
        bool _stylesCached;
        GUIStyle _boxStyle, _titleStyle, _labelStyle, _btnStyle, _smallStyle, _btnSelectedStyle;

        static readonly string[] TowerNames  = { "", "弓箭塔", "炮台", "魔法塔" };
        static readonly string[] TowerIcons  = { "", "\u2191", "\uD83D\uDCA3", "\u2605" };
        static readonly Color[]  TowerColors =
        {
            Color.white,
            new Color(0.7f, 1f, 0.7f),
            new Color(1f, 0.7f, 0.4f),
            new Color(0.6f, 0.6f, 1f),
        };

        void Start()
        {
            _sim     = new TDSimulation();
            _network = GetComponent<BoomNetworkManager>();
            var c    = _network.Client;

            c.OnFrameSyncStart  += OnFrameSyncStart;
            c.OnFrameSyncStop   += OnFrameSyncStop;
            c.OnFrame           += OnFrame;
            c.OnJoinedRoom      += OnJoinedRoom;
            c.OnPlayerJoined    += OnPlayerJoined;
            c.OnPlayerLeft      += OnPlayerLeft;
            c.OnTakeSnapshot     = TakeSnapshot;
            c.OnLoadSnapshot     = LoadSnapshot;
            c.OnDesyncDetected  += OnDesync;

            _network.QuickStart();
        }

        void Update()
        {
            if (!_syncing || _renderer == null) return;

            // Keyboard shortcut: 1/2/3 = tower type, S = sell mode
            if (Input.GetKeyDown(KeyCode.Alpha1)) { _selectedTower = TowerType.Arrow;  _sellMode = false; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { _selectedTower = TowerType.Cannon; _sellMode = false; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { _selectedTower = TowerType.Magic;  _sellMode = false; }
            if (Input.GetKeyDown(KeyCode.S))        _sellMode = !_sellMode;

            // Mouse click → send input
            if (Input.GetMouseButtonDown(0))
            {
                if (_renderer.TryGetGridCell(Input.mousePosition, out int gx, out int gy))
                    SendAction(gx, gy);
            }
        }

        void SendAction(int gx, int gy)
        {
            byte towerByte = _sellMode
                ? TDInput.SellAction
                : (byte)_selectedTower;

            TDInput.Encode(_inputBuf, gx, gy, towerByte);
            _network.SendInput(_inputBuf);
        }

        // ==================== Network Events ====================

        void OnFrameSyncStart(FrameSyncInitData init)
        {
            uint seed = (uint)(init.StartTime & 0xFFFFFFFF);

            if (!_snapshotLoaded)
                _sim.Init(seed);

            _syncing = true;

            _renderer = GetComponent<TDRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<TDRenderer>();
            _renderer.Init(_sim.State);

            Debug.Log($"[TD] FrameSync started. Pid={_network.PlayerId}, snapshot={_snapshotLoaded}, fps={init.FrameRate}");
        }

        void OnFrameSyncStop() { _syncing = false; }

        void OnJoinedRoom(int roomId, int[] existingPlayerIds)
        {
            Debug.Log($"[TD] Joined room {roomId}, {existingPlayerIds.Length} existing players");
        }

        void OnPlayerJoined(int pid)
        {
            _sim.PidToSlot(pid); // register mapping deterministically
            Debug.Log($"[TD] Player {pid} joined");
        }

        void OnPlayerLeft(int pid)
        {
            Debug.Log($"[TD] Player {pid} left");
        }

        void OnFrame(FrameData frame)
        {
            if (_desyncDetected) return;

            _sim.Tick(frame);
            if (_renderer != null) _renderer.SyncVisuals();

            uint hash = _sim.State.ComputeHash();
            _network.Client.SendFrameHash(frame.FrameNumber, hash);
        }

        void OnDesync(FrameHashMismatch mismatch)
        {
            _desyncDetected = true;
            _desyncFrame    = mismatch.FrameNumber;
            string detail   = $"DESYNC at frame {mismatch.FrameNumber}:";
            foreach (var (pid, h) in mismatch.PlayerHashes)
                detail += $"\n  P{pid}: 0x{h:X8}";
            Debug.LogError($"[TD] {detail}");
        }

        byte[] TakeSnapshot() => _syncing ? TDSnapshot.Serialize(_sim) : null;

        void LoadSnapshot(byte[] data)
        {
            _snapshotLoaded = true;
            TDSnapshot.Deserialize(data, _sim);
            if (_renderer != null) _renderer.SyncVisuals();
            Debug.Log($"[TD] Snapshot loaded. Frame={_sim.State.FrameNumber}, Wave={_sim.State.Wave.WaveNumber}");
        }

        // ==================== OnGUI ====================

        void OnGUI()
        {
            if (!_syncing) return;
            CacheStyles();
            DrawStatusHUD();
            DrawTowerPalette();
            DrawDesyncOverlay();
            DrawGameOverOverlay();
        }

        void CacheStyles()
        {
            if (_stylesCached) return;
            _stylesCached = true;

            _boxStyle = new GUIStyle(GUI.skin.box)
                { normal = { background = MakeTex(1, 1, new Color(0, 0, 0, 0.75f)) } };
            _titleStyle = new GUIStyle(GUI.skin.label)
                { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = Color.white }, richText = true };
            _labelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 12, normal = { textColor = Color.white }, richText = true };
            _btnStyle = new GUIStyle(GUI.skin.button)
                { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _smallStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }, richText = true };
            _btnSelectedStyle = new GUIStyle(_btnStyle)
                { normal = { textColor = Color.yellow, background = MakeTex(1, 1, new Color(0.3f, 0.3f, 0f, 0.9f)) } };
        }

        void DrawStatusHUD()
        {
            var state  = _sim.State;
            int alive  = CountAliveEnemies();
            string waveInfo;
            if (state.Wave.AllWavesDone && alive == 0)
                waveInfo = "<color=yellow>所有波次已清除！</color>";
            else if (state.Wave.SpawnRemaining > 0)
                waveInfo = $"第 <b>{state.Wave.WaveNumber}</b>/{GameState.MaxWaves} 波  剩余敌人：{state.Wave.SpawnRemaining}";
            else
                waveInfo = $"第 <b>{state.Wave.WaveNumber}</b>/{GameState.MaxWaves} 波  下波倒计时：{state.Wave.InterWaveTimer / 30}s";

            float x = 10, y = 10, w = 340;
            GUI.Box(new Rect(x, y, w, 90), "", _boxStyle);
            y += 5;
            GUI.Label(new Rect(x + 5, y, w, 20),
                $"<b>塔防守卫</b>  帧：{state.FrameNumber}  延迟：{_network.Client.RttMs}ms", _titleStyle);
            y += 22;
            GUI.Label(new Rect(x + 5, y, w, 20),
                $"基地血量：<color=#ff8888>{state.BaseHp}</color>/3   金币：<color=#ffdd44>{state.Gold}</color>", _labelStyle);
            y += 22;
            GUI.Label(new Rect(x + 5, y, w, 20), waveInfo, _labelStyle);
            y += 20;
            GUI.Label(new Rect(x + 5, y, w, 16),
                $"屏幕上 {alive} 只敌人，帧包大小不变（纯帧同步）", _smallStyle);
        }

        void DrawTowerPalette()
        {
            float px = 10, py = Screen.height - 130, w = 340, h = 120;
            GUI.Box(new Rect(px, py, w, h), "", _boxStyle);
            py += 8;

            GUI.Label(new Rect(px + 5, py, w, 18), "建造  [1] 弓箭塔  [2] 炮台  [3] 魔法塔  [S] 出售", _labelStyle);
            py += 22;

            // Tower buttons
            float bw = 80, bh = 34, bx = px + 5;
            for (int i = 1; i <= 3; i++)
            {
                var tt    = (TowerType)i;
                bool sel  = !_sellMode && _selectedTower == tt;
                int cost  = GameState.GetTowerCost(tt);
                bool affordable = _sim.State.Gold >= cost;
                string label = $"{TowerIcons[i]} {TowerNames[i]}\n<size=10>{cost}金</size>";

                GUI.color = affordable ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                if (GUI.Button(new Rect(bx, py, bw, bh), label, sel ? _btnSelectedStyle : _btnStyle))
                {
                    _selectedTower = tt;
                    _sellMode = false;
                }
                GUI.color = Color.white;
                bx += bw + 4;
            }

            // Sell button
            if (GUI.Button(new Rect(bx, py, bw, bh), $"\u2715 出售\n<size=10>+{GameState.SellRefund}金</size>",
                _sellMode ? _btnSelectedStyle : _btnStyle))
                _sellMode = !_sellMode;

            py += 40;
            string modeStr = _sellMode
                ? "<color=red>出售模式：点击塔格将其出售</color>"
                : $"建造：<color=yellow>{TowerNames[(int)_selectedTower]}</color> — 点击地图放置";
            GUI.Label(new Rect(px + 5, py, w, 18), modeStr, _labelStyle);
        }

        void DrawDesyncOverlay()
        {
            if (!_desyncDetected) return;
            float w = 400, h = 60;
            float px = (Screen.width - w) / 2f;
            float py = Screen.height * 0.2f;
            GUI.Box(new Rect(px, py, w, h), "", _boxStyle);
            var style = new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                  normal = { textColor = Color.red }, richText = true };
            GUI.Label(new Rect(px, py, w, h),
                $"<b>检测到不同步</b>\n第 {_desyncFrame} 帧", style);
        }

        void DrawGameOverOverlay()
        {
            if (!_sim.IsGameOver()) return;
            string msg = _sim.IsVictory()
                ? "<color=yellow><b>胜利！</b></color>\n所有波次已通关！"
                : "<color=red><b>基地已失守</b></color>\n游戏结束";

            float w = 400, h = 80;
            float px = (Screen.width - w) / 2f;
            float py = Screen.height * 0.35f;
            GUI.Box(new Rect(px, py, w, h), "", _boxStyle);
            var style = new GUIStyle(GUI.skin.label)
                { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                  normal = { textColor = Color.white }, richText = true };
            GUI.Label(new Rect(px, py, w, h), msg, style);
        }

        int CountAliveEnemies()
        {
            int c = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (_sim.State.Enemies[i].IsAlive) c++;
            return c;
        }

        static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix); tex.Apply();
            return tex;
        }
    }
}
