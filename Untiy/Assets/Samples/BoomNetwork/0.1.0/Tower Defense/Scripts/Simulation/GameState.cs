// BoomNetwork TowerDefense Demo — Game State (Fixed-Point)
//
// 20×20 grid. Enemies pour in from all four edges. Defend the 2×2 center base.
// Pure FrameSync: only "place/sell tower" inputs are transmitted.
// 200 enemies on screen = zero extra bandwidth.

namespace BoomNetwork.Samples.TowerDefense
{
    public enum TowerType : byte { None = 0, Arrow = 1, Cannon = 2, Magic = 3 }
    public enum EnemyType : byte { Basic = 0, Fast = 1, Tank = 2 }

    public struct Tower
    {
        public TowerType Type;      // 0 = empty cell
        public int CooldownFrames;  // frames until next attack
        public int Level;           // 1-3 (0 when empty)
    }

    public struct Enemy
    {
        public bool IsAlive;
        public EnemyType Type;
        public FInt PosX, PosZ;     // world coords (cell = 1 unit, top-left = (0,0))
        public int Hp;
        public int SlowFrames;      // Magic tower slow remaining
    }

    public struct WaveState
    {
        public int WaveNumber;          // 1-10
        public int SpawnRemaining;      // enemies left to spawn this wave
        public int InterWaveTimer;      // countdown to next wave (frames)
        public bool AllWavesDone;
    }

    public class GameState
    {
        // ==================== Map ====================
        public const int GridW = 20;
        public const int GridH = 20;
        public const int GridSize = GridW * GridH; // 400

        // Base occupies center 2×2: cells (9,9),(10,9),(9,10),(10,10)
        public const int BaseCX = 9; // top-left corner X of base
        public const int BaseCY = 9; // top-left corner Y of base

        // ==================== Capacities ====================
        public const int MaxPlayers = 4;
        public const int MaxEnemies = 512;
        public const int MaxWaves = 10;

        // ==================== Gold ====================
        public const int InitialGold = 200;

        // ==================== Tower params ====================
        public const int ArrowCost = 50;
        public const int CannonCost = 100;
        public const int MagicCost = 80;

        public const int ArrowCooldown = 15;    // frames between shots
        public const int CannonCooldown = 35;   // 约 1.2s @30fps（原 60 = 2s，太慢）
        public const int MagicCooldown = 30;

        // Arrow/Magic range in grid units (FInt)
        public static readonly FInt ArrowRange  = FInt.FromInt(3);
        public static readonly FInt CannonRange = FInt.FromInt(5); // 原 2 格太近，看起来打自己
        public static readonly FInt MagicRange  = FInt.FromInt(4);

        // AoE blast radius for Cannon (grid units)
        public static readonly FInt CannonAoeRadius = FInt.FromFloat(1.8f); // 跟着射程放大

        public const int ArrowDamage  = 1;
        public const int CannonDamage = 3;
        public const int MagicDamage  = 1;
        public const int MagicSlowFrames = 60;

        // Upgrade cost: L1→L2 = base cost, L2→L3 = 2× base cost
        public static int GetTowerUpgradeCost(TowerType t, int currentLevel)
        {
            int baseCost = GetTowerCost(t);
            return currentLevel == 1 ? baseCost : baseCost * 2;
        }

        // Sell refund = 50% of total invested gold
        public static int GetSellRefund(TowerType t, int level)
        {
            int baseCost = GetTowerCost(t);
            int total = baseCost;
            if (level >= 2) total += GetTowerUpgradeCost(t, 1);
            if (level >= 3) total += GetTowerUpgradeCost(t, 2);
            return total / 2;
        }

        public const int MaxTowerLevel = 3;

        // ==================== Enemy params ====================
        // Speed in FInt units per frame (grid/frame)
        public static readonly FInt BasicSpeed = FInt.FromFloat(0.020f);  // 20
        public static readonly FInt FastSpeed  = FInt.FromFloat(0.040f);  // 40
        public static readonly FInt TankSpeed  = FInt.FromFloat(0.010f);  // 10

        public const int BasicHp    = 3;
        public const int FastHp     = 2;
        public const int TankHp     = 10;
        public const int BasicDamage = 1;  // damage to base on reach
        public const int FastDamage  = 1;
        public const int TankDamage  = 2;
        public const int BasicReward = 10;
        public const int FastReward  = 15;
        public const int TankReward  = 30;

        // ==================== Wave timing ====================
        public const int InterWaveFrames = 300; // ~10 sec @30fps before next wave starts

        // ==================== State ====================
        public uint FrameNumber;
        public uint RngState;
        public int BaseHp = 3;
        public int Gold;
        public int[] PlayerGoldContrib = new int[MaxPlayers]; // informational only (not synced in hash)

        public Tower[] Grid = new Tower[GridSize];
        public Enemy[] Enemies = new Enemy[MaxEnemies];
        public WaveState Wave;

        // ==================== Helpers ====================

        public static int CellIndex(int x, int y) => y * GridW + x;

        public static bool IsBase(int x, int y)
            => x >= BaseCX && x < BaseCX + 2 && y >= BaseCY && y < BaseCY + 2;

        public static bool IsInBounds(int x, int y)
            => x >= 0 && x < GridW && y >= 0 && y < GridH;

        public bool CanBuildAt(int x, int y)
        {
            if (!IsInBounds(x, y)) return false;
            if (IsBase(x, y)) return false;
            if (Grid[CellIndex(x, y)].Type != TowerType.None) return false;
            return true;
        }

        public int AllocEnemy()
        {
            for (int i = 0; i < MaxEnemies; i++)
                if (!Enemies[i].IsAlive) return i;
            return -1;
        }

        public static FInt GetEnemySpeed(EnemyType t)
        {
            switch (t) { case EnemyType.Fast: return FastSpeed; case EnemyType.Tank: return TankSpeed; default: return BasicSpeed; }
        }

        public static int GetEnemyHp(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankHp; case EnemyType.Fast: return FastHp; default: return BasicHp; }
        }

        public static int GetEnemyDamage(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankDamage; case EnemyType.Fast: return FastDamage; default: return BasicDamage; }
        }

        public static int GetEnemyReward(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankReward; case EnemyType.Fast: return FastReward; default: return BasicReward; }
        }

        public static int GetTowerCost(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonCost; case TowerType.Magic: return MagicCost; default: return ArrowCost; }
        }

        public static FInt GetTowerRange(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonRange; case TowerType.Magic: return MagicRange; default: return ArrowRange; }
        }

        // Level-aware range: each level adds 0.5 grid units
        public static FInt GetTowerRange(TowerType t, int level)
        {
            FInt baseRange = GetTowerRange(t);
            if (level <= 1) return baseRange;
            // 512 raw = 0.5 in 22.10 fixed-point
            return new FInt(baseRange.Raw + (level - 1) * 512);
        }

        public static int GetTowerCooldown(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonCooldown; case TowerType.Magic: return MagicCooldown; default: return ArrowCooldown; }
        }

        // Level-aware cooldown: each level reduces by 20% of base
        public static int GetTowerCooldown(TowerType t, int level)
        {
            int baseCd = GetTowerCooldown(t);
            return baseCd - (level - 1) * (baseCd / 5);
        }

        // Level-aware damage: Arrow 1/2/3, Cannon 3/5/7, Magic 1/2/3
        public static int GetTowerDamage(TowerType t, int level)
        {
            switch (t)
            {
                case TowerType.Cannon: return 1 + level * 2;  // 3/5/7
                default:               return level;           // 1/2/3
            }
        }

        // Level-aware AoE radius: +0.4 per level
        public static FInt GetCannonAoeRadius(int level)
        {
            if (level <= 1) return CannonAoeRadius;
            // 410 raw ≈ 0.4 in 22.10
            return new FInt(CannonAoeRadius.Raw + (level - 1) * 410);
        }

        // Level-aware magic slow: 60 / 90 / 120 frames
        public static int GetMagicSlowFrames(int level) => MagicSlowFrames + (level - 1) * 30;

        // Cell center in world coords (cells start at 0,0 top-left)
        public static FInt CellCenterX(int cx) => FInt.FromInt(cx) + FInt.Half;
        public static FInt CellCenterZ(int cy) => FInt.FromInt(cy) + FInt.Half;

        // ==================== ComputeHash ====================

        /// <summary>
        /// FNV-1a hash of all deterministic state fields.
        /// Used for desync detection.
        /// </summary>
        public uint ComputeHash()
        {
            uint h = 2166136261u;

            h = Fnv(h, FrameNumber);
            h = Fnv(h, RngState);
            h = Fnv(h, (uint)BaseHp);
            h = Fnv(h, (uint)Gold);
            h = Fnv(h, (uint)Wave.WaveNumber);
            h = Fnv(h, (uint)Wave.SpawnRemaining);
            h = Fnv(h, (uint)Wave.InterWaveTimer);
            h = Fnv(h, Wave.AllWavesDone ? 1u : 0u);

            for (int i = 0; i < GridSize; i++)
            {
                ref var t = ref Grid[i];
                h = Fnv(h, (uint)t.Type);
                h = Fnv(h, (uint)t.CooldownFrames);
                h = Fnv(h, (uint)t.Level);
            }

            for (int i = 0; i < MaxEnemies; i++)
            {
                ref var e = ref Enemies[i];
                if (!e.IsAlive) continue;
                h = Fnv(h, (uint)i);
                h = Fnv(h, (uint)e.Type);
                h = Fnv(h, (uint)e.PosX.Raw);
                h = Fnv(h, (uint)e.PosZ.Raw);
                h = Fnv(h, (uint)e.Hp);
                h = Fnv(h, (uint)e.SlowFrames);
            }

            return h;
        }

        static uint Fnv(uint hash, uint value)
        {
            hash ^= value & 0xFF;         hash *= 16777619u;
            hash ^= (value >> 8) & 0xFF;  hash *= 16777619u;
            hash ^= (value >> 16) & 0xFF; hash *= 16777619u;
            hash ^= (value >> 24) & 0xFF; hash *= 16777619u;
            return hash;
        }
    }
}
