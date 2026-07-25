/// <summary>
/// Counters for the run currently being played, shown on the game over screen.
/// Static so the pieces that produce them (enemies, placement, the spawner) do not
/// need a wired-up reference; <see cref="ResetRun"/> starts a fresh run.
/// </summary>
public static class RunStats
{
    /// <summary>Enemies that were killed. Enemies that despawn on their own do not count.</summary>
    public static int EnemiesDefeated { get; private set; }

    /// <summary>Pieces bought from the shop and placed on the grid. Pre-placed towers do not count.</summary>
    public static int TowersPlaced { get; private set; }

    /// <summary>The highest round the player reached, counting from 1.</summary>
    public static int Round { get; private set; }

    /// <summary>
    /// Clears every counter. Called when a level starts so a reloaded scene does not
    /// inherit the previous run's totals, which statics would otherwise keep alive.
    /// </summary>
    public static void ResetRun()
    {
        EnemiesDefeated = 0;
        TowersPlaced = 0;
        Round = 0;
    }

    public static void RecordEnemyDefeated()
    {
        EnemiesDefeated++;
    }

    public static void RecordTowerPlaced()
    {
        TowersPlaced++;
    }

    /// <summary>Records the round that just began. Never moves the count backwards.</summary>
    public static void RecordRoundStarted(int round)
    {
        if (round > Round)
        {
            Round = round;
        }
    }
}
