// BoomNetwork VampireSurvivors Demo — Game State
//
// Pure C# data model, no Unity dependencies.
// All game state lives here for deterministic simulation + snapshot serialization.

using System;

namespace BoomNetwork.Samples.VampireSurvivors
{
    // === Enums ===

    public enum EnemyType : byte
    {
        Zombie = 0,
        Bat = 1,
        SkeletonMage = 2,
    }

    public enum WeaponType : byte
    {
        None = 0,
        Knife = 1,
        Orb = 2,
        Lightning = 3,
        HolyWater = 4,
    }

    public enum ProjectileType : byte
    {
        Knife = 0,
        BoneShard = 1,  // skeleton mage ranged attack
        HolyPuddle = 2,
    }

    // === Structs ===

    public struct WeaponSlot
    {
        public WeaponType Type;
        public int Level;      // 1-5
        public uint Cooldown;  // frames remaining
    }

    public struct OrbState
    {
        public bool Active;
        public float AngleDeg;
    }

    public struct LightningFlash
    {
        public float PosX, PosZ;
        public uint FramesLeft;
    }

    public struct PlayerState
    {
        public bool IsActive;
        public bool IsAlive;
        public float PosX, PosZ;
        public float FacingX, FacingZ;
        public int Hp, MaxHp;
        public int Xp, Level;
        public int XpToNextLevel;
        public uint InvincibilityFrames;
        public int KillCount;

        // Weapon system — 4 slots
        public const int MaxWeaponSlots = 4;
        public WeaponSlot Weapon0, Weapon1, Weapon2, Weapon3;

        // Upgrade selection
        public bool PendingLevelUp;
        public byte UpgradeChoice; // 0=none, 1-4=weapon slot index (set via input)

        // Orbs (Magic Orb weapon visual state)
        public const int MaxOrbs = 5;
        public OrbState Orb0, Orb1, Orb2, Orb3, Orb4;

        public ref WeaponSlot GetWeapon(int i)
        {
            switch (i)
            {
                case 0: return ref Weapon0;
                case 1: return ref Weapon1;
                case 2: return ref Weapon2;
                default: return ref Weapon3;
            }
        }

        public ref OrbState GetOrb(int i)
        {
            switch (i)
            {
                case 0: return ref Orb0;
                case 1: return ref Orb1;
                case 2: return ref Orb2;
                case 3: return ref Orb3;
                default: return ref Orb4;
            }
        }

        public int WeaponCount()
        {
            int c = 0;
            if (Weapon0.Type != WeaponType.None) c++;
            if (Weapon1.Type != WeaponType.None) c++;
            if (Weapon2.Type != WeaponType.None) c++;
            if (Weapon3.Type != WeaponType.None) c++;
            return c;
        }

        public int FindWeaponSlot(WeaponType type)
        {
            if (Weapon0.Type == type) return 0;
            if (Weapon1.Type == type) return 1;
            if (Weapon2.Type == type) return 2;
            if (Weapon3.Type == type) return 3;
            return -1;
        }

        public int FindEmptyWeaponSlot()
        {
            if (Weapon0.Type == WeaponType.None) return 0;
            if (Weapon1.Type == WeaponType.None) return 1;
            if (Weapon2.Type == WeaponType.None) return 2;
            if (Weapon3.Type == WeaponType.None) return 3;
            return -1;
        }
    }

    public struct EnemyState
    {
        public bool IsAlive;
        public EnemyType Type;
        public float PosX, PosZ;
        public float DirX, DirZ;      // movement direction (for bats)
        public int Hp;
        public int TargetPlayerId;
        public uint BehaviorTimer;    // general-purpose AI timer
    }

    public struct ProjectileState
    {
        public bool IsAlive;
        public ProjectileType Type;
        public float PosX, PosZ;
        public float DirX, DirZ;
        public float Radius;          // for AoE (holy puddle)
        public uint LifetimeFrames;
        public int OwnerPlayerId;
        public uint DamageTick;        // for puddle: frames since last damage
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
        public const int MaxLightningFlashes = 32;

        // --- Arena ---
        public const float ArenaHalfSize = 20f;

        // --- Player ---
        public const float PlayerSpeed = 6f;
        public const float PlayerRadius = 0.4f;
        public const int PlayerMaxHp = 100;
        public const int PlayerBaseXpToLevel = 10;
        public const uint InvincibilityDuration = 20; // 1s at 20fps

        // --- Zombie ---
        public const float ZombieSpeed = 2.5f;
        public const float ZombieRadius = 0.4f;
        public const int ZombieHp = 3;
        public const int ZombieDamage = 10;
        public const int ZombieXpValue = 1;

        // --- Bat ---
        public const float BatSpeed = 5f;
        public const float BatRadius = 0.3f;
        public const int BatHp = 2;
        public const int BatDamage = 5;
        public const int BatXpValue = 2;
        public const uint BatDirChangeInterval = 8; // change direction every 0.4s

        // --- Skeleton Mage ---
        public const float MageSpeed = 1.8f;
        public const float MageRadius = 0.4f;
        public const int MageHp = 5;
        public const int MageDamage = 8;
        public const int MageXpValue = 5;
        public const float MageAttackRange = 8f;
        public const uint MageFireCooldown = 40; // 2s
        public const float BoneShardSpeed = 7f;
        public const uint BoneShardLifetime = 30; // 1.5s
        public const float BoneShardRadius = 0.2f;
        public const int BoneShardDamage = 15;

        // --- Knife weapon ---
        public const float KnifeSpeed = 12f;
        public const float KnifeRadius = 0.15f;
        public const int KnifeDamage = 1;
        public const uint KnifeBaseCooldown = 10; // 0.5s
        public const uint KnifeLifetimeFrames = 40; // 2s

        // --- Magic Orb weapon ---
        public const float OrbOrbitRadius = 1.5f;
        public const float OrbAngularSpeed = 180f; // deg/s
        public const float OrbHitRadius = 0.4f;
        public const int OrbDamage = 2;
        public const uint OrbHitCooldown = 10; // can't hit same enemy twice in 0.5s

        // --- Lightning weapon ---
        public const float LightningRange = 6f;
        public const int LightningDamage = 3;
        public const uint LightningBaseCooldown = 30; // 1.5s
        public const int LightningBaseChains = 3;

        // --- Holy Water weapon ---
        public const float HolyWaterBaseRadius = 1.5f;
        public const uint HolyWaterBaseCooldown = 60; // 3s
        public const uint HolyWaterLifetime = 80;     // 4s
        public const int HolyWaterDamage = 1;
        public const uint HolyWaterDamageTick = 10;   // damage every 0.5s

        // --- XP ---
        public const float XpGemRadius = 0.5f;
        public const float XpPickupRadius = 1.5f;

        // --- State ---
        public uint FrameNumber;
        public uint RngState;
        public int WaveNumber;
        public uint WaveSpawnTimer;
        public uint WaveSpawnRemaining;
        public float Dt;

        public PlayerState[] Players = new PlayerState[MaxPlayers];
        public EnemyState[] Enemies = new EnemyState[MaxEnemies];
        public ProjectileState[] Projectiles = new ProjectileState[MaxProjectiles];
        public XpGemState[] Gems = new XpGemState[MaxGems];
        public LightningFlash[] Flashes = new LightningFlash[MaxLightningFlashes];

        // --- Helpers ---

        public void InitPlayer(int slot)
        {
            ref var p = ref Players[slot];
            p.IsActive = true;
            p.IsAlive = true;
            float angle = slot * 1.5708f;
            p.PosX = (float)Math.Cos(angle) * 2f;
            p.PosZ = (float)Math.Sin(angle) * 2f;
            p.FacingX = 0f;
            p.FacingZ = 1f;
            p.Hp = PlayerMaxHp;
            p.MaxHp = PlayerMaxHp;
            p.Xp = 0;
            p.Level = 1;
            p.XpToNextLevel = PlayerBaseXpToLevel;
            p.InvincibilityFrames = 0;
            p.KillCount = 0;
            p.PendingLevelUp = false;
            p.UpgradeChoice = 0;

            // Start with Knife in slot 0
            p.Weapon0 = new WeaponSlot { Type = WeaponType.Knife, Level = 1, Cooldown = 0 };
            p.Weapon1 = default;
            p.Weapon2 = default;
            p.Weapon3 = default;

            // Clear orbs
            for (int i = 0; i < PlayerState.MaxOrbs; i++)
                p.GetOrb(i) = default;
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

        public int AllocFlash()
        {
            for (int i = 0; i < MaxLightningFlashes; i++)
                if (Flashes[i].FramesLeft == 0) return i;
            return -1;
        }

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

        /// <summary>Find the nearest alive enemy to (x,z) within maxDist. Returns index or -1.</summary>
        public int FindNearestEnemy(float x, float z, float maxDist)
        {
            float maxDistSq = maxDist * maxDist;
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < MaxEnemies; i++)
            {
                ref var e = ref Enemies[i];
                if (!e.IsAlive) continue;
                float dx = e.PosX - x;
                float dz = e.PosZ - z;
                float d = dx * dx + dz * dz;
                if (d < maxDistSq && d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public bool HasAlivePlayers()
        {
            for (int i = 0; i < MaxPlayers; i++)
                if (Players[i].IsActive && Players[i].IsAlive) return true;
            return false;
        }

        public static int GetEnemyXpValue(EnemyType type)
        {
            switch (type)
            {
                case EnemyType.Zombie: return ZombieXpValue;
                case EnemyType.Bat: return BatXpValue;
                case EnemyType.SkeletonMage: return MageXpValue;
                default: return 1;
            }
        }

        public static float GetEnemyRadius(EnemyType type)
        {
            switch (type)
            {
                case EnemyType.Zombie: return ZombieRadius;
                case EnemyType.Bat: return BatRadius;
                case EnemyType.SkeletonMage: return MageRadius;
                default: return 0.4f;
            }
        }

        public static int GetEnemyDamage(EnemyType type)
        {
            switch (type)
            {
                case EnemyType.Zombie: return ZombieDamage;
                case EnemyType.Bat: return BatDamage;
                case EnemyType.SkeletonMage: return MageDamage;
                default: return 10;
            }
        }
    }
}
