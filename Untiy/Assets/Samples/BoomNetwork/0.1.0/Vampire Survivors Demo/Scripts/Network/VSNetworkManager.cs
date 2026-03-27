// BoomNetwork VampireSurvivors Demo — Network Manager
//
// Pure FrameSync mode:
//   - Only player inputs (4 bytes each) travel over the wire
//   - All enemies/projectiles/XP simulated deterministically in OnFrame
//   - 512 enemies on screen with ~800 bytes/sec total bandwidth
//
// Usage: Attach to a GameObject alongside BoomNetworkManager.
//        Set BoomNetworkManager.matchKey = "vampiresurvivors"

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
        int _localSlot = -1; // 0-based player slot
        bool _syncing;

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

            _sendTimer += Time.deltaTime * 1000f;
            if (_sendTimer < 50f) return;
            _sendTimer -= 50f;

            // Collect local input
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // Silent When Idle: don't send if no input
            if (h == 0f && v == 0f) return;

            VSInput.Encode(_inputBuf, h, v);
            _network.SendInput(_inputBuf);
        }

        void OnFrameSyncStart(FrameSyncInitData init)
        {
            float dt = init.FrameInterval / 1000f;
            _localSlot = _network.PlayerId - 1;
            _sim.Init(dt, (uint)(init.StartTime & 0xFFFFFFFF));
            _syncing = true;

            // Init renderer
            _renderer = GetComponent<VSRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<VSRenderer>();
            _renderer.Init(_sim.State, _localSlot);

            Debug.Log($"[VS] FrameSync started. LocalSlot={_localSlot}, dt={dt}s, FrameRate={init.FrameRate}fps");
        }

        void OnFrameSyncStop()
        {
            _syncing = false;
            Debug.Log("[VS] FrameSync stopped.");
        }

        void OnJoinedRoom(int roomId, int[] existingPlayerIds)
        {
            Debug.Log($"[VS] Joined room {roomId}, {existingPlayerIds.Length} existing players");

            // Activate slots for existing players
            foreach (int pid in existingPlayerIds)
            {
                int slot = pid - 1;
                if (slot >= 0 && slot < GameState.MaxPlayers)
                    _sim.State.InitPlayer(slot);
            }

            // Activate self
            int mySlot = _network.PlayerId - 1;
            if (mySlot >= 0 && mySlot < GameState.MaxPlayers)
                _sim.State.InitPlayer(mySlot);
        }

        void OnPlayerJoined(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
                _sim.State.InitPlayer(slot);
            Debug.Log($"[VS] Player {pid} joined (slot {slot})");
        }

        void OnPlayerLeft(int pid)
        {
            int slot = pid - 1;
            if (slot >= 0 && slot < GameState.MaxPlayers)
            {
                _sim.State.Players[slot].IsActive = false;
                _sim.State.Players[slot].IsAlive = false;
            }
            Debug.Log($"[VS] Player {pid} left");
        }

        void OnFrame(FrameData frame)
        {
            _sim.Tick(frame);
            if (_renderer != null) _renderer.SyncVisuals();
        }

        byte[] TakeSnapshot()
        {
            return VSSnapshot.Serialize(_sim.State);
        }

        void LoadSnapshot(byte[] data)
        {
            VSSnapshot.Deserialize(data, _sim.State);
            if (_renderer != null) _renderer.SyncVisuals();
            Debug.Log($"[VS] Snapshot loaded. Frame={_sim.State.FrameNumber}, Wave={_sim.State.WaveNumber}");
        }

        void OnGUI()
        {
            if (!_syncing) return;
            var state = _sim.State;

            // Count alive enemies
            int aliveEnemies = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (state.Enemies[i].IsAlive) aliveEnemies++;

            int y = 10;
            GUI.Label(new Rect(10, y, 500, 22),
                $"<b>Vampire Survivors Demo</b>  |  Frame: {state.FrameNumber}  RTT: {_network.Client.RttMs}ms");
            y += 22;
            GUI.Label(new Rect(10, y, 400, 22),
                $"Wave: {state.WaveNumber}  Enemies: {aliveEnemies}/{GameState.MaxEnemies}  Players: {CountActivePlayers()}/{GameState.MaxPlayers}");
            y += 28;

            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref state.Players[i];
                if (!p.IsActive) continue;
                string alive = p.IsAlive ? $"HP {p.Hp}/{p.MaxHp}" : "<color=red>DEAD</color>";
                string me = (i == _localSlot) ? " ★" : "";
                GUI.Label(new Rect(10, y, 500, 20),
                    $"[P{i + 1}{me}] {alive}  Lv.{p.Level}  XP: {p.Xp}/{p.XpToNextLevel}  Kills: {p.KillCount}");
                y += 20;
            }

            // Bandwidth callout
            y += 10;
            GUI.Label(new Rect(10, y, 500, 20),
                $"<color=#888>Bandwidth: ~{CountActivePlayers() * 4 * 20} B/s up | {aliveEnemies} enemies = 0 extra bytes (pure FrameSync)</color>");
        }

        int CountActivePlayers()
        {
            int c = 0;
            for (int i = 0; i < GameState.MaxPlayers; i++)
                if (_sim.State.Players[i].IsActive) c++;
            return c;
        }
    }
}
