// BoomNetwork VampireSurvivors Demo — Network Manager
//
// Late-join: snapshot restores full game state for new players.
// Upgrade pause: game freezes when any player is choosing, all clients see it.

using UnityEngine;
using BoomNetwork.Client.FrameSync;
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
        bool _snapshotLoaded;
        bool _desyncDetected;
        uint _desyncFrame;
        byte _pendingUpgradeChoice;

        // Track which player IDs are in the room (for fresh-game init)
        readonly bool[] _knownPlayers = new bool[GameState.MaxPlayers];

        // Cached GUIStyles
        bool _stylesCached;
        GUIStyle _boxStyle, _titleStyle, _labelStyle, _btnStyle, _smallStyle, _pauseStyle;

        static readonly string[] WeaponNames = { "", "Knife", "Orb", "Lightning", "Holy Water" };
        static readonly string[] WeaponIcons = { "", "🗡", "🔮", "⚡", "💧" };

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
            c.OnDesyncDetected += OnDesync;

            _network.QuickStart();
        }

        void Update()
        {
            if (!_syncing) return;

            // Upgrade key presses
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

            if (h == 0f && v == 0f && ability == 0) return;
            VSInput.Encode(_inputBuf, h, v, ability);
            _network.SendInput(_inputBuf);
        }

        // ==================== Network Events ====================

        void OnFrameSyncStart(FrameSyncInitData init)
        {
            FInt dt = FInt.FromInt(init.FrameInterval) / FInt.FromInt(1000);
            _localSlot = _network.PlayerId - 1;

            if (!_snapshotLoaded)
            {
                // Fresh game — no snapshot from server, init all known players
                _sim.Init(dt, (uint)(init.StartTime & 0xFFFFFFFF));
                for (int i = 0; i < GameState.MaxPlayers; i++)
                    if (_knownPlayers[i]) _sim.State.InitPlayer(i);
            }
            else
            {
                // Late join — snapshot already loaded, just set Dt
                _sim.State.Dt = dt;
            }

            _syncing = true;

            _renderer = GetComponent<VSRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<VSRenderer>();
            _renderer.Init(_sim.State, _localSlot);

            Debug.Log($"[VS] FrameSync started. Slot={_localSlot}, snapshot={_snapshotLoaded}, dt={dt}, fps={init.FrameRate}");
        }

        void OnFrameSyncStop() { _syncing = false; }

        void OnJoinedRoom(int roomId, int[] existingPlayerIds)
        {
            // Only track who's in the room. DON'T InitPlayer for existing players —
            // their state comes from the snapshot (if any).
            foreach (int pid in existingPlayerIds)
            {
                int slot = pid - 1;
                if (slot >= 0 && slot < GameState.MaxPlayers)
                    _knownPlayers[slot] = true;
            }

            // Always init self (new player always starts fresh)
            int mySlot = _network.PlayerId - 1;
            if (mySlot >= 0 && mySlot < GameState.MaxPlayers)
            {
                _knownPlayers[mySlot] = true;
                // Don't InitPlayer here — wait for OnFrameSyncStart to decide
                // whether it's a fresh game or a snapshot-loaded late join.
            }

            Debug.Log($"[VS] Joined room {roomId}, {existingPlayerIds.Length} existing players");
        }

        void OnPlayerJoined(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
                _knownPlayers[slot] = true;
            // DON'T InitPlayer here — it's non-deterministic (fires at different times
            // on different clients). Instead, VSSimulation.ApplyInputs auto-inits when
            // the player's first input appears in FrameData (frame-exact, deterministic).
            Debug.Log($"[VS] Player {pid} joined (slot {slot}), will spawn on first input");
        }

        void OnPlayerLeft(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
            {
                _knownPlayers[slot] = false;
                _sim.State.Players[slot].IsActive = false;
                _sim.State.Players[slot].IsAlive = false;
            }
        }

        void OnFrame(FrameData frame)
        {
            _sim.Tick(frame);
            if (_renderer != null) _renderer.SyncVisuals();

            // Desync detection: send state hash to server every frame
            uint hash = _sim.State.ComputeHash();
            _network.Client.SendFrameHash(frame.FrameNumber, hash);
        }

        void OnDesync(FrameHashMismatch mismatch)
        {
            _desyncDetected = true;
            _desyncFrame = mismatch.FrameNumber;
            string detail = $"DESYNC at frame {mismatch.FrameNumber}:";
            foreach (var (pid, h) in mismatch.PlayerHashes)
                detail += $"\n  P{pid}: 0x{h:X8}";
            Debug.LogError($"[VS] {detail}");
        }

        byte[] TakeSnapshot() => VSSnapshot.Serialize(_sim.State);

        void LoadSnapshot(byte[] data)
        {
            _snapshotLoaded = true;
            VSSnapshot.Deserialize(data, _sim.State);

            // Init self in the loaded state (new player joining mid-game)
            int mySlot = _network.PlayerId - 1;
            if (mySlot >= 0 && mySlot < GameState.MaxPlayers
                && !_sim.State.Players[mySlot].IsActive)
            {
                _sim.State.InitPlayer(mySlot);
            }

            if (_renderer != null) _renderer.SyncVisuals();
            Debug.Log($"[VS] Snapshot loaded. Frame={_sim.State.FrameNumber}, Wave={_sim.State.WaveNumber}");
        }

        // ==================== OnGUI ====================

        void OnGUI()
        {
            if (!_syncing) return;
            CacheStyles();
            DrawStatusHUD();
            DrawDesyncOverlay();
            DrawPauseOverlay();
            DrawUpgradePanel();
        }

        void CacheStyles()
        {
            if (_stylesCached) return;
            _stylesCached = true;

            _boxStyle = new GUIStyle(GUI.skin.box)
                { normal = { background = MakeTex(1, 1, new Color(0, 0, 0, 0.7f)) } };
            _titleStyle = new GUIStyle(GUI.skin.label)
                { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = Color.white }, richText = true };
            _labelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 12, normal = { textColor = Color.white }, richText = true };
            _btnStyle = new GUIStyle(GUI.skin.button)
                { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _smallStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }, richText = true };
            _pauseStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                  normal = { textColor = new Color(1f, 1f, 0.3f) }, richText = true };
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

            GUI.Label(new Rect(x + 5, y, w, 20),
                $"<b>Vampire Survivors</b>  F:{state.FrameNumber}  RTT:{_network.Client.RttMs}ms", _titleStyle);
            y += 20;
            GUI.Label(new Rect(x + 5, y, w, 18),
                $"Wave {state.WaveNumber}  Enemies: {aliveEnemies}/{GameState.MaxEnemies}", _labelStyle);
            y += 20;

            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref state.Players[i];
                if (!p.IsActive) continue;
                string me = (i == _localSlot) ? "★" : " ";
                string hp = p.IsAlive ? $"<color=#88ff88>HP {p.Hp}/{p.MaxHp}</color>" : "<color=red>DEAD</color>";
                string upgrading = p.PendingLevelUp ? " <color=yellow>[CHOOSING...]</color>" : "";
                string weapons = GetWeaponString(ref p);
                GUI.Label(new Rect(x + 5, y, w, 20),
                    $"{me}P{i + 1} {hp} Lv{p.Level} K:{p.KillCount} {weapons}{upgrading}", _labelStyle);
                y += 22;
            }

            y += 4;
            GUI.Label(new Rect(x + 5, y, w, 16),
                $"{aliveEnemies} enemies, 0 extra bandwidth (pure FrameSync)", _smallStyle);
        }

        void DrawDesyncOverlay()
        {
            if (!_desyncDetected) return;
            float w = 400, h = 60;
            float px = (Screen.width - w) / 2f;
            float py = Screen.height * 0.2f;
            GUI.Box(new Rect(px, py, w, h), "", _boxStyle);
            GUI.Label(new Rect(px, py, w, h),
                $"<color=red><b>DESYNC DETECTED</b></color>\nFrame {_desyncFrame} — State hashes differ. Game paused.", _pauseStyle);
        }

        void DrawPauseOverlay()
        {
            // Find who's upgrading
            int upgradingSlot = -1;
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                if (_sim.State.Players[i].IsActive && _sim.State.Players[i].PendingLevelUp)
                { upgradingSlot = i; break; }
            }
            if (upgradingSlot < 0) return;
            if (upgradingSlot == _localSlot) return; // local player sees upgrade panel instead

            float w = 300, h = 50;
            float px = (Screen.width - w) / 2f;
            float py = Screen.height * 0.3f;
            GUI.Box(new Rect(px, py, w, h), "", _boxStyle);
            GUI.Label(new Rect(px, py, w, h),
                $"PAUSED\nP{upgradingSlot + 1} is choosing an upgrade...", _pauseStyle);
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
            for (int i = 0; i < 4; i++)
            {
                WeaponType wt = (WeaponType)(i + 1);
                int existingSlot = player.FindWeaponSlot(wt);
                string label = existingSlot >= 0
                    ? $"[{i + 1}] {WeaponIcons[(int)wt]} {WeaponNames[(int)wt]} Lv{player.GetWeapon(existingSlot).Level} → Lv{player.GetWeapon(existingSlot).Level + 1}"
                    : $"[{i + 1}] {WeaponIcons[(int)wt]} {WeaponNames[(int)wt]} (NEW)";

                if (GUI.Button(new Rect(px + 10, btnY, panelW - 20, 36), label, _btnStyle))
                    _pendingUpgradeChoice = (byte)(1 << i);
                btnY += 40;
            }
        }

        string GetWeaponString(ref PlayerState p)
        {
            string s = "";
            for (int i = 0; i < PlayerState.MaxWeaponSlots; i++)
            {
                var w = p.GetWeapon(i);
                if (w.Type == WeaponType.None) continue;
                if (s.Length > 0) s += " ";
                s += $"{WeaponIcons[(int)w.Type]}{w.Level}";
            }
            return s;
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
            tex.SetPixels(pix); tex.Apply();
            return tex;
        }
    }
}
