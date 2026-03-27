// BoomNetwork VampireSurvivors Demo — Wave Spawner
//
// Spawns zombies at arena edges in escalating waves.
// All RNG through DeterministicRng for cross-client determinism.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class WaveSystem
    {
        const uint WaveGapFrames = 100;     // 5s pause between waves
        const uint SpawnIntervalFrames = 5; // 1 enemy per 0.25s
        const int BaseEnemyCount = 15;
        const int EnemiesPerWave = 10;

        public static void Tick(GameState state)
        {
            if (!state.HasAlivePlayers()) return;

            // Between-wave cooldown
            if (state.WaveSpawnRemaining == 0)
            {
                if (state.WaveSpawnTimer > 0)
                {
                    state.WaveSpawnTimer--;
                    return;
                }
                // Start new wave
                state.WaveNumber++;
                state.WaveSpawnRemaining = (uint)(BaseEnemyCount + state.WaveNumber * EnemiesPerWave);
                state.WaveSpawnTimer = SpawnIntervalFrames;
                return;
            }

            // Spawn timer
            if (state.WaveSpawnTimer > 0)
            {
                state.WaveSpawnTimer--;
                return;
            }

            // Spawn one enemy
            int slot = state.AllocEnemy();
            if (slot < 0)
            {
                // Pool full, skip this spawn tick
                state.WaveSpawnTimer = SpawnIntervalFrames;
                return;
            }

            SpawnZombie(state, slot);
            state.WaveSpawnRemaining--;
            state.WaveSpawnTimer = SpawnIntervalFrames;
        }

        static void SpawnZombie(GameState state, int slot)
        {
            ref var e = ref state.Enemies[slot];
            e.IsAlive = true;
            e.Hp = GameState.ZombieHp + state.WaveNumber / 3; // scale HP with waves

            // Pick a random edge (0=top, 1=bottom, 2=left, 3=right)
            int edge = DeterministicRng.RangeInt(ref state.RngState, 0, 4);
            float along = DeterministicRng.Range(ref state.RngState,
                -GameState.ArenaHalfSize, GameState.ArenaHalfSize);
            float margin = GameState.ArenaHalfSize + 2f;

            switch (edge)
            {
                case 0: e.PosX = along; e.PosZ = margin; break;
                case 1: e.PosX = along; e.PosZ = -margin; break;
                case 2: e.PosX = -margin; e.PosZ = along; break;
                default: e.PosX = margin; e.PosZ = along; break;
            }

            e.TargetPlayerId = state.FindNearestPlayer(e.PosX, e.PosZ);
        }
    }
}
