# Enemy performance benchmark

Use a Development Build on the intended low-end device for final numbers. Editor
Play Mode is useful for correctness checks, but it is not a shipping-performance
measurement. Keep Deep Profiling disabled while recording final captures.

## Scene setup

1. Add an empty GameObject named `Enemy Stress Test` to a representative gameplay
   scene and add `EnemyStressTest`.
2. Assign Basic, Heavy, and Flyer enemy prefabs. Assign the tower projectile prefab
   for the projectile-heavy scenario.
3. Add `EnemySimulationManager` and `CombatObjectPool` to dedicated scene objects
   if their settings need inspector overrides. They otherwise create themselves
   with safe defaults.
4. Place the stress-test center where the player, towers, cages, world colliders,
   and camera exercise normal gameplay.
5. Set the scenario, enter Play Mode, open the component context menu, and select
   `Spawn 100 Enemies`, `Spawn 250 Enemies`, or `Spawn 500 Enemies`.
6. Use `Release Stress Objects` between captures. Verify `Pool Misses` remains zero.

Run all population sizes for:

- `AllVisible`
- `DenseCluster`
- `ChokePoint` in a real narrow passage
- `ProjectileHeavy`
- `Offscreen`

## Correctness pass

For every scenario, verify player and enemy damage, death/reuse, Basic and Flyer
movement, Flyer shooting, tower targeting, fan forces, saw knockback, cages,
Tesla chains, wave completion, and scene reload. A captured or pooled enemy must
not remain in `EnemySimulationManager.ActiveEnemyCount`.

## Profiler capture

1. Make a Development Build with Autoconnect Profiler enabled.
2. In the Profiler, record at least 30 seconds after pools and shaders are warm.
3. Record CPU Usage, Physics 2D, Rendering, Memory, and GC Alloc.
4. Inspect `EnemySimulation.GridRebuild`, `EnemySimulation.Steering`,
   `EnemySimulation.Decisions`, `EnemySimulation.Movement`,
   `EnemySimulation.Registration`, `EnemySpawning.Spawn`, `CombatPool.Get`,
   `CombatPool.Release`, and `EnemyStressTest.Spawn`.
5. Confirm steady combat reports 0 B/frame managed allocation. Separately note
   Physics2D simulation, contacts, queries, shapes, callbacks, and sync time.
6. Save the `.data` capture and use Unity Profile Analyzer to report median,
   95th-percentile, and 99th-percentile frame times.
7. Compare against the 16.67 ms 60 FPS budget on the target device.

Keep Physics2D multithreading disabled for the baseline. After the fundamental
changes are validated, capture the same workload with Use Multithreading enabled.
Keep it only if the target device improves without visible order-dependent
differences; test Consistency Sorting if ordering changes.
