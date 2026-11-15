using System.Collections.Generic;

namespace Glowrunner
{
    /// <summary>
    /// Generates mirror-symmetric 21×21 mazes with no dead ends, a fixed central hangar and the player start
    /// at the bottom centre. The same seed always gives the same maze.
    /// </summary>
    /// <remarks>
    /// Works on a lattice: odd (x, y) cells are junctions, cells between two junctions are corridors,
    /// and even/even cells are always wall pillars (so corridors are always one cell wide).
    /// </remarks>
    public static class MazeGenerator
    {
        public const int W = 21, H = 21;

        static readonly (int dx, int dy)[] Steps = { (0, -1), (-1, 0), (0, 1), (1, 0) };

        public static string[] Generate(int seed, float loopiness)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var rows = TryGenerate(new System.Random(seed + attempt * 7777), loopiness);
                if (rows != null && Validate(rows)) return rows;
            }
            return Maze.DefaultLayout;
        }

        static bool IsNode(int x, int y) => (x & 1) == 1 && (y & 1) == 1;
        static bool InNodeRange(int x, int y) => x >= 1 && x <= W - 2 && y >= 1 && y <= H - 2;
        /// <summary>Junctions inside the hangar box: never part of the maze.</summary>
        static bool Reserved(int x, int y) => y == 9 && x >= 7 && x <= 13;
        static bool ValidNode(int x, int y) => InNodeRange(x, y) && IsNode(x, y) && !Reserved(x, y);
        static int Mirror(int x) => W - 1 - x;

        static string[] TryGenerate(System.Random rng, float loopiness)
        {
            var g = new char[W, H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    g[x, y] = ValidNode(x, y) ? '.' : '#';

            void Open(int x, int y) { g[x, y] = '.'; g[Mirror(x), y] = '.'; }

            // Fixed: corridor ring around the hangar, and the start corridor at the bottom centre.
            for (int x = 5; x <= 15; x++) { Open(x, 7); Open(x, 11); }
            for (int y = 7; y <= 11; y++) { Open(5, y); Open(15, y); }
            Open(10, H - 2);

            // Union-find over junctions.
            var parent = new int[W * H];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) => parent[Find(a)] = Find(b);
            int Id(int x, int y) => y * W + x;

            // Corridor cells (between two junctions) in the left half, including the centre column.
            var edges = new List<(int x, int y, int ax, int ay, int bx, int by)>();
            for (int y = 1; y < H - 1; y++)
            {
                for (int x = 1; x <= W / 2; x++)
                {
                    bool horiz = (x & 1) == 0 && (y & 1) == 1;
                    bool vert = (x & 1) == 1 && (y & 1) == 0;
                    if (!horiz && !vert) continue;
                    int ax = horiz ? x - 1 : x, ay = horiz ? y : y - 1;
                    int bx = horiz ? x + 1 : x, by = horiz ? y : y + 1;
                    if (!ValidNode(ax, ay) || !ValidNode(bx, by)) continue;
                    if (x >= 6 && x <= 14 && y >= 8 && y <= 10) continue; // hangar box
                    edges.Add((x, y, ax, ay, bx, by));
                }
            }

            foreach (var e in edges)
            {
                if (g[e.x, e.y] != '.') continue;
                Union(Id(e.ax, e.ay), Id(e.bx, e.by));
                Union(Id(Mirror(e.ax), e.ay), Id(Mirror(e.bx), e.by));
            }

            // Randomised Kruskal, applied symmetrically.
            Shuffle(edges, rng);
            foreach (var e in edges)
            {
                if (Find(Id(e.ax, e.ay)) == Find(Id(e.bx, e.by))) continue;
                Open(e.x, e.y);
                Union(Id(e.ax, e.ay), Id(e.bx, e.by));
                Union(Id(Mirror(e.ax), e.ay), Id(Mirror(e.bx), e.by));
            }

            // Remove every dead end by opening another corridor from it.
            int Degree(int x, int y)
            {
                int n = 0;
                foreach (var (dx, dy) in Steps)
                    if (ValidNode(x + 2 * dx, y + 2 * dy) && g[x + dx, y + dy] == '.') n++;
                return n;
            }

            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                for (int y = 1; y < H - 1; y += 2)
                {
                    for (int x = 1; x <= W / 2; x += 2)
                    {
                        if (!ValidNode(x, y) || Degree(x, y) > 1) continue;
                        var choices = new List<(int cx, int cy, int score)>();
                        foreach (var (dx, dy) in Steps)
                        {
                            int nx = x + 2 * dx, ny = y + 2 * dy, cx = x + dx, cy = y + dy;
                            if (!ValidNode(nx, ny) || g[cx, cy] == '.') continue;
                            if (cx >= 6 && cx <= 14 && cy >= 8 && cy <= 10) continue;
                            choices.Add((cx, cy, Degree(nx, ny) <= 1 ? 0 : 1));
                        }
                        if (choices.Count == 0) continue;
                        choices.Sort((a, b) => a.score.CompareTo(b.score));
                        int best = choices[0].score;
                        var top = choices.FindAll(c => c.score == best);
                        var pick = top[rng.Next(top.Count)];
                        Open(pick.cx, pick.cy);
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            // Extra loops: more of them makes a more open, faster-flowing maze.
            var closed = edges.FindAll(e => g[e.x, e.y] != '.');
            Shuffle(closed, rng);
            int extra = (int)(closed.Count * loopiness);
            for (int i = 0; i < extra && i < closed.Count; i++) Open(closed[i].x, closed[i].y);

            // Power cores near each corner (mirrored).
            int[] cx0 = { 1, 3 };
            int[] topY = { 1, 3 };
            int[] botY = { 15, 17 };
            int tx = cx0[rng.Next(2)], ty = topY[rng.Next(2)];
            int bx2 = cx0[rng.Next(2)], by2 = botY[rng.Next(2)];
            g[tx, ty] = 'o'; g[Mirror(tx), ty] = 'o';
            g[bx2, by2] = 'o'; g[Mirror(bx2), by2] = 'o';

            // Hangar box, gate and start.
            for (int y = 8; y <= 10; y++)
                for (int x = 6; x <= 14; x++)
                    g[x, y] = '#';
            for (int x = 8; x <= 12; x++) g[x, 9] = 'S';
            g[10, 8] = '-';
            g[10, H - 2] = 'P';

            var rows = new string[H];
            var sb = new System.Text.StringBuilder(W);
            for (int y = 0; y < H; y++)
            {
                sb.Clear();
                for (int x = 0; x < W; x++) sb.Append(g[x, y]);
                rows[y] = sb.ToString();
            }
            return rows;
        }

        /// <summary>Checks that every open cell is reachable from the start and that there are no dead ends.</summary>
        public static bool Validate(string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            bool Walk(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && rows[y][x] != '#' && rows[y][x] != '-' && rows[y][x] != 'S';

            int sx = -1, sy = -1, open = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!Walk(x, y)) continue;
                    open++;
                    if (rows[y][x] == 'P') { sx = x; sy = y; }
                    int n = 0;
                    foreach (var (dx, dy) in Steps) if (Walk(x + dx, y + dy)) n++;
                    if (n <= 1) return false;
                }
            }
            if (sx < 0) return false;

            var seen = new bool[w, h];
            var q = new Queue<(int, int)>();
            q.Enqueue((sx, sy));
            seen[sx, sy] = true;
            int reached = 0;
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                reached++;
                foreach (var (dx, dy) in Steps)
                {
                    int nx = x + dx, ny = y + dy;
                    if (Walk(nx, ny) && !seen[nx, ny]) { seen[nx, ny] = true; q.Enqueue((nx, ny)); }
                }
            }
            return reached == open;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
