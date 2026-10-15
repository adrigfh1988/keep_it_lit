namespace KeepItLit.Tests
{
    /// <summary>F118: the seeds / floors / DESCENT depths the maze reachability test loads. Trim the arrays here for a quick run.</summary>
    public static class MazeTestMatrix
    {
        public static readonly int[] StorySeeds = { 101, 202, 303, 404, 505, 606, 707, 808 };
        public static readonly int[] StoryFloors = { 1, 2, 3, 4, 5 };

        public static readonly int[] DescentSeeds = { 101, 202, 303 };
        public static readonly int[] DescentDepths = { 1, 4, 8, 12, 16 };
    }
}
