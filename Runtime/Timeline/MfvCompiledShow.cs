using System.Collections.Generic;

namespace ManeuverForVRC
{
    /// <summary>
    /// A timeline flattened into the arrays <see cref="MfvShowEvaluator"/> reads, plus the
    /// fixtures they refer to. The same object feeds editor preview and the Udon player.
    /// </summary>
    public class MfvCompiledShow
    {
        public float[] clips = new float[0];
        public float[] effects = new float[0];
        public float[] parameters = new float[0];
        public float[] colors = new float[0];
        public float[] gobos = new float[0];
        public string[] userNames = new string[0];

        public int[] groupCount = new int[0];

        /// <summary>Fixture position inside each group, one row of <see cref="fixtureCount"/> per group, -1 for non members.</summary>
        public int[] groupIndex = new int[0];

        /// <summary>Number of fixture columns in <see cref="groupIndex"/>.</summary>
        public int fixtureCount;

        /// <summary>Every fixture the show drives, in the index order the arrays use.</summary>
        public readonly List<MfvFixture> fixtures = new List<MfvFixture>();

        /// <summary>Problems found while compiling. Errors stop a build.</summary>
        public readonly List<string> errors = new List<string>();

        public readonly List<string> warnings = new List<string>();

        public int ClipCount => clips.Length / MfvShowEvaluator.ClipStride;

        public int FixtureCount => fixtures.Count;
    }
}
