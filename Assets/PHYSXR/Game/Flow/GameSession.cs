namespace PHYSXR.Game.Flow
{
    // Plain C# record of the application session, owned by AppBootstrap in
    // the persistent Bootstrap scene so it survives game-scene reloads.
    //
    // Not to be confused with PHYSXR.Ghost.GameplaySession, which owns the
    // ghost pipeline's terminal gate for one run.
    //
    // RunNumber counts full runs started this session. Attempt is the
    // attempt within the current run: a full restart always returns it to 1.
    // Checkpoint restarts (which would increment Attempt) do not exist until
    // M4, so nothing increments it yet.
    public class GameSession
    {
        public int RunNumber { get; private set; }

        public int Attempt { get; private set; }

        public bool HasActiveRun => RunNumber > 0;

        public void BeginNewRun()
        {
            RunNumber++;
            Attempt = 1;
        }
    }
}
