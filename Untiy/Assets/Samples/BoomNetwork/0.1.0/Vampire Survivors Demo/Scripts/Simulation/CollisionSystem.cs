// BoomNetwork VampireSurvivors Demo — Spatial Hash Collision System
//
// Flat spatial hash for O(1) circle-overlap queries.
// Rebuild once per frame, then resolve all collision pairs.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class CollisionSystem
    {
        const float CellSize = 2f;
        const int GridCells = 20; // per axis: [-20,20] / 2 = 20
        const int TotalCells = GridCells * GridCells;

        static readonly int[] BucketHeads = new int[TotalCells];
        static readonly int[] NextInBucket = new int[GameState.MaxEnemies];

        static int CellIndex(float x, float z)
        {
            int cx = (int)((x + GameState.ArenaHalfSize) / CellSize);
            int cz = (int)((z + GameState.ArenaHalfSize) / CellSize);
            if (cx < 0) cx = 0; else if (cx >= GridCells) cx = GridCells - 1;
            if (cz < 0) cz = 0; else if (cz >= GridCells) cz = GridCells - 1;
            return cz * GridCells + cx;
        }

        public static void Rebuild(GameState state)
        {
            for (int i = 0; i < TotalCells; i++) BucketHeads[i] = -1;

            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;
                int cell = CellIndex(e.PosX, e.PosZ);
                NextInBucket[i] = BucketHeads[cell];
                BucketHeads[cell] = i;
            }
        }

        public static void Resolve(GameState state)
        {
            ResolveProjectilesVsEnemies(state);
            ResolveEnemiesVsPlayers(state);
            ResolvePlayersVsGems(state);
        }

        static void ResolveProjectilesVsEnemies(GameState state)
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive) continue;

                float r = GameState.KnifeRadius + GameState.ZombieRadius;
                int hit = QueryNearest(proj.PosX, proj.PosZ, r);
                if (hit < 0) continue;

                ref var enemy = ref state.Enemies[hit];
                enemy.Hp -= GameState.KnifeDamage;
                proj.IsAlive = false;

                if (enemy.Hp <= 0)
                {
                    enemy.IsAlive = false;
                    // Drop XP gem
                    int gem = state.AllocGem();
                    if (gem >= 0)
                    {
                        ref var g = ref state.Gems[gem];
                        g.IsAlive = true;
                        g.PosX = enemy.PosX;
                        g.PosZ = enemy.PosZ;
                        g.Value = GameState.ZombieXpValue;
                    }
                    // Credit kill
                    if (proj.OwnerPlayerId >= 0 && proj.OwnerPlayerId < GameState.MaxPlayers)
                        state.Players[proj.OwnerPlayerId].KillCount++;
                }
            }
        }

        static void ResolveEnemiesVsPlayers(GameState state)
        {
            float r = GameState.PlayerRadius + GameState.ZombieRadius;
            float rSq = r * r;

            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive || player.InvincibilityFrames > 0)
                    continue;

                int hit = QueryNearest(player.PosX, player.PosZ, r);
                if (hit < 0) continue;

                // Verify distance (spatial hash is approximate)
                ref var enemy = ref state.Enemies[hit];
                float dx = player.PosX - enemy.PosX;
                float dz = player.PosZ - enemy.PosZ;
                if (dx * dx + dz * dz > rSq) continue;

                player.Hp -= GameState.ZombieDamage;
                player.InvincibilityFrames = GameState.InvincibilityDuration;

                if (player.Hp <= 0)
                {
                    player.Hp = 0;
                    player.IsAlive = false;
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
                        // Heal on level up
                        player.Hp = Math.Min(player.Hp + 20, player.MaxHp);
                    }
                }
            }
        }

        /// <summary>Find the nearest alive enemy within radius of (cx,cz). Returns index or -1.</summary>
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
            {
                for (int gx = minCx; gx <= maxCx; gx++)
                {
                    int idx = BucketHeads[gz * GridCells + gx];
                    while (idx >= 0)
                    {
                        float dx = cx - CollisionSystem_PosX(idx);
                        float dz = cz - CollisionSystem_PosZ(idx);
                        float dSq = dx * dx + dz * dz;
                        if (dSq < rSq && dSq < bestDist)
                        {
                            bestDist = dSq;
                            bestIdx = idx;
                        }
                        idx = NextInBucket[idx];
                    }
                }
            }
            return bestIdx;
        }

        // Cache enemy positions for spatial query (avoids passing GameState into QueryNearest)
        static readonly float[] _posX = new float[GameState.MaxEnemies];
        static readonly float[] _posZ = new float[GameState.MaxEnemies];

        static float CollisionSystem_PosX(int idx) => _posX[idx];
        static float CollisionSystem_PosZ(int idx) => _posZ[idx];

        /// <summary>Call before Rebuild to cache positions for query.</summary>
        public static void CachePositions(GameState state)
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                _posX[i] = state.Enemies[i].PosX;
                _posZ[i] = state.Enemies[i].PosZ;
            }
        }
    }
}
