using System;

namespace MoriGame.Economy
{
    public class SupplyCrateState
    {
        private const float TimingEpsilon = 0.0005f;
        private readonly Random random;
        private readonly float openSeconds;
        private readonly float respawnSeconds;
        private readonly float matchDurationSeconds;
        private readonly float stopRespawnBeforeEndSeconds;

        public bool Available { get; private set; } = true;
        public float OpenElapsedSeconds { get; private set; }
        public float RespawnRemainingSeconds { get; private set; }
        public float OpenProgress => Available ? Math.Min(1f, OpenElapsedSeconds / openSeconds) : 0f;

        public SupplyCrateState(int randomSeed, float openSeconds = 1.5f, float respawnSeconds = 120f,
            float matchDurationSeconds = 480f, float stopRespawnBeforeEndSeconds = 60f)
        {
            random = new Random(randomSeed);
            this.openSeconds = openSeconds;
            this.respawnSeconds = respawnSeconds;
            this.matchDurationSeconds = matchDurationSeconds;
            this.stopRespawnBeforeEndSeconds = stopRespawnBeforeEndSeconds;
        }

        public int Tick(bool opening, float deltaSeconds, float matchElapsedSeconds)
        {
            if (Available)
            {
                OpenElapsedSeconds = opening ? OpenElapsedSeconds + deltaSeconds : 0f;
                if (OpenElapsedSeconds < openSeconds - TimingEpsilon)
                {
                    return 0;
                }
                Available = false;
                OpenElapsedSeconds = 0f;
                RespawnRemainingSeconds = respawnSeconds;
                return random.Next(400, 601);
            }

            RespawnRemainingSeconds = Math.Max(0f, RespawnRemainingSeconds - deltaSeconds);
            if (RespawnRemainingSeconds <= TimingEpsilon &&
                matchElapsedSeconds < matchDurationSeconds - stopRespawnBeforeEndSeconds)
            {
                Available = true;
                RespawnRemainingSeconds = 0f;
            }
            return 0;
        }
    }
}
