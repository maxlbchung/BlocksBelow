/// <summary>
/// Counters and once-per-run moments for the run currently being played; the counters
/// are shown on the game over screen. Static so the pieces that produce them (enemies,
/// placement, the spawner) do not need a wired-up reference; <see cref="ResetRun"/>
/// starts a fresh run.
/// </summary>
public static class RunStats
{
    /// <summary>Enemies that were killed. Enemies that despawn on their own do not count.</summary>
    public static int EnemiesDefeated { get; private set; }

    /// <summary>Pieces bought from the shop and placed on the grid. Pre-placed towers do not count.</summary>
    public static int TowersPlaced { get; private set; }

    /// <summary>The highest round the player reached, counting from 1.</summary>
    public static int Round { get; private set; }

    /// <summary>Whether the run's first cage capture has already been claimed.</summary>
    public static bool FirstCageCaptureClaimed { get; private set; }

    /// <summary>
    /// Clears every counter. Called when a level starts so a reloaded scene does not
    /// inherit the previous run's totals, which statics would otherwise keep alive.
    /// </summary>
    public static void ResetRun()
    {
        EnemiesDefeated = 0;
        TowersPlaced = 0;
        Round = 0;
        FirstCageCaptureClaimed = false;
    }

    /// <summary>
    /// True the first time it is called in a run and false ever after, so a once-per-run
    /// flourish can claim the moment without every cage having to coordinate.
    /// </summary>
    public static bool TryClaimFirstCageCapture()
    {
        if (FirstCageCaptureClaimed)
        {
            return false;
        }

        FirstCageCaptureClaimed = true;
        return true;
    }

    public static void RecordEnemyDefeated()
    {
        EnemiesDefeated++;
    }

    /// <summary>
    /// Puts the kill count back to what it was, for a retry that replays a round from
    /// its opening state. Round and the once-per-run flags are deliberately left where
    /// they are: the round was still reached, and its flourish was still spent.
    /// </summary>
    public static void RestoreEnemiesDefeated(int count)
    {
        EnemiesDefeated = count > 0 ? count : 0;
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
