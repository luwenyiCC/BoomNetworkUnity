using System;
using BoomNetwork.Core.Prediction;
using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo 的确定性模拟实现。
    ///
    /// 用平行数组（排序 playerId）替代 Dictionary，保证：
    ///   1. 遍历顺序确定性（按 playerId 升序）
    ///   2. 连续内存，缓存友好
    ///   3. N<10 时线性扫描比 hash 更快
    ///   4. SaveState/LoadState 字节级一致
    /// </summary>
    public class DemoSimulation : ISimulation
    {
        private int[] _playerIds;
        private float[] _posX;
        private float[] _posY;
        private int _count;
        private readonly int _capacity;

        public float MoveSpeed { get; set; } = 5f;
        public float FrameInterval { get; set; } = 1f / 20f;
        public float BoundsX { get; set; } = 8f;
        public float BoundsY { get; set; } = 5f;

        public int PlayerCount => _count;

        public DemoSimulation(int maxPlayers = 16)
        {
            _capacity = maxPlayers;
            _playerIds = new int[maxPlayers];
            _posX = new float[maxPlayers];
            _posY = new float[maxPlayers];
            _count = 0;
        }

        /// <summary>
        /// 查找 playerId 的索引。不存在返回 -1。
        /// N<10 线性扫描比二分快。
        /// </summary>
        private int IndexOf(int playerId)
        {
            for (int i = 0; i < _count; i++)
                if (_playerIds[i] == playerId) return i;
            return -1;
        }

        /// <summary>
        /// 插入玩家（保持按 playerId 排序）
        /// </summary>
        private int InsertSorted(int playerId)
        {
            if (_count >= _capacity) return -1;

            // 找插入位置
            int pos = _count;
            for (int i = 0; i < _count; i++)
            {
                if (_playerIds[i] > playerId) { pos = i; break; }
            }

            // 后移
            for (int i = _count; i > pos; i--)
            {
                _playerIds[i] = _playerIds[i - 1];
                _posX[i] = _posX[i - 1];
                _posY[i] = _posY[i - 1];
            }

            _playerIds[pos] = playerId;
            _posX[pos] = 0;
            _posY[pos] = 0;
            _count++;
            return pos;
        }

        /// <summary>
        /// 获取玩家位置。不存在返回 Vector2.zero。
        /// </summary>
        public Vector2 GetPosition(int playerId)
        {
            int idx = IndexOf(playerId);
            if (idx < 0) return Vector2.zero;
            return new Vector2(_posX[idx], _posY[idx]);
        }

        /// <summary>
        /// 设置玩家位置（不存在则插入）
        /// </summary>
        public void SetPosition(int playerId, Vector2 pos)
        {
            int idx = IndexOf(playerId);
            if (idx < 0) idx = InsertSorted(playerId);
            if (idx < 0) return;
            _posX[idx] = pos.x;
            _posY[idx] = pos.y;
        }

        /// <summary>
        /// 遍历所有玩家位置（确定性顺序：按 playerId 升序）
        /// </summary>
        public void ForEachPosition(Action<int, Vector2> callback)
        {
            for (int i = 0; i < _count; i++)
                callback(_playerIds[i], new Vector2(_posX[i], _posY[i]));
        }

        public void Simulate(FrameInput[] inputs, int inputCount)
        {
            if (inputs == null) return;

            float delta = MoveSpeed * FrameInterval;

            for (int i = 0; i < inputCount; i++)
            {
                var pid = inputs[i].PlayerId;
                var data = inputs[i].Data;
                if (data == null || data.Length < 8) continue;

                float dx = BitConverter.ToSingle(data, 0);
                float dy = BitConverter.ToSingle(data, 4);

                int idx = IndexOf(pid);
                if (idx < 0) idx = InsertSorted(pid);
                if (idx < 0) continue;

                float x = _posX[idx] + dx * delta;
                float y = _posY[idx] + dy * delta;

                // 屏幕环绕
                if (x > BoundsX) x -= BoundsX * 2;
                if (x < -BoundsX) x += BoundsX * 2;
                if (y > BoundsY) y -= BoundsY * 2;
                if (y < -BoundsY) y += BoundsY * 2;

                _posX[idx] = x;
                _posY[idx] = y;
            }
        }

        // 复用 buffer 避免每帧分配
        private byte[] _saveBuffer;

        public byte[] SaveState()
        {
            int size = 2 + _count * 12;
            if (_saveBuffer == null || _saveBuffer.Length < size)
                _saveBuffer = new byte[size];

            _saveBuffer[0] = (byte)(_count & 0xFF);
            _saveBuffer[1] = (byte)((_count >> 8) & 0xFF);
            int offset = 2;

            // 已按 playerId 排序，遍历顺序确定
            for (int i = 0; i < _count; i++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(_saveBuffer, offset, 4), _playerIds[i]);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(_saveBuffer, offset, 4), _posX[i]);
                offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(_saveBuffer, offset, 4), _posY[i]);
                offset += 4;
            }

            // 返回精确大小的副本（SnapshotBuffer 会存储它）
            var result = new byte[size];
            Buffer.BlockCopy(_saveBuffer, 0, result, 0, size);
            return result;
        }

        public void LoadState(byte[] data)
        {
            _count = 0;
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

                if (_count < _capacity)
                {
                    _playerIds[_count] = pid;
                    _posX[_count] = x;
                    _posY[_count] = y;
                    _count++;
                }
            }
            // data 已按 playerId 排序（SaveState 保证的），不需要再排
        }

        public uint StateHash()
        {
            uint hash = (uint)_count;
            for (int i = 0; i < _count; i++)
            {
                hash ^= (uint)_playerIds[i];
                hash ^= (uint)BitConverter.SingleToInt32Bits(_posX[i]);
                hash ^= (uint)BitConverter.SingleToInt32Bits(_posY[i]);
                hash = (hash << 7) | (hash >> 25); // rotate 避免简单 XOR 碰撞
            }
            return hash;
        }
    }
}
