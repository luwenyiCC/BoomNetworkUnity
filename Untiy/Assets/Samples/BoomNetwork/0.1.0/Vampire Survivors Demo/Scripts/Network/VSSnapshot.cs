// BoomNetwork VampireSurvivors Demo — Snapshot Serialization (Phase 2)
//
// Serializes/deserializes complete GameState including weapon slots,
// orb state, enemy types, projectile types, and lightning flashes.

using System;
using System.IO;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class VSSnapshot
    {
        static readonly byte[] Magic = { (byte)'V', (byte)'S', (byte)'0', (byte)'2' };

        public static byte[] Serialize(GameState state)
        {
            using var ms = new MemoryStream(8192);
            using var w = new BinaryWriter(ms);

            // Header
            w.Write(Magic);
            w.Write(state.FrameNumber);
            w.Write(state.RngState);
            w.Write(state.WaveNumber);
            w.Write(state.WaveSpawnTimer);
            w.Write(state.WaveSpawnRemaining);

            // Players
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
                w.Write(p.InvincibilityFrames);
                w.Write(p.KillCount);
                w.Write(p.PendingLevelUp);
                w.Write(p.UpgradeChoice);

                // Weapon slots
                for (int ws = 0; ws < PlayerState.MaxWeaponSlots; ws++)
                {
                    ref var wslot = ref p.GetWeapon(ws);
                    w.Write((byte)wslot.Type);
                    w.Write(wslot.Level);
                    w.Write(wslot.Cooldown);
                }

                // Orbs
                for (int o = 0; o < PlayerState.MaxOrbs; o++)
                {
                    ref var orb = ref p.GetOrb(o);
                    w.Write(orb.Active);
                    w.Write(orb.AngleDeg);
                }
            }

            // Enemies (alive only)
            ushort enemyCount = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (state.Enemies[i].IsAlive) enemyCount++;
            w.Write(enemyCount);
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;
                w.Write((byte)e.Type);
                w.Write(e.PosX); w.Write(e.PosZ);
                w.Write(e.DirX); w.Write(e.DirZ);
                w.Write(e.Hp);
                w.Write(e.TargetPlayerId);
                w.Write(e.BehaviorTimer);
            }

            // Projectiles (alive only)
            ushort projCount = 0;
            for (int i = 0; i < GameState.MaxProjectiles; i++)
                if (state.Projectiles[i].IsAlive) projCount++;
            w.Write(projCount);
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var p = ref state.Projectiles[i];
                if (!p.IsAlive) continue;
                w.Write((byte)p.Type);
                w.Write(p.PosX); w.Write(p.PosZ);
                w.Write(p.DirX); w.Write(p.DirZ);
                w.Write(p.Radius);
                w.Write(p.LifetimeFrames);
                w.Write(p.OwnerPlayerId);
                w.Write(p.DamageTick);
            }

            // Gems (alive only)
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

            // Lightning flashes
            w.Write((byte)GameState.MaxLightningFlashes);
            for (int i = 0; i < GameState.MaxLightningFlashes; i++)
            {
                ref var f = ref state.Flashes[i];
                w.Write(f.PosX); w.Write(f.PosZ);
                w.Write(f.FramesLeft);
            }

            return ms.ToArray();
        }

        public static void Deserialize(byte[] data, GameState state)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);

            r.ReadBytes(4); // magic
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
                p.InvincibilityFrames = r.ReadUInt32();
                p.KillCount = r.ReadInt32();
                p.PendingLevelUp = r.ReadBoolean();
                p.UpgradeChoice = r.ReadByte();

                for (int ws = 0; ws < PlayerState.MaxWeaponSlots; ws++)
                {
                    ref var wslot = ref p.GetWeapon(ws);
                    wslot.Type = (WeaponType)r.ReadByte();
                    wslot.Level = r.ReadInt32();
                    wslot.Cooldown = r.ReadUInt32();
                }

                for (int o = 0; o < PlayerState.MaxOrbs; o++)
                {
                    ref var orb = ref p.GetOrb(o);
                    orb.Active = r.ReadBoolean();
                    orb.AngleDeg = r.ReadSingle();
                }
            }

            // Clear arrays
            Array.Clear(state.Enemies, 0, GameState.MaxEnemies);
            Array.Clear(state.Projectiles, 0, GameState.MaxProjectiles);
            Array.Clear(state.Gems, 0, GameState.MaxGems);
            Array.Clear(state.Flashes, 0, GameState.MaxLightningFlashes);

            // Enemies
            ushort enemyCount = r.ReadUInt16();
            for (int i = 0; i < enemyCount; i++)
            {
                ref var e = ref state.Enemies[i];
                e.IsAlive = true;
                e.Type = (EnemyType)r.ReadByte();
                e.PosX = r.ReadSingle(); e.PosZ = r.ReadSingle();
                e.DirX = r.ReadSingle(); e.DirZ = r.ReadSingle();
                e.Hp = r.ReadInt32();
                e.TargetPlayerId = r.ReadInt32();
                e.BehaviorTimer = r.ReadUInt32();
            }

            // Projectiles
            ushort projCount = r.ReadUInt16();
            for (int i = 0; i < projCount; i++)
            {
                ref var p = ref state.Projectiles[i];
                p.IsAlive = true;
                p.Type = (ProjectileType)r.ReadByte();
                p.PosX = r.ReadSingle(); p.PosZ = r.ReadSingle();
                p.DirX = r.ReadSingle(); p.DirZ = r.ReadSingle();
                p.Radius = r.ReadSingle();
                p.LifetimeFrames = r.ReadUInt32();
                p.OwnerPlayerId = r.ReadInt32();
                p.DamageTick = r.ReadUInt32();
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

            // Lightning flashes
            byte flashCount = r.ReadByte();
            for (int i = 0; i < flashCount; i++)
            {
                ref var f = ref state.Flashes[i];
                f.PosX = r.ReadSingle(); f.PosZ = r.ReadSingle();
                f.FramesLeft = r.ReadUInt32();
            }
        }
    }
}
