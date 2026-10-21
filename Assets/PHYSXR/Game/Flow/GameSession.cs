using PHYSXR.Core.Enums;

namespace PHYSXR.Game.Flow
{
    // Plain C# record of the application session, owned by AppBootstrap in
    // the persistent Bootstrap scene so it survives game-scene reloads.
    //
    // Not to be confused with PHYSXR.Ghost.GameplaySession, which owns the
    // ghost pipeline's terminal gate for one run.
    //
    // RunNumber counts full runs started this session. Attempt is the
    // attempt within the current run: a full restart always returns it to 1,
    // a checkpoint restart adds 1.
    //
    // Scene reloads are the only restart mechanism, so the session also says
    // how the NEXT game scene should start (NextStart): a new run, or the saved
    // checkpoint. GameFlowController reads it once in Start().
    public class GameSession
    {
        public enum StartKind
        {
            NewRun = 0,
            Checkpoint
        }

        public int RunNumber { get; private set; }

        public int Attempt { get; private set; }

        public bool HasActiveRun => RunNumber > 0;

        // Trial settings chosen on the start screen.
        public RunCondition Condition { get; set; } = RunCondition.VIRTUAL_ONLY;

        public string TrialId { get; set; } = string.Empty;

        public StartKind NextStart { get; private set; } = StartKind.NewRun;

        public bool HasCheckpoint { get; private set; }

        public CheckpointData Checkpoint { get; private set; }

        public int Deaths { get; private set; }

        // Unpaused seconds spent in earlier attempts of this run, so the run
        // duration survives checkpoint reloads.
        public float SecondsBeforeAttempt { get; private set; }

        public void BeginNewRun()
        {
            RunNumber++;
            Attempt = 1;
            HasCheckpoint = false;
            Checkpoint = default;
            Deaths = 0;
            SecondsBeforeAttempt = 0f;
            NextStart = StartKind.NewRun;
        }

        public void SaveCheckpoint(CheckpointData checkpoint)
        {
            Checkpoint = checkpoint;
            HasCheckpoint = true;
        }

        public void RecordDeath()
        {
            Deaths++;
        }

        // The next game scene starts a fresh run.
        public void RequestNewRun()
        {
            NextStart = StartKind.NewRun;
        }

        // The next game scene resumes from the checkpoint. Returns false (and
        // requests a new run instead) when there is no checkpoint yet.
        public bool RequestCheckpointRestart(float secondsThisAttempt)
        {
            if (!HasCheckpoint)
            {
                NextStart = StartKind.NewRun;
                return false;
            }

            SecondsBeforeAttempt += secondsThisAttempt;
            NextStart = StartKind.Checkpoint;
            return true;
        }

        // Called by the game scene when it actually starts from the checkpoint.
        public CheckpointData BeginCheckpointAttempt()
        {
            Attempt++;
            NextStart = StartKind.NewRun;
            return Checkpoint;
        }
    }

    // Where and in what state a checkpoint attempt starts (spec: the start of
    // the main encounter, saved when power is restored). Plain values so the
    // session stays Unity-independent.
    public struct CheckpointData
    {
        public ObjectiveStage stage;
        public float x;
        public float y;
        public float z;
        public float yaw;
    }
}
