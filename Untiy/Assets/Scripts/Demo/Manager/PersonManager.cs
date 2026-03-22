using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;
using BoomNetwork.Core.Prediction;

namespace BoomNetworkDemo
{
    public class PersonManager : MonoBehaviour
    {
        // ===================== Config =====================

        [InfoBox(
            "Quick Start:\n" +
            "1. BoomNetwork > Server Window > Start Server\n" +
            "2. Play this scene\n" +
            "3. Click [Connect All] below\n" +
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

            [TableColumnWidth(50)]
            [Button("Conn")]
            public void BtnConnect()
            {
                _manager?.ConnectPerson(this, null);
            }

            [TableColumnWidth(50)]
            [Button("Join")]
            public void BtnJoin()
            {
                if (person?.State == PersonState.Connected && _manager != null)
                {
                    person.JoinRoom(_manager._currentRoomId);
                    _manager.Log($"[{inputMode}] Joining room {_manager._currentRoomId}...");
                }
            }

            [TableColumnWidth(50)]
            [Button("Leave")]
            public void BtnLeave()
            {
                person?.LeaveRoom();
            }

            [TableColumnWidth(50)]
            [Button("Disc")]
            public void BtnDisconnect()
            {
                _manager?.DisconnectPerson(this);
            }

            // 运行时数据（不序列化）
            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal PersonManager _manager;
        }

        // ===================== Batch Actions =====================

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
        void ConnectAll()
        {
            ConnectSequential(0);
        }

        /// <summary>
        /// 依次连接：等前一个加入房间后再连下一个
        /// </summary>
        void ConnectSequential(int index)
        {
            if (index >= persons.Count) return;
            var slot = persons[index];

            // 跳过已在线的
            if (slot.person != null && slot.person.State != PersonState.Disconnected)
            {
                ConnectSequential(index + 1);
                return;
            }

            ConnectPerson(slot, () =>
            {
                // OnReady（首次加入 或 重连成功）后，连下一个
                ConnectSequential(index + 1);
            });
        }

        [HorizontalGroup("Actions/H")]
        [Button("Start Game", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void StartGame()
        {
            // 任意一个 InRoom 状态的 Person 发 RequestStart
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
            foreach (var slot in persons)
                DisconnectPerson(slot);
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
                {
                    _roomList.Add(new RoomDisplay
                    {
                        roomId = r.RoomId,
                        players = $"{r.PlayerCount}/{r.MaxPlayers}",
                        status = r.Running ? "Playing" : "Waiting",
                        _mgr = this,
                    });
                }
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
            [TableColumnWidth(50)]
            public int roomId;

            [TableColumnWidth(70)]
            public string players;

            [TableColumnWidth(70)]
            public string status;

            [TableColumnWidth(80)]
            [Button("Join All")]
            public void BtnJoinAll()
            {
                if (_mgr == null) return;
                _mgr._currentRoomId = roomId;
                foreach (var slot in _mgr.persons)
                {
                    if (slot.person?.State == PersonState.Connected)
                    {
                        slot.person.JoinRoom(roomId);
                        _mgr.Log($"[{slot.inputMode}] Joining room {roomId}...");
                    }
                }
            }

            [TableColumnWidth(50)]
            [Button("Select")]
            public void BtnSelect()
            {
                if (_mgr != null)
                {
                    _mgr._currentRoomId = roomId;
                    _mgr.Log($"Selected room {roomId}");
                }
            }

            [NonSerialized] internal PersonManager _mgr;
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
            {
                if (slot.person?.State == PersonState.InRoom || slot.person?.State == PersonState.Syncing)
                {
                    slot.person.LeaveRoom();
                    Log($"[{slot.inputMode}] Leaving room...");
                }
            }
        }

        Person FindConnectedPerson()
        {
            foreach (var slot in persons)
            {
                if (slot.person != null && (slot.person.State == PersonState.Connected ||
                    slot.person.State == PersonState.InRoom || slot.person.State == PersonState.Syncing))
                    return slot.person;
            }
            return null;
        }

        // ===================== Sync Check =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel]
        [GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        // ===================== Log =====================

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel, MultiLineProperty(8)]
        [PropertyOrder(100)]
        public string logText = "";

        [TitleGroup("Log")]
        [Button("Clear Log")]
        [PropertyOrder(101)]
        void ClearLog() => logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private Person _authorityPerson; // 当前驱动世界渲染的 Person
        private int _currentRoomId = -1;
        private byte[] _inputBuf = new byte[8];
        private float _inputSendAccumulator; // 输入发送节流器
        private const float INPUT_SEND_INTERVAL_MS = 50f; // 20fps

        // --- 预测回滚 ---
        private DemoSimulation _simulation;
        private PredictionManager _prediction;
        private bool _predictionEnabled;
        [TitleGroup("Config")]
        [LabelWidth(120)]
        public bool enablePrediction = false;
        [TitleGroup("Config")]
        [LabelWidth(120), ShowIf("enablePrediction")]
        [Tooltip("渲染追逐逻辑位置的速度。越大越快到位，0=立刻到位（无平滑）")]
        [Range(0f, 30f)]
        public float smoothSpeed = 15f;

        void Awake()
        {
            // 绑定 slot → manager 引用
            foreach (var slot in persons)
                slot._manager = this;
        }

        private bool _shouldSendInput;

        void Update()
        {
            float dt = Time.deltaTime * 1000;

            // 输入发送节流: 每 50ms (20fps) 才发一次，不是每 Unity Update (60fps)
            _inputSendAccumulator += dt;
            _shouldSendInput = _inputSendAccumulator >= INPUT_SEND_INTERVAL_MS;
            if (_shouldSendInput)
                _inputSendAccumulator -= INPUT_SEND_INTERVAL_MS;

            for (int i = 0; i < persons.Count; i++)
            {
                var slot = persons[i];
                slot._manager = this;

                if (slot.person == null) continue;

                // Tick 网络
                slot.person.Tick(dt);

                // Tick 输入
                slot.inputProvider?.Tick(Time.deltaTime);

                // 发送输入（按服务器帧率节流 + 只在有输入时发送）
                if (_shouldSendInput && slot.person.State == PersonState.Syncing && slot.inputProvider != null)
                {
                    var dir = slot.inputProvider.GetMoveInput();
                    bool hasInput = dir.x != 0 || dir.y != 0;

                    if (hasInput)
                    {
                        EncodeInput(dir, _inputBuf);

                        if (_predictionEnabled && slot.person == _authorityPerson)
                        {
                            slot.person.PredictWithInput(dt, _inputBuf);
                        }
                        else
                        {
                            slot.person.SendInput(_inputBuf);
                        }
                    }
                }

                // 更新面板显示
                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? slot.person.PlayerId.ToString() : "-";
                slot.frame = _predictionEnabled && _prediction != null && slot.person == _authorityPerson
                    ? $"{_prediction.PredictedFrame}({_prediction.ConfirmedFrame})"
                    : slot.person.FrameNumber.ToString();
            }

            // 预测模式: 从 DemoSimulation 逻辑位置渲染 Entity（带平滑）
            if (_predictionEnabled && _simulation != null)
            {
                foreach (var kv in _simulation.LogicPositions)
                {
                    if (_entities.TryGetValue(kv.Key, out var entity) && entity != null)
                    {
                        var target = new Vector3(kv.Value.x, kv.Value.y, 0);
                        if (smoothSpeed > 0)
                            entity.transform.position = Vector3.Lerp(entity.transform.position, target, smoothSpeed * Time.deltaTime);
                        else
                            entity.transform.position = target;
                    }
                }
            }

            UpdateSyncStatus();
            UpdateAuthority();
            selectedRoom = _currentRoomId > 0 ? $"Room {_currentRoomId}" : "None";

            foreach (var rd in _roomList)
                rd._mgr ??= this;
        }

        void OnDestroy()
        {
            foreach (var slot in persons)
            {
                slot.person?.Disconnect();
                slot.person = null;
            }
            foreach (var kv in _entities)
            {
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
        }

        // ===================== Person Lifecycle =====================

        public void ConnectPerson(PersonSlot slot, Action onReady = null)
        {
            if (config == null)
            {
                Log("ERROR: NetworkConfig not assigned!");
                return;
            }

            // 已有 Person 且正在连接/已连接，不重复
            if (slot.person != null && slot.person.State != PersonState.Disconnected)
                return;

            bool isReconnect = slot.person != null && slot.person.HasPreviousIdentity;
            var person = slot.person ?? new Person();
            slot.person = person;
            slot.inputProvider ??= InputProviderFactory.Create(slot.inputMode);

            // 只在首次创建时注册事件，避免重连时重复注册
            if (!isReconnect)
            {
                person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");

                person.OnConnected += p =>
                {
                    if (_currentRoomId > 0)
                    {
                        p.JoinRoom(_currentRoomId);
                    }
                    else
                    {
                        p.CreateAndJoinRoom(config.defaultMaxPlayers);
                    }
                };

                person.OnJoinedRoom += p =>
                {
                    _currentRoomId = p.RoomId;
                    selectedRoom = $"Room {p.RoomId}";
                    SpawnEntity(p.PlayerId, slot.color, slot.inputMode.ToString());
                };

                person.OnReconnected += p =>
                {
                    Log($"[{slot.inputMode}] Reconnected as P{p.PlayerId}!");
                };

                // Fix #3: OnReady 统一回调，一次性（避免手动重连时误触 ConnectSequential）
                if (onReady != null)
                {
                    Action<Person> readyHandler = null;
                    readyHandler = p =>
                    {
                        person.OnReady -= readyHandler; // 触发一次后取消订阅
                        onReady.Invoke();
                    };
                    person.OnReady += readyHandler;
                }

                person.OnFrameSyncStart += (p, data) =>
                {
                    Log($"[{slot.inputMode}] Syncing!");
                };

                // 所有 Person 都注册 OnFrame，由 authority 机制决定谁驱动渲染
                person.OnFrame += (p, frame) =>
                {
                    if (p == _authorityPerson)
                        OnAuthorityFrame(frame);
                };

                person.OnLeftRoom += (p, oldPlayerId) =>
                {
                    Log($"[{slot.inputMode}] Left room, destroying P{oldPlayerId}");
                    DestroyEntity(oldPlayerId);
                };

                person.OnDisconnected += p =>
                {
                    Log($"[{slot.inputMode}] Lost connection");
                };

                // 快照: 序列化/反序列化所有 Entity 位置
                person.TakeSnapshot = () => TakeWorldSnapshot();
                person.LoadSnapshot = data => LoadWorldSnapshot(data);
            }

            person.Connect(config);
        }

        public void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;

            // Disconnect 保留身份（PlayerId/RoomId），下次 Connect 会发 Reconnect
            slot.person.Disconnect();
            // 保留 slot.person 引用！重连时 ConnectPerson 检测到 hasIdentity 走重连
            slot.state = "Disconnected";
            slot.frame = "0";
        }

        /// <summary>
        /// 完全移除 Person（不可重连）
        /// </summary>
        public void RemovePerson(PersonSlot slot)
        {
            if (slot.person != null)
            {
                slot.person.DisconnectAndClear();
                slot.person = null;
            }
            slot.inputProvider = null;
            slot.state = "Idle";
            slot.pid = "-";
            slot.frame = "0";

            // TODO: 移除实体
        }

        // ===================== Frame Handler =====================

        void OnAuthorityFrame(FrameData frame)
        {
            if (frame.Inputs == null) return;

            // 帧同步固定增量: moveSpeed * frameInterval(秒)
            float frameInterval = 1f / 20f;  // TODO: 从 FrameSyncInitData 获取
            float delta = moveSpeed * frameInterval;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                if (input.DataLength < 8) continue;

                var dir = DecodeInput(input.DataSpan);
                var pid = input.PlayerId;

                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                if (_entities.TryGetValue(pid, out var entity))
                    entity.ApplyMove(dir, delta);
            }
        }

        // ===================== Entity =====================

        void SpawnEntity(int playerId, Color color, string label)
        {
            if (_entities.ContainsKey(playerId)) return;
            var entity = PlayerEntity.Spawn(playerId, color, label);
            _entities[playerId] = entity;
            Log($"Spawned {label} (Player {playerId})");
        }

        void DestroyEntity(int playerId)
        {
            if (playerId <= 0) return;
            if (_entities.TryGetValue(playerId, out var entity))
            {
                if (entity != null)
                    Destroy(entity.gameObject);
                _entities.Remove(playerId);
            }
        }

        // ===================== Sync Check =====================

        void UpdateAuthority()
        {
            // 当前 authority 还在 Syncing，不用迁移
            if (_authorityPerson?.State == PersonState.Syncing)
                return;

            // 找第一个 Syncing 的 Person 作为新 authority
            Person oldAuth = _authorityPerson;
            _authorityPerson = null;
            foreach (var slot in persons)
            {
                if (slot.person?.State == PersonState.Syncing)
                {
                    _authorityPerson = slot.person;
                    break;
                }
            }

            if (_authorityPerson != oldAuth && _authorityPerson != null)
            {
                var authSlot = persons.Find(s => s.person == _authorityPerson);
                Log($"Authority migrated to [{authSlot?.inputMode}] P{_authorityPerson.PlayerId}");
                SetupPrediction(_authorityPerson);
            }

            // 旧 authority 失效时清理预测
            if (oldAuth != null && oldAuth != _authorityPerson)
            {
                ClearPrediction(oldAuth);
            }
        }

        void UpdateSyncStatus()
        {
            uint? first = null;
            int syncCount = 0;

            foreach (var slot in persons)
            {
                if (slot.person == null || slot.person.State != PersonState.Syncing) continue;
                syncCount++;
                var f = slot.person.FrameNumber;
                if (first == null) first = f;
                else if (f != first.Value)
                {
                    syncStatus = $"DIFF: {first} vs {f} ({syncCount} syncing)";
                    return;
                }
            }

            if (syncCount >= 2)
                syncStatus = $"IN SYNC (frame {first}, {syncCount} clients)";
            else if (syncCount == 1)
                syncStatus = $"1 client syncing (frame {first})";
            else
                syncStatus = "Waiting...";
        }

        // ===================== Codec =====================

        static void EncodeInput(Vector2 dir, byte[] buf)
        {
            var xb = BitConverter.GetBytes(dir.x);
            var yb = BitConverter.GetBytes(dir.y);
            Buffer.BlockCopy(xb, 0, buf, 0, 4);
            Buffer.BlockCopy(yb, 0, buf, 4, 4);
        }

        static Vector2 DecodeInput(ReadOnlySpan<byte> buf)
        {
            var tmp = new byte[8];
            buf.Slice(0, 8).CopyTo(tmp);
            return new Vector2(BitConverter.ToSingle(tmp, 0), BitConverter.ToSingle(tmp, 4));
        }

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000)
                logText = logText.Substring(0, 3000);
            Debug.Log($"[PersonMgr] {msg}");
        }

        // ===================== Prediction =====================

        void SetupPrediction(Person person)
        {
            if (!enablePrediction) { _predictionEnabled = false; return; }

            _simulation = new DemoSimulation
            {
                MoveSpeed = moveSpeed,
                FrameInterval = 1f / 20f,
            };

            // 初始化逻辑位置（从当前 Entity 位置）
            foreach (var kv in _entities)
            {
                if (kv.Value != null)
                    _simulation.LogicPositions[kv.Key] = (Vector2)kv.Value.transform.position;
            }

            // 获取房间内所有玩家 ID
            var playerIds = new List<int>();
            foreach (var slot in persons)
            {
                if (slot.person != null && slot.person.PlayerId > 0 &&
                    (slot.person.State == PersonState.Syncing || slot.person.State == PersonState.InRoom))
                    playerIds.Add(slot.person.PlayerId);
            }

            _prediction = new PredictionManager(_simulation);
            _prediction.MaxPredictionFrames = 8;
            _prediction.OnRollback += (frame, count) =>
            {
                Log($"Rollback! from={frame} replay={count}");
            };
            _prediction.OnFrameSimulated += (frame, isRollback) =>
            {
                // 预测或回滚执行帧后，确保新玩家有 Entity
                foreach (var kv in _simulation.LogicPositions)
                {
                    if (!_entities.ContainsKey(kv.Key))
                        SpawnEntity(kv.Key, Color.gray, $"P{kv.Key}");
                }
            };

            _prediction.Start(person.PlayerId, playerIds);

            // 绑定到 FrameSyncClient
            person.SetPrediction(_prediction);
            _predictionEnabled = true;

            Log($"Prediction enabled (maxAhead={_prediction.MaxPredictionFrames})");
        }

        void ClearPrediction(Person person)
        {
            person.ClearPrediction();
            _prediction = null;
            _simulation = null;
            _predictionEnabled = false;
        }

        // ===================== Snapshot =====================

        /// <summary>
        /// 序列化所有 Entity 位置
        /// Wire: [Count:2] + N × [PlayerId:4][PosX:4][PosY:4]
        /// </summary>
        byte[] TakeWorldSnapshot()
        {
            int count = _entities.Count;
            var buf = new byte[2 + count * 12];
            buf[0] = (byte)(count & 0xFF);
            buf[1] = (byte)((count >> 8) & 0xFF);
            int offset = 2;
            foreach (var kv in _entities)
            {
                var pos = kv.Value != null ? (Vector2)kv.Value.transform.position : Vector2.zero;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), kv.Key);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.x);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.y);
                offset += 4;
            }
            return buf;
        }

        /// <summary>
        /// 从快照恢复所有 Entity 位置
        /// </summary>
        void LoadWorldSnapshot(byte[] data)
        {
            if (data == null || data.Length < 2) return;
            int count = data[0] | (data[1] << 8);
            int offset = 2;
            for (int i = 0; i < count && offset + 12 <= data.Length; i++)
            {
                int pid = BitConverter.ToInt32(data, offset);
                offset += 4;
                float x = BitConverter.ToSingle(data, offset);
                offset += 4;
                float y = BitConverter.ToSingle(data, offset);
                offset += 4;

                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                if (_entities.TryGetValue(pid, out var entity) && entity != null)
                    entity.transform.position = new Vector3(x, y, 0);
            }
            Log($"Snapshot loaded: {count} entities restored");
        }

        // --- 供 HUD 读取 ---
        public (int ahead, int totalRollbacks, int totalRollbackFrames)? PredictionStats =>
            _prediction != null
                ? (_prediction.AheadFrames, _prediction.TotalRollbacks, _prediction.TotalRollbackFrames)
                : null;

        Color GetSyncColor()
        {
            if (syncStatus.Contains("IN SYNC")) return Color.green;
            if (syncStatus.Contains("DIFF")) return Color.yellow;
            return Color.white;
        }
    }
}
