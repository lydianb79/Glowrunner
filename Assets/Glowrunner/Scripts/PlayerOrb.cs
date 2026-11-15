using UnityEngine;

namespace Glowrunner
{
    /// <summary>The player: a hovering light orb with orbiting satellites.</summary>
    public class PlayerOrb : GridMover
    {
        public float Speed = 4.6f;

        Vector2Int desired;
        Transform visual;
        Transform satellites;
        Light glow;
        float yaw;

        static readonly Color OrbColor = new Color(0.2f, 0.95f, 1f);

        public void Setup(GameManager g)
        {
            Init(g);
            name = "Player Orb";

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            var bodyMat = Art.Mat(OrbColor, OrbColor * 1.4f, 0.9f);
            var visorMat = Art.Mat(new Color(0.02f, 0.03f, 0.05f), new Color(0f, 0.05f, 0.08f), 0.95f, 0.6f);
            var satMat = Art.Mat(Color.white, new Color(0.8f, 1f, 1f) * 1.5f, 0.8f);

            Art.Prim(PrimitiveType.Sphere, visual, Vector3.zero, Vector3.one * 0.72f, bodyMat, "Body");
            Art.Prim(PrimitiveType.Cube, visual, new Vector3(0f, 0.08f, 0.31f), new Vector3(0.42f, 0.12f, 0.14f), visorMat, "Visor");

            satellites = new GameObject("Satellites").transform;
            satellites.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Art.Prim(PrimitiveType.Sphere, satellites, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.58f, Vector3.one * 0.12f, satMat, "Satellite");
            }
            satellites.localRotation = Quaternion.Euler(18f, 0f, 0f);

            glow = Art.PointLight(transform, Vector3.zero, OrbColor, 5f, 2.2f);
        }

        public void ResetTo(Vector2Int c)
        {
            PlaceAt(c);
            desired = Vector2Int.zero;
            Direction = Vector2Int.zero;
            yaw = 0f;
            transform.localScale = Vector3.one;
            glow.intensity = 2.2f;
            UpdateVisual(0f);
        }

        void Update()
        {
            if (game == null) return;

            if (game.IsPlaying)
            {
                var input = GameInput.ReadDirection();
                if (input != Vector2Int.zero) desired = input;
                if (Moving && desired == -Direction) Reverse();
                Advance(Speed * Time.deltaTime);
            }

            if (!game.IsDying) UpdateVisual(Time.deltaTime);
        }

        protected override bool CanEnter(Vector2Int c) => maze.Walkable(c, false);

        protected override Vector2Int ChooseDirection()
        {
            if (desired != Vector2Int.zero && CanEnter(Cell + desired)) return desired;
            if (Direction != Vector2Int.zero && CanEnter(Cell + Direction)) return Direction;
            return Vector2Int.zero;
        }

        protected override void OnArrive() => game.OnPlayerEnteredCell(Cell);

        void UpdateVisual(float dt)
        {
            float t = Time.time;
            transform.position = FlatPosition + Vector3.up * (0.5f + Mathf.Sin(t * 3f) * 0.06f);
            if (Direction != Vector2Int.zero)
                yaw = Mathf.LerpAngle(yaw, YawOf(Direction), dt <= 0f ? 1f : 1f - Mathf.Exp(-18f * dt));
            visual.localRotation = Quaternion.Euler(0f, yaw, 0f);
            satellites.Rotate(0f, 200f * dt, 0f, Space.Self);
        }

        /// <summary>Death animation; t goes 0 → 1.</summary>
        public void AnimateDeath(float t)
        {
            t = Mathf.Clamp01(t);
            transform.position = FlatPosition + Vector3.up * (0.5f + t * 1.2f);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, 1f - t * t);
            visual.Rotate(0f, 900f * Time.deltaTime, 0f, Space.Self);
            satellites.Rotate(0f, -1200f * Time.deltaTime, 0f, Space.Self);
            glow.intensity = Mathf.Lerp(6f, 0f, t);
        }
    }
}
