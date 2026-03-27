// BoomNetwork VampireSurvivors Demo — Game State
//
// Pure C# data model, no Unity dependencies.
// All game state lives here for deterministic simulation + snapshot serialization.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public struct PlayerState
    {
        public bool IsActive;
        public bool IsAlive;
        public float PosX, PosZ;
        public float FacingX, FacingZ;
        public int Hp, MaxHp;
        public int Xp, Level;
        public int XpToNextLevel;
        public uint KnifeCooldown;
        public uint InvincibilityFrames;
        public int KillCount;
    }

    public struct EnemyState
    {
        public bool IsAlive;
        public float PosX, PosZ;
        public int Hp;
        public int TargetPlayerId;
    }

    public struct ProjectileState
    {
        public bool IsAlive;
        public float PosX, PosZ;
        public float DirX, DirZ;
        public uint LifetimeFrames;
        public int OwnerPlayerId;
    }

    public struct XpGemState
    {
        public bool IsAlive;
        public float PosX, PosZ;
        public int Value;
    }

    public class GameState
    {
        // --- Constants ---
        public const int MaxPlayers = 4;
        public const int MaxEnemies = 512;
        public const int MaxProjectiles = 256;
        public const int MaxGems = 512;

        // --- Tuning ---
        public const float ArenaHalfSize = 20f;
        public const float PlayerSpeed = 6f;
        public const float PlayerRadius = 0.4f;
        public const int PlayerMaxHp = 100;
        public const int PlayerStartXp = 0;
        public const int PlayerBaseXpToLevel = 10;

        public const float ZombieSpeed = 2.5f;
        public const float ZombieRadius = 0.4f;
        public const int ZombieHp = 3;
        public const int ZombieDamage = 10;
        public const int ZombieXpValue = 1;

        public const float KnifeSpeed = 12f;
        public const float KnifeRadius = 0.15f;
        public const int KnifeDamage = 1;
        public const uint KnifeCooldownFrames = 10; // 0.5s at 20fps
        public const uint KnifeLifetimeFrames = 40; // 2s

        public const float XpGemRadius = 0.5f;
        public const float XpPickupRadius = 1.5f;

        public const uint InvincibilityDuration = 20; // 1s

        // --- State ---
        public uint FrameNumber;
        public uint RngState;
        public int WaveNumber;
        public uint WaveSpawnTimer;
        public uint WaveSpawnRemaining;
        public float Dt; // fixed timestep in seconds, set from FrameSyncInitData

        public PlayerState[] Players = new PlayerState[MaxPlayers];
        public EnemyState[] Enemies = new EnemyState[MaxEnemies];
        public ProjectileState[] Projectiles = new ProjectileState[MaxProjectiles];
        public XpGemState[] Gems = new XpGemState[MaxGems];

        // --- Helpers ---

        public void InitPlayer(int slot)
        {
            ref var p = ref Players[slot];
            p.IsActive = true;
            p.IsAlive = true;
            // Spread starting positions
            float angle = slot * 1.5708f; // PI/2
            p.PosX = (float)Math.Cos(angle) * 2f;
            p.PosZ = (float)Math.Sin(angle) * 2f;
            p.FacingX = 0f;
            p.FacingZ = 1f;
            p.Hp = PlayerMaxHp;
            p.MaxHp = PlayerMaxHp;
            p.Xp = PlayerStartXp;
            p.Level = 1;
            p.XpToNextLevel = PlayerBaseXpToLevel;
            p.KnifeCooldown = 0;
            p.InvincibilityFrames = 0;
            p.KillCount = 0;
        }

        public int AllocEnemy()
        {
            for (int i = 0; i < MaxEnemies; i++)
                if (!Enemies[i].IsAlive) return i;
            return -1;
        }

        public int AllocProjectile()
        {
            for (int i = 0; i < MaxProjectiles; i++)
                if (!Projectiles[i].IsAlive) return i;
            return -1;
        }

        public int AllocGem()
        {
            for (int i = 0; i < MaxGems; i++)
                if (!Gems[i].IsAlive) return i;
            return -1;
        }

        /// <summary>Find the nearest alive player to (x,z). Returns slot index or -1.</summary>
        public int FindNearestPlayer(float x, float z)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < MaxPlayers; i++)
            {
                ref var p = ref Players[i];
                if (!p.IsActive || !p.IsAlive) continue;
                float dx = p.PosX - x;
                float dz = p.PosZ - z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public bool HasAlivePlayers()
        {
            for (int i = 0; i < MaxPlayers; i++)
                if (Players[i].IsActive && Players[i].IsAlive) return true;
            return false;
        }
    }
}
