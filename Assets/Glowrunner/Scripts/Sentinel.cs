using System.Collections.Generic;
using UnityEngine;

namespace Glowrunner
{
    public enum SentinelMode { Hangar, Leaving, Patrol, Hunt, Disrupted, Rebooting }

    /// <summary>
    /// How each sentinel picks its target while hunting.
    /// Tracker: heads straight for the player.
    /// Interceptor: aims a few cells ahead of where the player is going.
    /// Flanker: goes straight in while the Tracker is far away, otherwise circles behind the player.
    /// Wanderer: roams to random spots, but locks on when the player comes close.
    /// </summary>
    public enum Personality { Tracker, Interceptor, Flanker, Wanderer }

    public class Sentinel : GridMover
    {
        public Personality Personality { get; private set; }
        public SentinelMode Mode { get; private set; }
        public float DisruptedTimeLeft => disruptTimer;

        Color color;
        Vector2Int startCell;
        Vector2Int patrolCorner;
        float baseReleaseTime;
        bool startsOutside;
        float disruptTimer;
        Vector2Int wanderTarget;
        float wanderTimer;
        float phase;
        float yaw;

        Transform visual;
        GameObject body;
        Renderer[] bodyRenderers;
        GameObject[] pupils;
        Light glow;
        Material normalMat, disruptedMat, flashMat, currentMat;

        static readonly Color DisruptedColor = new Color(0.15f, 0.2f, 0.95f);
        static readonly List<Vector2Int> options = new List<Vector2Int>(4);

        public void Setup(GameManager g, Personality p, Color c, Vector2Int start, Vector2Int corner, float releaseTime, bool outside)
        {
            Init(g);
            Personality = p;
            color = c;
            startCell = start;
            patrolCorner = corner;
            baseReleaseTime = releaseTime;
            startsOutside = outside;
            phase = Random.value * 10f;
            name = "Sentinel " + p;
            BuildVisual();
        }

        void BuildVisual()
        {
            normalMat = Art.Mat(color, color * 0.9f, 0.75f, 0.2f);
            disruptedMat = Art.Mat(DisruptedColor, DisruptedColor * 0.8f, 0.6f);
            flashMat = Art.Mat(Color.white, Color.white * 0.9f, 0.6f);
            var eyeMat = Art.Mat(Color.white, new Color(0.6f, 0.6f, 0.6f), 0.9f);
            var pupilMat = Art.Mat(new Color(0.02f, 0.02f, 0.05f), Color.black, 0.9f);

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            body = new GameObject("Body");
            body.transform.SetParent(visual, false);
            Art.Prim(PrimitiveType.Sphere, body.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.78f, 0.6f, 0.78f), normalMat, "Dome");
            Art.Prim(PrimitiveType.Cylinder, body.transform, new Vector3(0f, -0.22f, 0f), new Vector3(0.86f, 0.04f, 0.86f), normalMat, "Skirt");
            Art.Prim(PrimitiveType.Cube, body.transform, new Vector3(0f, 0.36f, -0.05f), new Vector3(0.07f, 0.22f, 0.07f), normalMat, "Antenna");
            Art.Prim(PrimitiveType.Sphere, body.transform, new Vector3(0f, 0.5f, -0.05f), Vector3.one * 0.11f, normalMat, "Beacon");
            bodyRenderers = body.GetComponentsInChildren<Renderer>();
            currentMat = normalMat;

            pupils = new GameObject[2];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.15f : 0.15f;
                var eye = Art.Prim(PrimitiveType.Sphere, visual, new Vector3(x, 0.08f, 0.3f), Vector3.one * 0.2f, eyeMat, "Eye");
                pupils[i] = Art.Prim(PrimitiveType.Sphere, eye.transform, new Vector3(0f, 0f, 0.38f), Vector3.one * 0.5f, pupilMat, "Pupil");
            }

            glow = Art.PointLight(transform, new Vector3(0f, -0.2f, 0f), color, 3.5f, 1.6f);
        }

        public void ResetToStart()
        {
            PlaceAt(startCell);
            Direction = Dir.Left;
            disruptTimer = 0f;
            wanderTimer = 0f;
            Mode = startsOutside ? GlobalToMode() : SentinelMode.Hangar;
            yaw = startsOutside ? YawOf(Dir.Left) : 180f;
            UpdateVisual(0f);
        }

        SentinelMode GlobalToMode() => game.CurrentMode == GlobalMode.Hunt ? SentinelMode.Hunt : SentinelMode.Patrol;

        bool IsRoaming => Mode == SentinelMode.Patrol || Mode == SentinelMode.Hunt;
        public bool IsDangerous => Mode != SentinelMode.Disrupted && Mode != SentinelMode.Rebooting;
        public bool IsDisrupted => Mode == SentinelMode.Disrupted;

        public void OnGlobalModeChanged()
        {
            if (!IsRoaming) return;
            Mode = GlobalToMode();
            Reverse();
        }

        public void Disrupt(float duration)
        {
            if (IsRoaming) { Mode = SentinelMode.Disrupted; Reverse(); }
            if (Mode == SentinelMode.Disrupted) disruptTimer = duration;
        }

        public void SendToReboot()
        {
            Mode = SentinelMode.Rebooting;
            disruptTimer = 0f;
        }

        float CurrentSpeed
        {
            get
            {
                switch (Mode)
                {
                    case SentinelMode.Disrupted: return game.DisruptedSpeed;
                    case SentinelMode.Rebooting: return 11f;
                    case SentinelMode.Leaving: return 3f;
                    default:
                        float s = game.SentinelSpeed;
                        // The Tracker gets faster when only a few sparks are left.
                        if (Personality == Personality.Tracker && game.SparksLeft <= 20) s *= 1.1f;
                        return s;
                }
            }
        }

        void Update()
        {
            if (game == null) return;
            float dt = Time.deltaTime;

            if (game.IsPlaying)
            {
                if (Mode == SentinelMode.Hangar && game.RoundTime >= ReleaseTime) Mode = SentinelMode.Leaving;
                if (Mode == SentinelMode.Disrupted)
                {
                    disruptTimer -= dt;
                    if (disruptTimer <= 0f) Mode = GlobalToMode();
                }
                wanderTimer -= dt;
                if (Mode != SentinelMode.Hangar) Advance(CurrentSpeed * dt);
            }

            UpdateVisual(dt);
        }

        float ReleaseTime => baseReleaseTime * Levels.ReleaseScale(game.Level);

        protected override bool CanEnter(Vector2Int c)
        {
            bool viaHangar = Mode == SentinelMode.Leaving || Mode == SentinelMode.Rebooting || Mode == SentinelMode.Hangar;
            return maze.Walkable(c, viaHangar);
        }

        protected override Vector2Int ChooseDirection()
        {
            switch (Mode)
            {
                case SentinelMode.Hangar:
                    return Vector2Int.zero;
                case SentinelMode.Leaving:
                    if (Cell != maze.GateExit) return maze.StepToward(Cell, maze.GateExit, true);
                    Mode = GlobalToMode();
                    Direction = Dir.Up;
                    break;
                case SentinelMode.Rebooting:
                    if (Cell != maze.HangarCenter) return maze.StepToward(Cell, maze.HangarCenter, true);
                    Mode = SentinelMode.Leaving;
                    return maze.StepToward(Cell, maze.GateExit, true);
            }

            options.Clear();
            foreach (var d in Dir.All)
                if (d != -Direction && CanEnter(Cell + d)) options.Add(d);

            if (options.Count == 0) return CanEnter(Cell - Direction) ? -Direction : Vector2Int.zero;
            if (Mode == SentinelMode.Disrupted) return options[Random.Range(0, options.Count)];

            var target = ComputeTarget();
            Vector2Int best = options[0];
            float bestDist = float.MaxValue;
            foreach (var d in options)
            {
                float dist = ((Vector2)(Cell + d - target)).sqrMagnitude;
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            return best;
        }

        Vector2Int ComputeTarget()
        {
            if (Mode == SentinelMode.Patrol) return patrolCorner;

            var player = game.Player;
            var pc = player.NearestCell;
            var pd = player.Direction;

            switch (Personality)
            {
                case Personality.Interceptor:
                    return pc + pd * 3;

                case Personality.Flanker:
                {
                    var tracker = game.GetSentinel(Personality.Tracker);
                    float trackerDist = tracker != null ? ((Vector2)(tracker.NearestCell - pc)).magnitude : 99f;
                    return trackerDist > 6f ? pc : pc - pd * 3;
                }

                case Personality.Wanderer:
                    if (((Vector2)(Cell - pc)).sqrMagnitude <= 25f) return pc;
                    if (wanderTimer <= 0f || Cell == wanderTarget)
                    {
                        wanderTarget = maze.RandomOpenCell();
                        wanderTimer = 4f;
                    }
                    return wanderTarget;

                default:
                    return pc;
            }
        }

        void UpdateVisual(float dt)
        {
            float t = Time.time;
            float bob = Mode == SentinelMode.Hangar
                ? Mathf.Sin(t * 4f + phase) * 0.12f
                : Mathf.Sin(t * 3f + phase) * 0.05f;
            transform.position = FlatPosition + Vector3.up * (0.5f + bob);

            if (Direction != Vector2Int.zero && Mode != SentinelMode.Hangar)
                yaw = Mathf.LerpAngle(yaw, YawOf(Direction), dt <= 0f ? 1f : 1f - Mathf.Exp(-14f * dt));
            float shake = Mode == SentinelMode.Disrupted ? Mathf.Sin(t * 40f) * 6f : 0f;
            visual.localRotation = Quaternion.Euler(0f, yaw + shake, 0f);

            bool showBody = Mode != SentinelMode.Rebooting;
            if (body.activeSelf != showBody) body.SetActive(showBody);
            glow.enabled = showBody && visual.gameObject.activeSelf;

            Material want = normalMat;
            if (Mode == SentinelMode.Disrupted)
                want = disruptTimer < 2f && Mathf.Repeat(t, 0.4f) < 0.2f ? flashMat : disruptedMat;
            if (want != currentMat)
            {
                foreach (var r in bodyRenderers) r.sharedMaterial = want;
                currentMat = want;
            }
            glow.color = Mode == SentinelMode.Disrupted ? DisruptedColor : color;

            bool showPupils = Mode != SentinelMode.Disrupted;
            foreach (var p in pupils)
                if (p.activeSelf != showPupils) p.SetActive(showPupils);
        }

        public void SetVisible(bool visible) => visual.gameObject.SetActive(visible);
    }
}
