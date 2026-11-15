using UnityEngine;

namespace Glowrunner
{
    /// <summary>Base for anything that moves cell-to-cell through the maze.</summary>
    public abstract class GridMover : MonoBehaviour
    {
        protected GameManager game;
        protected Maze maze;

        /// <summary>The cell last reached (the cell being left while moving).</summary>
        public Vector2Int Cell { get; protected set; }
        /// <summary>The cell being moved into.</summary>
        public Vector2Int Target { get; protected set; }
        public Vector2Int Direction { get; protected set; }
        public bool Moving { get; protected set; }

        Vector3 flatPos;
        /// <summary>Position on the floor plane (y = 0).</summary>
        public Vector3 FlatPosition => flatPos;
        public Vector2Int NearestCell => maze.WorldToCell(flatPos);

        protected void Init(GameManager g)
        {
            game = g;
            maze = g.Maze;
        }

        /// <summary>Switch to a new maze (when a level is loaded).</summary>
        public void Rebind(Maze m) => maze = m;

        public void PlaceAt(Vector2Int c)
        {
            Cell = Target = c;
            Moving = false;
            flatPos = maze.CellToWorld(c);
        }

        protected abstract bool CanEnter(Vector2Int c);

        /// <summary>Called when standing on a cell centre. Return zero to stay put.</summary>
        protected abstract Vector2Int ChooseDirection();

        protected virtual void OnArrive() { }

        protected void Advance(float distance)
        {
            for (int i = 0; i < 8 && distance > 1e-5f; i++)
            {
                if (!Moving)
                {
                    var d = ChooseDirection();
                    if (d == Vector2Int.zero || !CanEnter(Cell + d)) return;
                    Direction = d;
                    Target = Cell + d;
                    Moving = true;
                }

                var targetPos = maze.CellToWorld(Target);
                float remaining = Vector3.Distance(flatPos, targetPos);
                if (remaining <= distance)
                {
                    flatPos = targetPos;
                    distance -= remaining;
                    Cell = Target;
                    Moving = false;
                    OnArrive();
                }
                else
                {
                    flatPos = Vector3.MoveTowards(flatPos, targetPos, distance);
                    distance = 0f;
                }
            }
        }

        /// <summary>Turn around on the spot, even between cells.</summary>
        protected void Reverse()
        {
            if (Moving)
            {
                var c = Cell;
                Cell = Target;
                Target = c;
            }
            Direction = -Direction;
        }

        protected static float YawOf(Vector2Int d)
        {
            var w = Dir.ToWorld(d);
            return Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg;
        }
    }
}
