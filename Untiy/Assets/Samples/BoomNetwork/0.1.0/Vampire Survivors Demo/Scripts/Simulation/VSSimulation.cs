// BoomNetwork VampireSurvivors Demo — Deterministic Simulation Driver (Phase 2)
//
// Pure C#. Receives decoded inputs, advances GameState one frame.
// Handles upgrade selection through input ability bits.

using BoomNetwork.Core.FrameSync;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public class VSSimulation
    {
        public readonly GameState State = new GameState();

        // Available weapons for upgrade pool
        static readonly WeaponType[] UpgradePool =
        {
            WeaponType.Knife, WeaponType.Orb, WeaponType.Lightning, WeaponType.HolyWater
        };

        public void Init(float dt, uint rngSeed)
        {
            State.Dt = dt;
            State.RngState = rngSeed == 0 ? 0xDEADBEEFu : rngSeed;
            State.WaveNumber = 0;
            State.WaveSpawnTimer = 40;
            State.WaveSpawnRemaining = 0;
            State.FrameNumber = 0;
        }

        public void Tick(FrameData frame)
        {
            State.FrameNumber = frame.FrameNumber;

            // 1. Apply player inputs (including upgrade choices)
            ApplyInputs(frame);

            // 2. Wave spawning
            WaveSystem.Tick(State);

            // 3. Enemy AI + movement
            EnemySystem.Tick(State);

            // 4. Weapon auto-fire + projectile movement
            WeaponSystem.Tick(State);

            // 5. Collision
            CollisionSystem.CachePositions(State);
            CollisionSystem.Rebuild(State);
            CollisionSystem.Resolve(State);

            // 6. Decrement invincibility + lightning flashes
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
                int slot = input.PlayerId - 1;
                if (slot < 0 || slot >= GameState.MaxPlayers) continue;

                ref var player = ref State.Players[slot];
                if (!player.IsActive || !player.IsAlive) continue;

                VSInput.Decode(input.Data, 0, out float dirX, out float dirZ, out byte abilityMask);

                // Handle upgrade selection first
                if (player.PendingLevelUp && abilityMask > 0)
                {
                    ApplyUpgrade(ref player, abilityMask);
                    player.PendingLevelUp = false;
                }

                // Update facing direction
                if (dirX != 0f || dirZ != 0f)
                {
                    float len = (float)System.Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len > 0.001f)
                    {
                        float invLen = 1f / len;
                        player.FacingX = dirX * invLen;
                        player.FacingZ = dirZ * invLen;
                    }

                    player.PosX += player.FacingX * GameState.PlayerSpeed * State.Dt;
                    player.PosZ += player.FacingZ * GameState.PlayerSpeed * State.Dt;

                    float limit = GameState.ArenaHalfSize - GameState.PlayerRadius;
                    if (player.PosX < -limit) player.PosX = -limit;
                    if (player.PosX > limit) player.PosX = limit;
                    if (player.PosZ < -limit) player.PosZ = -limit;
                    if (player.PosZ > limit) player.PosZ = limit;
                }
            }
        }

        void ApplyUpgrade(ref PlayerState player, byte abilityMask)
        {
            // abilityMask bits: 1=option0, 2=option1, 4=option2, 8=option3
            int choice = -1;
            if ((abilityMask & 1) != 0) choice = 0;
            else if ((abilityMask & 2) != 0) choice = 1;
            else if ((abilityMask & 4) != 0) choice = 2;
            else if ((abilityMask & 8) != 0) choice = 3;
            if (choice < 0 || choice >= UpgradePool.Length) return;

            WeaponType wtype = UpgradePool[choice];

            // Check if player already has this weapon
            int existingSlot = player.FindWeaponSlot(wtype);
            if (existingSlot >= 0)
            {
                // Level up existing weapon (max 5)
                ref var w = ref player.GetWeapon(existingSlot);
                if (w.Level < 5) w.Level++;
            }
            else
            {
                // Add new weapon to empty slot
                int emptySlot = player.FindEmptyWeaponSlot();
                if (emptySlot >= 0)
                {
                    ref var w = ref player.GetWeapon(emptySlot);
                    w.Type = wtype;
                    w.Level = 1;
                    w.Cooldown = 0;

                    // If adding Orb, activate initial orbs
                    if (wtype == WeaponType.Orb)
                    {
                        player.GetOrb(0) = new OrbState { Active = true, AngleDeg = 0 };
                        player.GetOrb(1) = new OrbState { Active = true, AngleDeg = 180 };
                    }
                }
            }
        }
    }
}
