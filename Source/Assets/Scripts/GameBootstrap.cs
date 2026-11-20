using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HandsOff
{
    public enum GameState { Title, Playing, GameOver, Paused }

    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float comboWindow = 3f;
        [SerializeField, Min(1)] private int maximumCombo = 4;

        private const string BestKey = "Thumbgrudge.Best";
        private const string LookKey = "Thumbgrudge.Look";
        private const float NerveSeconds = 0.85f;
        private const float ShieldImmunity = 0.60f;
        private const float HitStopSeconds = 0.03f;
        private const float DeathPoseSeconds = 0.085f;
        private const float RetryCardDelay = 0.24f;
        private const string Touched = "YOU TOUCHED IT";
        private const string BeamHit = "BEAM HIT";
        private const string MineHit = "MINE HIT";
        private Arena2D arena;
        private BotRace race;
        private bool racing;
        private BotRace.Difficulty raceDifficulty;
        private float playerProtectedUntil, botProtectedUntil;
        private bool RaceHasMines => raceDifficulty == BotRace.Difficulty.Hard ||
            raceDifficulty == BotRace.Difficulty.Impossible;
        private RepulsionInput controls;
        private HazardDirector hazards;
        private GameView view;
        private Camera worldCamera;
        private Rect safeArea;
        private Vector2Int resolution;
        private int score, best, combo;
        private int lookIndex;
        private bool bestDirty;
        private float comboUntil;
        private float nerveElapsed, immuneUntil;
        private bool nerveHeld, shielded;
        private bool hitStop;
        private float resumeTime, previousTimeScale;
        private float timeBeforePause;
        private string deathReason;
        private float deathTime;
        private bool shattered;

        public GameState State { get; private set; }
        public int NodesCollected { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "SampleScene") return;
            foreach (var root in scene.GetRootGameObjects())
                if (root.activeInHierarchy && root.GetComponentInChildren<GameBootstrap>() != null) return;
            new GameObject("THUMBGRUDGE").AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            worldCamera = Camera.main;
            if (worldCamera == null)
            {
                worldCamera = new GameObject("Main Camera").AddComponent<Camera>();
                worldCamera.tag = "MainCamera";
                worldCamera.transform.position = Vector3.back * 10f;
            }

            Application.targetFrameRate = 60;
            worldCamera.orthographic = true;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = GameView.Paper;
            best = Mathf.Max(0, PlayerPrefs.GetInt(BestKey, 0));
            int savedLook = PlayerPrefs.GetInt(LookKey, 0);
            lookIndex = CanEquip(savedLook) ? savedLook : 0;

            view = gameObject.AddComponent<GameView>();
            view.Initialize(StartRun, StartRace, ReturnToTitle, PauseRun, ResumeRun, EquipLook, worldCamera);
            var creature = new GameObject("Core");
            creature.transform.SetParent(transform, false);
            arena = creature.AddComponent<Arena2D>();
            arena.Initialize(view, transform);
            race = new BotRace(arena, view, transform);
            hazards = gameObject.AddComponent<HazardDirector>();
            hazards.Initialize(arena, view);
            controls = gameObject.AddComponent<RepulsionInput>();
            controls.Initialize(arena, view, worldCamera, hazards);
            ApplyLook();
            RefreshLayout();
            arena.PrepareRun(false);
            view.ShowTitle();
            view.ShowFirstLaunchGuide();
        }

        private void RefreshLayout()
        {
            resolution = new Vector2Int(Screen.width, Screen.height);
            safeArea = Screen.safeArea;
            Rect safe = safeArea;
            if (safe.width < 1f || safe.height < 1f)
                safe = new Rect(0, 0, Mathf.Max(1, resolution.x), Mathf.Max(1, resolution.y));

            // Keep the same portrait composition in a wide Editor Game view.
            float width = Mathf.Min(safe.width, safe.height * 0.64f);
            var frame = new Rect(safe.center.x - width / 2f, safe.yMin, width, safe.height);
            worldCamera.orthographicSize = resolution.y * 0.5f * Mathf.Max(9f / width, 12f / safe.height);
            view.Layout(frame, resolution);
            const float gutter = 0.76f;
            Vector2 lo = (Vector2)worldCamera.ScreenToWorldPoint(frame.min) - view.CameraOffset;
            Vector2 hi = (Vector2)worldCamera.ScreenToWorldPoint(new Vector2(frame.xMax, frame.yMax - frame.height * GameView.HudFraction))
                - view.CameraOffset;
            arena.SetBounds(Rect.MinMaxRect(lo.x + gutter, lo.y + gutter, hi.x - gutter, hi.y - gutter));
            if (racing && (State == GameState.Playing || State == GameState.Paused)) race.Resize();
            hazards.ResetCycle(racing);
            controls.ResetGesture();
        }

        private void StartRun()
        {
            if (State == GameState.Playing) return;
            ReleaseHitStop();
            RestorePausedTime();
            State = GameState.Playing;
            NodesCollected = score = 0;
            combo = 1;
            comboUntil = float.NegativeInfinity;
            immuneUntil = float.NegativeInfinity;
            nerveElapsed = 0f;
            shielded = nerveHeld = false;
            deathReason = null;
            controls.ResetGesture();
            hazards.ResetCycle(racing);
            playerProtectedUntil = botProtectedUntil = float.NegativeInfinity;
            if (racing) race.Begin(lookIndex, raceDifficulty);
            else
            {
                race.Stop();
                arena.PrepareRun(true);
            }
            view.ShowPlaying(score, combo, best, racing ? race.Goal : 0);
        }

        private void StartRace(BotRace.Difficulty difficulty)
        {
            if (State != GameState.Title) return;
            raceDifficulty = difficulty;
            racing = true;
            StartRun();
        }

        private void ReturnToTitle()
        {
            ReleaseHitStop();
            RestorePausedTime();
            controls.ResetGesture();
            hazards.Clear();
            race.Stop();
            racing = false;
            deathReason = null;
            State = GameState.Title;
            arena.PrepareRun(false);
            view.ShowTitle();
        }

        private void PauseRun()
        {
            if (State != GameState.Playing) return;
            ReleaseHitStop();
            timeBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            State = GameState.Paused;
            controls.ResetGesture();
            nerveElapsed = 0f;
            nerveHeld = false;
            SaveBest();
            view.ShowPause();
        }

        private void ResumeRun()
        {
            if (State != GameState.Paused) return;
            RestorePausedTime();
            State = GameState.Playing;
            controls.ResetGesture();
            view.ResumePlaying();
        }

        private void RestorePausedTime()
        {
            if (State == GameState.Paused) Time.timeScale = timeBeforePause;
        }

        private void LateUpdate()
        {
            if (hitStop && Time.unscaledTime >= resumeTime) ReleaseHitStop();
            if (Screen.width != resolution.x || Screen.height != resolution.y || Screen.safeArea != safeArea)
                RefreshLayout();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (!view.CloseMenu() && State == GameState.Playing) PauseRun();
            }
            if (State == GameState.Playing)
            {
                if (racing) StepRace(Time.deltaTime);
                else StepRun(Time.deltaTime);
            }
            else if (State == GameState.GameOver) StepDeath();
            else if (State == GameState.Title) arena.DrawCreature(Time.unscaledDeltaTime, arena.NodePosition, false, 0f, false, false);
            view.Tick(Time.unscaledDeltaTime);
        }

        private void StepRace(float dt)
        {
            if (controls.Sample()) ResetRacePlayer();
            if (dt > 0f && RaceHasMines) StepRaceHazards(dt);
            bool held = controls.TryGetHeldPointer(out Vector2 pointer);
            Vector2 lookAt = held ? pointer : arena.NodePosition;
            bool near = held &&
                Vector2.Distance(lookAt, arena.VisibleCorePosition) <= arena.CoreRadius * 1.65f;
            arena.DrawCreature(Time.unscaledDeltaTime, lookAt, near, 0f, false, false);
            race.Draw(Time.unscaledDeltaTime);
            if (dt <= 0f || !race.TryScore(out bool playerScored)) return;

            if (playerScored) view.PlaySound(GameView.Sound.Pickup);
            view.SetRaceScore(race.PlayerPoints, race.BotPoints, race.Goal);
            if (race.PlayerPoints < race.Goal && race.BotPoints < race.Goal) return;
            State = GameState.GameOver;
            controls.ResetGesture();
            arena.Freeze();
            race.Stop();
            hazards.Clear();
            view.ShowRaceResult(race.PlayerPoints, race.BotPoints, race.Goal, raceDifficulty);
        }

        private void StepRaceHazards(float dt)
        {
            hazards.StepRace(dt, race.Rival, raceDifficulty == BotRace.Difficulty.Impossible,
                out HazardDirector.Hit playerHit, out HazardDirector.Hit botHit);
            if (playerHit != HazardDirector.Hit.None && Time.time >= playerProtectedUntil)
            {
                if (playerHit == HazardDirector.Hit.Mine) hazards.ConsumeHitMine();
                ResetRacePlayer();
            }
            if (botHit != HazardDirector.Hit.None && Time.time >= botProtectedUntil)
            {
                if (botHit == HazardDirector.Hit.Mine) hazards.ConsumeBotMine();
                view.Pulse(race.Rival.VisibleCorePosition, GameView.BlueInk, 0.40f, 1.35f);
                race.ResetBot();
                botProtectedUntil = Time.time + 0.75f;
            }
        }

        private void ResetRacePlayer()
        {
            view.Shatter(arena.VisibleCorePosition, GameView.Ink);
            view.PlaySound(GameView.Sound.Shatter);
            race.ResetPlayer();
            controls.ResetGesture();
            playerProtectedUntil = Time.time + 0.75f;
        }

        private void StepRun(float dt)
        {
            // UI has processed this frame's events. Direct contact wins all simultaneous hits.
            if (controls.Sample())
            {
                EndRun(Touched);
                return;
            }

            // Continue checking fingers during hit-stop, including shield-break i-frames.
            if (dt > 0f && !ResolveHazard(hazards.Step(dt, score))) return;
            bool scoreChanged = false;
            if (dt > 0f && arena.TryCollect())
            {
                NodesCollected++;
                combo = Time.time <= comboUntil ? Mathf.Min(maximumCombo, combo + 1) : 1;
                score += combo;
                comboUntil = Time.time + comboWindow;
                view.PlaySound(GameView.Sound.Pickup, 1f + (combo - 1) * 0.11f);
                if (NodesCollected % 5 == 0 && !shielded)
                {
                    shielded = true;
                    view.Pulse(arena.VisibleCorePosition, GameView.BlueInk, 0.46f, 0.93f);
                }
                scoreChanged = true;
            }
            else if (combo > 1 && Time.time > comboUntil)
            {
                combo = 1;
                scoreChanged = true;
            }

            Vector2 core = arena.VisibleCorePosition;
            bool held = controls.TryGetHeldPointer(out Vector2 pointer);
            float distance = held ? Vector2.Distance(pointer, core) / arena.CoreRadius : float.PositiveInfinity;
            bool near = distance <= 1.65f;
            bool earningNerve = near && distance >= 1f;
            if (!earningNerve || !nerveHeld || controls.NewGestureThisFrame) nerveElapsed = 0f;
            else nerveElapsed += dt;
            nerveHeld = earningNerve;
            while (nerveElapsed >= NerveSeconds)
            {
                nerveElapsed -= NerveSeconds;
                score++;
                comboUntil = Time.time + comboWindow;
                view.ShowNerveBonus();
                scoreChanged = true;
            }
            if (near) view.TryBark(GameView.Bark.Near, core);
            Vector2 target = held ? pointer : hazards.LookTarget(core, arena.NodePosition);
            float nerveProgress = earningNerve ? Mathf.Max(0.01f, nerveElapsed / NerveSeconds) : 0f;
            arena.DrawCreature(Time.unscaledDeltaTime, target, near, nerveProgress, shielded, Time.time < immuneUntil);
            if (scoreChanged)
            {
                UpdateBest();
                view.SetScore(score, combo, best);
            }
            view.SetComboTime(Mathf.Clamp01((comboUntil - Time.time) / comboWindow));
        }

        private bool ResolveHazard(HazardDirector.Hit hit)
        {
            if (hit == HazardDirector.Hit.None || Time.time < immuneUntil) return true;
            if (!shielded)
            {
                EndRun(hit == HazardDirector.Hit.Mine ? MineHit : BeamHit);
                return false;
            }
            shielded = false;
            immuneUntil = Time.time + ShieldImmunity;
            if (hit == HazardDirector.Hit.Mine) hazards.ConsumeHitMine();
            view.Shatter(arena.VisibleCorePosition, GameView.BlueInk);
            view.PlaySound(GameView.Sound.Shield);
            Impact();
            return true;
        }

        private void FixedUpdate()
        {
            if (State != GameState.Playing) return;
            controls.ApplyPhysics();
            if (racing)
            {
                race.Drive(Time.fixedDeltaTime);
                if (RaceHasMines) hazards.MoveMines(Time.fixedDeltaTime, race.Rival);
            }
            else hazards.MoveMines(Time.fixedDeltaTime);
        }

        private void EndRun(string reason)
        {
            State = GameState.GameOver;
            deathReason = reason;
            deathTime = Time.unscaledTime;
            shattered = false;
            controls.ResetGesture();
            arena.StopRun();
            hazards.Clear();
            view.ClearBark();
            view.PlaySound(GameView.Sound.Shatter);
            Impact();
            SaveBest();
        }

        private bool CanEquip(int index) =>
            index >= 0 && index < Arena2D.Looks.Length && best >= Arena2D.Looks[index].RequiredBest;

        private void EquipLook(int index)
        {
            if (!CanEquip(index) || index == lookIndex) return;
            lookIndex = index;
            PlayerPrefs.SetInt(LookKey, lookIndex);
            PlayerPrefs.Save();
            bestDirty = false;
            ApplyLook();
        }

        private void ApplyLook()
        {
            arena.SetLook(lookIndex);
            race.SetLook(lookIndex);
            view.SetLooks(best, lookIndex);
        }

        private void UpdateBest()
        {
            if (score <= best) return;
            int oldBest = best;
            best = score;
            bestDirty = true;
            PlayerPrefs.SetInt(BestKey, best);
            view.SetLooks(best, lookIndex);
            for (int i = 1; i < Arena2D.Looks.Length; i++)
            {
                var look = Arena2D.Looks[i];
                if (oldBest < look.RequiredBest && best >= look.RequiredBest)
                    view.ShowLookUnlocked(look.Name);
            }
        }

        private void SaveBest()
        {
            if (!bestDirty) return;
            PlayerPrefs.Save();
            bestDirty = false;
        }

        private void StepDeath()
        {
            if (deathReason == null) return;
            float age = Time.unscaledTime - deathTime;
            if (!shattered && age >= DeathPoseSeconds)
            {
                shattered = true;
                view.Shatter(arena.VisibleCorePosition, GameView.Ink);
                arena.HideCreature();
            }
            if (age < RetryCardDelay) return;
            view.ShowGameOver(deathReason, score, best);
            deathReason = null;
        }

        private void Impact()
        {
            if (!hitStop)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                hitStop = true;
            }
            resumeTime = Time.unscaledTime + HitStopSeconds;
            view.Shake();
        }

        private void ReleaseHitStop()
        {
            if (!hitStop) return;
            hitStop = false;
            Time.timeScale = previousTimeScale;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused || controls == null) return;
            PauseRun();
            SaveBest();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused || controls == null) return;
            PauseRun();
            SaveBest();
        }

        private void OnApplicationQuit() => SaveBest();

        private void OnDisable()
        {
            ReleaseHitStop();
            RestorePausedTime();
        }
    }
}
