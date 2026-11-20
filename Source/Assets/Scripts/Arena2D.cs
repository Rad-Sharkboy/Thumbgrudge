using UnityEngine;

namespace HandsOff
{
    public sealed class Arena2D : MonoBehaviour
    {
        internal readonly struct Look
        {
            public readonly string Name;
            public readonly int RequiredBest;
            public readonly Color Body;
            public readonly Color Accent;
            public readonly string Perk;
            public readonly float Speed, Bounce, Pickup;

            public Look(string name, int requiredBest, Color body, Color accent, string perk,
                float speed = 1f, float bounce = 0f, float pickup = 1f)
            {
                Name = name;
                RequiredBest = requiredBest;
                Body = body;
                Accent = accent;
                Perk = perk;
                Speed = speed;
                Bounce = bounce;
                Pickup = pickup;
            }
        }

        internal static readonly Look[] Looks =
        {
            new Look("PAPER", 0, Color.white, new Color32(237, 148, 151, 255), "STANDARD"),
            new Look("MINT", 8, new Color32(183, 232, 204, 255), new Color32(47, 141, 109, 255), "+15% SPEED", speed: 1.15f),
            new Look("PEACH", 20, new Color32(255, 203, 169, 255), new Color32(224, 100, 83, 255), "+0.10 BOUNCE", bounce: 0.10f),
            new Look("TARO", 40, new Color32(217, 203, 246, 255), new Color32(132, 98, 194, 255), "+20% PICKUP REACH", pickup: 1.20f)
        };

        [SerializeField, Min(0.05f)] private float coreRadius = 0.32f;
        [SerializeField, Min(0f)] private float linearDamping = 0.35f;
        [SerializeField, Range(0f, 1f)] private float bounciness = 0.88f;
        [SerializeField, Min(0.1f)] private float nodeRadius = 0.24f;
        [SerializeField, Min(1f)] private float pickupScale = 1.15f;
        [SerializeField, Min(0f)] private float nodeWallClearance = 0.75f;
        [SerializeField, Min(0f)] private float nodeCoreClearance = 1.5f;

        private readonly BoxCollider2D[] walls = new BoxCollider2D[4];
        private readonly Transform[] rails = new Transform[4];
        private readonly Transform[] cornerTicks = new Transform[8];
        private readonly Transform[] eyes = new Transform[2];
        private readonly Transform[] pupils = new Transform[2];
        private readonly Transform[] brows = new Transform[2];
        private readonly Transform[] crossedEyes = new Transform[2];
        private readonly SpriteRenderer[] cheeks = new SpriteRenderer[2];
        private GameView view;
        private PhysicsMaterial2D bankMaterial;
        private Transform art;
        private SpriteRenderer fill, mouth, shieldRing, nerveRing;
        private TrailRenderer trail;
        private Transform node;
        private Transform pickupPop;
        private Vector2 lastSample;
        private bool positioned;
        private Vector2 lookOffset, impactScale = Vector2.one;
        private float panicAmount, impact, lastBankTime, pickupPopTime;
        private int lookIndex;
        private bool isBot;

        public Rect Bounds { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public CircleCollider2D CoreCollider { get; private set; }
        public float CoreRadius => coreRadius;
        public float SpeedMultiplier => Looks[lookIndex].Speed;
        internal float PickupReach => (coreRadius + nodeRadius * pickupScale) * Looks[lookIndex].Pickup;
        internal static readonly Color32 BotColor = new Color32(132, 193, 241, 255);
        private Color BodyColor => isBot ? (Color)BotColor : Looks[lookIndex].Body;
        public Vector2 VisibleCorePosition => transform.position;
        public Vector2 NodePosition => node.position;

        public void Initialize(GameView gameView, Transform arenaRoot)
        {
            InitializeCore(gameView, false);
            node = MakeNode("Energy node", arenaRoot);
            pickupPop = MakeNode("Collected node", arenaRoot);
            pickupPop.gameObject.SetActive(false);

            for (int i = 0; i < walls.Length; i++)
            {
                var wall = new GameObject("Bank " + (i + 1)).AddComponent<BoxCollider2D>();
                wall.transform.SetParent(arenaRoot, false);
                wall.sharedMaterial = bankMaterial;
                walls[i] = wall;
                rails[i] = view.MakeSprite("Rail", GameView.Shape.Solid, GameView.Ink, 1, wall.transform).transform;
            }
            for (int i = 0; i < cornerTicks.Length; i++)
                cornerTicks[i] = view.MakeSprite("Inner corner tick", GameView.Shape.Solid,
                    GameView.Tint(GameView.Ink, 0.36f), 1, arenaRoot).transform;
        }

        internal void InitializeCore(GameView gameView, bool bot)
        {
            view = gameView;
            isBot = bot;
            bankMaterial = new PhysicsMaterial2D("Bank surface") { friction = 0f, bounciness = bounciness };
            transform.localScale = Vector3.one * (coreRadius * 2f);
            CoreCollider = gameObject.AddComponent<CircleCollider2D>();
            CoreCollider.radius = 0.5f;
            CoreCollider.sharedMaterial = bankMaterial;
            Body = gameObject.AddComponent<Rigidbody2D>();
            Body.gravityScale = 0f;
            Body.linearDamping = linearDamping;
            Body.freezeRotation = true;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.simulated = false;

            BuildCreature();
        }

        private void BuildCreature()
        {
            art = new GameObject("Creature drawing").transform;
            art.SetParent(transform, false);
            var shell = Feature("Ink silhouette", GameView.Shape.Blob, GameView.Ink, 6, art,
                Vector2.zero, Vector2.one);
            fill = Feature("Face", GameView.Shape.Blob, GameView.White, 7, art, Vector2.zero, Vector2.one * 0.881f);
            if (isBot)
                Feature("Bot antenna", GameView.Shape.Diamond, GameView.BlueInk, 8, art,
                    new Vector2(0f, 0.52f), Vector2.one * 0.20f);
            mouth = Feature("Mouth", GameView.Shape.Bar, GameView.Ink, 10, art,
                new Vector2(0f, -0.185f), new Vector2(0.142f, 0.037f));

            for (int i = 0; i < eyes.Length; i++)
            {
                float x = i == 0 ? -0.158f : 0.158f;
                eyes[i] = Feature("Eye rim", GameView.Shape.Circle, GameView.Ink, 8, art,
                    new Vector2(x, 0.035f), new Vector2(0.284f, 0.252f)).transform;
                Feature("Eye white", GameView.Shape.Circle, GameView.White, 9, eyes[i], Vector2.zero, Vector2.one * 0.76f);
                pupils[i] = Feature("Pupil", GameView.Shape.Circle, GameView.Ink, 10, eyes[i],
                    Vector2.zero, Vector2.one * 0.40f).transform;
                Feature("Eye shine", GameView.Shape.Circle, GameView.White, 11, pupils[i],
                    new Vector2(-0.15f, 0.17f), Vector2.one * 0.23f);
                cheeks[i] = Feature("Cheek", GameView.Shape.Circle, GameView.Tint(Looks[0].Accent, 0.48f),
                    8, art, new Vector2(x * 1.69f, -0.119f), new Vector2(0.122f, 0.073f));
                brows[i] = Feature("Eyebrow", GameView.Shape.Bar, GameView.Ink, 10, art,
                    new Vector2(x, 0.221f), new Vector2(0.216f, 0.045f)).transform;
                crossedEyes[i] = new GameObject("Crossed eye").transform;
                crossedEyes[i].SetParent(art, false);
                crossedEyes[i].localPosition = new Vector2(x, 0.052f);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var stroke = Feature("Cross stroke", GameView.Shape.Bar, GameView.Ink, 10, crossedEyes[i],
                        Vector2.zero, new Vector2(0.215f, 0.052f));
                    stroke.transform.localRotation = Quaternion.Euler(0f, 0f, 45f * sign);
                }
                crossedEyes[i].gameObject.SetActive(false);
            }
            shieldRing = Feature("Glass bubble", GameView.Shape.Ring, GameView.BlueInk, 11, transform,
                Vector2.zero, Vector2.one * 1.27f);
            nerveRing = Feature("Nerve boundary", GameView.Shape.DashedRing, GameView.Ink, 5, transform,
                Vector2.zero, Vector2.one * 1.65f);
            shieldRing.enabled = nerveRing.enabled = false;

            trail = new GameObject("Ink trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(transform, false);
            trail.sharedMaterial = shell.sharedMaterial;
            trail.sortingOrder = 2;
            trail.time = 0.17f;
            trail.startWidth = 0.24f;
            trail.endWidth = 0.015f;
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 2;
            trail.startColor = GameView.Tint(GameView.Ink, 0.38f);
            trail.endColor = GameView.Tint(GameView.White, 0f);
            trail.emitting = false;
            trail.enabled = trail.sharedMaterial != null;
        }

        public void SetLook(int index)
        {
            lookIndex = index;
            bankMaterial.bounciness = Mathf.Min(1f, bounciness + Looks[lookIndex].Bounce);
            if (art.gameObject.activeSelf) fill.color = BodyColor;
        }

        private SpriteRenderer Feature(string name, GameView.Shape shape, Color color, int order,
            Transform parent, Vector2 offset, Vector2 scale)
        {
            var feature = view.MakeSprite(name, shape, color, order, parent);
            feature.transform.localPosition = offset;
            feature.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            return feature;
        }

        private Transform MakeNode(string name, Transform parent)
        {
            var rim = Feature(name, GameView.Shape.Diamond, GameView.Ink, 3, parent,
                Vector2.zero, Vector2.one * nodeRadius * 2f);
            Feature("Amber", GameView.Shape.Diamond, GameView.Amber, 4, rim.transform, Vector2.zero, Vector2.one * 0.81f);
            return rim.transform;
        }

        public void SetBounds(Rect bounds)
        {
            Bounds = bounds;
            const float depth = 0.3f;
            Vector2 mid = bounds.center;
            for (int i = 0; i < walls.Length; i++)
            {
                bool vertical = i < 2;
                float sign = (i % 2 == 0) ? -1f : 1f;
                Vector2 axis = vertical ? Vector2.right : Vector2.up;
                float span = vertical ? bounds.height : bounds.width;
                float offset = vertical ? bounds.width : bounds.height;
                var size = vertical ? new Vector2(depth, span + depth * 2f) : new Vector2(span + depth * 2f, depth);
                walls[i].transform.position = mid + axis * (sign * (offset + depth) * 0.5f);
                walls[i].size = size;
                rails[i].localPosition = -axis * sign * depth * 0.5f;
                rails[i].localScale = vertical ? new Vector3(0.048f, span + 0.048f, 1f) : new Vector3(span + 0.048f, 0.048f, 1f);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? bounds.xMin : bounds.xMax;
                float y = i < 2 ? bounds.yMin : bounds.yMax;
                float sx = i % 2 == 0 ? 1f : -1f;
                float sy = i < 2 ? 1f : -1f;
                cornerTicks[i * 2].position = new Vector2(x + 0.27f * sx, y + 0.17f * sy);
                cornerTicks[i * 2].localScale = new Vector3(0.23f, 0.032f, 1f);
                cornerTicks[i * 2 + 1].position = new Vector2(x + 0.17f * sx, y + 0.27f * sy);
                cornerTicks[i * 2 + 1].localScale = new Vector3(0.032f, 0.23f, 1f);
            }
            if (positioned)
            {
                Body.position = new Vector2(Mathf.Clamp(Body.position.x, bounds.xMin + coreRadius, bounds.xMax - coreRadius),
                    Mathf.Clamp(Body.position.y, bounds.yMin + coreRadius, bounds.yMax - coreRadius));
                lastSample = Body.position;
                trail.Clear();
                SpawnNode();
            }
        }

        public void PrepareRun(bool playing, Vector2? initialPosition = null)
        {
            Body.simulated = false;
            art.gameObject.SetActive(true);
            if (node != null) node.gameObject.SetActive(true);
            fill.color = BodyColor;
            for (int i = 0; i < eyes.Length; i++)
            {
                eyes[i].gameObject.SetActive(true);
                brows[i].gameObject.SetActive(true);
                crossedEyes[i].gameObject.SetActive(false);
            }
            nerveRing.enabled = shieldRing.enabled = false;
            trail.emitting = false;
            trail.Clear();
            pickupPopTime = 0f;
            if (pickupPop != null) pickupPop.gameObject.SetActive(false);
            impact = panicAmount = 0f;
            lastBankTime = float.NegativeInfinity;
            lookOffset = Vector2.zero;
            art.localScale = Vector3.one;
            art.localPosition = Vector3.zero;
            Vector2 start = initialPosition ?? Bounds.center + Vector2.down * Bounds.height * 0.09f;
            PlaceCore(start);
            Body.simulated = playing;
            positioned = true;
            if (node != null && !initialPosition.HasValue) SpawnNode();
        }

        internal void PlaceCore(Vector2 position)
        {
            Body.transform.position = position;
            Body.position = position;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            lastSample = position;
            trail.Clear();
        }

        internal void Freeze()
        {
            Body.simulated = false;
            trail.emitting = false;
        }

        public void StopRun()
        {
            Freeze();
            trail.Clear();
            shieldRing.enabled = nerveRing.enabled = false;
            node.gameObject.SetActive(false);
            pickupPop.gameObject.SetActive(false);
            pickupPopTime = 0f;
            art.localPosition = Vector3.zero;
            art.localScale = new Vector3(1.07f, 0.94f, 1f);
            fill.color = Color.Lerp(Looks[lookIndex].Body, GameView.Blush, 0.78f);
            for (int i = 0; i < eyes.Length; i++)
            {
                crossedEyes[i].gameObject.SetActive(true);
                brows[i].gameObject.SetActive(false);
                eyes[i].gameObject.SetActive(false);
            }
            mouth.sprite = view.Art(GameView.Shape.OpenMouth);
            mouth.transform.localScale = new Vector3(0.108f, 0.142f, 1f);
        }

        public void HideCreature() => art.gameObject.SetActive(false);

        internal Vector2 PlayerStart => Bounds.center + new Vector2(-Bounds.width * 0.22f, -Bounds.height * 0.19f);
        internal Vector2 BotStart => Bounds.center + new Vector2(Bounds.width * 0.22f, -Bounds.height * 0.19f);

        internal void SetNode(Vector2 position) => node.position = position;

        internal void SpawnNode(Vector2? opponent = null)
        {
            // Preserve the Phase 1 spawn envelope.
            float pad = nodeWallClearance + nodeRadius * 1.55f;
            Vector2 min = Bounds.min + Vector2.one * pad;
            Vector2 max = Bounds.max - Vector2.one * pad;
            Vector2 origin = Body.position;
            float separation = nodeCoreClearance + coreRadius + nodeRadius;
            Vector2 candidate = new Vector2(origin.x < Bounds.center.x ? max.x : min.x,
                                            origin.y < Bounds.center.y ? max.y : min.y);
            if (opponent.HasValue)
            {
                float safest = -1f;
                for (int corner = 0; corner < 4; corner++)
                {
                    var point = new Vector2(corner % 2 == 0 ? min.x : max.x, corner < 2 ? min.y : max.y);
                    float clearance = Mathf.Min((point - origin).sqrMagnitude, (point - opponent.Value).sqrMagnitude);
                    if (clearance <= safest) continue;
                    safest = clearance;
                    candidate = point;
                }
            }
            for (int i = 0; i < 16; i++)
            {
                Vector2 sample = new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));
                if ((sample - origin).sqrMagnitude < separation * separation) continue;
                if (opponent.HasValue && (sample - opponent.Value).sqrMagnitude < separation * separation) continue;
                candidate = sample;
                break;
            }
            node.position = candidate;
        }

        public bool TryCollect()
        {
            Vector2 current = Body.position;
            bool collected = DistanceToSegment(node.position, lastSample, current) <= PickupReach;
            lastSample = current;
            if (!collected) return false;
            ShowPickup();
            SpawnNode();
            return true;
        }

        internal void ShowPickup()
        {
            view.Pulse(node.position, GameView.Amber, 0.45f, 1.25f);
            pickupPop.position = node.position;
            pickupPop.localScale = node.localScale;
            pickupPop.gameObject.SetActive(true);
            pickupPopTime = 0.17f;
        }

        public void ReactToPush(Vector2 force)
        {
            if (force.sqrMagnitude >= 3.8f * 3.8f) ReactToImpact(force, 0.84f);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!Body.simulated || collision.contactCount == 0 || Time.time - lastBankTime < 0.08f) return;
            Vector2 normal = collision.GetContact(0).normal;
            float speed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, normal));
            if (speed < 1.3f) return;
            lastBankTime = Time.time;
            ReactToImpact(normal, Mathf.Clamp01(speed / 6f));
            if (isBot) return;
            view.PlaySound(GameView.Sound.Bank);
            if (speed >= 3f) view.TryBark(GameView.Bark.Bank, VisibleCorePosition);
        }

        private void ReactToImpact(Vector2 direction, float strength)
        {
            impact = 1f;
            float squash = 1f - 0.17f * strength;
            float stretch = 1f + 0.18f * strength;
            impactScale = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? new Vector2(squash, stretch) : new Vector2(stretch, squash);
        }

        public void DrawCreature(float dt, Vector2 lookAt, bool panic, float nerve, bool shielded, bool immune)
        {
            panicAmount = Mathf.MoveTowards(panicAmount, panic ? 1f : 0f, dt * 8f);
            impact = Mathf.MoveTowards(impact, 0f, dt * 5.5f);
            float startled = Mathf.Max(panicAmount, impact);
            float clock = Time.unscaledTime;
            float breath = Mathf.Sin(clock * 4.4f) * 0.021f * (1f - startled);
            Vector2 scale = Vector2.Lerp(Vector2.one, impactScale, impact);
            art.localScale = new Vector3(scale.x - breath * 0.48f, scale.y + breath, 1f);
            art.localPosition = panicAmount * new Vector3(Mathf.Sin(clock * 89f) * 0.021f,
                Mathf.Sin(clock * 111f) * 0.021f, 0f);
            Look look = Looks[lookIndex];
            fill.color = Color.Lerp(BodyColor, GameView.Blush, panicAmount);
            float blink = 0.5f + 0.5f * Mathf.Sin(clock * 69f);
            if (immune) fill.color = Color.Lerp(fill.color, GameView.BlueInk, 0.12f * blink);

            Vector2 target = Vector2.ClampMagnitude(lookAt - VisibleCorePosition, 0.7f) / 0.7f;
            lookOffset = Vector2.Lerp(lookOffset, target * 0.16f, 1f - Mathf.Exp(-dt * 18f));
            float blinkPhase = Mathf.Repeat(clock + 1.72f, 3.87f) / 0.14f;
            float eyelid = 1f - Mathf.Sin(Mathf.Clamp01(blinkPhase) * Mathf.PI) * 0.90f * (1f - startled);
            for (int i = 0; i < eyes.Length; i++)
            {
                cheeks[i].color = GameView.Tint(look.Accent, Mathf.Lerp(0.42f, 0.72f, panicAmount));
                eyes[i].localScale = new Vector3(0.284f, Mathf.Lerp(0.252f, 0.337f, startled) * eyelid, 1f);
                pupils[i].localPosition = lookOffset;
                pupils[i].localScale = Vector3.one * Mathf.Lerp(0.40f, 0.29f, startled);
                brows[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? -1f : 1f) * 24f * panicAmount);
                brows[i].localPosition = new Vector2(i == 0 ? -0.158f : 0.158f, Mathf.Lerp(0.221f, 0.262f, startled));
            }
            bool open = startled > 0.22f;
            mouth.sprite = view.Art(open ? GameView.Shape.OpenMouth : GameView.Shape.Bar);
            mouth.transform.localScale = open ? new Vector3(0.116f, 0.147f, 1f) : new Vector3(0.142f, 0.037f, 1f);

            nerveRing.enabled = nerve > 0f;
            if (nerveRing.enabled)
            {
                nerveRing.transform.localScale = Vector3.one * Mathf.Lerp(1.68f, 1.24f, nerve);
                nerveRing.color = GameView.Tint(GameView.Ink, Mathf.Lerp(0.30f, 0.68f, nerve));
            }
            shieldRing.enabled = shielded || immune;
            shieldRing.color = GameView.Tint(GameView.BlueInk, shielded ? 0.72f : Mathf.Lerp(0.14f, 0.45f, blink));
            trail.emitting = Body.simulated && Body.linearVelocity.sqrMagnitude > 0.25f;

            if (pickupPopTime <= 0f) return;
            pickupPopTime = Mathf.Max(0f, pickupPopTime - dt);
            float progress = 1f - pickupPopTime / 0.17f;
            float punch = 1f + 0.28f * Mathf.Sin(progress * Mathf.PI * 0.83f);
            pickupPop.localScale = Vector3.one * nodeRadius * 2f * punch * (1f - progress * progress);
            if (pickupPopTime == 0f) pickupPop.gameObject.SetActive(false);
        }

        internal static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return Vector2.Distance(point, from);
            float t = Mathf.Clamp01(Vector2.Dot(point - from, delta) / delta.sqrMagnitude);
            return Vector2.Distance(point, from + delta * t);
        }

        private void OnDestroy()
        {
            if (bankMaterial != null) Destroy(bankMaterial);
        }
    }
}
