// BoomNetwork VampireSurvivors Demo — Enemy AI (Phase 2)
//
// Three enemy types:
//   Zombie: slow chase toward nearest player
//   Bat: fast, erratic, periodic random direction changes
//   Skeleton Mage: stops at range, fires bone shards

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class EnemySystem
    {
        const uint RetargetInterval = 20; // re-acquire target every 1s

        public static void Tick(GameState state)
        {
            float dt = state.Dt;

            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;

                // Re-target periodically or if current target is dead
                bool needRetarget = e.TargetPlayerId < 0
                    || e.TargetPlayerId >= GameState.MaxPlayers
                    || !state.Players[e.TargetPlayerId].IsAlive
                    || (state.FrameNumber % RetargetInterval == 0);

                if (needRetarget)
                    e.TargetPlayerId = state.FindNearestPlayer(e.PosX, e.PosZ);

                if (e.TargetPlayerId < 0) continue;

                switch (e.Type)
                {
                    case EnemyType.Zombie: TickZombie(ref e, state, dt); break;
                    case EnemyType.Bat: TickBat(ref e, state, dt, i); break;
                    case EnemyType.SkeletonMage: TickMage(ref e, state, dt, i); break;
                }

                // Clamp to arena
                float limit = GameState.ArenaHalfSize + 3f;
                if (e.PosX < -limit) e.PosX = -limit;
                if (e.PosX > limit) e.PosX = limit;
                if (e.PosZ < -limit) e.PosZ = -limit;
                if (e.PosZ > limit) e.PosZ = limit;
            }
        }

        static void TickZombie(ref EnemyState e, GameState state, float dt)
        {
            ref var target = ref state.Players[e.TargetPlayerId];
            float dx = target.PosX - e.PosX;
            float dz = target.PosZ - e.PosZ;
            float distSq = dx * dx + dz * dz;
            if (distSq < 0.001f) return;

            float invDist = 1f / (float)Math.Sqrt(distSq);
            float speed = GameState.ZombieSpeed * dt;
            e.PosX += dx * invDist * speed;
            e.PosZ += dz * invDist * speed;
        }

        static void TickBat(ref EnemyState e, GameState state, float dt, int idx)
        {
            // Change direction periodically with random jitter
            if (e.BehaviorTimer == 0)
            {
                ref var target = ref state.Players[e.TargetPlayerId];
                float dx = target.PosX - e.PosX;
                float dz = target.PosZ - e.PosZ;
                float distSq = dx * dx + dz * dz;

                if (distSq > 0.01f)
                {
                    float invDist = 1f / (float)Math.Sqrt(distSq);
                    // Mix: 60% toward player + 40% random
                    float homeX = dx * invDist;
                    float homeZ = dz * invDist;
                    float randAngle = DeterministicRng.Range(ref state.RngState, -3.14159f, 3.14159f);
                    float randX = (float)Math.Cos(randAngle);
                    float randZ = (float)Math.Sin(randAngle);
                    e.DirX = homeX * 0.6f + randX * 0.4f;
                    e.DirZ = homeZ * 0.6f + randZ * 0.4f;
                    // Normalize
                    float len = (float)Math.Sqrt(e.DirX * e.DirX + e.DirZ * e.DirZ);
                    if (len > 0.001f) { e.DirX /= len; e.DirZ /= len; }
                }

                e.BehaviorTimer = GameState.BatDirChangeInterval;
            }
            else
            {
                e.BehaviorTimer--;
            }

            e.PosX += e.DirX * GameState.BatSpeed * dt;
            e.PosZ += e.DirZ * GameState.BatSpeed * dt;
        }

        static void TickMage(ref EnemyState e, GameState state, float dt, int idx)
        {
            ref var target = ref state.Players[e.TargetPlayerId];
            float dx = target.PosX - e.PosX;
            float dz = target.PosZ - e.PosZ;
            float distSq = dx * dx + dz * dz;

            // Move toward player if out of attack range
            if (distSq > GameState.MageAttackRange * GameState.MageAttackRange)
            {
                if (distSq > 0.01f)
                {
                    float invDist = 1f / (float)Math.Sqrt(distSq);
                    e.PosX += dx * invDist * GameState.MageSpeed * dt;
                    e.PosZ += dz * invDist * GameState.MageSpeed * dt;
                }
            }

            // Fire bone shard on cooldown
            if (e.BehaviorTimer == 0)
            {
                e.BehaviorTimer = GameState.MageFireCooldown;

                int slot = state.AllocProjectile();
                if (slot >= 0 && distSq > 0.01f)
                {
                    float invDist = 1f / (float)Math.Sqrt(distSq);
                    ref var proj = ref state.Projectiles[slot];
                    proj.IsAlive = true;
                    proj.Type = ProjectileType.BoneShard;
                    proj.PosX = e.PosX;
                    proj.PosZ = e.PosZ;
                    proj.DirX = dx * invDist;
                    proj.DirZ = dz * invDist;
                    proj.Radius = GameState.BoneShardRadius;
                    proj.LifetimeFrames = GameState.BoneShardLifetime;
                    proj.OwnerPlayerId = -1; // enemy projectile
                    proj.DamageTick = 0;
                }
            }
            else
            {
                e.BehaviorTimer--;
            }
        }
    }
}
