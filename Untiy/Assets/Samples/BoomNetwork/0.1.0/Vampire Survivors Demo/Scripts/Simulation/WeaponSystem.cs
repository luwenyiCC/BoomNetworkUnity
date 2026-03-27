// BoomNetwork VampireSurvivors Demo — Weapon System (MVP: Knife only)
//
// Auto-fires throwing knives in the player's facing direction on cooldown.
// Advances all live projectiles each frame.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class WeaponSystem
    {
        public static void Tick(GameState state)
        {
            float dt = state.Dt;

            // --- Auto-fire knives for each alive player ---
            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive) continue;

                if (player.KnifeCooldown > 0)
                {
                    player.KnifeCooldown--;
                    continue;
                }

                // Fire a knife
                int slot = state.AllocProjectile();
                if (slot < 0) continue; // pool full

                ref var proj = ref state.Projectiles[slot];
                proj.IsAlive = true;
                proj.PosX = player.PosX;
                proj.PosZ = player.PosZ;
                proj.DirX = player.FacingX;
                proj.DirZ = player.FacingZ;
                proj.LifetimeFrames = GameState.KnifeLifetimeFrames;
                proj.OwnerPlayerId = p;

                player.KnifeCooldown = GameState.KnifeCooldownFrames;
            }

            // --- Advance projectiles ---
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive) continue;

                proj.PosX += proj.DirX * GameState.KnifeSpeed * dt;
                proj.PosZ += proj.DirZ * GameState.KnifeSpeed * dt;
                proj.LifetimeFrames--;

                if (proj.LifetimeFrames == 0) proj.IsAlive = false;

                // Kill if out of arena
                float limit = GameState.ArenaHalfSize + 5f;
                if (proj.PosX < -limit || proj.PosX > limit ||
                    proj.PosZ < -limit || proj.PosZ > limit)
                    proj.IsAlive = false;
            }
        }
    }
}
