// BoomNetwork VampireSurvivors Demo — Deterministic Simulation Driver
//
// Pure C#. Receives decoded inputs, advances GameState one frame.
// No Unity types — all logic is deterministic and snapshot-safe.

using BoomNetwork.Core.FrameSync;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public class VSSimulation
    {
        public readonly GameState State = new GameState();

        public void Init(float dt, uint rngSeed)
        {
            State.Dt = dt;
            State.RngState = rngSeed == 0 ? 0xDEADBEEFu : rngSeed;
            State.WaveNumber = 0;
            State.WaveSpawnTimer = 40; // 2s initial delay
            State.WaveSpawnRemaining = 0;
            State.FrameNumber = 0;
        }

        public void Tick(FrameData frame)
        {
            State.FrameNumber = frame.FrameNumber;

            // 1. Apply player inputs
            ApplyInputs(frame);

            // 2. Wave spawning
            WaveSystem.Tick(State);

            // 3. Enemy AI + movement
            EnemySystem.Tick(State);

            // 4. Weapon auto-fire + projectile movement
            WeaponSystem.Tick(State);

            // 5. Collision: rebuild spatial hash, then resolve
            CollisionSystem.CachePositions(State);
            CollisionSystem.Rebuild(State);
            CollisionSystem.Resolve(State);

            // 6. Decrement invincibility
            for (int i = 0; i < GameState.MaxPlayers; i++)
            {
                ref var p = ref State.Players[i];
                if (p.InvincibilityFrames > 0) p.InvincibilityFrames--;
            }
        }

        void ApplyInputs(FrameData frame)
        {
            if (frame.Inputs == null) return;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                int pid = input.PlayerId;

                // PlayerId is 1-based, slots are 0-based
                int slot = pid - 1;
                if (slot < 0 || slot >= GameState.MaxPlayers) continue;

                ref var player = ref State.Players[slot];
                if (!player.IsActive || !player.IsAlive) continue;

                VSInput.Decode(input.Data, 0, out float dirX, out float dirZ, out byte _);

                // Update facing direction
                if (dirX != 0f || dirZ != 0f)
                {
                    // Normalize
                    float len = (float)System.Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len > 0.001f)
                    {
                        float invLen = 1f / len;
                        player.FacingX = dirX * invLen;
                        player.FacingZ = dirZ * invLen;
                    }

                    // Move
                    player.PosX += player.FacingX * GameState.PlayerSpeed * State.Dt;
                    player.PosZ += player.FacingZ * GameState.PlayerSpeed * State.Dt;

                    // Clamp to arena
                    float limit = GameState.ArenaHalfSize - GameState.PlayerRadius;
                    if (player.PosX < -limit) player.PosX = -limit;
                    if (player.PosX > limit) player.PosX = limit;
                    if (player.PosZ < -limit) player.PosZ = -limit;
                    if (player.PosZ > limit) player.PosZ = limit;
                }
            }
        }
    }
}
