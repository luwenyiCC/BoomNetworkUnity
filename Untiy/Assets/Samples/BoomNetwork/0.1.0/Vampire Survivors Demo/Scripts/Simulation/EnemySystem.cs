// BoomNetwork VampireSurvivors Demo — Enemy AI
//
// Zombies chase the nearest alive player each frame.
// Simple, deterministic, no pathfinding.

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

                if (e.TargetPlayerId < 0) continue; // no alive players

                ref var target = ref state.Players[e.TargetPlayerId];
                float dx = target.PosX - e.PosX;
                float dz = target.PosZ - e.PosZ;
                float distSq = dx * dx + dz * dz;

                if (distSq < 0.001f) continue;

                float invDist = 1f / (float)Math.Sqrt(distSq);
                float speed = GameState.ZombieSpeed * dt;
                e.PosX += dx * invDist * speed;
                e.PosZ += dz * invDist * speed;

                // Clamp to arena bounds (allow slight overflow for spawning)
                float limit = GameState.ArenaHalfSize + 3f;
                if (e.PosX < -limit) e.PosX = -limit;
                if (e.PosX > limit) e.PosX = limit;
                if (e.PosZ < -limit) e.PosZ = -limit;
                if (e.PosZ > limit) e.PosZ = limit;
            }
        }
    }
}
