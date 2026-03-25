using UnityEngine;
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
    public class MultiClientPersonManager : DemoManagerBase
    {
        protected override string DemoInfoText =>
            "Demo01.1 - Multi-Client Frame Sync (ParrelSync)\n" +
            "Editor A: Connect All → Create Room → Join All → Start Game\n" +
            "Editor B: 填 Target Room ID → Connect All → Join All\n" +
            "Drop1s/Drop8s 测试快速重连/快照重连";

        protected override string LogPrefix => "[Demo01.1]";

        // ===================== Sync / Hash =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel, GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        [TitleGroup("Sync")]
        [DisplayAsString, LabelWidth(80)]
        public string worldHash = "-";

        // ===================== 输入节流 =====================

        private float _inputSendAccumulator;
        private const float INPUT_SEND_INTERVAL_MS = 50f;
        private bool _shouldSendInput;

        // ===================== Update =====================

        protected override void Update()
        {
            float dt = Time.deltaTime;
            _inputSendAccumulator += dt * 1000f;
            _shouldSendInput = _inputSendAccumulator >= INPUT_SEND_INTERVAL_MS;
            if (_shouldSendInput) _inputSendAccumulator -= INPUT_SEND_INTERVAL_MS;

            base.Update();
        }

        protected override void OnUpdateExtra()
        {
            UpdateSyncStatus();
            UpdateWorldHash();
        }

        protected override void UpdateSlotInput(PersonSlot slot, float dt)
        {
            if (!_shouldSendInput) return;
            if (slot.person.State != PersonState.Syncing || slot.inputProvider == null) return;

            var dir = slot.inputProvider.GetMoveInput();
            if (dir.sqrMagnitude > 0.001f)
            {
                EncodeInput(dir, _inputBuf);
                slot.person.SendInput(_inputBuf);
            }
        }

        // ===================== 帧处理：传统模式 =====================

        protected override void OnFrame(PersonSlot slot, FrameData frame)
        {
            if (frame.FrameNumber <= _lastProcessedFrame) return;
            _lastProcessedFrame = frame.FrameNumber;

            if (frame.Inputs == null) return;
            float delta = moveSpeed * (GetFrameIntervalMs() / 1000f);

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

        // ===================== Sync / Hash =====================

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

        void UpdateWorldHash()
        {
            if (_entities.Count == 0) { worldHash = "-"; return; }
            var sortedKeys = new List<int>(_entities.Keys);
            sortedKeys.Sort();
            uint hash = 2166136261u;
            foreach (var key in sortedKeys)
            {
                var entity = _entities[key];
                if (entity == null) continue;
                var pos = entity.transform.position;
                var rot = entity.transform.rotation.eulerAngles;
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
