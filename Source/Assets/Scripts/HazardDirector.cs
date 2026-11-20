using UnityEngine;

namespace HandsOff
{
    public sealed class HazardDirector : MonoBehaviour
    {
        public enum Hit { None, Beam, Mine }

        [SerializeField, Min(0.1f)] private float firstWarningDelay = 5f;
        [SerializeField, Min(0.1f)] private float warningSeconds = 0.9f;
        [SerializeField, Min(0.1f)] private float activeSeconds = 0.55f;
        [SerializeField, Min(0.3f)] private float sweepWidth = 1.65f;
        [SerializeField, Min(0.1f)] private float quietSeconds = 2.4f;
        [SerializeField, Min(1)] private int mineUnlockScore = 5;
        [SerializeField, Min(0.5f)] private float mineInterval = 3.8f;

        private const float BeamThickness = 0.092f;
        private const float MineRadius = 0.25f;
        private const float MineArmTime = 0.48f;
        private enum Stage { Quiet, Warning, Active }
        private sealed class Mine
        {
            public Transform Art;
            public Vector2 Position, Velocity, LastRelative, LastBotRelative;
            public float Age, NextSwat;
            public bool Active;
        }

        private Stage stage;
        private Arena2D arena;
        private GameView view;
        private SpriteRenderer lane, beam, beamEdge;
        private readonly SpriteRenderer[] dashes = new SpriteRenderer[18];
        private readonly Mine[] mines = new Mine[3];
        private float elapsed, delay, start, end, lastRelative, lastBotRelative;
        private float nextMine;
        private int hitMine = -1;
        private int botHitMine = -1;
        private bool vertical;

        public void Initialize(Arena2D playfield, GameView gameView)
        {
            arena = playfield;
            view = gameView;
            lane = view.MakeSprite("Sweep lane", GameView.Shape.Solid, GameView.Tint(GameView.Crimson, 0.095f), 0, transform);
            beamEdge = view.MakeSprite("Live beam ink", GameView.Shape.Solid, GameView.Ink, 4, transform);
            beam = view.MakeSprite("Live beam", GameView.Shape.Solid, GameView.Crimson, 5, transform);
            for (int i = 0; i < dashes.Length; i++)
                dashes[i] = view.MakeSprite("Warning dash", GameView.Shape.Solid, GameView.Crimson, 2, transform);
            for (int i = 0; i < mines.Length; i++)
            {
                var rim = view.MakeSprite("Ink mine", GameView.Shape.SpikedDiamond, GameView.Ink, 3, transform);
                var face = view.MakeSprite("Crimson mine", GameView.Shape.SpikedDiamond, GameView.Crimson, 4, rim.transform);
                face.transform.localScale = Vector3.one * 0.76f;
                var center = view.MakeSprite("Mine center", GameView.Shape.Diamond, GameView.Ink, 5, rim.transform);
                center.transform.localScale = Vector3.one * 0.25f;
                mines[i] = new Mine { Art = rim.transform };
            }
            ResetCycle();
        }

        public void Clear()
        {
            ClearBeam();
            foreach (var mine in mines) Despawn(mine);
            hitMine = -1;
            botHitMine = -1;
            nextMine = 0f;
        }

        private void ClearBeam()
        {
            lane.enabled = beam.enabled = beamEdge.enabled = false;
            foreach (var dash in dashes) dash.enabled = false;
        }

        public void ResetCycle(bool racing = false)
        {
            Clear();
            stage = Stage.Quiet;
            elapsed = 0f;
            delay = firstWarningDelay;
            if (racing) nextMine = 2.1f;
            vertical = false;
        }

        private void PositionStrip(Transform strip, float across, float along, float thickness, float length)
        {
            strip.position = vertical ? new Vector2(across, along) : new Vector2(along, across);
            strip.localScale = vertical ? new Vector3(thickness, length, 1f) : new Vector3(length, thickness, 1f);
        }

        private void BeginWarning(Vector2 target)
        {
            stage = Stage.Warning;
            elapsed = 0f;
            vertical = !vertical;
            Rect bounds = arena.Bounds;
            float half = sweepWidth * 0.5f;
            float min = vertical ? bounds.xMin : bounds.yMin;
            float max = vertical ? bounds.xMax : bounds.yMax;
            float middle = Mathf.Clamp(Axis(target), min + half, max - half);
            float direction = Random.value < 0.5f ? -1f : 1f;
            start = middle - direction * half;
            end = middle + direction * half;
            float span = vertical ? bounds.height : bounds.width;
            PositionStrip(lane.transform, middle, vertical ? bounds.center.y : bounds.center.x, sweepWidth, span);
            lane.enabled = true;
            DrawWarning(0f);
        }

        private void DrawWarning(float fraction)
        {
            Rect bounds = arena.Bounds;
            float span = vertical ? bounds.height : bounds.width;
            float low = vertical ? bounds.yMin : bounds.xMin;
            float step = span / dashes.Length;
            // Preview the exact sweep twice so its direction is readable before it arms.
            float across = Mathf.Lerp(start, end, Mathf.Repeat(fraction * 2f, 1f));
            for (int i = 0; i < dashes.Length; i++)
            {
                var dash = dashes[i];
                dash.enabled = true;
                PositionStrip(dash.transform, across, low + step * (i + 0.5f), 0.035f, step * 0.54f);
            }
        }

        private float Axis(Vector2 point) => vertical ? point.x : point.y;

        public Hit Step(float dt, int score)
        {
            bool beamHit = StepBeam(dt, null, out _);
            UpdateMines(dt, score >= mineUnlockScore, null);
            return beamHit ? Hit.Beam : hitMine >= 0 ? Hit.Mine : Hit.None;
        }

        internal void StepRace(float dt, Arena2D rival, bool laser, out Hit player, out Hit bot)
        {
            bool botBeam = false;
            bool playerBeam = laser && StepBeam(dt, rival, out botBeam);
            UpdateMines(dt, true, rival);
            player = playerBeam ? Hit.Beam : hitMine >= 0 ? Hit.Mine : Hit.None;
            bot = botBeam ? Hit.Beam : botHitMine >= 0 ? Hit.Mine : Hit.None;
        }

        private bool StepBeam(float dt, Arena2D rival, out bool botHit)
        {
            botHit = false;
            elapsed += dt;
            if (stage == Stage.Quiet)
            {
                if (elapsed >= delay) BeginWarning(rival == null ? arena.Body.position : arena.NodePosition);
                return false;
            }
            if (stage == Stage.Warning)
            {
                if (elapsed < warningSeconds)
                {
                    DrawWarning(elapsed / warningSeconds);
                    return false;
                }
                foreach (var dash in dashes) dash.enabled = false;
                beam.enabled = beamEdge.enabled = true;
                stage = Stage.Active;
                elapsed = 0f;
                lastRelative = Axis(arena.Body.position) - start;
                if (rival != null) lastBotRelative = Axis(rival.Body.position) - start;
            }

            Rect bounds = arena.Bounds;
            float current = Mathf.Lerp(start, end, elapsed / activeSeconds);
            PositionStrip(beamEdge.transform, current, vertical ? bounds.center.y : bounds.center.x,
                BeamThickness + 0.055f, vertical ? bounds.height : bounds.width);
            PositionStrip(beam.transform, current, vertical ? bounds.center.y : bounds.center.x,
                BeamThickness, vertical ? bounds.height : bounds.width);
            bool hit = CrossedBeam(arena, current, ref lastRelative);
            if (rival != null) botHit = CrossedBeam(rival, current, ref lastBotRelative);
            if (elapsed >= activeSeconds)
            {
                ClearBeam();
                stage = Stage.Quiet;
                elapsed = 0f;
                delay = quietSeconds;
            }
            return hit;
        }

        private bool CrossedBeam(Arena2D racer, float current, ref float previous)
        {
            float relative = Axis(racer.Body.position) - current;
            float hitWidth = racer.CoreRadius * 0.85f + BeamThickness * 0.5f;
            bool hit = Mathf.Abs(relative) <= hitWidth || relative * previous < 0f;
            previous = relative;
            return hit;
        }

        public Vector2 LookTarget(Vector2 core, Vector2 target)
        {
            float nearest = (target - core).sqrMagnitude;
            foreach (var mine in mines)
            {
                if (!mine.Active) continue;
                float distance = (mine.Position - core).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                target = mine.Position;
            }
            if (stage != Stage.Quiet)
            {
                float axis = stage == Stage.Active ? (vertical ? beam.transform.position.x : beam.transform.position.y)
                    : (start + end) * 0.5f;
                Vector2 beamTarget = vertical ? new Vector2(axis, core.y) : new Vector2(core.x, axis);
                if ((beamTarget - core).sqrMagnitude < nearest) target = beamTarget;
            }
            return target;
        }

        private void UpdateMines(float dt, bool unlocked, Arena2D rival)
        {
            hitMine = botHitMine = -1;
            Vector2 core = arena.Body.position;
            int free = -1;
            for (int i = 0; i < mines.Length; i++)
            {
                Mine mine = mines[i];
                if (mine.Active && (mine.Age += dt) >= 13f) Despawn(mine);
                if (!mine.Active)
                {
                    if (free < 0) free = i;
                    continue;
                }

                float armed = Mathf.Clamp01(mine.Age / MineArmTime);
                float size = Mathf.Lerp(0.34f, 1f, 1f - (1f - armed) * (1f - armed));
                mine.Art.localScale = Vector3.one * (MineRadius * 2f * size);
                Vector2 relative = mine.Position - core;
                if (hitMine < 0 && armed >= 1f
                    && Arena2D.DistanceToSegment(Vector2.zero, mine.LastRelative, relative) <= arena.CoreRadius * 0.85f + MineRadius)
                    hitMine = i;
                mine.LastRelative = relative;
                if (rival != null)
                {
                    Vector2 botRelative = mine.Position - rival.Body.position;
                    if (botHitMine < 0 && armed >= 1f && Arena2D.DistanceToSegment(Vector2.zero,
                        mine.LastBotRelative, botRelative) <= rival.CoreRadius * 0.85f + MineRadius)
                        botHitMine = i;
                    mine.LastBotRelative = botRelative;
                }
            }
            nextMine -= dt;
            if (unlocked && free >= 0 && nextMine <= 0f)
                nextMine = TrySpawn(mines[free], core, rival) ? mineInterval : 0.45f;
        }

        private bool TrySpawn(Mine mine, Vector2 core, Arena2D rival)
        {
            Rect bounds = arena.Bounds;
            const float pad = 0.58f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int edge = Random.Range(0, 4);
                float x = edge < 2 ? (edge == 0 ? bounds.xMin + pad : bounds.xMax - pad)
                    : Random.Range(bounds.xMin + pad, bounds.xMax - pad);
                float y = edge >= 2 ? (edge == 2 ? bounds.yMin + pad : bounds.yMax - pad)
                    : Random.Range(bounds.yMin + pad, bounds.yMax - pad);
                var position = new Vector2(x, y);
                if ((position - core).sqrMagnitude < 8.41f || (position - arena.NodePosition).sqrMagnitude < 0.81f)
                    continue;
                if (rival != null && (position - rival.Body.position).sqrMagnitude < 8.41f)
                    continue;
                bool crowded = false;
                foreach (var other in mines)
                    if (other.Active && (position - other.Position).sqrMagnitude < 0.81f) crowded = true;
                if (crowded) continue;

                mine.Active = true;
                mine.Age = 0f;
                mine.NextSwat = 0f;
                mine.Position = position;
                mine.LastRelative = position - core;
                if (rival != null) mine.LastBotRelative = position - rival.Body.position;
                mine.Velocity = Vector2.zero;
                mine.Art.position = position;
                mine.Art.localScale = Vector3.one * (MineRadius * 2f * 0.34f);
                mine.Art.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        public void MoveMines(float dt, Arena2D rival = null)
        {
            Vector2 core = arena.Body.position;
            Rect bounds = arena.Bounds;
            foreach (var mine in mines)
            {
                if (!mine.Active) continue;
                Vector2 target = core;
                if (rival != null && (rival.Body.position - mine.Position).sqrMagnitude < (core - mine.Position).sqrMagnitude)
                    target = rival.Body.position;
                Vector2 pursuit = (target - mine.Position).normalized * 0.80f;
                mine.Velocity = Vector2.Lerp(mine.Velocity, pursuit, 1f - Mathf.Exp(-1.55f * dt));
                Vector2 next = mine.Position + mine.Velocity * dt;
                float x = Mathf.Clamp(next.x, bounds.xMin + MineRadius, bounds.xMax - MineRadius);
                float y = Mathf.Clamp(next.y, bounds.yMin + MineRadius, bounds.yMax - MineRadius);
                if (x != next.x) mine.Velocity.x *= -0.40f;
                if (y != next.y) mine.Velocity.y *= -0.40f;
                mine.Position = new Vector2(x, y);
                mine.Art.position = mine.Position;
            }
        }

        public void RepelMines(Vector2 origin, float strength, float reach, ForceMode2D mode)
        {
            foreach (var mine in mines)
            {
                if (!mine.Active) continue;
                Vector2 force = RepulsionInput.RepulsionForce(mine.Position - origin, strength, reach);
                if (force == Vector2.zero) continue;
                mine.Velocity += force * (mode == ForceMode2D.Impulse ? 1f : Time.fixedDeltaTime);
                mine.Velocity = Vector2.ClampMagnitude(mine.Velocity, 6.5f);
                if (Time.time < mine.NextSwat || Vector2.Dot(mine.Velocity, (mine.Position - arena.Body.position).normalized) < 1.5f)
                    continue;
                mine.NextSwat = Time.time + 0.60f;
                view.TryBark(GameView.Bark.Swat, arena.VisibleCorePosition);
            }
        }

        public void ConsumeHitMine()
        {
            if (hitMine >= 0) Despawn(mines[hitMine]);
            hitMine = -1;
        }

        internal void ConsumeBotMine()
        {
            if (botHitMine >= 0) Despawn(mines[botHitMine]);
            botHitMine = -1;
        }

        private static void Despawn(Mine mine)
        {
            mine.Active = false;
            mine.Art.gameObject.SetActive(false);
        }
    }
}
