using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;
using BoomNetworkDemo.EntitySync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo02 — 实体权威同步演示
    ///
    /// 每个玩家控制自己的实体（authority），输入消息自动携带权威状态。
    /// 远端实体通过 Dead Reckoning + 惯性模型平滑追踪。
    ///
    /// 验证流程：
    /// 1. BoomNetwork > Server Window > Start Server (config.yaml, 非 autoroom)
    /// 2. Play this scene
    /// 3. Connect All → Create Room → Join All → Start Game
    /// 4. WASD 控制第一个角色，观察远端实体的平滑纠偏
    /// </summary>
    public class EntitySyncDemoManager : MonoBehaviour
    {
        [InfoBox(
            "Demo02 - Entity Authority Sync\n" +
            "Connect All → Create Room → Join All → Start Game\n" +
            "WASD = Player 1, Arrows = Player 2\n" +
            "远端实体通过 Dead Reckoning + 惯性模型平滑追踪",
            InfoMessageType.Info)]
        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
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
            [TableColumnWidth(80)]  public InputMode inputMode = InputMode.WASD;
            [TableColumnWidth(60)]  public Color color = Color.green;
            [TableColumnWidth(70), DisplayAsString]  public string state = "Idle";
            [TableColumnWidth(40), DisplayAsString]  public string pid = "-";
            [TableColumnWidth(60), DisplayAsString]  public string frame = "0";

            [TableColumnWidth(45), Button("Conn")]
            public void BtnConnect() => _manager?.ConnectPerson(this, null);
            [TableColumnWidth(45), Button("Leave")]
            public void BtnLeave() => person?.LeaveRoom();
            [TableColumnWidth(40), Button("Disc")]
            public void BtnDisconnect() => _manager?.DisconnectPerson(this);

            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal EntitySyncDemoManager _manager;
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
                Log($"Room {rid} created");
            });
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H2")]
        [Button("Connect All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void ConnectAll()
        {
            foreach (var slot in persons)
                if (slot.person == null || slot.person.State == PersonState.Disconnected)
                    ConnectPerson(slot, null);
        }

        [HorizontalGroup("Room/H2")]
        [Button("Join All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.5f)]
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
        [Button("Disc All", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DisconnectAll()
        {
            foreach (var slot in persons) DisconnectPerson(slot);
        }

        // ===================== Status =====================

        [TitleGroup("Status")]
        [DisplayAsString, HideLabel]
        public string logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private Dictionary<int, NetworkTransformSync> _syncs = new();
        private byte[] _inputBuf = new byte[8];
        private uint _lastProcessedFrame;

        void Awake()
        {
            foreach (var slot in persons) slot._manager = this;
        }

        void Update()
        {
            foreach (var slot in persons)
            {
                if (slot.person == null) continue;
                slot.person.Tick(Time.deltaTime * 1000f);
                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? $"P{slot.person.PlayerId}" : "-";
                slot.frame = slot.person.FrameNumber.ToString();
            }
        }

        // ===================== Person Lifecycle =====================

        void ConnectPerson(PersonSlot slot, Action onReady)
        {
            if (slot.person == null)
            {
                slot.person = new Person();
                slot.inputProvider = InputProviderFactory.Create(slot.inputMode);
                WirePersonEvents(slot, onReady);
            }
            slot.person.Connect(config);
        }

        void WirePersonEvents(PersonSlot slot, Action onReady)
        {
            var person = slot.person;

            person.OnConnected += p => Log($"[{slot.inputMode}] Connected as P{p.PlayerId}");
            person.OnJoinedRoom += p => Log($"[{slot.inputMode}] Joined room {p.RoomId}");
            person.OnReady += p => { Log($"[{slot.inputMode}] Ready"); onReady?.Invoke(); };

            person.OnFrameSyncStart += (p, data) =>
            {
                Log($"[{slot.inputMode}] FrameSync started (rate={data.FrameRate})");
                SetupEntitySync(slot);
            };

            person.OnFrame += (p, frame) => OnAuthorityFrame(slot, frame);

            person.OnRemotePlayerJoined += (p, pid) =>
            {
                Log($"[{slot.inputMode}] Remote P{pid} joined");
                SpawnEntity(pid, Color.gray, $"P{pid}");
            };
            person.OnRemotePlayerLeft += (p, pid) =>
            {
                Log($"[{slot.inputMode}] Remote P{pid} left");
                DestroyEntity(pid);
            };

            person.OnDisconnected += p => Log($"[{slot.inputMode}] Disconnected");
            person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");
        }

        void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;
            slot.person.DisconnectAndClear();
            slot.person = null;
        }

        // ===================== Entity Sync Setup =====================

        void SetupEntitySync(PersonSlot slot)
        {
            int pid = slot.person.PlayerId;

            // 生成本地权威实体
            var entity = SpawnEntity(pid, slot.color, slot.inputMode.ToString());
            var sync = _syncs[pid];

            sync.SetAuthority(true);
            sync.InitVisual((Vector2)entity.transform.position, 0);

            // 注册到框架：SendInput 后自动发送权威实体状态
            slot.person.RegisterAuthorityEntity(sync);

            // 接收远端实体状态
            slot.person.OnEntityState += (senderPid, entityId, data, offset, length) =>
            {
                if (_syncs.TryGetValue(entityId, out var remoteSyncComp) && !remoteSyncComp.IsAuthority)
                    remoteSyncComp.OnRemoteState(data, offset, length, senderPid);
            };

            Log($"[{slot.inputMode}] Entity sync setup: P{pid} is authority");
        }

        // ===================== Frame Handling =====================

        void OnAuthorityFrame(PersonSlot slot, FrameData frame)
        {
            if (frame.FrameNumber <= _lastProcessedFrame) return;
            _lastProcessedFrame = frame.FrameNumber;

            float dt = 1f / 20f; // 服务器帧间隔

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                int pid = input.PlayerId;

                // 确保实体存在
                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                if (!_entities.TryGetValue(pid, out var entity) || entity == null)
                    continue;

                // 解码输入
                var dir = Vector2.zero;
                if (input.DataLength >= 8)
                {
                    dir.x = BitConverter.ToSingle(input.Data, 0);
                    dir.y = BitConverter.ToSingle(input.Data, 4);
                }

                // 判断是否是本 slot 管理的实体
                bool isLocal = (pid == slot.person?.PlayerId);
                if (isLocal)
                {
                    // Authority：直接应用移动
                    entity.ApplyMove(dir * moveSpeed, dt);

                    // 更新速度（用于 Dead Reckoning）
                    if (_syncs.TryGetValue(pid, out var sync))
                        sync.SetVelocity(dir * moveSpeed);
                }
                // Remote 实体由 OnEntityState → NetworkTransformSync 自动处理
            }
        }

        // ===================== Entity Management =====================

        PlayerEntity SpawnEntity(int pid, Color color, string label)
        {
            if (_entities.TryGetValue(pid, out var existing) && existing != null)
                return existing;

            var entity = PlayerEntity.Spawn(pid, color, label);
            _entities[pid] = entity;

            // 所有实体都加 NetworkTransformSync（Remote 模式，收到权威状态时纠偏）
            var sync = entity.gameObject.AddComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.InitVisual((Vector2)entity.transform.position, entity.FacingAngle);
            _syncs[pid] = sync;

            return entity;
        }

        void DestroyEntity(int pid)
        {
            if (_entities.TryGetValue(pid, out var entity) && entity != null)
                Destroy(entity.gameObject);
            _entities.Remove(pid);
            _syncs.Remove(pid);
        }

        // ===================== Helpers =====================

        Person FindConnectedPerson()
        {
            foreach (var slot in persons)
                if (slot.person != null && (slot.person.State == PersonState.Connected
                    || slot.person.State == PersonState.InRoom
                    || slot.person.State == PersonState.Syncing))
                    return slot.person;
            return null;
        }

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000) logText = logText.Substring(0, 3000);
            Debug.Log($"[Demo02] {msg}");
        }
    }
}
