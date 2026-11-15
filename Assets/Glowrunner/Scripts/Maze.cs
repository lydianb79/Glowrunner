using System.Collections.Generic;
using UnityEngine;

namespace Glowrunner
{
    /// <summary>Grid directions. Cells are (column, row) with row 0 at the top (far side of the board).</summary>
    public static class Dir
    {
        public static readonly Vector2Int Up = new Vector2Int(0, -1);
        public static readonly Vector2Int Down = new Vector2Int(0, 1);
        public static readonly Vector2Int Left = new Vector2Int(-1, 0);
        public static readonly Vector2Int Right = new Vector2Int(1, 0);

        /// <summary>Priority order used to break ties when a sentinel picks a direction.</summary>
        public static readonly Vector2Int[] All = { Up, Left, Down, Right };

        public static Vector3 ToWorld(Vector2Int d) => new Vector3(d.x, 0f, -d.y);
    }

    /// <summary>
    /// Maze layout and grid queries.
    /// Legend: '#' wall, '.' spark, 'o' power core, ' ' empty floor,
    /// 'P' player start, '-' sentinel gate, 'S' sentinel hangar.
    /// </summary>
    public class Maze
    {
        public static readonly string[] DefaultLayout =
        {
            "#####################",
            "#o...#.........#...o#",
            "#.##.#.###.###.#.##.#",
            "#...................#",
            "#.##.#.#.###.#.#.##.#",
            "#....#.#.....#.#....#",
            "####.#.###.###.#.####",
            "####.#.........#.####",
            "####.#.###-###.#.####",
            "####...#SSSSS#...####",
            "####.#.#######.#.####",
            "####.#.........#.####",
            "####.#.###.###.#.####",
            "#...................#",
            "#.##.###.#.#.###.##.#",
            "#o.#.....#.#.....#.o#",
            "##.#.#.#.#.#.#.#.#.##",
            "#....#.#.....#.#....#",
            "#.####.#.#.#.#.####.#",
            "#.........P.........#",
            "#####################",
        };

        public readonly int Width;
        public readonly int Height;
        readonly char[,] cells;
        readonly List<Vector2Int> openCells = new List<Vector2Int>();

        public Vector2Int PlayerStart { get; }
        public Vector2Int Gate { get; }
        public Vector2Int GateExit => Gate + Dir.Up;
        public Vector2Int HangarCenter { get; }

        public Maze(string[] rows)
        {
            Height = rows.Length;
            Width = rows[0].Length;
            cells = new char[Width, Height];
            var hangar = new List<Vector2Int>();

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    char ch = x < rows[y].Length ? rows[y][x] : '#';
                    var c = new Vector2Int(x, y);
                    if (ch == 'P') { PlayerStart = c; ch = ' '; }
                    if (ch == '-') Gate = c;
                    if (ch == 'S') hangar.Add(c);
                    cells[x, y] = ch;
                    if (ch == '.' || ch == 'o' || ch == ' ') openCells.Add(c);
                }
            }

            HangarCenter = hangar.Count > 0 ? hangar[hangar.Count / 2] : Gate + Dir.Down;
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
        public char At(Vector2Int c) => InBounds(c) ? cells[c.x, c.y] : '#';
        public bool IsWall(Vector2Int c) => At(c) == '#';

        /// <summary>True if the cell can be entered. The gate and hangar are only open when <paramref name="allowHangar"/> is set.</summary>
        public bool Walkable(Vector2Int c, bool allowHangar)
        {
            char ch = At(c);
            if (ch == '#') return false;
            if (!allowHangar && (ch == '-' || ch == 'S')) return false;
            return true;
        }

        public Vector3 CellToWorld(Vector2Int c) =>
            new Vector3(c.x - (Width - 1) * 0.5f, 0f, (Height - 1) * 0.5f - c.y);

        public Vector2Int WorldToCell(Vector3 p) =>
            new Vector2Int(Mathf.RoundToInt(p.x + (Width - 1) * 0.5f), Mathf.RoundToInt((Height - 1) * 0.5f - p.z));

        public Vector2Int RandomOpenCell() => openCells[Random.Range(0, openCells.Count)];

        /// <summary>Direction of the first step on a shortest path from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public Vector2Int StepToward(Vector2Int from, Vector2Int to, bool allowHangar)
        {
            if (from == to) return Vector2Int.zero;

            var dist = new int[Width * Height];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<Vector2Int>();
            dist[to.y * Width + to.x] = 0;
            queue.Enqueue(to);

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int d = dist[c.y * Width + c.x];
                foreach (var dir in Dir.All)
                {
                    var n = c + dir;
                    if (!InBounds(n) || !Walkable(n, allowHangar)) continue;
                    int idx = n.y * Width + n.x;
                    if (dist[idx] >= 0) continue;
                    dist[idx] = d + 1;
                    queue.Enqueue(n);
                }
            }

            Vector2Int best = Vector2Int.zero;
            int bestDist = int.MaxValue;
            foreach (var dir in Dir.All)
            {
                var n = from + dir;
                if (!InBounds(n)) continue;
                int d = dist[n.y * Width + n.x];
                if (d >= 0 && d < bestDist) { bestDist = d; best = dir; }
            }
            return best;
        }
    }
}
