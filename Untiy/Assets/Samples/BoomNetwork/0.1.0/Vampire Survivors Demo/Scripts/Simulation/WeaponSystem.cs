// BoomNetwork VampireSurvivors Demo — Weapon System (Phase 2)
//
// Four weapon types, auto-fire on cooldown:
//   Knife: fires projectile in facing direction
//   Magic Orb: orbiting balls that damage on contact
//   Lightning: chain strikes nearest enemies
//   Holy Water: AoE damage puddle at player position

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class WeaponSystem
    {
        public static void Tick(GameState state)
        {
            float dt = state.Dt;

            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive) continue;

                for (int w = 0; w < PlayerState.MaxWeaponSlots; w++)
                {
                    ref var weapon = ref player.GetWeapon(w);
                    if (weapon.Type == WeaponType.None) continue;

                    if (weapon.Cooldown > 0)
                    {
                        weapon.Cooldown--;
                        continue;
                    }

                    switch (weapon.Type)
                    {
                        case WeaponType.Knife:
                            FireKnife(state, ref player, ref weapon, p);
                            break;
                        case WeaponType.Orb:
                            // Orbs don't fire — they just orbit. Set up active count by level.
                            UpdateOrbs(state, ref player, ref weapon);
                            break;
                        case WeaponType.Lightning:
                            FireLightning(state, ref player, ref weapon, p);
                            break;
                        case WeaponType.HolyWater:
                            FireHolyWater(state, ref player, ref weapon, p);
                            break;
                    }
                }
            }

            // --- Advance all projectiles ---
            AdvanceProjectiles(state, dt);

            // --- Advance orb angles ---
            AdvanceOrbs(state, dt);

            // --- Decay lightning flashes ---
            for (int i = 0; i < GameState.MaxLightningFlashes; i++)
                if (state.Flashes[i].FramesLeft > 0) state.Flashes[i].FramesLeft--;
        }

        static void FireKnife(GameState state, ref PlayerState player, ref WeaponSlot weapon, int playerIdx)
        {
            int count = 1 + (weapon.Level - 1) / 2; // Lv1=1, Lv3=2, Lv5=3 knives
            float spread = count > 1 ? 30f : 0f; // degrees total spread
            float startAngle = -spread / 2f;
            float step = count > 1 ? spread / (count - 1) : 0f;

            for (int k = 0; k < count; k++)
            {
                int slot = state.AllocProjectile();
                if (slot < 0) break;

                float angleDeg = startAngle + step * k;
                float rad = angleDeg * 0.01745329f; // deg2rad
                // Rotate facing direction
                float cos = (float)Math.Cos(rad);
                float sin = (float)Math.Sin(rad);
                float dx = player.FacingX * cos - player.FacingZ * sin;
                float dz = player.FacingX * sin + player.FacingZ * cos;

                ref var proj = ref state.Projectiles[slot];
                proj.IsAlive = true;
                proj.Type = ProjectileType.Knife;
                proj.PosX = player.PosX;
                proj.PosZ = player.PosZ;
                proj.DirX = dx;
                proj.DirZ = dz;
                proj.Radius = GameState.KnifeRadius;
                proj.LifetimeFrames = GameState.KnifeLifetimeFrames;
                proj.OwnerPlayerId = playerIdx;
                proj.DamageTick = 0;
            }

            // Cooldown decreases with level
            weapon.Cooldown = Math.Max(4, GameState.KnifeBaseCooldown - (uint)weapon.Level);
        }

        static void UpdateOrbs(GameState state, ref PlayerState player, ref WeaponSlot weapon)
        {
            int orbCount = Math.Min(weapon.Level + 1, PlayerState.MaxOrbs); // Lv1=2, Lv2=3...
            for (int i = 0; i < PlayerState.MaxOrbs; i++)
            {
                ref var orb = ref player.GetOrb(i);
                if (i < orbCount)
                {
                    if (!orb.Active)
                    {
                        orb.Active = true;
                        orb.AngleDeg = i * (360f / orbCount);
                    }
                }
                else
                {
                    orb.Active = false;
                }
            }
            // Orbs don't have a cooldown concept
            weapon.Cooldown = 20; // check every 1s
        }

        static void AdvanceOrbs(GameState state, float dt)
        {
            float angularSpeed = GameState.OrbAngularSpeed * dt;
            for (int p = 0; p < GameState.MaxPlayers; p++)
            {
                ref var player = ref state.Players[p];
                if (!player.IsActive || !player.IsAlive) continue;
                for (int i = 0; i < PlayerState.MaxOrbs; i++)
                {
                    ref var orb = ref player.GetOrb(i);
                    if (!orb.Active) continue;
                    orb.AngleDeg += angularSpeed;
                    if (orb.AngleDeg >= 360f) orb.AngleDeg -= 360f;
                }
            }
        }

        static void FireLightning(GameState state, ref PlayerState player, ref WeaponSlot weapon, int playerIdx)
        {
            int chains = GameState.LightningBaseChains + weapon.Level - 1;
            float range = GameState.LightningRange + weapon.Level * 0.5f;
            int damage = GameState.LightningDamage + weapon.Level;

            float cx = player.PosX;
            float cz = player.PosZ;

            // Track hit enemies to avoid hitting same enemy twice
            // Use a simple inline approach: mark by setting BehaviorTimer high bit temporarily
            // Actually, just use a small fixed buffer
            int hitCount = 0;

            for (int c = 0; c < chains; c++)
            {
                int nearest = -1;
                float nearestDist = float.MaxValue;

                for (int i = 0; i < GameState.MaxEnemies; i++)
                {
                    ref var e = ref state.Enemies[i];
                    if (!e.IsAlive) continue;
                    float dx = e.PosX - cx;
                    float dz = e.PosZ - cz;
                    float d = dx * dx + dz * dz;
                    if (d < range * range && d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = i;
                    }
                }

                if (nearest < 0) break;

                ref var enemy = ref state.Enemies[nearest];
                enemy.Hp -= damage;

                // Spawn visual flash
                int flash = state.AllocFlash();
                if (flash >= 0)
                {
                    state.Flashes[flash].PosX = enemy.PosX;
                    state.Flashes[flash].PosZ = enemy.PosZ;
                    state.Flashes[flash].FramesLeft = 4;
                }

                if (enemy.Hp <= 0)
                {
                    enemy.IsAlive = false;
                    SpawnXpGem(state, enemy.PosX, enemy.PosZ, enemy.Type);
                    if (playerIdx >= 0 && playerIdx < GameState.MaxPlayers)
                        state.Players[playerIdx].KillCount++;
                }

                // Chain from this enemy's position
                cx = enemy.PosX;
                cz = enemy.PosZ;
            }

            weapon.Cooldown = Math.Max(10, GameState.LightningBaseCooldown - (uint)(weapon.Level * 3));
        }

        static void FireHolyWater(GameState state, ref PlayerState player, ref WeaponSlot weapon, int playerIdx)
        {
            int slot = state.AllocProjectile();
            if (slot < 0)
            {
                weapon.Cooldown = 5; // retry soon
                return;
            }

            ref var proj = ref state.Projectiles[slot];
            proj.IsAlive = true;
            proj.Type = ProjectileType.HolyPuddle;
            proj.PosX = player.PosX;
            proj.PosZ = player.PosZ;
            proj.DirX = 0;
            proj.DirZ = 0;
            proj.Radius = GameState.HolyWaterBaseRadius + weapon.Level * 0.3f;
            proj.LifetimeFrames = GameState.HolyWaterLifetime + (uint)(weapon.Level * 10);
            proj.OwnerPlayerId = playerIdx;
            proj.DamageTick = 0;

            weapon.Cooldown = Math.Max(20, GameState.HolyWaterBaseCooldown - (uint)(weapon.Level * 5));
        }

        static void AdvanceProjectiles(GameState state, float dt)
        {
            for (int i = 0; i < GameState.MaxProjectiles; i++)
            {
                ref var proj = ref state.Projectiles[i];
                if (!proj.IsAlive) continue;

                // Move projectiles that have direction
                if (proj.Type == ProjectileType.Knife || proj.Type == ProjectileType.BoneShard)
                {
                    float speed = proj.Type == ProjectileType.Knife
                        ? GameState.KnifeSpeed
                        : GameState.BoneShardSpeed;
                    proj.PosX += proj.DirX * speed * dt;
                    proj.PosZ += proj.DirZ * speed * dt;
                }

                // Holy puddle ticks damage
                if (proj.Type == ProjectileType.HolyPuddle)
                    proj.DamageTick++;

                proj.LifetimeFrames--;
                if (proj.LifetimeFrames == 0) { proj.IsAlive = false; continue; }

                // Kill if out of arena
                float limit = GameState.ArenaHalfSize + 5f;
                if (proj.PosX < -limit || proj.PosX > limit ||
                    proj.PosZ < -limit || proj.PosZ > limit)
                    proj.IsAlive = false;
            }
        }

        public static void SpawnXpGem(GameState state, float x, float z, EnemyType type)
        {
            int gem = state.AllocGem();
            if (gem < 0) return;
            ref var g = ref state.Gems[gem];
            g.IsAlive = true;
            g.PosX = x;
            g.PosZ = z;
            g.Value = GameState.GetEnemyXpValue(type);
        }
    }
}
