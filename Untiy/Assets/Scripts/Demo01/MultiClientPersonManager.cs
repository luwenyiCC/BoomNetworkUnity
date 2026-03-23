using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01.1: 多客户端帧同步 — 适配 ParrelSync 多编辑器测试。
    ///
    /// 与 BasicPersonManager 的区别：
    /// - 连接和入房分离：Connect 只做 SessionBind，不自动建房
    /// - targetRoomId：指定要加入的房间号（0=新建房间）
    /// - 适合 ParrelSync：Editor A 建房 → Editor B 填房间号 → Join All
    /// </summary>
    public class MultiClientPersonManager : MonoBehaviour
    {
        // ===================== Config =====================

        [InfoBox(
            "Demo01.1 - Multi-Client Frame Sync (ParrelSync)\n" +
            "Editor A: Connect All → Create Room → Join All → Start Game\n" +
            "Editor B: 填 Target Room ID → Connect All → Join All\n" +
            "Drop1s/Drop8s 测试快速重连/快照重连",
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
            [TableColumnWidth(50), Button("Disc")]
            public void BtnDisconnect() => _manager?.DisconnectPerson(this);

            [TableColumnWidth(55), Button("Drop1s"), GUIColor(1f, 0.8f, 0.3f)]
            public void BtnDrop1s() => _manager?.SimulateNetworkDrop(this, 1f);
            [TableColumnWidth(55), Button("Drop8s"), GUIColor(1f, 0.5f, 0.3f)]
            public void BtnDrop8s() => _manager?.SimulateNetworkDrop(this, 8f);

            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal MultiClientPersonManager _manager;
        }

        // ===================== Actions =====================

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/H1")]
        [Button("Add Person", ButtonSizes.Medium)]
        void AddPerson()
        {
            persons.Add(new PersonSlot
            {
                inputMode = InputMode.None,
                color = UnityEngine.Random.ColorHSV(0, 1, 0.5f, 1, 0.7f, 1),
            });
        }

        [HorizontalGroup("Actions/H1")]
        [Button("Connect All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void ConnectAll()
        {
            foreach (var slot in persons)
            {
                if (slot.person == null || slot.person.State == PersonState.Disconnected)
                    ConnectPerson(slot, null);
            }
        }

        [HorizontalGroup("Actions/H1")]
        [Button("Disconnect All", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DisconnectAll()
        {
            foreach (var slot in persons) DisconnectPerson(slot);
        }

        // ===================== Room =====================

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H1")]
        [LabelWidth(100), LabelText("Target Room ID")]
        public int targetRoomId = 0;

        [HorizontalGroup("Room/H1")]
        [Button("Create Room"), GUIColor(0.2f, 0.7f, 0.4f)]
        void CreateRoom()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.GetRoomClient()?.CreateRoom(config.defaultMaxPlayers, rid =>
            {
                targetRoomId = rid;
                Log($"Room {rid} created → targetRoomId updated");
                RefreshRooms();
            });
        }

        [HorizontalGroup("Room/H1")]
        [Button("Refresh"), GUIColor(0.3f, 0.6f, 0.9f)]
        void RefreshRooms()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.GetRoomClient()?.GetRooms(rooms =>
            {
                _roomList.Clear();
                foreach (var r in rooms)
                    _roomList.Add(new RoomDisplay
                    {
                        roomId = r.RoomId,
                        players = $"{r.PlayerCount}/{r.MaxPlayers}",
                        status = r.Running ? "Playing" : "Waiting",
                        _mgr = this,
                    });
                Log($"Refreshed: {rooms.Length} room(s)");
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
            [TableColumnWidth(60), Button("Select")]
            public void BtnSelect()
            {
                if (_mgr != null) { _mgr.targetRoomId = roomId; _mgr.Log($"Selected room {roomId}"); }
            }
            [NonSerialized] internal MultiClientPersonManager _mgr;
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H2")]
        [Button("Join All → Target Room", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.5f)]
        void JoinAllToTargetRoom()
        {
            if (targetRoomId <= 0) { Log("Set Target Room ID first (or Create Room)"); return; }
            foreach (var slot in persons)
            {
                if (slot.person?.State == PersonState.Connected)
                {
                    slot.person.JoinRoom(targetRoomId);
                    Log($"[{slot.inputMode}] Joining room {targetRoomId}...");
                }
            }
        }

        [HorizontalGroup("Room/H2")]
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

        [HorizontalGroup("Room/H2")]
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
                if (slot.person != null && (slot.person.State == PersonState.Connected
                    || slot.person.State == PersonState.InRoom
                    || slot.person.State == PersonState.Syncing))
                    return slot.person;
            return null;
        }

        // ===================== Sync / Log =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel, GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        [TitleGroup("Sync")]
        [DisplayAsString, LabelWidth(80)]
        public string worldHash = "-";

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel, MultiLineProperty(8), PropertyOrder(100)]
        public string logText = "";
        [TitleGroup("Log"), Button("Clear Log"), PropertyOrder(101)]
        void ClearLog() => logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private uint _lastProcessedFrame;
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
            UpdateWorldHash();
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
                // 连接成功只做 SessionBind，不自动入房
                person.OnConnected += p => Log($"[{slot.inputMode}] Connected, ready to join room");
                person.OnJoinedRoom += p =>
                {
                    targetRoomId = p.RoomId;
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

        public void SimulateNetworkDrop(PersonSlot slot, float dropSeconds)
        {
            if (slot.person == null || slot.person.State != PersonState.Syncing) return;
            Log($"[{slot.inputMode}] Simulating network drop for {dropSeconds}s...");
            slot.person.SimulateNetworkDrop();
            StartCoroutine(ReconnectAfterDelay(slot, dropSeconds));
        }

        private IEnumerator ReconnectAfterDelay(PersonSlot slot, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (slot.person != null && slot.person.State == PersonState.Disconnected)
            {
                Log($"[{slot.inputMode}] Reconnecting after {delay}s drop...");
                ConnectPerson(slot);
            }
        }

        // ===================== Frame Handler =====================

        void OnAuthorityFrame(FrameData frame)
        {
            if (frame.Inputs == null) return;
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
            Debug.Log($"[Demo01.1] {msg}");
        }

        void UpdateWorldHash()
        {
            if (_entities.Count == 0) { worldHash = "-"; return; }

            // 按 PlayerId 排序保证两边顺序一致
            var sortedKeys = new List<int>(_entities.Keys);
            sortedKeys.Sort();

            // FNV-1a hash: 位置(truncate to int) + 旋转(truncate to int)
            uint hash = 2166136261u;
            foreach (var key in sortedKeys)
            {
                var entity = _entities[key];
                if (entity == null) continue;

                var pos = entity.transform.position;
                var rot = entity.transform.rotation.eulerAngles;

                // 乘 100 再取整，保留两位小数精度
                int px = (int)(pos.x * 100);
                int py = (int)(pos.y * 100);
                int rz = (int)(rot.z * 100);

                hash ^= (uint)key; hash *= 16777619u;
                hash ^= (uint)px;  hash *= 16777619u;
                hash ^= (uint)py;  hash *= 16777619u;
                hash ^= (uint)rz;  hash *= 16777619u;
            }

            worldHash = $"{hash:X8} ({_entities.Count}e)";
        }

        Color GetSyncColor()
        {
            if (syncStatus.Contains("IN SYNC")) return Color.green;
            if (syncStatus.Contains("DIFF")) return Color.yellow;
            return Color.white;
        }
    }
}
