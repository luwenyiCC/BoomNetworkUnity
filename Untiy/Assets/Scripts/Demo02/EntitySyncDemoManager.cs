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
    /// </summary>
    public class EntitySyncDemoManager : DemoManagerBase
    {
        protected override string DemoInfoText =>
            "Demo02 - Entity Authority Sync\n" +
            "Connect All → Create Room → Join All → Start Game\n" +
            "WASD = Player 1, Arrows = Player 2\n" +
            "远端实体通过 Dead Reckoning + 惯性模型平滑追踪";

        protected override string LogPrefix => "[Demo02]";

        // ===================== Entity Sync State =====================

        protected Dictionary<int, NetworkTransformSync> _syncs = new();

        // ===================== Entity Spawn: 加 NetworkTransformSync =====================

        protected override PlayerEntity OnSpawnEntity(int pid, Color color, string label)
        {
            var entity = PlayerEntity.Spawn(pid, color, label);
            var sync = entity.gameObject.AddComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.InitVisual((Vector2)entity.transform.position, entity.FacingAngle);
            _syncs[pid] = sync;
            return entity;
        }

        protected override void OnDestroyEntity(int pid)
        {
            _syncs.Remove(pid);
        }

        // ===================== 输入：权威实体立刻执行 =====================

        protected override void UpdateSlotInput(PersonSlot slot, float dt)
        {
            if (slot.person.State != PersonState.Syncing || slot.inputProvider == null) return;

            int pid = slot.person.PlayerId;
            if (!_entities.TryGetValue(pid, out var entity) || entity == null) return;
            if (!_syncs.TryGetValue(pid, out var sync) || !sync.IsAuthority) return;

            var dir = slot.inputProvider.GetMoveInput();
            if (dir.sqrMagnitude > 0.01f)
            {
                entity.ApplyMove(dir * moveSpeed, dt);
                sync.SetVelocity(dir * moveSpeed);
                EncodeInput(dir, _inputBuf);
                slot.person.SendInput(_inputBuf);
            }
            else
            {
                sync.SetVelocity(Vector2.zero);
            }
        }

        // ===================== FrameSync 开始：设置权威 =====================

        protected override void OnFrameSyncStart(PersonSlot slot, FrameSyncInitData data)
        {
            SetupEntitySync(slot);
        }

        void SetupEntitySync(PersonSlot slot)
        {
            int pid = slot.person.PlayerId;
            var entity = SpawnEntity(pid, slot.color, slot.inputMode.ToString());
            var sync = _syncs[pid];
            sync.SetAuthority(true);
            sync.InitVisual((Vector2)entity.transform.position, 0);

            slot.person.RegisterAuthorityEntity(sync);

            slot.person.OnEntityState += (senderPid, entityId, data, offset, length) =>
            {
                if (_syncs.TryGetValue(entityId, out var remoteSyncComp) && !remoteSyncComp.IsAuthority)
                    remoteSyncComp.OnRemoteState(data, offset, length, senderPid);
            };

            Log($"[{slot.inputMode}] Entity sync setup: P{pid} is authority");
        }

        // ===================== 帧处理：跳过本地，远端兜底 =====================

        protected override void OnFrame(PersonSlot slot, FrameData frame)
        {
            if (frame.FrameNumber <= _lastProcessedFrame) return;
            _lastProcessedFrame = frame.FrameNumber;

            int myPid = slot.person?.PlayerId ?? 0;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                int pid = input.PlayerId;
                if (pid == myPid) continue;

                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                if (!_syncs.TryGetValue(pid, out var sync) || sync.IsAuthority) continue;
                if (!_entities.TryGetValue(pid, out var entity) || entity == null) continue;

                if (sync.CorrectionCount == 0 && input.DataLength >= 8)
                {
                    var dir = new Vector2(
                        BitConverter.ToSingle(input.Data, 0),
                        BitConverter.ToSingle(input.Data, 4));
                    entity.ApplyMove(dir * moveSpeed, 1f / 20f);
                }
            }
        }
    }
}
