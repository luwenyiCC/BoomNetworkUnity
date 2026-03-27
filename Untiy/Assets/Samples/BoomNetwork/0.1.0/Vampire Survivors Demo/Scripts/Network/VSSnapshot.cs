// BoomNetwork VampireSurvivors Demo — Snapshot Serialization
//
// Serializes/deserializes the complete GameState for reconnect recovery.
// Only writes alive entities to minimize payload (~30KB max).

using System;
using System.IO;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class VSSnapshot
    {
        static readonly byte[] Magic = { (byte)'V', (byte)'S', (byte)'0', (byte)'1' };

        public static byte[] Serialize(GameState state)
        {
            using var ms = new MemoryStream(4096);
            using var w = new BinaryWriter(ms);

            // Header
            w.Write(Magic);
            w.Write(state.FrameNumber);
            w.Write(state.RngState);
            w.Write(state.WaveNumber);
            w.Write(state.WaveSpawnTimer);
            w.Write(state.WaveSpawnRemaining);

            // Players (always write all 4 slots)
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref state.Players[i];
                w.Write(p.IsActive);
                w.Write(p.IsAlive);
                w.Write(p.PosX); w.Write(p.PosZ);
                w.Write(p.FacingX); w.Write(p.FacingZ);
                w.Write(p.Hp); w.Write(p.MaxHp);
                w.Write(p.Xp); w.Write(p.Level);
                w.Write(p.XpToNextLevel);
                w.Write(p.KnifeCooldown);
                w.Write(p.InvincibilityFrames);
                w.Write(p.KillCount);
            }

            // Enemies (count-prefixed, alive only)
            ushort enemyCount = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (state.Enemies[i].IsAlive) enemyCount++;
            w.Write(enemyCount);
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;
                w.Write(e.PosX); w.Write(e.PosZ);
                w.Write(e.Hp);
                w.Write(e.TargetPlayerId);
            }

            // Projectiles
            ushort projCount = 0;
            for (int i = 0; i < GameState.MaxProjectiles; i++)
                if (state.Projectiles[i].IsAlive) projCount++;
            w.Write(projCount);
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var p = ref state.Projectiles[i];
                if (!p.IsAlive) continue;
                w.Write(p.PosX); w.Write(p.PosZ);
                w.Write(p.DirX); w.Write(p.DirZ);
                w.Write(p.LifetimeFrames);
                w.Write(p.OwnerPlayerId);
            }

            // Gems
            ushort gemCount = 0;
            for (int i = 0; i < GameState.MaxGems; i++)
                if (state.Gems[i].IsAlive) gemCount++;
            w.Write(gemCount);
            for (int i = 0; i < GameState.MaxGems; i++)
            {
                ref var g = ref state.Gems[i];
                if (!g.IsAlive) continue;
                w.Write(g.PosX); w.Write(g.PosZ);
                w.Write(g.Value);
            }

            return ms.ToArray();
        }

        public static void Deserialize(byte[] data, GameState state)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);

            // Header
            byte[] magic = r.ReadBytes(4);
            state.FrameNumber = r.ReadUInt32();
            state.RngState = r.ReadUInt32();
            state.WaveNumber = r.ReadInt32();
            state.WaveSpawnTimer = r.ReadUInt32();
            state.WaveSpawnRemaining = r.ReadUInt32();

            // Players
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref state.Players[i];
                p.IsActive = r.ReadBoolean();
                p.IsAlive = r.ReadBoolean();
                p.PosX = r.ReadSingle(); p.PosZ = r.ReadSingle();
                p.FacingX = r.ReadSingle(); p.FacingZ = r.ReadSingle();
                p.Hp = r.ReadInt32(); p.MaxHp = r.ReadInt32();
                p.Xp = r.ReadInt32(); p.Level = r.ReadInt32();
                p.XpToNextLevel = r.ReadInt32();
                p.KnifeCooldown = r.ReadUInt32();
                p.InvincibilityFrames = r.ReadUInt32();
                p.KillCount = r.ReadInt32();
            }

            // Clear all entity arrays
            Array.Clear(state.Enemies, 0, GameState.MaxEnemies);
            Array.Clear(state.Projectiles, 0, GameState.MaxProjectiles);
            Array.Clear(state.Gems, 0, GameState.MaxGems);

            // Enemies
            ushort enemyCount = r.ReadUInt16();
            for (int i = 0; i < enemyCount; i++)
            {
                ref var e = ref state.Enemies[i];
                e.IsAlive = true;
                e.PosX = r.ReadSingle(); e.PosZ = r.ReadSingle();
                e.Hp = r.ReadInt32();
                e.TargetPlayerId = r.ReadInt32();
            }

            // Projectiles
            ushort projCount = r.ReadUInt16();
            for (int i = 0; i < projCount; i++)
            {
                ref var p = ref state.Projectiles[i];
                p.IsAlive = true;
                p.PosX = r.ReadSingle(); p.PosZ = r.ReadSingle();
                p.DirX = r.ReadSingle(); p.DirZ = r.ReadSingle();
                p.LifetimeFrames = r.ReadUInt32();
                p.OwnerPlayerId = r.ReadInt32();
            }

            // Gems
            ushort gemCount = r.ReadUInt16();
            for (int i = 0; i < gemCount; i++)
            {
                ref var g = ref state.Gems[i];
                g.IsAlive = true;
                g.PosX = r.ReadSingle(); g.PosZ = r.ReadSingle();
                g.Value = r.ReadInt32();
            }
        }
    }
}
