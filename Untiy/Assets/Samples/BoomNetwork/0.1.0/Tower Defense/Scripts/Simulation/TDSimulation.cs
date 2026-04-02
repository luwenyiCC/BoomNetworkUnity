// BoomNetwork TowerDefense Demo — Deterministic Simulation Coordinator
//
// Tick order per frame:
//   ApplyInputs → (if tower placed) PathSystem.Rebuild → WaveSystem → TowerSystem → EnemySystem
//
// All GameState mutation happens inside this class, driven by FrameData.
// No GameState mutation outside of Tick/ApplyInputs.

using BoomNetwork.Core.FrameSync;

namespace BoomNetwork.Samples.TowerDefense
{
    public class TDSimulation
    {
        public readonly GameState State = new GameState();

        // PlayerId → slot mapping (same pattern as VS Demo)
        readonly int[] _pidSlotMap = new int[256];
        int _nextSlot;

        public int PidToSlot(int pid)
        {
            if (pid < 0 || pid >= _pidSlotMap.Length) return -1;
            if (_pidSlotMap[pid] < 0 && _nextSlot < GameState.MaxPlayers)
                _pidSlotMap[pid] = _nextSlot++;
            return _pidSlotMap[pid];
        }

        public void GetPidMap(out int[] map, out int nextSlot) { map = _pidSlotMap; nextSlot = _nextSlot; }
        public void SetPidMap(int[] map, int nextSlot)
        {
            System.Array.Copy(map, _pidSlotMap, System.Math.Min(map.Length, _pidSlotMap.Length));
            _nextSlot = nextSlot;
        }

        public void Init(uint rngSeed)
        {
            State.FrameNumber = 0;
            State.RngState    = rngSeed == 0 ? 0xDEADBEEFu : rngSeed;
            State.BaseHp      = 3;
            State.Gold        = GameState.InitialGold;

            // Clear grid
            for (int i = 0; i < GameState.GridSize; i++)
                State.Grid[i] = default;

            // Clear enemies
            for (int i = 0; i < GameState.MaxEnemies; i++)
                State.Enemies[i] = default;

            // Wave 1 starts after InterWaveFrames
            State.Wave = new WaveState
            {
                WaveNumber       = 0,
                SpawnRemaining   = 0,
                InterWaveTimer   = GameState.InterWaveFrames,
                AllWavesDone     = false,
            };

            WaveSystem.SpawnTickCounter = 0;

            // Build initial flow field (no towers yet)
            PathSystem.Rebuild(State);

            // Reset pid mapping
            for (int i = 0; i < _pidSlotMap.Length; i++) _pidSlotMap[i] = -1;
            _nextSlot = 0;
        }

        public void Tick(FrameData frame)
        {
            State.FrameNumber = frame.FrameNumber;

            bool flowDirty = ApplyInputs(frame);
            if (flowDirty)
                PathSystem.Rebuild(State);

            if (IsGameOver()) return;

            WaveSystem.Tick(State);
            TowerSystem.Tick(State);
            EnemySystem.Tick(State);
        }

        public bool IsGameOver()
        {
            return State.BaseHp <= 0 || (State.Wave.AllWavesDone && CountAliveEnemies() == 0);
        }

        public bool IsVictory()
        {
            return State.Wave.AllWavesDone && CountAliveEnemies() == 0 && State.BaseHp > 0;
        }

        int CountAliveEnemies()
        {
            int c = 0;
            for (int i = 0; i < GameState.MaxEnemies; i++)
                if (State.Enemies[i].IsAlive) c++;
            return c;
        }

        /// <summary>
        /// Process inputs from all players.
        /// Returns true if any tower was placed or sold (flow field must be rebuilt).
        /// </summary>
        bool ApplyInputs(FrameData frame)
        {
            if (frame.Inputs == null) return false;
            bool flowDirty = false;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                if (input.Data == null || input.Data.Length < TDInput.InputSize) continue;

                TDInput.Decode(input.Data, 0, out int gx, out int gy, out TowerType towerType);

                // TowerType == 0 (None) means Silent When Idle — skip
                if (towerType == TowerType.None) continue;

                // Sell action
                if ((byte)towerType == TDInput.SellAction)
                {
                    if (GameState.IsInBounds(gx, gy))
                    {
                        int idx = GameState.CellIndex(gx, gy);
                        ref var existing = ref State.Grid[idx];
                        if (existing.Type != TowerType.None)
                        {
                            State.Gold += GameState.GetSellRefund(existing.Type, existing.Level);
                            State.Grid[idx] = default;
                            flowDirty = true;
                        }
                    }
                    continue;
                }

                // Upgrade action
                if ((byte)towerType == TDInput.UpgradeAction)
                {
                    if (GameState.IsInBounds(gx, gy))
                    {
                        int idx = GameState.CellIndex(gx, gy);
                        ref var t = ref State.Grid[idx];
                        if (t.Type != TowerType.None && t.Level < GameState.MaxTowerLevel)
                        {
                            int upgradeCost = GameState.GetTowerUpgradeCost(t.Type, t.Level);
                            if (State.Gold >= upgradeCost)
                            {
                                State.Gold -= upgradeCost;
                                t.Level++;
                            }
                        }
                    }
                    continue;
                }

                // Place tower
                if (!State.CanBuildAt(gx, gy)) continue;
                int cost = GameState.GetTowerCost(towerType);
                if (State.Gold < cost) continue;

                State.Gold -= cost;
                int cellIdx = GameState.CellIndex(gx, gy);
                State.Grid[cellIdx] = new Tower
                {
                    Type           = towerType,
                    CooldownFrames = 0,
                    Level          = 1,
                };
                flowDirty = true;
            }

            return flowDirty;
        }
    }
}
