using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;
using BoomNetworkDemo.EntitySync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo03 — 实体权威同步 × 真实多客户端（ParrelSync）
    ///
    /// UI 结构继承自 DemoManagerBase（PersonSlot 表格 + Room 管理 + Drop 测试）
    /// 帧同步逻辑使用 Entity Authority Sync:
    ///   本地立刻执行 + 远端 Dead Reckoning + 惯性纠偏
    /// </summary>
    public class EntitySyncMultiClientManager : DemoManagerBase
    {
        protected override string DemoInfoText =>
            "Demo03 - Entity Authority Sync (Multi-Client)\n" +
            "Editor A: Connect All → Create Room → Join All → Start Game\n" +
            "Editor B: 填 Target Room ID → Connect All → Join All\n" +
            "Server Window 开 Network Simulation 测试延迟效果";

        protected override string LogPrefix => "[Demo03]";

        // ===================== Entity Sync State =====================

        protected Dictionary<int, NetworkTransformSync> _syncs = new();
        private readonly HashSet<int> _entitySyncSetupDone = new();
        private Action<int, int, byte[], int, int> _entityStateHandler;

        // ===================== Entity Spawn: 加 NetworkTransformSync =====================

        protected override PlayerEntity OnSpawnEntity(int pid, Color color, string label)
        {
            var entity = PlayerEntity.Spawn(pid, color, label);
            var sync = entity.gameObject.AddComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.UseWorldWrap = true;
            sync.WorldHalfWidth = PlayerEntity.WorldHalfWidth;
            sync.WorldHalfHeight = PlayerEntity.WorldHalfHeight;
            sync.InitVisual((Vector2)entity.transform.position, entity.FacingAngle);
            _syncs[pid] = sync;
            return entity;
        }

        protected override void OnDestroyEntity(int pid)
        {
            _syncs.Remove(pid);
            _entitySyncSetupDone.Remove(pid);
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

            // 幂等保护：防止 OnFrameSyncStart 重复触发（late-join / reconnect）
            if (!_entitySyncSetupDone.Add(pid))
            {
                Log($"[{slot.inputMode}] SetupEntitySync skipped (already done for P{pid})");
                return;
            }

            var entity = SpawnEntity(pid, slot.color, slot.inputMode.ToString());
            var sync = _syncs[pid];
            sync.SetAuthority(true);
            sync.InitVisual((Vector2)entity.transform.position, 0);

            slot.person.RegisterAuthorityEntity(sync);

            // 移除旧 handler 再添加新的，防止事件累积
            if (_entityStateHandler != null)
                slot.person.OnEntityState -= _entityStateHandler;

            _entityStateHandler = (senderPid, entityId, stateData, offset, length) =>
            {
                if (_syncs.TryGetValue(entityId, out var remoteSyncComp) && !remoteSyncComp.IsAuthority)
                    remoteSyncComp.OnRemoteState(stateData, offset, length, senderPid);
            };
            slot.person.OnEntityState += _entityStateHandler;

            Log($"[{slot.inputMode}] Entity sync: P{pid} is authority");
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
                {
                    Log($"[DIAG] OnFrame#{frame.FrameNumber}: spawning unknown P{pid} (myPid={myPid}, inputCount={frame.Inputs.Length}, dataLen={input.DataLength})");
                    SpawnEntity(pid, Color.gray, $"P{pid}");
                }

                if (_syncs.TryGetValue(pid, out var sync) && sync.CorrectionCount == 0 && input.DataLength >= 8)
                {
                    if (_entities.TryGetValue(pid, out var entity) && entity != null)
                    {
                        var dir = new Vector2(
                            BitConverter.ToSingle(input.Data, 0),
                            BitConverter.ToSingle(input.Data, 4));
                        entity.ApplyMove(dir * moveSpeed, 1f / 20f);
                    }
                }
            }
        }

        // ===================== Snapshot: 额外重置 _syncs visual =====================

        protected override void LoadWorldSnapshot(byte[] data)
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
                if (_syncs.TryGetValue(pid, out var sync))
                    sync.InitVisual(new Vector2(x, y), ang);
            }
            _lastProcessedFrame = 0;
            Log($"Snapshot loaded: {count} entities");
        }
    }
}
