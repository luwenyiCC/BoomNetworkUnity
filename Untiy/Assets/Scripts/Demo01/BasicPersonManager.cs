using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01: 基础帧同步 — 纯传统模式，无预测回滚。
    /// 最简实现：连接 → 房间 → 帧同步移动 → 重连。
    /// </summary>
    public class BasicPersonManager : MonoBehaviour
    {
        // ===================== Config =====================

        [InfoBox(
            "Demo01 - Basic Frame Sync\n" +
            "1. BoomNetwork > Server Window > Start Server (no -autoroom)\n" +
            "2. Play this scene\n" +
            "3. Click [Connect All]\n" +
            "4. Click [Start Game]\n" +
            "5. WASD = Player 1, Arrows = Player 2",
            InfoMessageType.Info)]
        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
        [LabelWidth(80)]
        public float moveSpeed = 5f;

        // ===================== Person Slots =====================

        [TitleGroup("Persons")]
        [TableList(AlwaysExpanded = true)]
        public List<PersonSlot> persons = new()
        {
            new PersonSlot { inputMode = InputMode.WASD, color = Color.green },
            new PersonSlot { inputMode = InputMode.Arrows, color = new Color(0.3f, 0.5f, 1f) },
        };

        [Serializable]
        public class PersonSlot
        {
            [TableColumnWidth(80)]
            public InputMode inputMode = InputMode.WASD;
            [TableColumnWidth(60)]
            public Color color = Color.green;
            [TableColumnWidth(70), DisplayAsString]
            public string state = "Idle";
            [TableColumnWidth(40), DisplayAsString]
            public string pid = "-";
            [TableColumnWidth(60), DisplayAsString]
            public string frame = "0";

            [TableColumnWidth(50), Button("Conn")]
            public void BtnConnect() => _manager?.ConnectPerson(this, null);
            [TableColumnWidth(50), Button("Join")]
            public void BtnJoin()
            {
                if (person?.State == PersonState.Connected && _manager != null)
                {
                    person.JoinRoom(_manager._currentRoomId);
                    _manager.Log($"[{inputMode}] Joining room {_manager._currentRoomId}...");
                }
            }
            [TableColumnWidth(50), Button("Leave")]
            public void BtnLeave() => person?.LeaveRoom();
            [TableColumnWidth(50), Button("Disc")]
            public void BtnDisconnect() => _manager?.DisconnectPerson(this);

            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal BasicPersonManager _manager;
        }

        // ===================== Actions =====================

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/H")]
        [Button("Add Person", ButtonSizes.Medium)]
        void AddPerson()
        {
            persons.Add(new PersonSlot
            {
                inputMode = InputMode.None,
                color = UnityEngine.Random.ColorHSV(0, 1, 0.5f, 1, 0.7f, 1),
            });
        }

        [HorizontalGroup("Actions/H")]
        [Button("Connect All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void ConnectAll() => ConnectSequential(0);

        void ConnectSequential(int index)
        {
            if (index >= persons.Count) return;
            var slot = persons[index];
            if (slot.person != null && slot.person.State != PersonState.Disconnected)
            {
                ConnectSequential(index + 1);
                return;
            }
            ConnectPerson(slot, () => ConnectSequential(index + 1));
        }

        [HorizontalGroup("Actions/H")]
        [Button("Start Game", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void StartGame()
        {
            foreach (var slot in persons)
            {
                if (slot.person?.State == PersonState.InRoom)
                {
                    slot.person.RequestStart();
                    Log("Requested start!");
                    return;
                }
            }
            Log("No person in room to start");
        }

        [HorizontalGroup("Actions/H")]
        [Button("Disconnect All", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DisconnectAll()
        {
            foreach (var slot in persons) DisconnectPerson(slot);
        }

        // ===================== Room =====================

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H1")]
        [Button("Refresh Rooms"), GUIColor(0.3f, 0.6f, 0.9f)]
        void RefreshRooms()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.GetRoomClient()?.GetRooms(rooms =>
            {
                _roomList.Clear();
                foreach (var r in rooms)
                    _roomList.Add(new RoomDisplay { roomId = r.RoomId, players = $"{r.PlayerCount}/{r.MaxPlayers}", status = r.Running ? "Playing" : "Waiting", _mgr = this });
                Log($"Refreshed: {rooms.Length} room(s)");
            });
        }

        [HorizontalGroup("Room/H1")]
        [Button("Create Room"), GUIColor(0.2f, 0.7f, 0.4f)]
        void CreateRoom()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.GetRoomClient()?.CreateRoom(config.defaultMaxPlayers, rid =>
            {
                _currentRoomId = rid;
                Log($"Room {rid} created");
                RefreshRooms();
            });
        }

        [TitleGroup("Room")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        [SerializeField]
        private List<RoomDisplay> _roomList = new();

        [Serializable]
        public class RoomDisplay
        {
            [TableColumnWidth(50)] public int roomId;
            [TableColumnWidth(70)] public string players;
            [TableColumnWidth(70)] public string status;
            [TableColumnWidth(80), Button("Join All")]
            public void BtnJoinAll()
            {
                if (_mgr == null) return;
                _mgr._currentRoomId = roomId;
                foreach (var slot in _mgr.persons)
                    if (slot.person?.State == PersonState.Connected)
                    {
                        slot.person.JoinRoom(roomId);
                        _mgr.Log($"[{slot.inputMode}] Joining room {roomId}...");
                    }
            }
            [TableColumnWidth(50), Button("Select")]
            public void BtnSelect() { if (_mgr != null) { _mgr._currentRoomId = roomId; _mgr.Log($"Selected room {roomId}"); } }
            [NonSerialized] internal BasicPersonManager _mgr;
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H3")]
        [DisplayAsString, LabelWidth(80)]
        public string selectedRoom = "None";

        [HorizontalGroup("Room/H3")]
        [Button("Leave All"), GUIColor(0.8f, 0.5f, 0.2f)]
        void LeaveAll()
        {
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.InRoom || slot.person?.State == PersonState.Syncing)
                {
                    slot.person.LeaveRoom();
                    Log($"[{slot.inputMode}] Leaving room...");
                }
        }

        Person FindConnectedPerson()
        {
            foreach (var slot in persons)
                if (slot.person != null && (slot.person.State == PersonState.Connected || slot.person.State == PersonState.InRoom || slot.person.State == PersonState.Syncing))
                    return slot.person;
            return null;
        }

        // ===================== Sync / Log =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel, GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel, MultiLineProperty(8), PropertyOrder(100)]
        public string logText = "";
        [TitleGroup("Log"), Button("Clear Log"), PropertyOrder(101)]
        void ClearLog() => logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private uint _lastProcessedFrame; // 帧号去重，避免同一帧被多个 Person 重复处理
        internal int _currentRoomId = -1;
        private byte[] _inputBuf = new byte[8];
        private float _inputSendAccumulator;
        private const float INPUT_SEND_INTERVAL_MS = 50f;
        private bool _shouldSendInput;

        void Awake()
        {
            foreach (var slot in persons) slot._manager = this;
        }

        void Update()
        {
            float dt = Time.deltaTime * 1000;

            _inputSendAccumulator += dt;
            _shouldSendInput = _inputSendAccumulator >= INPUT_SEND_INTERVAL_MS;
            if (_shouldSendInput) _inputSendAccumulator -= INPUT_SEND_INTERVAL_MS;

            for (int i = 0; i < persons.Count; i++)
            {
                var slot = persons[i];
                slot._manager = this;
                if (slot.person == null) continue;

                slot.person.Tick(dt);
                slot.inputProvider?.Tick(Time.deltaTime);

                // 传统模式: 只在有输入时发送，按 20fps 节流
                if (_shouldSendInput && slot.person.State == PersonState.Syncing && slot.inputProvider != null)
                {
                    var dir = slot.inputProvider.GetMoveInput();
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        EncodeInput(dir, _inputBuf);
                        slot.person.SendInput(_inputBuf);
                    }
                }

                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? slot.person.PlayerId.ToString() : "-";
                slot.frame = slot.person.FrameNumber.ToString();
            }

            UpdateSyncStatus();
            selectedRoom = _currentRoomId > 0 ? $"Room {_currentRoomId}" : "None";
            foreach (var rd in _roomList) rd._mgr ??= this;
        }

        void OnDestroy()
        {
            foreach (var slot in persons) { slot.person?.Disconnect(); slot.person = null; }
            foreach (var kv in _entities) { if (kv.Value != null) Destroy(kv.Value.gameObject); }
        }

        // ===================== Person Lifecycle =====================

        public void ConnectPerson(PersonSlot slot, Action onReady = null)
        {
            if (config == null) { Log("ERROR: NetworkConfig not assigned!"); return; }
            if (slot.person != null && slot.person.State != PersonState.Disconnected) return;

            bool isReconnect = slot.person != null && slot.person.HasPreviousIdentity;
            var person = slot.person ?? new Person();
            slot.person = person;
            slot.inputProvider ??= InputProviderFactory.Create(slot.inputMode);

            if (!isReconnect)
            {
                person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");
                person.OnConnected += p =>
                {
                    if (_currentRoomId > 0) p.JoinRoom(_currentRoomId);
                    else p.CreateAndJoinRoom(config.defaultMaxPlayers);
                };
                person.OnJoinedRoom += p =>
                {
                    _currentRoomId = p.RoomId;
                    selectedRoom = $"Room {p.RoomId}";
                    SpawnEntity(p.PlayerId, slot.color, slot.inputMode.ToString());
                };
                person.OnReconnected += p => Log($"[{slot.inputMode}] Reconnected as P{p.PlayerId}!");

                if (onReady != null)
                {
                    Action<Person> readyHandler = null;
                    readyHandler = p => { person.OnReady -= readyHandler; onReady.Invoke(); };
                    person.OnReady += readyHandler;
                }

                person.OnFrameSyncStart += (p, data) => Log($"[{slot.inputMode}] Syncing!");
                // 帧号去重: 任何 Person 收到的帧都处理，同一帧号只处理一次
                // 不依赖 authority，避免重连时补帧被丢弃
                person.OnFrame += (p, frame) =>
                {
                    if (frame.FrameNumber > _lastProcessedFrame)
                    {
                        _lastProcessedFrame = frame.FrameNumber;
                        OnAuthorityFrame(frame);
                    }
                };
                person.OnLeftRoom += (p, oldPid) => { Log($"[{slot.inputMode}] Left room, destroying P{oldPid}"); DestroyEntity(oldPid); };
                person.OnDisconnected += p => Log($"[{slot.inputMode}] Lost connection");

                person.TakeSnapshot = () => TakeWorldSnapshot();
                person.LoadSnapshot = data =>
                {
                    LoadWorldSnapshot(data);
                    Log($"Snapshot loaded ({data?.Length ?? 0} bytes, {_entities.Count} entities restored)");
                };
            }

            person.Connect(config);
        }

        public void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;
            slot.person.Disconnect();
            slot.state = "Disconnected";
            slot.frame = "0";
        }

        // ===================== Frame Handler =====================

        void OnAuthorityFrame(FrameData frame)
        {
            if (frame.Inputs == null) return;
            // 从任意 Syncing 的 Person 获取帧间隔
            float frameIntervalMs = 50f;
            foreach (var s in persons)
            {
                var init = s.person?.GetFrameSyncInitData();
                if (init.HasValue) { frameIntervalMs = init.Value.FrameInterval; break; }
            }
            float delta = moveSpeed * (frameIntervalMs / 1000f);

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                if (input.DataLength < 8) continue;
                var dir = DecodeInput(input.DataSpan);
                var pid = input.PlayerId;
                if (!_entities.ContainsKey(pid)) SpawnEntity(pid, Color.gray, $"P{pid}");
                if (_entities.TryGetValue(pid, out var entity)) entity.ApplyMove(dir, delta);
            }
        }

        // ===================== Entity =====================

        void SpawnEntity(int playerId, Color color, string label)
        {
            if (_entities.ContainsKey(playerId)) return;
            _entities[playerId] = PlayerEntity.Spawn(playerId, color, label);
            Log($"Spawned {label} (Player {playerId})");
        }

        void DestroyEntity(int playerId)
        {
            if (playerId <= 0) return;
            if (_entities.TryGetValue(playerId, out var entity))
            {
                if (entity != null) Destroy(entity.gameObject);
                _entities.Remove(playerId);
            }
        }

        // Demo01 不用 authority 模式，用帧号去重

        void UpdateSyncStatus()
        {
            uint? first = null; int syncCount = 0;
            foreach (var slot in persons)
            {
                if (slot.person == null || slot.person.State != PersonState.Syncing) continue;
                syncCount++;
                var f = slot.person.FrameNumber;
                if (first == null) first = f;
                else if (f != first.Value) { syncStatus = $"DIFF: {first} vs {f} ({syncCount} syncing)"; return; }
            }
            if (syncCount >= 2) syncStatus = $"IN SYNC (frame {first}, {syncCount} clients)";
            else if (syncCount == 1) syncStatus = $"1 client syncing (frame {first})";
            else syncStatus = "Waiting...";
        }

        // ===================== Codec =====================

        static void EncodeInput(Vector2 dir, byte[] buf)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(dir.x), 0, buf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(dir.y), 0, buf, 4, 4);
        }

        [ThreadStatic] private static byte[] _decodeTmp;
        static Vector2 DecodeInput(ReadOnlySpan<byte> buf)
        {
            _decodeTmp ??= new byte[8];
            buf.Slice(0, 8).CopyTo(_decodeTmp);
            return new Vector2(BitConverter.ToSingle(_decodeTmp, 0), BitConverter.ToSingle(_decodeTmp, 4));
        }

        // ===================== Snapshot =====================

        byte[] TakeWorldSnapshot()
        {
            // 排序 key 保证确定性遍历顺序
            var sortedKeys = new List<int>(_entities.Keys);
            sortedKeys.Sort();

            int count = sortedKeys.Count;
            var buf = new byte[2 + count * 12];
            buf[0] = (byte)(count & 0xFF);
            buf[1] = (byte)((count >> 8) & 0xFF);
            int offset = 2;
            foreach (var key in sortedKeys)
            {
                var pos = _entities[key] != null ? (Vector2)_entities[key].transform.position : Vector2.zero;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), key); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.x); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.y); offset += 4;
            }
            return buf;
        }

        void LoadWorldSnapshot(byte[] data)
        {
            if (data == null || data.Length < 2) return;
            int count = data[0] | (data[1] << 8);
            int offset = 2;
            for (int i = 0; i < count && offset + 12 <= data.Length; i++)
            {
                int pid = BitConverter.ToInt32(data, offset); offset += 4;
                float x = BitConverter.ToSingle(data, offset); offset += 4;
                float y = BitConverter.ToSingle(data, offset); offset += 4;
                if (!_entities.ContainsKey(pid)) SpawnEntity(pid, Color.gray, $"P{pid}");
                if (_entities.TryGetValue(pid, out var entity) && entity != null)
                    entity.transform.position = new Vector3(x, y, 0);
            }
            Log($"Snapshot loaded: {count} entities restored");
        }

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000) logText = logText.Substring(0, 3000);
            Debug.Log($"[Demo01] {msg}");
        }

        Color GetSyncColor()
        {
            if (syncStatus.Contains("IN SYNC")) return Color.green;
            if (syncStatus.Contains("DIFF")) return Color.yellow;
            return Color.white;
        }
    }
}
