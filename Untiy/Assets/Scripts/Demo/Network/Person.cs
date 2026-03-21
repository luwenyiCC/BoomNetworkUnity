using System;
using BoomNetwork.Client.Transport;
using BoomNetwork.Client.Session;
using BoomNetwork.Client.Connection;
using BoomNetwork.Client.FrameSync;
using BoomNetwork.Client.Room;
using BoomNetwork.Core;
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
    /// Person — 一个完整的网络客户端身份
    /// 支持重连：断线后保留 PlayerId/RoomId/LastFrame，重新连接时发 Reconnect 恢复
    /// </summary>
    public class Person
    {
        public PersonState State { get; private set; } = PersonState.Idle;
        public int PlayerId { get; private set; }
        public int RoomId { get; private set; }
        public uint FrameNumber => _frameSync?.LastFrameNumber ?? _savedFrameNumber;
        public bool HasPreviousIdentity => PlayerId > 0 && RoomId > 0;

        // Fix #1: 断线时保存帧号，重连时发给服务器
        private uint _savedFrameNumber;

        // 内部网络栈
        private TcpClientTransport _transport;
        private NetworkSession _session;
        private ConnectionManager _connMgr;
        private RoomClient _roomClient;
        private FrameSyncClient _frameSync;
        private NetworkConfig _config;

        // 事件
        /// <summary>首次连接成功（非重连）</summary>
        public event Action<Person> OnConnected;
        /// <summary>首次加入房间</summary>
        public event Action<Person> OnJoinedRoom;
        /// <summary>重连成功（身份恢复）</summary>
        public event Action<Person> OnReconnected;
        /// <summary>准备就绪（首次加入 或 重连成功，统一回调）</summary>
        public event Action<Person> OnReady;
        /// <summary>离开房间（携带旧 PlayerId，用于清理 Entity）</summary>
        public event Action<Person, int> OnLeftRoom;
        public event Action<Person, FrameSyncInitData> OnFrameSyncStart;
        public event Action<Person, FrameData> OnFrame;
        public event Action<Person> OnDisconnected;
        public event Action<Person, string> OnLog;

        public void Connect(NetworkConfig config)
        {
            if (State != PersonState.Idle && State != PersonState.Disconnected)
                return;

            _config = config;
            State = PersonState.Connecting;

            _transport = new TcpClientTransport();
            _session = new NetworkSession(_transport);
            _connMgr = new ConnectionManager(_session, reconnectStrategy: null);
            _connMgr.HeartbeatIntervalMs = config.heartbeatIntervalMs;
            _connMgr.HeartbeatTimeoutMs = config.heartbeatTimeoutMs;

            _roomClient = new RoomClient(_session);
            _frameSync = new FrameSyncClient(_session, _connMgr, skipAutoSessionBind: HasPreviousIdentity);

            _connMgr.OnConnected += HandleConnected;
            _connMgr.OnDisconnected += HandleDisconnected;

            _frameSync.OnFrameSyncStart += data =>
            {
                State = PersonState.Syncing;
                Log($"FrameSync started (rate={data.FrameRate})");
                OnFrameSyncStart?.Invoke(this, data);
            };

            _frameSync.OnFrame += frame =>
            {
                OnFrame?.Invoke(this, frame);
            };

            _frameSync.OnError += err => Log($"Error: {err}");
            _roomClient.OnError += err => Log($"Room Error: {err}");

            _connMgr.Connect(config.host, config.port);
            Log($"Connecting to {config.host}:{config.port}..." +
                (HasPreviousIdentity ? $" (reconnecting as P{PlayerId}, lastFrame={_savedFrameNumber})" : ""));
        }

        void HandleConnected()
        {
            if (HasPreviousIdentity)
            {
                // Fix #1: 用保存的帧号，不是 FrameNumber（新 _frameSync 是 0）
                Log($"Sending Reconnect (pid={PlayerId}, lastFrame={_savedFrameNumber})");
                var data = new byte[8];
                BitConverter.GetBytes(PlayerId).CopyTo(data, 0);
                BitConverter.GetBytes((int)_savedFrameNumber).CopyTo(data, 4);

                _session.SendAsync(FrameSyncCmd.Reconnect, data, 5000,
                    onResponse: HandleReconnectResponse,
                    onTimeout: err =>
                    {
                        Log($"Reconnect timeout: {err}. Falling back to fresh connect.");
                        ClearIdentity();
                        State = PersonState.Connected;
                        OnConnected?.Invoke(this);
                    }
                );
            }
            else
            {
                State = PersonState.Connected;
                Log("Connected");
                OnConnected?.Invoke(this);
            }
        }

        void HandleReconnectResponse(Message msg)
        {
            // ReconnectRsp: [success:1][frame:4][roomId:4]
            if (msg.DataLength >= 5)
            {
                var tmp = new byte[9];
                int copyLen = Math.Min(msg.DataLength, 9);
                msg.DataSpan.Slice(0, copyLen).CopyTo(tmp);
                byte success = tmp[0];
                uint serverFrame = BitConverter.ToUInt32(tmp, 1);

                if (success == 1)
                {
                    int roomId = copyLen >= 9 ? (int)BitConverter.ToUInt32(tmp, 5) : RoomId;
                    RoomId = roomId;

                    // Fix #2: 恢复 FrameSyncClient 状态
                    if (serverFrame > 0)
                    {
                        _frameSync.ResumeAsSyncing(PlayerId);
                        State = PersonState.Syncing;
                    }
                    else
                    {
                        _frameSync.ResumeAsWaiting(PlayerId);
                        State = PersonState.InRoom;
                    }

                    Log($"Reconnected! Room {roomId}, serverFrame={serverFrame}");
                    OnReconnected?.Invoke(this);
                    // Fix #3: 统一回调，ConnectSequential 用这个
                    OnReady?.Invoke(this);
                    return;
                }
            }

            Log("Reconnect failed, server doesn't recognize identity. Fresh connect.");
            ClearIdentity();
            State = PersonState.Connected;
            OnConnected?.Invoke(this);
        }

        void HandleDisconnected()
        {
            // Fix #1: 断线前保存帧号
            if (_frameSync != null)
                _savedFrameNumber = _frameSync.LastFrameNumber;

            State = PersonState.Disconnected;
            Log($"Disconnected (identity preserved: P{PlayerId} R{RoomId} F{_savedFrameNumber})");
            OnDisconnected?.Invoke(this);
        }

        public void CreateAndJoinRoom(int maxPlayers)
        {
            if (State != PersonState.Connected) return;

            _roomClient.CreateRoom(maxPlayers, roomId =>
            {
                Log($"Room {roomId} created");
                JoinRoom(roomId);
            });
        }

        public void JoinRoom(int targetRoomId)
        {
            if (State != PersonState.Connected) return;

            _roomClient.JoinRoom(targetRoomId, (pid, rid) =>
            {
                PlayerId = pid;
                RoomId = rid;
                _connMgr.SetPlayerId(pid);
                State = PersonState.InRoom;
                Log($"Joined room {rid} as Player {pid}");
                OnJoinedRoom?.Invoke(this);
                // Fix #3: 首次加入也触发 OnReady
                OnReady?.Invoke(this);
            });
        }

        public void LeaveRoom()
        {
            if (State != PersonState.InRoom && State != PersonState.Syncing) return;
            int oldPlayerId = PlayerId;
            _roomClient?.LeaveRoom(() =>
            {
                Log($"Left room {RoomId}");
                OnLeftRoom?.Invoke(this, oldPlayerId);
                ClearIdentity();
                _savedFrameNumber = 0;
                State = PersonState.Connected;
            });
        }

        public void RequestStart()
        {
            if (State != PersonState.InRoom) return;
            _session?.Send(FrameSyncCmd.RequestStart);
            Log("Requested start");
        }

        public void SendInput(byte[] data)
        {
            if (State == PersonState.Syncing)
                _frameSync?.SendInput(data);
        }

        public void Tick(float deltaTimeMs)
        {
            _connMgr?.Tick(deltaTimeMs);
        }

        public void Disconnect()
        {
            // Fix #1: 保存帧号
            if (_frameSync != null)
                _savedFrameNumber = _frameSync.LastFrameNumber;

            _connMgr?.Disconnect();
            _transport = null;
            _session = null;
            _connMgr = null;
            _roomClient = null;
            _frameSync = null;
            State = PersonState.Disconnected;
        }

        public void DisconnectAndClear()
        {
            Disconnect();
            ClearIdentity();
            _savedFrameNumber = 0;
            State = PersonState.Idle;
        }

        void ClearIdentity()
        {
            PlayerId = 0;
            RoomId = 0;
        }

        public RoomClient GetRoomClient() => _roomClient;

        void Log(string msg) => OnLog?.Invoke(this, msg);
    }
}
