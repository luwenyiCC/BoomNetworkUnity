// BoomNetwork VampireSurvivors Demo — Spatial Hash Collision System (Phase 2)
//
// Handles: player weapons vs enemies, enemies vs players,
// bone shards vs players, orbs vs enemies, holy puddle vs enemies,
// player vs XP gems.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class CollisionSystem
    {
        const float CellSize = 2f;
        const int GridCells = 20;
        const int TotalCells = GridCells * GridCells;

        static readonly int[] BucketHeads = new int[TotalCells];
        static readonly int[] NextInBucket = new int[GameState.MaxEnemies];
        static readonly float[] _posX = new float[GameState.MaxEnemies];
        static readonly float[] _posZ = new float[GameState.MaxEnemies];

        static int CellIndex(float x, float z)
        {
            int cx = (int)((x + GameState.ArenaHalfSize) / CellSize);
            int cz = (int)((z + GameState.ArenaHalfSize) / CellSize);
            if (cx < 0) cx = 0; else if (cx >= GridCells) cx = GridCells - 1;
            if (cz < 0) cz = 0; else if (cz >= GridCells) cz = GridCells - 1;
            return cz * GridCells + cx;
        }

        public static void CachePositions(GameState state)
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                _posX[i] = state.Enemies[i].PosX;
                _posZ[i] = state.Enemies[i].PosZ;
            }
        }

        public static void Rebuild(GameState state)
        {
            for (int i = 0; i < TotalCells; i++) BucketHeads[i] = -1;
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                if (!state.Enemies[i].IsAlive) continue;
                int cell = CellIndex(_posX[i], _posZ[i]);
                NextInBucket[i] = BucketHeads[cell];
                BucketHeads[cell] = i;
            }
        }

        public static void Resolve(GameState state)
        {
            ResolvePlayerProjectilesVsEnemies(state);
            ResolveOrbsVsEnemies(state);
            ResolveHolyPuddleVsEnemies(state);
            ResolveEnemiesVsPlayers(state);
            ResolveBoneShardsVsPlayers(state);
            ResolvePlayersVsGems(state);
        }

        static void ResolvePlayerProjectilesVsEnemies(GameState state)
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive || proj.Type != ProjectileType.Knife) continue;

                float r = proj.Radius + 0.4f; // approximate enemy radius
                int hit = QueryNearest(proj.PosX, proj.PosZ, r);
                if (hit < 0) continue;

                ref var enemy = ref state.Enemies[hit];
                float actualR = proj.Radius + GameState.GetEnemyRadius(enemy.Type);
                float dx = proj.PosX - _posX[hit];
                float dz = proj.PosZ - _posZ[hit];
                if (dx * dx + dz * dz > actualR * actualR) continue;

                enemy.Hp -= GameState.KnifeDamage;
                proj.IsAlive = false;

                if (enemy.Hp <= 0)
                    KillEnemy(state, hit, proj.OwnerPlayerId);
            }
        }

        static void ResolveOrbsVsEnemies(GameState state)
        {
            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive) continue;

                for (int o = 0; o < PlayerState.MaxOrbs; o++)
                {
                    ref var orb = ref player.GetOrb(o);
                    if (!orb.Active) continue;

                    // Compute orb world position
                    float rad = orb.AngleDeg * 0.01745329f;
                    float orbX = player.PosX + (float)Math.Cos(rad) * GameState.OrbOrbitRadius;
                    float orbZ = player.PosZ + (float)Math.Sin(rad) * GameState.OrbOrbitRadius;

                    float r = GameState.OrbHitRadius + 0.4f;
                    int hit = QueryNearest(orbX, orbZ, r);
                    if (hit < 0) continue;

                    ref var enemy = ref state.Enemies[hit];
                    float actualR = GameState.OrbHitRadius + GameState.GetEnemyRadius(enemy.Type);
                    float dx = orbX - _posX[hit];
                    float dz = orbZ - _posZ[hit];
                    if (dx * dx + dz * dz > actualR * actualR) continue;

                    enemy.Hp -= GameState.OrbDamage;
                    if (enemy.Hp <= 0)
                        KillEnemy(state, hit, p);
                }
            }
        }

        static void ResolveHolyPuddleVsEnemies(GameState state)
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive || proj.Type != ProjectileType.HolyPuddle) continue;

                // Only deal damage on tick interval
                if (proj.DamageTick % GameState.HolyWaterDamageTick != 0) continue;

                float r = proj.Radius + 0.4f;
                // Query all enemies in range (not just nearest)
                ForEachInRange(proj.PosX, proj.PosZ, r, (int idx) =>
                {
                    ref var enemy = ref state.Enemies[idx];
                    float actualR = proj.Radius + GameState.GetEnemyRadius(enemy.Type);
                    float dx = proj.PosX - _posX[idx];
                    float dz = proj.PosZ - _posZ[idx];
                    if (dx * dx + dz * dz > actualR * actualR) return;

                    enemy.Hp -= GameState.HolyWaterDamage;
                    if (enemy.Hp <= 0)
                        KillEnemy(state, idx, proj.OwnerPlayerId);
                });
            }
        }

        static void ResolveEnemiesVsPlayers(GameState state)
        {
            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive || player.InvincibilityFrames > 0)
                    continue;

                float r = GameState.PlayerRadius + 0.4f;
                int hit = QueryNearest(player.PosX, player.PosZ, r);
                if (hit < 0) continue;

                ref var enemy = ref state.Enemies[hit];
                float actualR = GameState.PlayerRadius + GameState.GetEnemyRadius(enemy.Type);
                float dx = player.PosX - _posX[hit];
                float dz = player.PosZ - _posZ[hit];
                if (dx * dx + dz * dz > actualR * actualR) continue;

                player.Hp -= GameState.GetEnemyDamage(enemy.Type);
                player.InvincibilityFrames = GameState.InvincibilityDuration;

                if (player.Hp <= 0)
                {
                    player.Hp = 0;
                    player.IsAlive = false;
                }
            }
        }

        static void ResolveBoneShardsVsPlayers(GameState state)
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive || proj.Type != ProjectileType.BoneShard) continue;

                for (int p = 0; p < GameState.MaxPlayers; p++)
                {
                    ref var player = ref state.Players[p];
                    if (!player.IsActive || !player.IsAlive || player.InvincibilityFrames > 0)
                        continue;

                    float r = proj.Radius + GameState.PlayerRadius;
                    float dx = proj.PosX - player.PosX;
                    float dz = proj.PosZ - player.PosZ;
                    if (dx * dx + dz * dz > r * r) continue;

                    player.Hp -= GameState.BoneShardDamage;
                    player.InvincibilityFrames = GameState.InvincibilityDuration;
                    proj.IsAlive = false;

                    if (player.Hp <= 0)
                    {
                        player.Hp = 0;
                        player.IsAlive = false;
                    }
                    break;
                }
            }
        }

        static void ResolvePlayersVsGems(GameState state)
        {
            float rSq = GameState.XpPickupRadius * GameState.XpPickupRadius;

            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive) continue;

                for (int g = 0; g < GameState.MaxGems; g++)
                {
                    ref var gem = ref state.Gems[g];
                    if (!gem.IsAlive) continue;

                    float dx = player.PosX - gem.PosX;
                    float dz = player.PosZ - gem.PosZ;
                    if (dx * dx + dz * dz > rSq) continue;

                    player.Xp += gem.Value;
                    gem.IsAlive = false;

                    // Level up check
                    while (player.Xp >= player.XpToNextLevel)
                    {
                        player.Xp -= player.XpToNextLevel;
                        player.Level++;
                        player.XpToNextLevel = (int)(player.XpToNextLevel * 1.5f);
                        player.Hp = Math.Min(player.Hp + 20, player.MaxHp);
                        player.PendingLevelUp = true;
                    }
                }
            }
        }

        // --- Helpers ---

        static void KillEnemy(GameState state, int idx, int killerPlayerId)
        {
            ref var enemy = ref state.Enemies[idx];
            WeaponSystem.SpawnXpGem(state, enemy.PosX, enemy.PosZ, enemy.Type);
            enemy.IsAlive = false;

            if (killerPlayerId >= 0 && killerPlayerId < GameState.MaxPlayers)
                state.Players[killerPlayerId].KillCount++;
        }

        static int QueryNearest(float cx, float cz, float radius)
        {
            float rSq = radius * radius;
            int minCx = (int)((cx - radius + GameState.ArenaHalfSize) / CellSize);
            int maxCx = (int)((cx + radius + GameState.ArenaHalfSize) / CellSize);
            int minCz = (int)((cz - radius + GameState.ArenaHalfSize) / CellSize);
            int maxCz = (int)((cz + radius + GameState.ArenaHalfSize) / CellSize);
            if (minCx < 0) minCx = 0; if (maxCx >= GridCells) maxCx = GridCells - 1;
            if (minCz < 0) minCz = 0; if (maxCz >= GridCells) maxCz = GridCells - 1;

            int bestIdx = -1;
            float bestDist = float.MaxValue;

            for (int gz = minCz; gz <= maxCz; gz++)
                for (int gx = minCx; gx <= maxCx; gx++)
                {
                    int idx = BucketHeads[gz * GridCells + gx];
                    while (idx >= 0)
                    {
                        float dx = cx - _posX[idx];
                        float dz = cz - _posZ[idx];
                        float dSq = dx * dx + dz * dz;
                        if (dSq < rSq && dSq < bestDist)
                        {
                            bestDist = dSq;
                            bestIdx = idx;
                        }
                        idx = NextInBucket[idx];
                    }
                }
            return bestIdx;
        }

        delegate void EnemyCallback(int idx);

        static void ForEachInRange(float cx, float cz, float radius, EnemyCallback cb)
        {
            float rSq = radius * radius;
            int minCx = (int)((cx - radius + GameState.ArenaHalfSize) / CellSize);
            int maxCx = (int)((cx + radius + GameState.ArenaHalfSize) / CellSize);
            int minCz = (int)((cz - radius + GameState.ArenaHalfSize) / CellSize);
            int maxCz = (int)((cz + radius + GameState.ArenaHalfSize) / CellSize);
            if (minCx < 0) minCx = 0; if (maxCx >= GridCells) maxCx = GridCells - 1;
            if (minCz < 0) minCz = 0; if (maxCz >= GridCells) maxCz = GridCells - 1;

            for (int gz = minCz; gz <= maxCz; gz++)
                for (int gx = minCx; gx <= maxCx; gx++)
                {
                    int idx = BucketHeads[gz * GridCells + gx];
                    while (idx >= 0)
                    {
                        float dx = cx - _posX[idx];
                        float dz = cz - _posZ[idx];
                        if (dx * dx + dz * dz < rSq) cb(idx);
                        idx = NextInBucket[idx];
                    }
                }
        }
    }
}
