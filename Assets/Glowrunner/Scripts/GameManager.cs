using System.Collections.Generic;
using UnityEngine;

namespace Glowrunner
{
    public enum GlobalMode { Patrol, Hunt }

    /// <summary>
    /// Builds the world and runs the game: scoring, lives, the 99 levels, sentinel modes and the HUD.
    /// Everything is generated from code, so an empty scene with this component is all that's needed.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Tooltip("Optional Standard-shader material used as the template for every generated material (keeps the shader in builds).")]
        public Material baseMaterial;

        [Header("Tuning")]
        public int startingLives = 3;
        [Tooltip("An extra life is awarded every time the score passes another multiple of this.")]
        public int extraLifeScore = 10000;

        enum State { Title, Ready, Playing, Dying, LevelClear, GameOver, Victory }

        class Pickup
        {
            public GameObject go;
            public bool isCore;
        }

        const float DeathDuration = 1.8f;
        const float ReadyDuration = 2.2f;
        const float ClearDuration = 2.6f;
        const string HighScoreKey = "Glowrunner.HighScore";
        const string BestLevelKey = "Glowrunner.BestLevel";
        static readonly float[] ModeDurations = { 7f, 20f, 7f, 20f, 5f, 20f, 5f };

        public Maze Maze { get; private set; }
        public PlayerOrb Player { get; private set; }
        public int Level { get; private set; } = 1;
        public GlobalMode CurrentMode { get; private set; } = GlobalMode.Patrol;
        public float RoundTime { get; private set; }
        public int SparksLeft => pickups.Count;

        public bool IsPlaying => state == State.Playing && !paused;
        public bool IsDying => state == State.Dying;
        public bool WantsOverview => state == State.Title || state == State.GameOver || state == State.Victory;

        public float SentinelSpeed => Levels.SentinelSpeed(Level);
        public float DisruptedSpeed => 2.6f;

        readonly List<Sentinel> sentinels = new List<Sentinel>();
        readonly Dictionary<Vector2Int, Pickup> pickups = new Dictionary<Vector2Int, Pickup>();

        State state = State.Title;
        float stateTimer;
        bool paused;
        int score, highScore, lives;
        int nextExtraLife;
        int chainValue;
        int modeIndex;
        float modeTimer;
        int bestLevel = 1;
        int selectedLevel = 1;

        Transform mazeRoot;
        readonly List<Mesh> mazeMeshes = new List<Mesh>();
        Mesh cubeMesh;
        Transform pickupRoot;
        Material sparkMat, coreMat, trimMat, wallMat, floorMat, gateMat;
        Color trimColor;
        CameraRig cameraRig;
        Sfx sfx;
        Music music;

        string popupText;
        Vector3 popupWorld;
        float popupTimer;

        GUIStyle hudStyle, bigStyle, midStyle, smallStyle;

        /// <summary>Lets the game run in any scene: if no GameManager exists, one is created.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            if (FindAnyObjectByType<GameManager>() == null)
                new GameObject("Glowrunner").AddComponent<GameManager>();
        }

        void Start()
        {
            Art.Init(baseMaterial);
            highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
            bestLevel = Levels.Clamp(PlayerPrefs.GetInt(BestLevelKey, 1));
            selectedLevel = bestLevel;
            sfx = gameObject.AddComponent<Sfx>();
            music = gameObject.AddComponent<Music>();
            music.Play(Music.Track.Title);

            BuildEnvironment();
            CreateMaterials();
            LoadLevel(selectedLevel);
            cameraRig.Setup(this);
            state = State.Title;
        }

        // ───────────────────────── World building ─────────────────────────

        void BuildEnvironment()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cameraRig = cam.gameObject.AddComponent<CameraRig>();

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Moonlight").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(62f, -30f, 0f);
                sun.color = new Color(0.6f, 0.7f, 1f);
                sun.intensity = 0.35f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.6f;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.14f);
            RenderSettings.skybox = null;
            QualitySettings.pixelLightCount = 16;
            QualitySettings.shadowDistance = 60f;
        }

        void CreateMaterials()
        {
            floorMat = Art.Mat(new Color(0.02f, 0.02f, 0.05f), Color.black, 0.85f);
            wallMat = Art.Mat(new Color(0.05f, 0.07f, 0.2f), new Color(0.01f, 0.02f, 0.08f), 0.6f, 0.3f);
            trimMat = Art.Mat(Color.white, Color.white, 0.8f);
            gateMat = Art.Mat(new Color(1f, 0.4f, 0.8f), new Color(1f, 0.3f, 0.7f) * 1.5f, 0.8f);
            sparkMat = Art.Mat(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.8f, 0.45f) * 1.3f, 0.7f);
            coreMat = Art.Mat(new Color(1f, 0.35f, 0.85f), new Color(1f, 0.3f, 0.8f) * 1.6f, 0.8f);

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
        }

        /// <summary>Builds the maze, colours and pickups for a level and puts the actors at their starts.</summary>
        void LoadLevel(int level)
        {
            Level = Levels.Clamp(level);
            Maze = new Maze(Levels.Layout(Level));
            BuildMaze();
            ApplyTheme();

            if (Player == null) SpawnActors();
            else
            {
                Player.Rebind(Maze);
                foreach (var s in sentinels) s.Rebind(Maze);
            }

            BuildPickups();
            ResetActors();
        }

        void ApplyTheme()
        {
            trimColor = Levels.Theme(Level);
            trimMat.color = trimColor;
            Art.SetEmission(trimMat, trimColor * 1.6f);
            wallMat.color = Color.Lerp(new Color(0.04f, 0.05f, 0.12f), trimColor, 0.12f);
            Art.SetEmission(wallMat, trimColor * 0.035f);
        }

        void BuildMaze()
        {
            if (mazeRoot != null) Destroy(mazeRoot.gameObject);
            foreach (var m in mazeMeshes) Destroy(m);
            mazeMeshes.Clear();
            mazeRoot = new GameObject("Maze").transform;

            var floor = Art.Prim(PrimitiveType.Cube, mazeRoot, new Vector3(0f, -0.05f, 0f),
                new Vector3(Maze.Width + 6f, 0.1f, Maze.Height + 6f), floorMat, "Floor");
            floor.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var blocks = new List<CombineInstance>();
            var trims = new List<CombineInstance>();
            const float wallH = 0.8f;
            for (int y = 0; y < Maze.Height; y++)
            {
                for (int x = 0; x < Maze.Width; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (!Maze.IsWall(c)) continue;
                    var p = Maze.CellToWorld(c);
                    blocks.Add(new CombineInstance
                    {
                        mesh = cubeMesh,
                        transform = Matrix4x4.TRS(p + Vector3.up * wallH * 0.5f, Quaternion.identity, new Vector3(1f, wallH, 1f))
                    });

                    // Neon strips along every wall edge that faces an open corridor.
                    foreach (var d in Dir.All)
                    {
                        var n = c + d;
                        if (!Maze.InBounds(n) || Maze.IsWall(n)) continue;
                        var w = Dir.ToWorld(d);
                        var size = d.x != 0 ? new Vector3(0.06f, 0.05f, 1.02f) : new Vector3(1.02f, 0.05f, 0.06f);
                        trims.Add(new CombineInstance
                        {
                            mesh = cubeMesh,
                            transform = Matrix4x4.TRS(p + w * 0.48f + Vector3.up * (wallH + 0.01f), Quaternion.identity, size)
                        });
                    }
                }
            }

            mazeMeshes.Add(MakeCombined("Walls", mazeRoot, blocks, wallMat, true));
            mazeMeshes.Add(MakeCombined("Neon Trim", mazeRoot, trims, trimMat, false));

            Art.Prim(PrimitiveType.Cube, mazeRoot, Maze.CellToWorld(Maze.Gate) + Vector3.up * 0.45f,
                new Vector3(1f, 0.08f, 0.08f), gateMat, "Gate");
        }

        static Mesh MakeCombined(string name, Transform parent, List<CombineInstance> parts, Material mat, bool shadows)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }

        void SpawnActors()
        {
            Player = new GameObject().AddComponent<PlayerOrb>();
            Player.Setup(this);

            int w = Maze.Width, h = Maze.Height;
            var hangar = Maze.HangarCenter;
            AddSentinel(Personality.Tracker, new Color(1f, 0.15f, 0.25f), Maze.GateExit, new Vector2Int(w - 2, -3), 0f, true);
            AddSentinel(Personality.Interceptor, new Color(0.75f, 0.3f, 1f), hangar, new Vector2Int(1, -3), 1.5f, false);
            AddSentinel(Personality.Flanker, new Color(0.35f, 1f, 0.3f), hangar + Dir.Left * 2, new Vector2Int(w - 2, h + 2), 5f, false);
            AddSentinel(Personality.Wanderer, new Color(1f, 0.6f, 0.1f), hangar + Dir.Right * 2, new Vector2Int(1, h + 2), 9f, false);
        }

        void AddSentinel(Personality p, Color c, Vector2Int start, Vector2Int corner, float release, bool outside)
        {
            var s = new GameObject().AddComponent<Sentinel>();
            s.Setup(this, p, c, start, corner, release, outside);
            sentinels.Add(s);
        }

        public Sentinel GetSentinel(Personality p) => sentinels.Find(s => s.Personality == p);

        void BuildPickups()
        {
            if (pickupRoot != null) Destroy(pickupRoot.gameObject);
            pickups.Clear();
            pickupRoot = new GameObject("Pickups").transform;

            for (int y = 0; y < Maze.Height; y++)
            {
                for (int x = 0; x < Maze.Width; x++)
                {
                    var c = new Vector2Int(x, y);
                    char ch = Maze.At(c);
                    if (ch != '.' && ch != 'o') continue;
                    bool isCore = ch == 'o';
                    var pos = Maze.CellToWorld(c) + Vector3.up * (isCore ? 0.5f : 0.42f);
                    GameObject go;
                    if (isCore)
                    {
                        go = Art.Prim(PrimitiveType.Sphere, pickupRoot, pos, Vector3.one * 0.42f, coreMat, "Power Core");
                        Art.PointLight(go.transform, Vector3.zero, new Color(1f, 0.3f, 0.8f), 3f, 1.4f);
                    }
                    else
                    {
                        go = Art.Prim(PrimitiveType.Cube, pickupRoot, pos, Vector3.one * 0.15f, sparkMat, "Spark");
                        go.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
                        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    pickups[c] = new Pickup { go = go, isCore = isCore };
                }
            }
        }

        // ───────────────────────── Game flow ─────────────────────────

        void NewGame(int startLevel)
        {
            score = 0;
            lives = startingLives;
            nextExtraLife = extraLifeScore;
            LoadLevel(startLevel);
            StartRound();
        }

        void StartRound()
        {
            CurrentMode = GlobalMode.Patrol;
            modeIndex = 0;
            modeTimer = 0f;
            RoundTime = 0f;
            paused = false;
            ResetActors();
            state = State.Ready;
            stateTimer = ReadyDuration;
            music.Play(Music.Track.None);
            sfx.Jingle();

            if (Level > bestLevel)
            {
                bestLevel = Level;
                PlayerPrefs.SetInt(BestLevelKey, bestLevel);
                PlayerPrefs.Save();
            }
        }

        void ResetActors()
        {
            Player.Speed = Levels.PlayerSpeed(Level);
            Player.ResetTo(Maze.PlayerStart);
            foreach (var s in sentinels)
            {
                s.SetVisible(true);
                s.ResetToStart();
            }
        }

        void ReturnToTitle(int levelToShow)
        {
            selectedLevel = Mathf.Clamp(levelToShow, 1, bestLevel);
            LoadLevel(selectedLevel);
            state = State.Title;
            music.Play(Music.Track.Title);
        }

        void Update()
        {
            if (Maze == null) return;

            if (GameInput.CameraTogglePressed()) cameraRig.Toggle();
            if (GameInput.MutePressed()) music.ToggleMute();
            if (state == State.Playing && GameInput.PausePressed()) paused = !paused;
            music.SetPaused(paused);
            music.SetDisrupted(state == State.Playing && sentinels.Exists(s => s.IsDisrupted));
            if (paused) return;

            float dt = Time.deltaTime;
            AnimatePickups(dt);
            popupTimer -= dt;
            stateTimer -= dt;

            switch (state)
            {
                case State.Title:
                    TickLevelSelect();
                    if (GameInput.ConfirmPressed()) NewGame(selectedLevel);
                    break;

                case State.GameOver:
                    if (GameInput.ConfirmPressed()) ReturnToTitle(Level);
                    break;

                case State.Victory:
                    if (GameInput.ConfirmPressed()) ReturnToTitle(Levels.Count);
                    break;

                case State.Ready:
                    if (stateTimer <= 0f)
                    {
                        state = State.Playing;
                        music.Play(Music.Track.Game);
                    }
                    break;

                case State.Playing:
                    TickPlaying(dt);
                    break;

                case State.Dying:
                    Player.AnimateDeath(1f - stateTimer / DeathDuration);
                    if (stateTimer <= 0f)
                    {
                        lives--;
                        if (lives <= 0) GameOver();
                        else StartRound();
                    }
                    break;

                case State.LevelClear:
                    bool on = Mathf.Repeat(stateTimer, 0.36f) < 0.18f;
                    Art.SetEmission(trimMat, (on ? Color.white : trimColor) * 1.6f);
                    if (stateTimer <= 0f)
                    {
                        if (Level >= Levels.Count) Victory();
                        else
                        {
                            LoadLevel(Level + 1);
                            StartRound();
                        }
                    }
                    break;
            }
        }

        void TickLevelSelect()
        {
            int step = GameInput.MenuStep();
            if (step == 0 || bestLevel <= 1) return;
            int next = selectedLevel + step;
            if (next < 1) next = bestLevel;
            if (next > bestLevel) next = 1;
            if (next == selectedLevel) return;
            selectedLevel = next;
            LoadLevel(selectedLevel);
            sfx.Spark();
        }

        void TickPlaying(float dt)
        {
            RoundTime += dt;

            bool anyDisrupted = sentinels.Exists(s => s.IsDisrupted);
            if (!anyDisrupted && modeIndex < ModeDurations.Length)
            {
                modeTimer += dt;
                float duration = ModeDurations[modeIndex] * (modeIndex % 2 == 0 ? Levels.PatrolScale(Level) : 1f);
                if (modeTimer >= duration)
                {
                    modeTimer = 0f;
                    modeIndex++;
                    CurrentMode = modeIndex % 2 == 0 ? GlobalMode.Patrol : GlobalMode.Hunt;
                    foreach (var s in sentinels) s.OnGlobalModeChanged();
                }
            }

            foreach (var s in sentinels)
            {
                if ((s.FlatPosition - Player.FlatPosition).sqrMagnitude > 0.42f) continue;
                if (s.IsDisrupted) CaptureSentinel(s);
                else if (s.IsDangerous) { PlayerCaught(); return; }
            }
        }

        public void OnPlayerEnteredCell(Vector2Int c)
        {
            if (state != State.Playing) return;
            if (!pickups.TryGetValue(c, out var p)) return;

            pickups.Remove(c);
            Destroy(p.go);

            if (p.isCore)
            {
                AddScore(50);
                chainValue = 200;
                float duration = Levels.DisruptDuration(Level);
                foreach (var s in sentinels) s.Disrupt(duration);
                sfx.Core();
            }
            else
            {
                AddScore(10);
                sfx.Spark();
            }

            if (pickups.Count == 0)
            {
                state = State.LevelClear;
                stateTimer = ClearDuration;
                music.Play(Music.Track.None);
                sfx.Clear();
            }
        }

        void CaptureSentinel(Sentinel s)
        {
            AddScore(chainValue);
            ShowPopup(chainValue.ToString(), s.transform.position);
            chainValue *= 2;
            s.SendToReboot();
            sfx.Zap();
        }

        void PlayerCaught()
        {
            state = State.Dying;
            stateTimer = DeathDuration;
            foreach (var s in sentinels) s.SetVisible(false);
            music.Play(Music.Track.None);
            sfx.Crash();
        }

        void GameOver()
        {
            state = State.GameOver;
            music.Play(Music.Track.Title);
            SaveHighScore();
            Player.transform.localScale = Vector3.zero;
        }

        void Victory()
        {
            state = State.Victory;
            Art.SetEmission(trimMat, trimColor * 1.6f);
            music.Play(Music.Track.Title);
            sfx.ExtraLife();
            SaveHighScore();
        }

        void SaveHighScore()
        {
            PlayerPrefs.SetInt(HighScoreKey, highScore);
            PlayerPrefs.Save();
        }

        void AddScore(int amount)
        {
            score += amount;
            if (extraLifeScore > 0 && score >= nextExtraLife)
            {
                nextExtraLife += extraLifeScore;
                lives++;
                sfx.ExtraLife();
            }
            if (score > highScore) highScore = score;
        }

        void ShowPopup(string text, Vector3 world)
        {
            popupText = text;
            popupWorld = world;
            popupTimer = 1f;
        }

        void AnimatePickups(float dt)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.15f;
            foreach (var p in pickups.Values)
            {
                if (p.isCore) p.go.transform.localScale = Vector3.one * 0.42f * pulse;
                else p.go.transform.Rotate(0f, 120f * dt, 0f, Space.World);
            }
        }

        void OnApplicationQuit()
        {
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }

        // ───────────────────────── HUD ─────────────────────────

        void OnGUI()
        {
            if (Maze == null) return;
            float k = Screen.height / 720f;
            EnsureStyles(k);
            var accent = trimColor;
            accent.a = 1f;

            float pad = 18f * k;
            var top = new Rect(pad, pad * 0.6f, Screen.width - pad * 2f, 60f * k);
            hudStyle.alignment = TextAnchor.UpperLeft;
            Label(top, $"SCORE\n<b>{score}</b>", hudStyle, Color.white);
            hudStyle.alignment = TextAnchor.UpperCenter;
            Label(top, $"HIGH SCORE\n<b>{highScore}</b>", hudStyle, new Color(1f, 0.85f, 0.5f));
            hudStyle.alignment = TextAnchor.UpperRight;
            Label(top, $"LEVEL\n<b>{Level}</b> / {Levels.Count}", hudStyle, accent);

            if (state != State.Title)
            {
                hudStyle.alignment = TextAnchor.LowerLeft;
                var bottom = new Rect(pad, Screen.height - 50f * k, 400f * k, 40f * k);
                Label(bottom, "LIVES  " + new string('●', Mathf.Max(0, lives)).Replace("●", "● "), hudStyle, new Color(0.3f, 0.95f, 1f));
            }

            hudStyle.alignment = TextAnchor.LowerRight;
            var hint = new Rect(Screen.width - 520f * k - pad, Screen.height - 50f * k, 520f * k, 40f * k);
            string hintText = (cameraRig.Overview ? "C: chase camera" : "C: overview camera")
                + "   P: pause   M: music " + (music.Muted ? "on" : "off");
            Label(hint, hintText, smallStyle, new Color(1f, 1f, 1f, 0.45f));

            var centre = new Rect(0f, Screen.height * 0.28f, Screen.width, Screen.height * 0.44f);
            if (state == State.Title || state == State.GameOver || state == State.Victory || paused)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0.02f, 0.72f);
                GUI.DrawTexture(new Rect(0f, centre.y - 20f * k, Screen.width, 360f * k), Texture2D.whiteTexture);
                GUI.color = old;
            }

            switch (state)
            {
                case State.Title:
                    Label(new Rect(centre.x, centre.y, centre.width, 120f * k), "GLOWRUNNER", bigStyle, new Color(0.3f, 0.95f, 1f));
                    Label(new Rect(centre.x, centre.y + 110f * k, centre.width, 120f * k),
                        "Collect every spark. Dodge the sentinels.\nGrab a pink power core to disrupt them, then ram them for bonus points.",
                        smallStyle, new Color(1f, 1f, 1f, 0.85f));
                    string select = bestLevel > 1
                        ? $"◀  START AT LEVEL {selectedLevel}  ▶"
                        : $"{Levels.Count} LEVELS";
                    Label(new Rect(centre.x, centre.y + 180f * k, centre.width, 50f * k), select, midStyle, accent);
                    if (Mathf.Repeat(Time.time, 1f) < 0.7f)
                        Label(new Rect(centre.x, centre.y + 235f * k, centre.width, 60f * k), "Press SPACE or ENTER to start", midStyle, new Color(1f, 0.85f, 0.4f));
                    string sub = bestLevel > 1
                        ? $"Left/Right: choose a level (unlocked up to {bestLevel})   ·   Move with arrow keys or WASD"
                        : "Move with the arrow keys or WASD";
                    Label(new Rect(centre.x, centre.y + 290f * k, centre.width, 40f * k), sub, smallStyle, new Color(1f, 1f, 1f, 0.6f));
                    break;
                case State.Ready:
                    Label(centre, $"LEVEL {Level}\n<size={Mathf.RoundToInt(40 * k)}>GET READY</size>", bigStyle, accent);
                    break;
                case State.LevelClear:
                    Label(centre, Level >= Levels.Count ? "FINAL LEVEL CLEAR!" : "LEVEL CLEAR!", bigStyle, new Color(0.4f, 1f, 0.5f));
                    break;
                case State.GameOver:
                    Label(new Rect(centre.x, centre.y, centre.width, 120f * k), "GAME OVER", bigStyle, new Color(1f, 0.3f, 0.35f));
                    Label(new Rect(centre.x, centre.y + 120f * k, centre.width, 60f * k), $"Final score: {score}   ·   Reached level {Level}", midStyle, Color.white);
                    if (Mathf.Repeat(Time.time, 1f) < 0.7f)
                        Label(new Rect(centre.x, centre.y + 180f * k, centre.width, 60f * k), "Press SPACE to continue", midStyle, new Color(1f, 0.85f, 0.4f));
                    break;
                case State.Victory:
                    Label(new Rect(centre.x, centre.y, centre.width, 120f * k), "YOU WIN!", bigStyle, new Color(0.4f, 1f, 0.5f));
                    Label(new Rect(centre.x, centre.y + 110f * k, centre.width, 60f * k), $"All {Levels.Count} levels cleared", midStyle, new Color(0.3f, 0.95f, 1f));
                    Label(new Rect(centre.x, centre.y + 160f * k, centre.width, 60f * k), $"Final score: {score}", midStyle, Color.white);
                    if (Mathf.Repeat(Time.time, 1f) < 0.7f)
                        Label(new Rect(centre.x, centre.y + 230f * k, centre.width, 60f * k), "Press SPACE to continue", midStyle, new Color(1f, 0.85f, 0.4f));
                    break;
            }

            if (paused) Label(centre, "PAUSED", bigStyle, Color.white);

            if (popupTimer > 0f && Camera.main != null)
            {
                var sp = Camera.main.WorldToScreenPoint(popupWorld + Vector3.up * (1f - popupTimer));
                if (sp.z > 0f)
                {
                    var r = new Rect(sp.x - 100f * k, Screen.height - sp.y - 20f * k, 200f * k, 40f * k);
                    Label(r, popupText, midStyle, new Color(0.5f, 0.8f, 1f, Mathf.Clamp01(popupTimer * 2f)));
                }
            }
        }

        void EnsureStyles(float k)
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Normal };
                bigStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                midStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                smallStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperCenter };
            }
            hudStyle.fontSize = Mathf.RoundToInt(22 * k);
            bigStyle.fontSize = Mathf.RoundToInt(76 * k);
            midStyle.fontSize = Mathf.RoundToInt(30 * k);
            smallStyle.fontSize = Mathf.RoundToInt(20 * k);
        }

        static void Label(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }
    }
}
