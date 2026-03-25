using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 所有 Demo Manager 的基类 — 提供 Person/Room/Entity/Log/Snapshot UI 框架。
    /// 子类只需 override 帧处理 + 输入发送逻辑。
    /// </summary>
    public abstract class DemoManagerBase : MonoBehaviour
    {
        // ===================== 子类必须提供 =====================

        /// <summary>Inspector 顶部 InfoBox 文字</summary>
        protected abstract string DemoInfoText { get; }

        /// <summary>Debug.Log 前缀，如 "[Demo01]"</summary>
        protected abstract string LogPrefix { get; }

        /// <summary>帧数据到达时调用（核心差异点）</summary>
        protected abstract void OnFrame(PersonSlot slot, FrameData frame);

        // ===================== 子类可选 override =====================

        /// <summary>每帧每 slot 调用，处理输入发送。dt 为 Time.deltaTime（秒）</summary>
        protected virtual void UpdateSlotInput(PersonSlot slot, float dt) { }

        /// <summary>Person 创建后注册额外事件（基类已注册通用事件）</summary>
        protected virtual void OnWirePersonEvents(PersonSlot slot) { }

        /// <summary>帧同步开始时调用（Demo02/03 在此 SetupEntitySync）</summary>
        protected virtual void OnFrameSyncStart(PersonSlot slot, FrameSyncInitData data) { }

        /// <summary>Override 自定义 entity spawn（Demo02/03 加 NetworkTransformSync）</summary>
        protected virtual PlayerEntity OnSpawnEntity(int pid, Color color, string label)
        {
            return PlayerEntity.Spawn(pid, color, label);
        }

        /// <summary>entity 销毁前的清理钩子</summary>
        protected virtual void OnDestroyEntity(int pid) { }

        /// <summary>Override 可自定义 ConnectAll 行为（Demo01 顺序连接）</summary>
        protected virtual void ConnectAllPersons()
        {
            foreach (var slot in persons)
                if (slot.person == null || slot.person.State == PersonState.Disconnected)
                    ConnectPerson(slot, null);
        }

        /// <summary>每帧 Update 末尾的额外逻辑（syncStatus, worldHash 等）</summary>
        protected virtual void OnUpdateExtra() { }

        /// <summary>Override 自定义 snapshot 序列化</summary>
        protected virtual byte[] TakeWorldSnapshot()
        {
            var sortedKeys = new List<int>(_entities.Keys);
            sortedKeys.Sort();
            int count = sortedKeys.Count;
            var buf = new byte[2 + count * 16];
            buf[0] = (byte)(count & 0xFF);
            buf[1] = (byte)((count >> 8) & 0xFF);
            int offset = 2;
            foreach (var key in sortedKeys)
            {
                var e = _entities[key];
                var pos = e != null ? (Vector2)e.transform.position : Vector2.zero;
                float angle = e != null ? e.FacingAngle : 0f;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), key);   offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.x); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.y); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), angle); offset += 4;
            }
            return buf;
        }

        /// <summary>Override 自定义 snapshot 加载</summary>
        protected virtual void LoadWorldSnapshot(byte[] data)
        {
            if (data == null || data.Length < 2) return;
            int count = data[0] | (data[1] << 8);
            int offset = 2;
            for (int i = 0; i < count && offset + 16 <= data.Length; i++)
            {
                int pid   = BitConverter.ToInt32(data, offset);  offset += 4;
                float x   = BitConverter.ToSingle(data, offset); offset += 4;
                float y   = BitConverter.ToSingle(data, offset); offset += 4;
                float ang = BitConverter.ToSingle(data, offset); offset += 4;
                if (!_entities.ContainsKey(pid)) SpawnEntity(pid, Color.gray, $"P{pid}");
                if (_entities.TryGetValue(pid, out var entity) && entity != null)
                {
                    entity.transform.position = new Vector3(x, y, 0);
                    entity.SetFacing(ang);
                }
            }
            _lastProcessedFrame = 0;
            Log($"Snapshot loaded: {count} entities restored, frame dedup reset");
        }

        // ===================== Config =====================

        [InfoBox("$DemoInfoText")]
        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
        [LabelWidth(80)]
        public float moveSpeed = 5f;

        [TitleGroup("Config")]
        [LabelWidth(120), Tooltip("Drop 按钮断线秒数 (1=快速重连, 8=快照重连)")]
        public float dropSeconds = 1f;

        // ===================== Person Slots =====================

        [TitleGroup("Persons")]
        [TableList(AlwaysExpanded = true)]
        public List<PersonSlot> persons = new();

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
            [TableColumnWidth(50), DisplayAsString]
            public string rtt = "-";

            [TableColumnWidth(45), Button("Conn")]
            public void BtnConnect() => _manager?.ConnectPerson(this, null);
            [TableColumnWidth(45), Button("Leave")]
            public void BtnLeave() => person?.LeaveRoom();
            [TableColumnWidth(40), Button("Disc")]
            public void BtnDisconnect() => _manager?.DisconnectPerson(this);
            [TableColumnWidth(30), Button("X"), GUIColor(0.9f, 0.3f, 0.3f)]
            public void BtnRemove() => _manager?.RemovePerson(this);
            [TableColumnWidth(55), Button("Drop"), GUIColor(1f, 0.7f, 0.3f)]
            public void BtnDrop() => _manager?.SimulateNetworkDrop(this, _manager?.dropSeconds ?? 1f);

            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal DemoManagerBase _manager;
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
        void BtnConnectAll() => ConnectAllPersons();

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
            p.CreateRoom(config.defaultMaxPlayers, rid =>
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
            p.GetRooms(rooms =>
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
            [NonSerialized] internal DemoManagerBase _mgr;
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H2")]
        [Button("Join All → Target Room", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.5f)]
        void JoinAllToTargetRoom()
        {
            if (targetRoomId <= 0) { Log("Set Target Room ID first (or Create Room)"); return; }
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.Connected)
                {
                    slot.person.JoinRoom(targetRoomId);
                    Log($"[{slot.inputMode}] Joining room {targetRoomId}...");
                }
        }

        [HorizontalGroup("Room/H2")]
        [Button("Start Game", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void StartGame()
        {
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.InRoom)
                {
                    slot.person.RequestStart();
                    Log("Requested start!");
                    return;
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

        // ===================== Log =====================

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel, MultiLineProperty(8), PropertyOrder(100)]
        public string logText = "";
        [TitleGroup("Log"), Button("Clear Log"), PropertyOrder(101)]
        void ClearLog() => logText = "";

        // ===================== Internal State =====================

        protected Dictionary<int, PlayerEntity> _entities = new();
        protected uint _lastProcessedFrame;
        protected byte[] _inputBuf = new byte[8];

        // ===================== Lifecycle =====================

        protected virtual void Awake()
        {
            foreach (var slot in persons) slot._manager = this;
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < persons.Count; i++)
            {
                var slot = persons[i];
                slot._manager = this;
                if (slot.person == null) continue;

                slot.person.Tick(dt * 1000f);
                slot.inputProvider?.Tick(dt);

                UpdateSlotInput(slot, dt);

                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? $"P{slot.person.PlayerId}" : "-";
                slot.frame = slot.person.FrameNumber.ToString();
                slot.rtt = slot.person.RttMs >= 0 ? $"{slot.person.RttMs:F0}" : "-";
            }

            OnUpdateExtra();
            foreach (var rd in _roomList) rd._mgr ??= this;
        }

        protected virtual void OnDestroy()
        {
            foreach (var slot in persons) { slot.person?.Disconnect(); slot.person = null; }
            foreach (var kv in _entities) { if (kv.Value != null) Destroy(kv.Value.gameObject); }
        }

        // ===================== Person Lifecycle =====================

        public void ConnectPerson(PersonSlot slot, Action onReady)
        {
            if (config == null) { Log("ERROR: NetworkConfig not assigned!"); return; }
            if (slot.person != null && slot.person.State != PersonState.Idle
                && slot.person.State != PersonState.Disconnected) return;

            if (slot.person == null)
            {
                slot.person = new Person();
                slot.inputProvider ??= InputProviderFactory.Create(slot.inputMode);
                WirePersonEvents(slot);
                OnWirePersonEvents(slot);
            }

            if (onReady != null)
            {
                Action<Person> readyHandler = null;
                readyHandler = p => { slot.person.OnReady -= readyHandler; onReady.Invoke(); };
                slot.person.OnReady += readyHandler;
            }

            slot.person.Connect(config);
        }

        private void WirePersonEvents(PersonSlot slot)
        {
            var person = slot.person;

            person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");
            person.OnConnected += p => Log($"[{slot.inputMode}] Connected");
            person.OnJoinedRoom += p =>
            {
                targetRoomId = p.RoomId;
                SpawnEntity(p.PlayerId, slot.color, slot.inputMode.ToString());
            };
            person.OnReconnected += p => Log($"[{slot.inputMode}] Reconnected as P{p.PlayerId}!");
            person.OnReady += p => Log($"[{slot.inputMode}] Ready");

            person.OnRemotePlayerJoined += (p, pid) =>
            {
                if (!_entities.ContainsKey(pid))
                {
                    SpawnEntity(pid, Color.gray, $"P{pid}");
                    Log($"Remote player {pid} joined");
                }
            };
            person.OnRemotePlayerLeft += (p, pid) => { DestroyEntity(pid); Log($"Remote player {pid} left"); };
            person.OnRemotePlayerOffline += (p, pid) =>
            {
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOffline();
                Log($"Remote player {pid} offline");
            };
            person.OnRemotePlayerOnline += (p, pid) =>
            {
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOnline();
                Log($"Remote player {pid} back online");
            };

            person.OnFrameSyncStart += (p, data) =>
            {
                Log($"[{slot.inputMode}] FrameSync started (rate={data.FrameRate})");
                OnFrameSyncStart(slot, data);
            };
            person.OnFrame += (p, frame) => OnFrame(slot, frame);
            person.OnLeftRoom += (p, oldPid) => { Log($"[{slot.inputMode}] Left room"); DestroyEntity(oldPid); };
            person.OnDisconnected += p => Log($"[{slot.inputMode}] Lost connection");

            person.TakeSnapshot = () => TakeWorldSnapshot();
            person.LoadSnapshot = data =>
            {
                LoadWorldSnapshot(data);
            };
        }

        public void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;
            slot.person.Disconnect();
            slot.state = "Disconnected";
            slot.frame = "0";
        }

        public void RemovePerson(PersonSlot slot)
        {
            if (slot.person != null)
            {
                int pid = slot.person.PlayerId;
                slot.person.DisconnectAndClear();
                if (pid > 0) DestroyEntity(pid);
                Log($"[{slot.inputMode}] Removed (P{pid})");
            }
            slot.person = null;
            slot.inputProvider = null;
            persons.Remove(slot);
        }

        public void SimulateNetworkDrop(PersonSlot slot, float seconds)
        {
            if (slot.person == null || slot.person.State != PersonState.Syncing) return;
            Log($"[{slot.inputMode}] Simulating network drop for {seconds}s...");
            slot.person.SimulateNetworkDrop();
            StartCoroutine(ReconnectAfterDelay(slot, seconds));
        }

        private IEnumerator ReconnectAfterDelay(PersonSlot slot, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (slot.person != null && slot.person.State == PersonState.Disconnected)
            {
                Log($"[{slot.inputMode}] Reconnecting after {delay}s drop...");
                ConnectPerson(slot, null);
            }
        }

        // ===================== Entity Management =====================

        protected PlayerEntity SpawnEntity(int playerId, Color color, string label)
        {
            if (_entities.TryGetValue(playerId, out var existing) && existing != null)
                return existing;

            var entity = OnSpawnEntity(playerId, color, label);
            _entities[playerId] = entity;
            Log($"Spawned {label} (Player {playerId})");
            return entity;
        }

        protected void DestroyEntity(int playerId)
        {
            if (playerId <= 0) return;
            OnDestroyEntity(playerId);
            if (_entities.TryGetValue(playerId, out var entity))
            {
                if (entity != null) Destroy(entity.gameObject);
                _entities.Remove(playerId);
            }
        }

        // ===================== Helpers =====================

        protected Person FindConnectedPerson()
        {
            foreach (var slot in persons)
                if (slot.person != null && (slot.person.State == PersonState.Connected
                    || slot.person.State == PersonState.InRoom
                    || slot.person.State == PersonState.Syncing))
                    return slot.person;
            return null;
        }

        protected float GetFrameIntervalMs()
        {
            foreach (var s in persons)
            {
                var init = s.person?.GetFrameSyncInitData();
                if (init.HasValue) return init.Value.FrameInterval;
            }
            return 50f;
        }

        // ===================== Codec =====================

        protected static void EncodeInput(Vector2 dir, byte[] buf)
        {
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), dir.x);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 4), dir.y);
        }

        [ThreadStatic] private static byte[] _decodeTmp;
        protected static Vector2 DecodeInput(ReadOnlySpan<byte> buf)
        {
            _decodeTmp ??= new byte[8];
            buf.Slice(0, 8).CopyTo(_decodeTmp);
            return new Vector2(BitConverter.ToSingle(_decodeTmp, 0), BitConverter.ToSingle(_decodeTmp, 4));
        }

        // ===================== Log =====================

        protected void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000) logText = logText.Substring(0, 3000);
            Debug.Log($"{LogPrefix} {msg}");
        }
    }
}
