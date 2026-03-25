using UnityEngine;
using System;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01: 基础帧同步 — 纯传统模式，无预测回滚。
    /// 最简实现：连接 → 房间 → 帧同步移动 → 重连。
    /// </summary>
    public class BasicPersonManager : DemoManagerBase
    {
        protected override string DemoInfoText =>
            "Demo01 - Basic Frame Sync\n" +
            "1. BoomNetwork > Server Window > Start Server (no -autoroom)\n" +
            "2. Play this scene\n" +
            "3. Click [Connect All]\n" +
            "4. Click [Start Game]\n" +
            "5. WASD = Player 1, Arrows = Player 2";

        protected override string LogPrefix => "[Demo01]";

        // ===================== Sync Status =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel, GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H3")]
        [DisplayAsString, LabelWidth(80)]
        public string selectedRoom = "None";

        // ===================== 输入节流 =====================

        private float _inputSendAccumulator;
        private const float INPUT_SEND_INTERVAL_MS = 50f;
        private bool _shouldSendInput;

        // ===================== Demo01 特殊：顺序连接 + 自动入房 =====================

        protected override void ConnectAllPersons() => ConnectSequential(0);

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

        protected override void OnWirePersonEvents(PersonSlot slot)
        {
            // Demo01: 连接后自动建房/入房
            slot.person.OnConnected += p =>
            {
                if (targetRoomId > 0) p.JoinRoom(targetRoomId);
                else p.CreateAndJoinRoom(config.defaultMaxPlayers);
            };
        }

        // ===================== 输入发送：20fps 节流 =====================

        protected override void Update()
        {
            float dt = Time.deltaTime;
            _inputSendAccumulator += dt * 1000f;
            _shouldSendInput = _inputSendAccumulator >= INPUT_SEND_INTERVAL_MS;
            if (_shouldSendInput) _inputSendAccumulator -= INPUT_SEND_INTERVAL_MS;

            base.Update();

            UpdateSyncStatus();
            selectedRoom = targetRoomId > 0 ? $"Room {targetRoomId}" : "None";
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

        // ===================== 帧处理：传统模式（全部 input ApplyMove）=====================

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

        // ===================== Sync Status =====================

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

        Color GetSyncColor()
        {
            if (syncStatus.Contains("IN SYNC")) return Color.green;
            if (syncStatus.Contains("DIFF")) return Color.yellow;
            return Color.white;
        }
    }
}
