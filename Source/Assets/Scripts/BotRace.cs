using UnityEngine;

namespace HandsOff
{
    internal sealed class BotRace
    {
        internal enum Difficulty { Easy, Medium, Hard, Impossible }

        private readonly struct Tuning
        {
            internal readonly float Speed, Reaction, AimInterval, Steer;
            internal readonly int MinGoal, MaxGoal;

            internal Tuning(float speed, float reaction, float aimInterval, float steer, int minGoal, int maxGoal)
            {
                Speed = speed;
                Reaction = reaction;
                AimInterval = aimInterval;
                Steer = steer;
                MinGoal = minGoal;
                MaxGoal = maxGoal;
            }
        }

        private static readonly Tuning[] Levels =
        {
            new Tuning(1.55f, 1.15f, 0.40f, 2.5f, 16, 20),
            new Tuning(2.45f, 0.76f, 0.30f, 3.0f, 12, 15),
            new Tuning(3.45f, 0.46f, 0.22f, 3.4f, 9, 11),
            new Tuning(6.25f, 0.15f, 0.11f, 5.1f, 8, 10)
        };

        private Tuning tuning;
        private readonly Arena2D player;
        private readonly Arena2D bot;
        private Vector2 previousPlayer, previousBot, target;
        private float reaction;
        private bool collectPlayer = true;

        internal int PlayerPoints { get; private set; }
        internal int BotPoints { get; private set; }
        internal int Goal { get; private set; }
        internal Arena2D Rival => bot;

        internal static string GoalRange(Difficulty difficulty)
        {
            var level = Levels[(int)difficulty];
            return $"{level.MinGoal}-{level.MaxGoal}";
        }

        internal BotRace(Arena2D player, GameView view, Transform root)
        {
            this.player = player;
            var opponent = new GameObject("Bot");
            opponent.transform.SetParent(root, false);
            bot = opponent.AddComponent<Arena2D>();
            bot.InitializeCore(view, true);
            opponent.SetActive(false);
        }

        internal void Begin(int look, Difficulty difficulty)
        {
            tuning = Levels[(int)difficulty];
            Goal = Random.Range(tuning.MinGoal, tuning.MaxGoal + 1);
            PlayerPoints = BotPoints = 0;
            collectPlayer = true;
            bot.gameObject.SetActive(true);
            SetLook(look);
            player.PrepareRun(true, player.PlayerStart);
            bot.PrepareRun(true, player.BotStart);
            Physics2D.IgnoreCollision(player.CoreCollider, bot.CoreCollider);
            player.SetNode(player.Bounds.center + Vector2.up * player.Bounds.height * 0.15f);
            PreviousPositions();
            target = bot.Body.position;
            reaction = tuning.Reaction + 0.09f;
        }

        internal void SetLook(int index) => bot.SetLook(index);

        internal void Resize()
        {
            Rect bounds = player.Bounds;
            Vector2 position = bot.Body.position;
            float r = bot.CoreRadius;
            bot.PlaceCore(new Vector2(Mathf.Clamp(position.x, bounds.xMin + r, bounds.xMax - r),
                Mathf.Clamp(position.y, bounds.yMin + r, bounds.yMax - r)));
            PreviousPositions();
            player.SpawnNode(bot.Body.position);
            Aim(tuning.Reaction);
        }

        internal void ResetPlayer()
        {
            player.PlaceCore(player.PlayerStart);
            previousPlayer = player.Body.position;
        }

        internal void ResetBot()
        {
            bot.PlaceCore(player.BotStart);
            target = previousBot = bot.Body.position;
            reaction = tuning.Reaction;
        }

        internal void Drive(float dt)
        {
            reaction -= dt;
            if (reaction <= 0f) Aim(tuning.AimInterval);
            Vector2 offset = target - bot.Body.position;
            float speed = Mathf.Min(offset.magnitude * tuning.Steer, tuning.Speed * bot.SpeedMultiplier);
            Vector2 desired = offset.normalized * speed;
            Vector2 steering = (desired - bot.Body.linearVelocity) * tuning.Steer;
            bot.Body.AddForce(steering * bot.Body.mass, ForceMode2D.Force);
        }

        internal void Draw(float dt) => bot.DrawCreature(dt, player.NodePosition, false, 0f, false, false);

        internal bool TryScore(out bool playerScored)
        {
            Vector2 node = player.NodePosition;
            float playerContact = PickupTime(previousPlayer, player.Body.position, node, player.PickupReach);
            float botContact = PickupTime(previousBot, bot.Body.position, node, player.PickupReach);
            PreviousPositions();
            playerScored = false;
            if (float.IsPositiveInfinity(playerContact) && float.IsPositiveInfinity(botContact)) return false;

            bool tied = Mathf.Abs(playerContact - botContact) < 0.0001f;
            playerScored = tied ? collectPlayer : playerContact < botContact;
            if (tied) collectPlayer = !collectPlayer;
            if (playerScored) PlayerPoints++;
            else BotPoints++;
            player.ShowPickup();
            if (PlayerPoints < Goal && BotPoints < Goal)
            {
                player.SpawnNode(bot.Body.position);
                // Give the player a moment to see the new node before the bot changes course.
                reaction = tuning.Reaction;
            }
            return true;
        }

        private static float PickupTime(Vector2 from, Vector2 to, Vector2 center, float radius)
        {
            Vector2 start = from - center;
            float outside = start.sqrMagnitude - radius * radius;
            if (outside <= 0f) return 0f;
            Vector2 delta = to - from;
            float speed = delta.sqrMagnitude;
            float approach = Vector2.Dot(start, delta);
            float discriminant = approach * approach - speed * outside;
            if (speed < 0.000001f || approach >= 0f || discriminant < 0f) return float.PositiveInfinity;
            float entry = (-approach - Mathf.Sqrt(discriminant)) / speed;
            return entry <= 1f ? entry : float.PositiveInfinity;
        }

        private void Aim(float delay)
        {
            target = player.NodePosition;
            reaction = delay;
        }

        private void PreviousPositions()
        {
            previousPlayer = player.Body.position;
            previousBot = bot.Body.position;
        }

        internal void Stop()
        {
            bot.Freeze();
            bot.gameObject.SetActive(false);
        }
    }
}
