// BoomNetwork VampireSurvivors Demo — Network Manager (Phase 2)
//
// Pure FrameSync mode with upgrade selection UI.
// Input byte[2] carries ability bitmask for weapon upgrade choices.

using UnityEngine;
using BoomNetwork.Core.FrameSync;
using BoomNetwork.Unity;

namespace BoomNetwork.Samples.VampireSurvivors
{
    [RequireComponent(typeof(BoomNetworkManager))]
    public class VSNetworkManager : MonoBehaviour
    {
        BoomNetworkManager _network;
        VSSimulation _sim;
        VSRenderer _renderer;

        readonly byte[] _inputBuf = new byte[VSInput.InputSize];
        float _sendTimer;
        int _localSlot = -1;
        bool _syncing;
        byte _pendingUpgradeChoice; // set by key press, sent next frame

        // Cached GUIStyles
        bool _stylesCached;
        GUIStyle _boxStyle, _titleStyle, _labelStyle, _btnStyle, _smallStyle;

        static readonly string[] WeaponNames = { "", "Knife", "Orb", "Lightning", "Holy Water" };
        static readonly string[] WeaponIcons = { "", "🗡", "🔮", "⚡", "💧" };
        static readonly string[] UpgradeDescs =
        {
            "Throwing Knife\n+1 knife / faster fire",
            "Magic Orb\nOrbiting damage balls",
            "Lightning\nChain strikes nearest",
            "Holy Water\nAoE damage puddle",
        };

        void Start()
        {
            _sim = new VSSimulation();
            _network = GetComponent<BoomNetworkManager>();
            var c = _network.Client;

            c.OnFrameSyncStart += OnFrameSyncStart;
            c.OnFrameSyncStop += OnFrameSyncStop;
            c.OnFrame += OnFrame;
            c.OnJoinedRoom += OnJoinedRoom;
            c.OnPlayerJoined += OnPlayerJoined;
            c.OnPlayerLeft += OnPlayerLeft;
            c.OnTakeSnapshot = TakeSnapshot;
            c.OnLoadSnapshot = LoadSnapshot;

            _network.QuickStart();
        }

        void Update()
        {
            if (!_syncing) return;

            // Check upgrade key presses
            if (_localSlot >= 0 && _localSlot < GameState.MaxPlayers
                && _sim.State.Players[_localSlot].PendingLevelUp)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) _pendingUpgradeChoice = 1;
                if (Input.GetKeyDown(KeyCode.Alpha2)) _pendingUpgradeChoice = 2;
                if (Input.GetKeyDown(KeyCode.Alpha3)) _pendingUpgradeChoice = 4;
                if (Input.GetKeyDown(KeyCode.Alpha4)) _pendingUpgradeChoice = 8;
            }

            _sendTimer += Time.deltaTime * 1000f;
            if (_sendTimer < 50f) return;
            _sendTimer -= 50f;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            byte ability = _pendingUpgradeChoice;
            _pendingUpgradeChoice = 0;

            // Silent When Idle: only skip if no input AND no ability
            if (h == 0f && v == 0f && ability == 0) return;

            VSInput.Encode(_inputBuf, h, v, ability);
            _network.SendInput(_inputBuf);
        }

        void OnFrameSyncStart(FrameSyncInitData init)
        {
            float dt = init.FrameInterval / 1000f;
            _localSlot = _network.PlayerId - 1;
            _sim.Init(dt, (uint)(init.StartTime & 0xFFFFFFFF));
            _syncing = true;

            _renderer = GetComponent<VSRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<VSRenderer>();
            _renderer.Init(_sim.State, _localSlot);

            Debug.Log($"[VS] FrameSync started. Slot={_localSlot}, dt={dt}s, fps={init.FrameRate}");
        }

        void OnFrameSyncStop() { _syncing = false; }

        void OnJoinedRoom(int roomId, int[] existingPlayerIds)
        {
            foreach (int pid in existingPlayerIds)
            {
                int slot = pid - 1;
                if (slot >= 0 && slot < GameState.MaxPlayers)
                    _sim.State.InitPlayer(slot);
            }
            int mySlot = _network.PlayerId - 1;
            if (mySlot >= 0 && mySlot < GameState.MaxPlayers)
                _sim.State.InitPlayer(mySlot);
        }

        void OnPlayerJoined(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
                _sim.State.InitPlayer(slot);
        }

        void OnPlayerLeft(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
            {
                _sim.State.Players[slot].IsActive = false;
                _sim.State.Players[slot].IsAlive = false;
            }
        }

        void OnFrame(FrameData frame)
        {
            _sim.Tick(frame);
            if (_renderer != null) _renderer.SyncVisuals();
        }

        byte[] TakeSnapshot() => VSSnapshot.Serialize(_sim.State);

        void LoadSnapshot(byte[] data)
        {
            VSSnapshot.Deserialize(data, _sim.State);
            if (_renderer != null) _renderer.SyncVisuals();
        }

        // ==================== OnGUI ====================

        void OnGUI()
        {
            if (!_syncing) return;
            CacheStyles();

            DrawStatusHUD();
            DrawUpgradePanel();
        }

        void CacheStyles()
        {
            if (_stylesCached) return;
            _stylesCached = true;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(1, 1, new Color(0, 0, 0, 0.7f)) }
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold, fontSize = 14,
                normal = { textColor = Color.white }, richText = true
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, normal = { textColor = Color.white }, richText = true
            };
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }, richText = true
            };
        }

        void DrawStatusHUD()
        {
            var state = _sim.State;
            int aliveEnemies = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (state.Enemies[i].IsAlive) aliveEnemies++;

            float w = 360, y = 10, x = 10;
            GUI.Box(new Rect(x, y, w, 30 + CountActivePlayers() * 22 + 30), "", _boxStyle);
            y += 5;

            GUI.Label(new Rect(x + 5, y, w, 20), $"<b>Vampire Survivors</b>  F:{state.FrameNumber}  RTT:{_network.Client.RttMs}ms", _titleStyle);
            y += 20;
            GUI.Label(new Rect(x + 5, y, w, 18), $"Wave {state.WaveNumber}  Enemies: {aliveEnemies}/{GameState.MaxEnemies}", _labelStyle);
            y += 20;

            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref state.Players[i];
                if (!p.IsActive) continue;

                string me = (i == _localSlot) ? "★" : " ";
                string hp = p.IsAlive ? $"<color=#88ff88>HP {p.Hp}/{p.MaxHp}</color>" : "<color=red>DEAD</color>";
                string weapons = GetWeaponString(ref p);
                GUI.Label(new Rect(x + 5, y, w, 20),
                    $"{me}P{i + 1} {hp} Lv{p.Level} K:{p.KillCount} {weapons}", _labelStyle);
                y += 22;
            }

            // Bandwidth callout
            y += 4;
            GUI.Label(new Rect(x + 5, y, w, 16),
                $"{aliveEnemies} enemies, 0 extra bandwidth (pure FrameSync)", _smallStyle);
        }

        string GetWeaponString(ref PlayerState p)
        {
            string s = "";
            for (int i = 0; i < PlayerState.MaxWeaponSlots; i++)
            {
                ref var w = ref p.GetWeapon(i);
                if (w.Type == WeaponType.None) continue;
                if (s.Length > 0) s += " ";
                s += $"{WeaponIcons[(int)w.Type]}{w.Level}";
            }
            return s;
        }

        void DrawUpgradePanel()
        {
            if (_localSlot < 0 || _localSlot >= GameState.MaxPlayers) return;
            ref var player = ref _sim.State.Players[_localSlot];
            if (!player.PendingLevelUp) return;

            float panelW = 400, panelH = 220;
            float px = (Screen.width - panelW) / 2f;
            float py = (Screen.height - panelH) / 2f;

            GUI.Box(new Rect(px, py, panelW, panelH), "", _boxStyle);
            GUI.Label(new Rect(px + 10, py + 10, panelW, 30),
                $"<color=yellow><b>LEVEL UP! (Lv.{player.Level})</b></color>  Choose upgrade:", _titleStyle);

            float btnY = py + 50;
            float btnH = 36;
            for (int i = 0; i < 4; i++)
            {
                WeaponType wt = (WeaponType)(i + 1);
                int existingSlot = player.FindWeaponSlot(wt);
                string label;

                if (existingSlot >= 0)
                {
                    int lv = player.GetWeapon(existingSlot).Level;
                    label = $"[{i + 1}] {WeaponIcons[(int)wt]} {WeaponNames[(int)wt]} Lv{lv} → Lv{lv + 1}";
                }
                else
                {
                    label = $"[{i + 1}] {WeaponIcons[(int)wt]} {WeaponNames[(int)wt]} (NEW)";
                }

                if (GUI.Button(new Rect(px + 10, btnY, panelW - 20, btnH), label, _btnStyle))
                {
                    _pendingUpgradeChoice = (byte)(1 << i);
                }
                btnY += btnH + 4;
            }
        }

        int CountActivePlayers()
        {
            int c = 0;
            for (int i = 0; i < GameState.MaxPlayers; i++)
                if (_sim.State.Players[i].IsActive) c++;
            return c;
        }

        static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
