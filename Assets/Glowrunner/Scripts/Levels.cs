using System.Collections.Generic;
using UnityEngine;

namespace Glowrunner
{
    /// <summary>
    /// The 99 levels. Level 1 is the hand-made maze; levels 2–99 are generated from fixed seeds,
    /// so they're the same in every playthrough. Each level also has its own neon colour,
    /// and difficulty ramps smoothly from level 1 to 99.
    /// </summary>
    public static class Levels
    {
        public const int Count = 99;

        static readonly Dictionary<int, string[]> cache = new Dictionary<int, string[]>();

        static readonly Color[] Palette =
        {
            new Color(0.25f, 0.6f, 1f),   // electric blue
            new Color(1f, 0.3f, 0.8f),    // magenta
            new Color(0.35f, 1f, 0.45f),  // lime
            new Color(1f, 0.6f, 0.2f),    // amber
            new Color(0.6f, 0.4f, 1f),    // violet
            new Color(0.2f, 1f, 0.85f),   // teal
            new Color(1f, 0.9f, 0.3f),    // gold
            new Color(1f, 0.42f, 0.38f),  // coral
            new Color(0.45f, 0.85f, 1f),  // sky
            new Color(0.85f, 0.9f, 1f),   // ice
        };

        public static int Clamp(int level) => Mathf.Clamp(level, 1, Count);

        /// <summary>0 at level 1, 1 at level 99.</summary>
        public static float Progress(int level) => (Clamp(level) - 1) / (float)(Count - 1);

        public static string[] Layout(int level)
        {
            level = Clamp(level);
            if (level == 1) return Maze.DefaultLayout;
            if (!cache.TryGetValue(level, out var rows))
            {
                rows = MazeGenerator.Generate(level * 7919 + 13, Loopiness(level));
                cache[level] = rows;
            }
            return rows;
        }

        /// <summary>How many extra loops the maze gets: varies from level to level between tight and open.</summary>
        public static float Loopiness(int level) => 0.04f + 0.22f * ((level * 37) % 10) / 9f;

        public static Color Theme(int level) => Palette[(Clamp(level) - 1) % Palette.Length];

        // ── Difficulty curve ──
        public static float PlayerSpeed(int level) => Mathf.Lerp(4.6f, 6.0f, Progress(level));
        public static float SentinelSpeed(int level) => Mathf.Lerp(4.1f, 6.0f, Mathf.Pow(Progress(level), 0.85f));
        public static float DisruptDuration(int level) => Mathf.Lerp(8f, 1.5f, Mathf.Pow(Progress(level), 0.7f));
        /// <summary>Multiplier on how long sentinels wait in the hangar.</summary>
        public static float ReleaseScale(int level) => Mathf.Lerp(1f, 0.25f, Mathf.Pow(Progress(level), 0.6f));
        /// <summary>Multiplier on patrol phases (shorter patrols = more time hunting).</summary>
        public static float PatrolScale(int level) => Mathf.Lerp(1f, 0.4f, Progress(level));
    }
}
