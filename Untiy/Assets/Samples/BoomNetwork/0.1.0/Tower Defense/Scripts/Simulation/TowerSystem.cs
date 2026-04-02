// BoomNetwork TowerDefense Demo — Tower Attack Logic (Fixed-Point)
//
// Arrow: nearest enemy in range, single target.
// Cannon: nearest enemy, AoE blast on hit (all enemies within blast radius).
// Magic: nearest enemy, single target + SlowFrames.
//
// All arithmetic uses FInt. No floating-point in simulation.

namespace BoomNetwork.Samples.TowerDefense
{
    public static class TowerSystem
    {
        public static void Tick(GameState state)
        {
            for (int cy = 0; cy < GameState.GridH; cy++)
            {
                for (int cx = 0; cx < GameState.GridW; cx++)
                {
                    int gIdx = GameState.CellIndex(cx, cy);
                    ref var tower = ref state.Grid[gIdx];
                    if (tower.Type == TowerType.None) continue;

                    // Cooldown
                    if (tower.CooldownFrames > 0)
                    {
                        tower.CooldownFrames--;
                        continue;
                    }

                    FInt towerX  = GameState.CellCenterX(cx);
                    FInt towerZ  = GameState.CellCenterZ(cy);
                    FInt range   = GameState.GetTowerRange(tower.Type, tower.Level);
                    FInt rangeSq = range * range;

                    int target = FindNearest(state, towerX, towerZ, rangeSq);
                    if (target < 0) continue;

                    int lvl = tower.Level;

                    // Fire
                    switch (tower.Type)
                    {
                        case TowerType.Arrow:
                            DamageEnemy(state, target, GameState.GetTowerDamage(TowerType.Arrow, lvl));
                            break;

                        case TowerType.Cannon:
                            FireCannon(state, target, lvl);
                            break;

                        case TowerType.Magic:
                            DamageEnemy(state, target, GameState.GetTowerDamage(TowerType.Magic, lvl));
                            if (state.Enemies[target].IsAlive)
                                state.Enemies[target].SlowFrames = GameState.GetMagicSlowFrames(lvl);
                            break;
                    }

                    tower.CooldownFrames = GameState.GetTowerCooldown(tower.Type, tower.Level);
                }
            }
        }

        static int FindNearest(GameState state, FInt towerX, FInt towerZ, FInt rangeSq)
        {
            int best = -1;
            FInt bestDist = FInt.MaxValue;
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;
                FInt dSq = FInt.DistanceSqr(towerX, towerZ, e.PosX, e.PosZ);
                if (dSq <= rangeSq && dSq < bestDist)
                {
                    bestDist = dSq;
                    best = i;
                }
            }
            return best;
        }

        static void FireCannon(GameState state, int primaryTarget, int level)
        {
            ref var primary = ref state.Enemies[primaryTarget];
            FInt hitX    = primary.PosX;
            FInt hitZ    = primary.PosZ;
            FInt blast   = GameState.GetCannonAoeRadius(level);
            FInt blastSq = blast * blast;
            int  dmg     = GameState.GetTowerDamage(TowerType.Cannon, level);

            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref state.Enemies[i];
                if (!e.IsAlive) continue;
                FInt dSq = FInt.DistanceSqr(hitX, hitZ, e.PosX, e.PosZ);
                if (dSq <= blastSq)
                    DamageEnemy(state, i, dmg);
            }
        }

        static void DamageEnemy(GameState state, int idx, int damage)
        {
            ref var e = ref state.Enemies[idx];
            if (!e.IsAlive) return;
            e.Hp -= damage;
            if (e.Hp <= 0)
            {
                e.IsAlive = false;
                state.Gold += GameState.GetEnemyReward(e.Type);
            }
        }
    }
}
