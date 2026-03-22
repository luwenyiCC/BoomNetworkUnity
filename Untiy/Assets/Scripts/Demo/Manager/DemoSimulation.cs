using System;
using System.Collections.Generic;
using BoomNetwork.Core.Prediction;
using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo 的确定性模拟实现。
    ///
    /// 管理所有玩家的逻辑位置（不直接操作 Entity Transform）。
    /// PredictionManager 通过此接口 Simulate/SaveState/LoadState。
    /// PersonManager 读取 LogicPositions 渲染 Entity（可加视觉平滑）。
    /// </summary>
    public class DemoSimulation : ISimulation
    {
        /// <summary>
        /// 每个玩家的逻辑位置
        /// </summary>
        public Dictionary<int, Vector2> LogicPositions { get; } = new();

        /// <summary>
        /// 移动速度
        /// </summary>
        public float MoveSpeed { get; set; } = 5f;

        /// <summary>
        /// 帧间隔（秒）
        /// </summary>
        public float FrameInterval { get; set; } = 1f / 20f;

        /// <summary>
        /// 屏幕边界（用于环绕）
        /// </summary>
        public float BoundsX { get; set; } = 8f;
        public float BoundsY { get; set; } = 5f;

        public void Simulate(FrameInput[] inputs)
        {
            if (inputs == null) return;

            float delta = MoveSpeed * FrameInterval;

            for (int i = 0; i < inputs.Length; i++)
            {
                var pid = inputs[i].PlayerId;
                var data = inputs[i].Data;
                if (data == null || data.Length < 8) continue;

                float dx = BitConverter.ToSingle(data, 0);
                float dy = BitConverter.ToSingle(data, 4);

                if (!LogicPositions.TryGetValue(pid, out var pos))
                    pos = Vector2.zero;

                pos.x += dx * delta;
                pos.y += dy * delta;

                // 屏幕环绕
                if (pos.x > BoundsX) pos.x -= BoundsX * 2;
                if (pos.x < -BoundsX) pos.x += BoundsX * 2;
                if (pos.y > BoundsY) pos.y -= BoundsY * 2;
                if (pos.y < -BoundsY) pos.y += BoundsY * 2;

                LogicPositions[pid] = pos;
            }
        }

        public byte[] SaveState()
        {
            int count = LogicPositions.Count;
            var buf = new byte[2 + count * 12];
            buf[0] = (byte)(count & 0xFF);
            buf[1] = (byte)((count >> 8) & 0xFF);
            int offset = 2;
            foreach (var kv in LogicPositions)
            {
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), kv.Key);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), kv.Value.x);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), kv.Value.y);
                offset += 4;
            }
            return buf;
        }

        public void LoadState(byte[] data)
        {
            LogicPositions.Clear();
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
                LogicPositions[pid] = new Vector2(x, y);
            }
        }

        public uint StateHash()
        {
            // 简单 hash: 所有位置的 XOR
            uint hash = 0;
            foreach (var kv in LogicPositions)
            {
                hash ^= (uint)kv.Key;
                hash ^= (uint)BitConverter.SingleToInt32Bits(kv.Value.x);
                hash ^= (uint)BitConverter.SingleToInt32Bits(kv.Value.y);
            }
            return hash;
        }
    }
}
