using System;
using BoomNetwork.Client.FrameSync;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    public enum PersonState
    {
        Idle,
        Connecting,
        Connected,
        InRoom,
        Syncing,
        Disconnected,
    }

    /// <summary>
    /// Person — 薄游戏层适配器
    ///
    /// 持有一个 FrameSyncClient，纯代理 + 事件桥接。
    /// 不包含任何帧同步/重连/快照协议逻辑。
    /// </summary>
    public class Person
    {
        // --- 状态（代理到 FrameSyncClient）---
        public PersonState State => _client == null ? PersonState.Idle : MapState(_client.CurrentState);
        public int PlayerId => _client?.PlayerId ?? 0;
        public int RoomId => _client?.RoomId ?? 0;
        public uint FrameNumber => _client?.LastFrameNumber ?? 0;
        public bool HasPreviousIdentity => _client?.HasPreviousIdentity ?? false;
        public FrameSyncInitData? GetFrameSyncInitData() => _client?.InitData;

        // --- 事件 ---
        public event Action<Person> OnConnected;
        public event Action<Person> OnJoinedRoom;
        public event Action<Person> OnReconnected;
        public event Action<Person> OnReady;
        public event Action<Person, int> OnLeftRoom;
        public event Action<Person, FrameSyncInitData> OnFrameSyncStart;
        public event Action<Person, FrameData> OnFrame;
        public event Action<Person, int> OnRemotePlayerJoined;
        public event Action<Person, int> OnRemotePlayerLeft;
        public event Action<Person, int> OnRemotePlayerOffline;
        public event Action<Person, int> OnRemotePlayerOnline;
        public event Action<Person> OnDisconnected;
        public event Action<Person, string> OnLog;

        // --- 快照回调（游戏层设置）---
        public Func<byte[]> TakeSnapshot;
        public Action<byte[]> LoadSnapshot;

        // --- 内部 ---
        private FrameSyncClient _client;
        private bool _eventsWired;

        // ===================== Lifecycle =====================

        public void Connect(NetworkConfig config)
        {
            if (_client == null)
            {
                _client = new FrameSyncClient(config.heartbeatIntervalMs, config.heartbeatTimeoutMs);
                WireEvents();
            }
            _client.Connect(config.host, config.port);
        }

        public void Tick(float deltaTimeMs) => _client?.Tick(deltaTimeMs);

        public void Disconnect()
        {
            _client?.Disconnect();
        }

        public void DisconnectAndClear()
        {
            _client?.DisconnectAndClear();
            _client = null;
            _eventsWired = false;
        }

        // ===================== Room (纯代理) =====================

        public void GetRooms(Action<RoomInfo[]> onResult) => _client?.GetRooms(onResult);
        public void CreateRoom(int maxPlayers, Action<int> onCreated) => _client?.CreateRoom(maxPlayers, onCreated);
        public void CreateAndJoinRoom(int maxPlayers) => _client?.CreateAndJoinRoom(maxPlayers);
        public void JoinRoom(int roomId) => _client?.JoinRoom(roomId);
        public void LeaveRoom() => _client?.LeaveRoom();
        public void RequestStart() => _client?.RequestStart();

        // ===================== Frame Sync (纯代理) =====================

        public void SendInput(byte[] data) => _client?.SendInput(data);
        public void PredictWithInput(float deltaTimeMs, byte[] data) => _client?.PredictWithInput(deltaTimeMs, data);

        // ===================== Test =====================

        public void SimulateNetworkDrop() => _client?.SimulateNetworkDrop();

        // ===================== Prediction (纯代理) =====================

        public void SetPrediction(BoomNetwork.Core.Prediction.PredictionManager prediction)
        {
            if (_client != null) _client.Prediction = prediction;
        }

        public void ClearPrediction()
        {
            if (_client != null) _client.Prediction = null;
        }

        // ===================== Event Wiring (一次) =====================

        private void WireEvents()
        {
            if (_eventsWired) return;
            _eventsWired = true;

            _client.OnConnected += () => OnConnected?.Invoke(this);
            _client.OnJoinedRoom += (rid, existing) =>
            {
                // 通知已有玩家
                foreach (var pid in existing)
                    OnRemotePlayerJoined?.Invoke(this, pid);
                OnJoinedRoom?.Invoke(this);
            };
            _client.OnReady += () => OnReady?.Invoke(this);
            _client.OnFrameSyncStart += data => OnFrameSyncStart?.Invoke(this, data);
            _client.OnFrame += frame => OnFrame?.Invoke(this, frame);
            _client.OnFrameSyncStop += () => { };
            _client.OnPlayerJoined += pid => OnRemotePlayerJoined?.Invoke(this, pid);
            _client.OnPlayerLeft += pid => OnRemotePlayerLeft?.Invoke(this, pid);
            _client.OnPlayerOffline += pid => OnRemotePlayerOffline?.Invoke(this, pid);
            _client.OnPlayerOnline += pid => OnRemotePlayerOnline?.Invoke(this, pid);
            _client.OnReconnected += () => OnReconnected?.Invoke(this);
            _client.OnDisconnected += () => OnDisconnected?.Invoke(this);
            _client.OnLeftRoom += oldPid => OnLeftRoom?.Invoke(this, oldPid);
            _client.OnError += err => OnLog?.Invoke(this, $"Error: {err}");
            _client.OnLog += msg => OnLog?.Invoke(this, msg);

            _client.OnTakeSnapshot = () => TakeSnapshot?.Invoke();
            _client.OnLoadSnapshot = data => LoadSnapshot?.Invoke(data);
        }

        private static PersonState MapState(FrameSyncClient.State s) => s switch
        {
            FrameSyncClient.State.Disconnected => PersonState.Disconnected,
            FrameSyncClient.State.Connecting => PersonState.Connecting,
            FrameSyncClient.State.Connected => PersonState.Connected,
            FrameSyncClient.State.InRoom => PersonState.InRoom,
            FrameSyncClient.State.Syncing => PersonState.Syncing,
            FrameSyncClient.State.Reconnecting => PersonState.Connecting,
            _ => PersonState.Idle,
        };
    }
}
