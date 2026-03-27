// BoomNetwork VampireSurvivors Demo — Wave Spawner (Phase 2)
//
// Escalating waves with mixed enemy types:
//   Wave 1-2: Zombies only
//   Wave 3+: Bats introduced (30%)
//   Wave 5+: Skeleton Mages introduced (20%)

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
                state.WaveNumber++;
                state.WaveSpawnRemaining = (uint)(BaseEnemyCount + state.WaveNumber * EnemiesPerWave);
                state.WaveSpawnTimer = SpawnIntervalFrames;
                return;
            }

            if (state.WaveSpawnTimer > 0)
            {
                state.WaveSpawnTimer--;
                return;
            }

            int slot = state.AllocEnemy();
            if (slot < 0)
            {
                state.WaveSpawnTimer = SpawnIntervalFrames;
                return;
            }

            SpawnEnemy(state, slot);
            state.WaveSpawnRemaining--;
            state.WaveSpawnTimer = SpawnIntervalFrames;
        }

        static void SpawnEnemy(GameState state, int slot)
        {
            ref var e = ref state.Enemies[slot];
            e.IsAlive = true;
            e.BehaviorTimer = 0;
            e.DirX = 0;
            e.DirZ = 0;

            // Determine enemy type based on wave
            e.Type = PickEnemyType(state);

            switch (e.Type)
            {
                case EnemyType.Zombie:
                    e.Hp = GameState.ZombieHp + state.WaveNumber / 3;
                    break;
                case EnemyType.Bat:
                    e.Hp = GameState.BatHp + state.WaveNumber / 4;
                    break;
                case EnemyType.SkeletonMage:
                    e.Hp = GameState.MageHp + state.WaveNumber / 2;
                    e.BehaviorTimer = (uint)DeterministicRng.RangeInt(ref state.RngState, 0, (int)GameState.MageFireCooldown);
                    break;
            }

            // Spawn at random arena edge
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

        static EnemyType PickEnemyType(GameState state)
        {
            int roll = DeterministicRng.RangeInt(ref state.RngState, 0, 100);

            if (state.WaveNumber >= 5)
            {
                // 50% Zombie, 30% Bat, 20% Mage
                if (roll < 20) return EnemyType.SkeletonMage;
                if (roll < 50) return EnemyType.Bat;
                return EnemyType.Zombie;
            }

            if (state.WaveNumber >= 3)
            {
                // 70% Zombie, 30% Bat
                if (roll < 30) return EnemyType.Bat;
                return EnemyType.Zombie;
            }

            return EnemyType.Zombie;
        }
    }
}
