using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HandsOff
{
    public sealed class GameView : MonoBehaviour
    {
        public const float HudFraction = 0.115f;
        public enum Shape { Circle, Ring, Diamond, Bar, Solid, DashedRing, SpikedDiamond, OpenMouth, Blob }
        public enum Sound { Push, Bank, Pickup, Nerve, Shield, Shatter }
        public enum Bark { Near, Bank, Swat }

        public static readonly Color32 Paper = new Color32(0xF4, 0xEF, 0xE6, 255);
        public static readonly Color32 Ink = new Color32(0x1B, 0x1B, 0x1F, 255);
        public static readonly Color32 White = new Color32(255, 255, 255, 255);
        public static readonly Color32 Blush = new Color32(0xFF, 0xCD, 0xD2, 255);
        public static readonly Color32 Amber = new Color32(0xF5, 0x9E, 0x0B, 255);
        public static readonly Color32 Crimson = new Color32(0xE6, 0x39, 0x46, 255);
        public static readonly Color32 BlueInk = new Color32(0x36, 0x79, 0xA3, 255);
        private static readonly Color32 Panel = new Color32(0xFF, 0xFD, 0xF9, 255);
        private static readonly Color32 MintButton = new Color32(0xC1, 0xE8, 0xD0, 255);
        private static readonly Color32 SkyButton = new Color32(0xC5, 0xDF, 0xF7, 255);
        private static readonly Color32 PeachButton = new Color32(0xFF, 0xD2, 0xAB, 255);
        private static readonly Color32 RoseButton = new Color32(0xF5, 0xC1, 0xC6, 255);
        private static readonly string[][] BarkLines =
        {
            new[] { "BACK OFF", "PERSONAL SPACE", "HEY!", "NOPE" },
            new[] { "WATCH IT", "RUDE", "WHOA!" },
            new[] { "GO AWAY", "SHOO" }
        };

        private struct Ripple
        {
            public SpriteRenderer Sprite;
            public Color Color;
            public float Age, Start, End;
        }

        private enum Menu { None, Options, Looks, Difficulty }

        private readonly Sprite[] sprites = new Sprite[(int)Shape.Blob + 1];
        private readonly Ripple[] pulses = new Ripple[6];
        private readonly SpriteRenderer[] splinters = new SpriteRenderer[10];
        private readonly List<RectTransform> borderInsets = new List<RectTransform>(20);
        private readonly List<RectTransform> buttonShadows = new List<RectTransform>(22);
        private readonly int[] barkIndex = new int[3];
        private readonly Button[] lookButtons = new Button[Arena2D.Looks.Length];
        private readonly TMP_Text[] lookCaptions = new TMP_Text[Arena2D.Looks.Length];
        private readonly TMP_Text[] optionValues = new TMP_Text[3];
        private readonly Button[] optionButtons = new Button[3];
        private readonly Image[] optionBadges = new Image[3];

        private Camera worldCamera;
        private GameFeedback feedback;
        private Rect screenFrame;
        private SpriteRenderer field, fingerMark;
        private RectTransform safeRoot, hud, card, diagram, results, barkBubble;
        private TMP_Text scoreLabel, comboLabel, bestLabel, hint, heading, eyebrow, buttonLabel, ghost;
        private TMP_Text runTotal, runBest, barkText, nerveNotice;
        private TMP_Text scoreCaption, bestCaption, runCaption, runBestCaption, secondaryLabel;
        private RectTransform secondaryButton;
        private bool titleScreen;
        private RectTransform footer, menuRoot, optionsPanel, looksPanel, difficultyPanel, unlockBanner;
        private TMP_Text optionsTitle, optionsBack, looksBest, unlockLabel;
        private RectTransform restartButton, homeButton;
        private Image mascotFill;
        private Action resume;
        private Image comboBar;
        private int nextPulse;
        private float breakTime, shakeAge, promptAlpha, nextBarkTime, barkTime, nerveNoticeTime;
        private float unlockTime;
        private Vector2 breakOrigin, barkOrigin;
        private Color breakColor;
        private bool promptFading;
        private bool paused;
        private Menu menu, looksBack;

        public Vector2 CameraOffset { get; private set; }
        public Sprite Art(Shape shape) => sprites[(int)shape];

        public static Color Tint(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        internal void Initialize(Action play, Action<BotRace.Difficulty> race, Action home, Action pause, Action resumeRun,
            Action<int> equip, Camera camera)
        {
            worldCamera = camera;
            resume = resumeRun;
            for (int i = 0; i < sprites.Length; i++) sprites[i] = MakeShape((Shape)i);
            for (int i = 0; i < pulses.Length; i++)
            {
                pulses[i].Sprite = MakeSprite("Ink ripple", Shape.Ring, Ink, 2, transform);
                pulses[i].Sprite.enabled = false;
            }
            field = MakeSprite("Push radius", Shape.Ring, Tint(Ink, 0.12f), 1, transform);
            for (int i = 0; i < splinters.Length; i++)
            {
                bool rim = i % 2 == 0;
                var piece = MakeSprite("Burst piece", rim ? Shape.Diamond : Shape.Bar, Ink, 13, transform);
                piece.transform.rotation = Quaternion.Euler(0f, 0f, 17f + 360f / splinters.Length * i);
                piece.transform.localScale = rim ? new Vector3(0.125f, 0.26f, 1f) : new Vector3(0.070f, 0.16f, 1f);
                piece.enabled = false;
                splinters[i] = piece;
            }
            fingerMark = MakeSprite("Field origin", Shape.Ring, Tint(Ink, 0.8f), 6, transform);
            fingerMark.transform.localScale = Vector3.one * 0.24f;
            SetField(false, Vector2.zero, 0f);
            feedback = new GameFeedback(gameObject);

            var surface = new GameObject("Interface", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            surface.transform.SetParent(transform, false);
            surface.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = surface.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (EventSystem.current == null)
            {
                var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            safeRoot = Region("Safe area", surface.transform, 0, 0, 1, 1);
            MakeHud(pause);
            MakeCard(play, home);
            ghost = Label("Push hint", safeRoot, "PUSH NEARBY", 0.1f, 0.004f, 0.9f, 0.041f, 36f,
                Tint(Ink, 0.62f), TextAlignmentOptions.Center);
            ghost.characterSpacing = 4f;

            barkBubble = Region("Comic bark", safeRoot, 0.5f, 0.5f, 0.5f, 0.5f);
            barkBubble.sizeDelta = new Vector2(268f, 65f);
            var bubble = Plate("Speech bubble", barkBubble, Panel, false);
            barkText = Label("Bark", bubble, "", 0.05f, 0.1f, 0.95f, 0.9f, 33f, Ink, TextAlignmentOptions.Center);
            barkBubble.gameObject.SetActive(false);
            BuildMenus(play, race, home, equip);
        }

        private void MakeHud(Action pause)
        {
            hud = Region("HUD", safeRoot, 0.055f, 1f - HudFraction, 0.945f, 0.986f);
            var face = Plate("Score panel", hud, Panel, true);
            scoreCaption = Label("Score caption", face, "SCORE", 0.06f, 0.65f, 0.35f, 0.88f, 28f, Ink);
            scoreLabel = Label("Score", face, "0", 0.055f, 0.16f, 0.39f, 0.66f, 75f, Ink);
            bestCaption = Label("Best caption", face, "BEST", 0.60f, 0.65f, 0.793f, 0.88f, 28f, Ink, TextAlignmentOptions.Right);
            bestLabel = Label("Best score", face, "0", 0.60f, 0.16f, 0.796f, 0.66f, 61f, Ink, TextAlignmentOptions.Right);
            var chip = Plate("Multiplier badge", Region("Multiplier area", face, 0.397f, 0.365f, 0.603f, 0.82f), Amber, false);
            comboLabel = Label("Multiplier", chip, "×1", 0.05f, 0.04f, 0.95f, 0.96f, 50f, Ink, TextAlignmentOptions.Center);
            nerveNotice = Label("Nerve award", face, "+1 NERVE", 0.35f, 0.16f, 0.65f, 0.35f, 24f, Ink,
                TextAlignmentOptions.Center);
            nerveNotice.gameObject.SetActive(false);
            var track = Picture("Combo window", face, Shape.Solid, Ink, 0.056f, 0.078f, 0.944f, 0.129f);
            comboBar = Picture("Time remaining", track.transform, Shape.Solid, Amber, 0.003f, 0.23f, 0.997f, 0.77f);
            comboBar.type = Image.Type.Filled;
            comboBar.fillMethod = Image.FillMethod.Horizontal;
            comboBar.fillOrigin = (int)Image.OriginHorizontal.Left;

            var pauseRoot = Region("Pause button", face, 0.818f, 0.238f, 0.956f, 0.857f);
            var pauseFace = RaisedPlate("Pause game", pauseRoot, SkyButton);
            MakeButton(pauseFace.GetComponent<Image>(), pause);
            Picture("Pause stroke", pauseFace, Shape.Solid, Ink, 0.33f, 0.28f, 0.45f, 0.72f);
            Picture("Pause stroke", pauseFace, Shape.Solid, Ink, 0.55f, 0.28f, 0.67f, 0.72f);

            unlockBanner = Region("Look unlock banner", safeRoot, 0.23f, 0.84f, 0.77f, 0.878f);
            var labelFace = Plate("Unlock", unlockBanner, Amber, false);
            unlockLabel = Label("New look", labelFace, "", 0.03f, 0.08f, 0.97f, 0.92f, 35f, Ink, TextAlignmentOptions.Center);
            unlockBanner.gameObject.SetActive(false);
        }

        private void MakeCard(Action play, Action home)
        {
            card = Region("Run card", safeRoot, 0.075f, 0.237f, 0.925f, 0.768f);
            var shadow = Picture("Card shadow", card, Shape.Bar, Ink, 0, 0, 1, 1);
            shadow.type = Image.Type.Sliced;
            shadow.rectTransform.offsetMin = shadow.rectTransform.offsetMax = new Vector2(7f, -7f);
            var face = Plate("Card", card, Panel, true);
            eyebrow = Label("Eyebrow", face, "", 0.076f, 0.85f, 0.92f, 0.926f, 31f, Ink);
            eyebrow.characterSpacing = 5f;
            heading = Label("Title", face, "", 0.065f, 0.665f, 0.935f, 0.852f, 119f, Ink);
            Picture("Rule", face, Shape.Solid, Ink, 0.075f, 0.64f, 0.925f, 0.6435f);

            diagram = Region("Push diagram", face, 0.11f, 0.303f, 0.89f, 0.62f);
            DiagramMark("Near field", 0.12f, 0.68f, Shape.Ring, Tint(Ink, 0.4f));
            DiagramMark("Finger", 0.12f, 0.375f, Shape.Ring, Ink);
            var markFrame = Region("Mascot frame", diagram, 0.235f, 0.035f, 0.55f, 0.965f);
            mascotFill = MakeMascot(markFrame, Blush, Arena2D.Looks[0].Accent, true);
            for (int i = 0; i < 4; i++)
                Picture("Push path", diagram, Shape.Solid, Ink, 0.57f + i * 0.055f, 0.49f,
                    0.593f + i * 0.055f, 0.507f);
            DiagramMark("Goal rim", 0.87f, 0.43f, Shape.Diamond, Ink);
            DiagramMark("Goal", 0.87f, 0.344f, Shape.Diamond, Amber, 0.8f);

            hint = Label("Hint or failure", face, "", 0.075f, 0.24f, 0.925f, 0.31f, 36f, Ink);

            results = Region("Score summary", face, 0.076f, 0.347f, 0.924f, 0.6f);
            var tally = Plate("Tally", results, Paper, false);
            Picture("Tally divider", tally, Shape.Solid, Ink, 0.499f, 0.14f, 0.502f, 0.86f);
            runCaption = Label("Run caption", tally, "SCORE", 0.065f, 0.59f, 0.44f, 0.89f, 30f, Ink);
            runTotal = Label("Run total", tally, "0", 0.065f, 0.1f, 0.44f, 0.58f, 72f, Ink);
            runBestCaption = Label("Best caption", tally, "BEST", 0.56f, 0.59f, 0.935f, 0.89f, 30f, Ink, TextAlignmentOptions.Right);
            runBest = Label("Best total", tally, "0", 0.56f, 0.1f, 0.935f, 0.58f, 72f, Ink, TextAlignmentOptions.Right);

            var start = Region("Primary action", face, 0.075f, 0.065f, 0.475f, 0.225f);
            var buttonFace = RaisedPlate("Start or retry", start, Amber);
            MakeButton(buttonFace.GetComponent<Image>(), play);
            buttonLabel = Label("Action", buttonFace, "", 0.10f, 0.12f, 0.79f, 0.88f, 54f, Ink);
            var mark = Picture("Action arrow", buttonFace, Shape.Diamond, Ink, 0.825f, 0.34f, 0.92f, 0.66f);
            mark.preserveAspect = true;

            var secondary = MenuButton(face, "VS BOT", 0.51f, 0.065f, 0.925f, 0.225f, SkyButton, Ink,
                () =>
                {
                    if (titleScreen) SetMenu(Menu.Difficulty);
                    else home();
                });
            secondaryButton = secondary.Root;
            secondaryLabel = secondary.Caption;
        }

        private Image MakeMascot(RectTransform frame, Color body, Color accent, bool alarmed)
        {
            var mascot = Region("Mascot", frame, 0, 0, 1, 1);
            var square = mascot.gameObject.AddComponent<AspectRatioFitter>();
            square.aspectRatio = 1f;
            square.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            mascot.localRotation = Quaternion.Euler(0f, 0f, alarmed ? -10f : 0f);
            Picture("Face rim", mascot, Shape.Blob, Ink, 0, 0, 1, 1);
            var bodyImage = Picture("Face color", mascot, Shape.Blob, body, 0.053f, 0.053f, 0.947f, 0.947f);
            for (int i = 0; i < 2; i++)
            {
                float x = 0.13f + 0.34f * i;
                Picture("Eye rim", mascot, Shape.Circle, Ink, x, 0.40f, x + 0.28f, 0.735f);
                Picture("Eye white", mascot, Shape.Circle, White, x + 0.029f, 0.429f, x + 0.251f, 0.706f);
                Picture("Pupil", mascot, Shape.Circle, Ink, x + 0.057f, 0.497f, x + 0.155f, 0.595f);
                Picture("Pupil glint", mascot, Shape.Circle, White, x + 0.075f, 0.551f, x + 0.105f, 0.581f);
                float cheek = i == 0 ? 0.199f : 0.754f;
                Picture("Cheek", mascot, Shape.Circle, Tint(accent, 0.64f), cheek - 0.06f, 0.37f, cheek + 0.06f, 0.413f);
                var brow = Picture("Frown", mascot, Shape.Bar, Ink, x + 0.025f, 0.765f, x + 0.248f, 0.81f);
                float angle = alarmed ? 23f : 4f;
                brow.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -angle : angle);
            }
            Picture("Mouth", mascot, alarmed ? Shape.OpenMouth : Shape.Bar, Ink,
                0.39f, alarmed ? 0.218f : 0.29f, 0.53f, alarmed ? 0.387f : 0.33f);
            return bodyImage;
        }

        private void BuildMenus(Action restart, Action<BotRace.Difficulty> race, Action home, Action<int> equip)
        {
            footer = Region("Title and retry controls", safeRoot, 0.075f, 0.146f, 0.925f, 0.212f);
            MenuButton(footer, "LOOKS", 0f, 0f, 0.48f, 1f, PeachButton, Ink, OpenLooks);
            MenuButton(footer, "OPTIONS", 0.52f, 0f, 1f, 1f, Panel, Ink, OpenOptions);

            var shade = Picture("Menu backdrop", safeRoot, Shape.Solid, Tint(Ink, 0.32f), 0, 0, 1, 1);
            shade.raycastTarget = true;
            menuRoot = shade.rectTransform;
            optionsPanel = Region("Options", menuRoot, 0.075f, 0.167f, 0.925f, 0.837f);
            RectTransform options = Plate("Options card", optionsPanel, Panel, true);
            optionsTitle = Label("Options title", options, "OPTIONS", 0.066f, 0.837f, 0.94f, 0.959f, 92f, Ink);
            MakeOption(options, "MUSIC", GameFeedback.Option.Music, 0.705f, 0.926f);
            MakeOption(options, "SOUND", GameFeedback.Option.Sound, 0.572f, 0.926f);
            MakeOption(options, "HAPTICS", GameFeedback.Option.Vibration, 0.439f, 0.926f);
            MenuButton(options, "LOOKS", 0.074f, 0.306f, 0.926f, 0.413f, PeachButton, Ink, OpenLooks);
            optionsBack = MenuButton(options, "BACK", 0.074f, 0.179f, 0.926f, 0.274f, Amber, Ink,
                () => CloseMenu()).Caption;
            homeButton = MenuButton(options, "HOME", 0.074f, 0.052f, 0.47f, 0.145f, SkyButton, Ink, home).Root;
            restartButton = MenuButton(options, "RESTART", 0.51f, 0.052f, 0.926f, 0.145f, Panel, Ink, restart).Root;

            looksPanel = Region("Looks", menuRoot, 0.075f, 0.119f, 0.925f, 0.869f);
            RectTransform wardrobe = Plate("Looks card", looksPanel, Panel, true);
            Label("Looks title", wardrobe, "LOOKS", 0.066f, 0.87f, 0.935f, 0.973f, 94f, Ink);
            looksBest = Label("Unlock progress", wardrobe, "", 0.074f, 0.801f, 0.926f, 0.864f, 38f, Ink);
            for (int i = 0; i < Arena2D.Looks.Length; i++)
            {
                Arena2D.Look look = Arena2D.Looks[i];
                float x = i % 2 == 0 ? 0.074f : 0.515f;
                float y = i < 2 ? 0.479f : 0.169f;
                int index = i;
                var tile = Region(look.Name, wardrobe, x, y, x + 0.411f, y + 0.29f);
                var tileFace = RaisedPlate(look.Name + " tile", tile, Panel);
                lookButtons[i] = MakeButton(tileFace.GetComponent<Image>(), () => equip(index));
                var portrait = Region("Look preview", tileFace, 0.12f, 0.37f, 0.88f, 0.935f);
                MakeMascot(portrait, look.Body, look.Accent, false);
                Label("Look name", tileFace, look.Name, 0.06f, 0.26f, 0.94f, 0.40f,
                    44f, Ink, TextAlignmentOptions.Center);
                Label("Perk", tileFace, look.Perk, 0.04f, 0.15f, 0.96f, 0.255f, 28f, Ink,
                    TextAlignmentOptions.Center);
                lookCaptions[i] = Label("Look availability", tileFace, "", 0.06f, 0.033f, 0.94f, 0.148f,
                    27f, Ink, TextAlignmentOptions.Center);
            }
            MenuButton(wardrobe, "BACK", 0.074f, 0.04f, 0.926f, 0.14f, Amber, Ink, () => CloseMenu());
            MakeDifficultyPicker(race);
            SetMenu(Menu.None);
        }

        private void MakeDifficultyPicker(Action<BotRace.Difficulty> race)
        {
            difficultyPanel = Region("Bot difficulty", menuRoot, 0.075f, 0.124f, 0.925f, 0.876f);
            var sheet = Plate("Race card", difficultyPanel, Panel, true);
            Label("Race title", sheet, "VS BOT", 0.075f, 0.851f, 0.925f, 0.962f, 92f, Ink);
            Label("Race goal", sheet, "COLLECT AMBER", 0.079f, 0.797f, 0.92f, 0.851f, 34f, Ink);

            Color[] colors = { MintButton, SkyButton, PeachButton, RoseButton };
            string[] details = { "SLOW BOT", "STEADY BOT", "MINES", "LASERS + MINES" };
            for (int i = 0; i < colors.Length; i++)
            {
                var level = (BotRace.Difficulty)i;
                float y = 0.65f - i * 0.147f;
                var row = MenuButton(sheet, level.ToString().ToUpperInvariant(), 0.074f, y, 0.926f, y + 0.116f,
                    colors[i], Ink, () => race(level));
                var caption = row.Caption.rectTransform;
                caption.anchorMin = new Vector2(0.061f, 0.39f);
                caption.anchorMax = new Vector2(0.77f, 0.93f);
                row.Caption.alignment = TextAlignmentOptions.Left;
                RectTransform surface = row.Caption.rectTransform.parent as RectTransform;
                Label("Race conditions", surface, $"{BotRace.GoalRange(level)} PTS / {details[i]}",
                    0.061f, 0.09f, 0.74f, 0.39f, 28f, Ink);
                for (int bar = 0; bar < 4; bar++)
                {
                    float x = 0.776f + bar * 0.039f;
                    Picture("Difficulty bar", surface, Shape.Bar, Tint(Ink, bar <= i ? 1f : 0.23f),
                        x, 0.26f, x + 0.025f, 0.44f + bar * 0.105f);
                }
            }
            MenuButton(sheet, "BACK", 0.074f, 0.046f, 0.926f, 0.151f, Panel, Ink, () => CloseMenu());
        }

        private void MakeOption(Transform parent, string title, GameFeedback.Option option, float bottom, float right)
        {
            var row = Region(title, parent, 0.074f, bottom, right, bottom + 0.11f);
            var plate = RaisedPlate(title + " toggle", row, Panel);
            optionButtons[(int)option] = MakeButton(plate.GetComponent<Image>(), () =>
            {
                feedback.Toggle(option);
                RefreshOptions();
            });
            bool music = option == GameFeedback.Option.Music;
            Label("Setting", plate, title, 0.06f, music ? 0.39f : 0.12f, 0.62f, 0.88f, 44f, Ink);
            if (music) Label("Track", plate, "TOYBOX SHUFFLE", 0.06f, 0.13f, 0.62f, 0.38f, 24f, Ink);
            var badge = Picture("Value badge", plate, Shape.Bar, Paper, 0.75f, 0.18f, 0.949f, 0.82f);
            badge.type = Image.Type.Sliced;
            optionBadges[(int)option] = badge;
            optionValues[(int)option] = Label("Value", badge.transform, "", 0.04f, 0.08f, 0.96f, 0.92f,
                38f, Ink, TextAlignmentOptions.Center);
        }

        private (Button Button, TMP_Text Caption, RectTransform Root) MenuButton(Transform parent, string label, float x0, float y0,
            float x1, float y1, Color face, Color ink, Action click)
        {
            var root = Region(label, parent, x0, y0, x1, y1);
            var plate = RaisedPlate(label + " button", root, face);
            var button = MakeButton(plate.GetComponent<Image>(), click);
            var caption = Label("Caption", plate, label, 0.055f, 0.13f, 0.945f, 0.87f, 49f, ink,
                TextAlignmentOptions.Center);
            return (button, caption, root);
        }

        private static Button MakeButton(Image face, Action click)
        {
            face.raycastTarget = true;
            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = new Color(0.97f, 0.96f, 0.93f);
            colors.pressedColor = new Color(0.82f, 0.80f, 0.76f);
            colors.disabledColor = new Color(0.90f, 0.89f, 0.87f);
            colors.fadeDuration = 0.055f;
            button.colors = colors;
            button.onClick.AddListener(() => click());
            return button;
        }

        private void OpenOptions() => SetMenu(Menu.Options);

        private void OpenLooks()
        {
            looksBack = menu;
            SetMenu(Menu.Looks);
        }

        public bool CloseMenu()
        {
            if (menu == Menu.None) return false;
            if (menu == Menu.Looks) SetMenu(looksBack);
            else if (paused) resume();
            else SetMenu(Menu.None);
            return true;
        }

        private void SetMenu(Menu page)
        {
            menu = page;
            optionsPanel.gameObject.SetActive(page == Menu.Options);
            looksPanel.gameObject.SetActive(page == Menu.Looks);
            difficultyPanel.gameObject.SetActive(page == Menu.Difficulty);
            menuRoot.gameObject.SetActive(page != Menu.None);
            footer.gameObject.SetActive(page == Menu.None && card.gameObject.activeSelf);
            unlockBanner.gameObject.SetActive(page == Menu.None && unlockTime > 0f && hud.gameObject.activeSelf);
            feedback.SetMusicLevel(page == Menu.None ? 0.32f : 0.19f);
            if (page == Menu.Options) RefreshOptions();
        }

        private void RefreshOptions()
        {
            optionsTitle.text = paused ? "PAUSED" : "OPTIONS";
            optionsBack.text = paused ? "RESUME" : "BACK";
            restartButton.gameObject.SetActive(paused);
            homeButton.gameObject.SetActive(paused);
            bool haptics = feedback.HasVibration;
            int vibration = (int)GameFeedback.Option.Vibration;
            optionButtons[vibration].interactable = haptics;
            for (int i = 0; i < optionValues.Length; i++)
            {
                bool available = i != vibration || haptics;
                bool enabled = feedback.IsOn((GameFeedback.Option)i);
                optionBadges[i].color = available && enabled ? MintButton : Paper;
                optionValues[i].text = !available ? "N/A" : enabled ? "ON" : "OFF";
            }
        }

        public void SetLooks(int best, int selected)
        {
            looksBest.text = $"BEST {best}";
            mascotFill.color = Color.Lerp(Arena2D.Looks[selected].Body, Blush, 0.58f);
            for (int i = 0; i < lookButtons.Length; i++)
            {
                var look = Arena2D.Looks[i];
                bool unlocked = best >= look.RequiredBest;
                bool equipped = i == selected;
                lookButtons[i].interactable = unlocked && !equipped;
                ((Image)lookButtons[i].targetGraphic).color = equipped ? Color.Lerp(look.Body, Panel, 0.56f)
                    : unlocked ? (Color)Panel : Paper;
                lookCaptions[i].text = equipped ? "EQUIPPED" : unlocked ? "TAP TO WEAR" : $"UNLOCK AT {look.RequiredBest}";
            }
        }

        public void ShowLookUnlocked(string look)
        {
            unlockTime = 1.9f;
            unlockLabel.text = look + " UNLOCKED";
            unlockBanner.gameObject.SetActive(true);
        }

        private void DiagramMark(string name, float x, float height, Shape shape, Color color, float width = 1f)
        {
            var image = Picture(name, diagram, shape, color, x - 0.108f * width, (1f - height) / 2f,
                x + 0.108f * width, (1f + height) / 2f);
            image.preserveAspect = true;
        }

        private RectTransform Plate(string name, RectTransform parent, Color paper, bool blocksInput)
        {
            var border = Picture(name + " ink", parent, Shape.Bar, Ink, 0, 0, 1, 1);
            border.type = Image.Type.Sliced;
            var face = Picture(name, border.transform, Shape.Bar, paper, 0, 0, 1, 1);
            face.type = Image.Type.Sliced;
            face.raycastTarget = blocksInput;
            borderInsets.Add(face.rectTransform);
            return face.rectTransform;
        }

        private RectTransform RaisedPlate(string name, RectTransform root, Color face)
        {
            var shadow = Picture("Ink shadow", root, Shape.Bar, Ink, 0, 0, 1, 1);
            shadow.type = Image.Type.Sliced;
            buttonShadows.Add(shadow.rectTransform);
            return Plate(name, root, face, true);
        }

        private static RectTransform Region(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private Image Picture(string name, Transform parent, Shape shape, Color color,
            float x0, float y0, float x1, float y1)
        {
            var image = Region(name, parent, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.sprite = Art(shape);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(string name, Transform parent, string text, float x0, float y0,
            float x1, float y1, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var label = Region(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.font = TMP_Settings.defaultFontAsset;
            label.color = color;
            label.alignment = align;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = size;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        public void Layout(Rect frame, Vector2Int resolution)
        {
            screenFrame = frame;
            Vector2 size = new Vector2(Mathf.Max(1, resolution.x), Mathf.Max(1, resolution.y));
            safeRoot.anchorMin = frame.min / size;
            safeRoot.anchorMax = frame.max / size;
            float canvasScale = Mathf.Sqrt((size.x / 1080f) * (size.y / 1920f));
            Vector2 inset = Vector2.one * (2f / canvasScale);
            Vector2 lift = new Vector2(2f, -4f) / canvasScale;
            foreach (var shadow in buttonShadows) shadow.offsetMin = shadow.offsetMax = lift;
            foreach (var rect in borderInsets)
            {
                rect.offsetMin = inset;
                rect.offsetMax = -inset;
            }
        }

        public void ShowTitle()
        {
            titleScreen = true;
            paused = false;
            SetMenu(Menu.None);
            hud.gameObject.SetActive(false);
            ghost.gameObject.SetActive(false);
            card.gameObject.SetActive(true);
            footer.gameObject.SetActive(true);
            diagram.gameObject.SetActive(true);
            results.gameObject.SetActive(false);
            eyebrow.text = "DON'T TOUCH ME";
            heading.text = "HANDS OFF";
            hint.text = "PUSH INTO AMBER";
            buttonLabel.text = "SOLO";
            secondaryLabel.text = "VS BOT";
            secondaryButton.gameObject.SetActive(true);
        }

        public void ShowPlaying(int score, int combo, int best, int raceGoal)
        {
            titleScreen = false;
            ResumePlaying();
            card.gameObject.SetActive(false);
            footer.gameObject.SetActive(false);
            hud.gameObject.SetActive(true);
            ghost.gameObject.SetActive(true);
            ghost.alpha = promptAlpha = 0.62f;
            promptFading = false;
            foreach (var ripple in pulses) ripple.Sprite.enabled = false;
            breakTime = 0f;
            foreach (var piece in splinters) piece.enabled = false;
            nerveNoticeTime = 0f;
            nerveNotice.gameObject.SetActive(false);
            unlockTime = 0f;
            unlockBanner.gameObject.SetActive(false);
            ClearBark();
            nextBarkTime = 0f;
            ResetShake();
            bool racing = raceGoal > 0;
            scoreCaption.text = racing ? "YOU" : "SCORE";
            bestCaption.text = racing ? "BOT" : "BEST";
            bestLabel.color = racing ? BlueInk : Ink;
            if (racing) SetRaceScore(0, 0, raceGoal);
            else SetScore(score, combo, best);
            SetComboTime(0f);
        }

        public void SetRaceScore(int player, int bot, int goal)
        {
            scoreLabel.text = player.ToString();
            bestLabel.text = bot.ToString();
            comboLabel.text = $"TO {goal}";
            SetComboTime((float)player / goal);
        }

        public void ShowPause()
        {
            ClearBark();
            paused = true;
            SetMenu(Menu.Options);
        }

        public void ResumePlaying()
        {
            paused = false;
            SetMenu(Menu.None);
        }

        public void SetScore(int score, int combo, int best)
        {
            scoreLabel.text = score.ToString();
            bestLabel.text = best.ToString();
            comboLabel.text = $"×{combo}";
        }

        public void SetComboTime(float fraction) => comboBar.fillAmount = fraction;

        public void ShowNerveBonus()
        {
            nerveNoticeTime = 0.55f;
            nerveNotice.alpha = 1f;
            nerveNotice.gameObject.SetActive(true);
            PlaySound(Sound.Nerve);
        }

        public void ShowGameOver(string reason, int score, int best)
        {
            ShowResultCard();
            eyebrow.text = "HANDS OFF";
            heading.text = reason switch
            {
                "BEAM HIT" => "ZAPPED",
                "MINE HIT" => "POKED",
                _ => "I SAID DON'T!"
            };
            hint.text = reason;
            runCaption.text = "SCORE";
            runBestCaption.text = "BEST";
            runTotal.text = score.ToString();
            runBest.text = best.ToString();
        }

        internal void ShowRaceResult(int player, int bot, int goal, BotRace.Difficulty difficulty)
        {
            ShowResultCard();
            eyebrow.text = "VS BOT / " + difficulty.ToString().ToUpperInvariant();
            heading.text = player >= goal ? "YOU WIN" : "BOT WINS";
            hint.text = "FIRST TO " + goal;
            runCaption.text = "YOU";
            runBestCaption.text = "BOT";
            runTotal.text = player.ToString();
            runBest.text = bot.ToString();
        }

        private void ShowResultCard()
        {
            titleScreen = false;
            hud.gameObject.SetActive(false);
            ghost.gameObject.SetActive(false);
            card.gameObject.SetActive(true);
            footer.gameObject.SetActive(true);
            unlockBanner.gameObject.SetActive(false);
            diagram.gameObject.SetActive(false);
            results.gameObject.SetActive(true);
            buttonLabel.text = "RETRY";
            secondaryLabel.text = "HOME";
        }

        public void TryBark(Bark kind, Vector2 point)
        {
            float now = Time.unscaledTime;
            if (now < nextBarkTime) return;
            nextBarkTime = now + 1.2f;
            int index = (int)kind;
            string[] phrases = BarkLines[index];
            barkText.text = phrases[barkIndex[index]];
            barkIndex[index] = (barkIndex[index] + 1) % phrases.Length;
            barkOrigin = point;
            barkTime = 0.72f;
            barkBubble.gameObject.SetActive(true);
            PositionBark();
        }

        public void ClearBark()
        {
            barkTime = 0f;
            barkBubble.gameObject.SetActive(false);
        }

        private void PositionBark()
        {
            if (screenFrame.width <= 0f || screenFrame.height <= 0f) return;
            Vector2 point = worldCamera.WorldToScreenPoint(barkOrigin + Vector2.up * 0.68f);
            Vector2 anchor = new Vector2(
                Mathf.Clamp((point.x - screenFrame.xMin) / screenFrame.width, 0.20f, 0.80f),
                Mathf.Clamp((point.y - screenFrame.yMin) / screenFrame.height, 0.12f, 0.83f));
            barkBubble.anchorMin = barkBubble.anchorMax = anchor;
            barkBubble.anchoredPosition = Vector2.up * ((0.72f - barkTime) * 47f);
            barkBubble.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, Mathf.Clamp01((0.72f - barkTime) * 17f));
        }

        public void OnFirstPush() => promptFading = true;

        public void Shatter(Vector2 origin, Color color)
        {
            breakOrigin = origin;
            breakColor = color;
            breakTime = 0.36f;
            for (int i = 0; i < splinters.Length; i++)
            {
                var piece = splinters[i];
                piece.transform.position = origin + (Vector2)piece.transform.up * 0.2f;
                piece.color = i % 2 == 0 ? color : (Color)White;
                piece.enabled = true;
            }
            Pulse(origin, color, 0.48f, 1.62f);
        }

        public void Shake() => shakeAge = 0.09f;

        private void ResetShake()
        {
            shakeAge = 0f;
            SetCameraOffset(Vector2.zero);
        }

        private void SetCameraOffset(Vector2 offset)
        {
            if (worldCamera != null) worldCamera.transform.position += (Vector3)(offset - CameraOffset);
            CameraOffset = offset;
        }

        public void SetField(bool active, Vector2 point, float radius)
        {
            field.enabled = fingerMark.enabled = active;
            if (!active) return;
            field.transform.position = fingerMark.transform.position = point;
            field.transform.localScale = Vector3.one * (2f * radius);
        }

        public SpriteRenderer MakeSprite(string name, Shape shape, Color color, int order, Transform parent)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = Art(shape);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public void Pulse(Vector2 point, Color color, float start, float end)
        {
            ref Ripple wave = ref pulses[nextPulse];
            nextPulse = (nextPulse + 1) % pulses.Length;
            wave.Age = 0f;
            wave.Color = wave.Sprite.color = color;
            wave.Start = start;
            wave.End = end;
            wave.Sprite.transform.position = point;
            wave.Sprite.transform.localScale = Vector3.one * start;
            wave.Sprite.enabled = true;
        }

        public void Tick(float dt)
        {
            feedback.Tick(dt);
            if (unlockTime > 0f)
            {
                unlockTime -= dt;
                if (unlockTime <= 0f) unlockBanner.gameObject.SetActive(false);
            }
            if (shakeAge > 0f)
            {
                shakeAge = Mathf.Max(0f, shakeAge - dt);
                float fade = shakeAge / 0.09f;
                float phase = (1f - fade) * 26f;
                SetCameraOffset(new Vector2(Mathf.Sin(phase), Mathf.Sin(phase * 1.46f)) * (0.047f * fade));
            }
            if (barkTime > 0f)
            {
                barkTime -= dt;
                if (barkTime <= 0f) ClearBark();
                else PositionBark();
            }
            if (nerveNoticeTime > 0f)
            {
                nerveNoticeTime -= dt;
                nerveNotice.alpha = Mathf.Clamp01(nerveNoticeTime / 0.19f);
                if (nerveNoticeTime <= 0f) nerveNotice.gameObject.SetActive(false);
            }
            if (breakTime > 0f)
            {
                breakTime = Mathf.Max(0f, breakTime - dt);
                float left = breakTime / 0.36f;
                for (int i = 0; i < splinters.Length; i++)
                {
                    var piece = splinters[i];
                    float distance = 0.2f + (1f - left * left) * (0.7f + 0.14f * (i % 3));
                    piece.transform.position = (Vector3)breakOrigin + piece.transform.up * distance;
                    piece.color = Tint(i % 2 == 0 ? breakColor : (Color)White, Mathf.Min(1f, left * 2.2f));
                    piece.enabled = breakTime > 0f;
                }
            }
            if (promptFading && promptAlpha > 0f)
                ghost.alpha = promptAlpha = Mathf.Max(0f, promptAlpha - dt * 2f);
            for (int i = 0; i < pulses.Length; i++)
            {
                ref Ripple wave = ref pulses[i];
                if (!wave.Sprite.enabled) continue;
                float t = Mathf.Clamp01((wave.Age += dt) / 0.34f);
                float fade = (1f - t) * (1f - t);
                wave.Sprite.transform.localScale = Vector3.one * Mathf.Lerp(wave.End, wave.Start, fade);
                wave.Sprite.color = Tint(wave.Color, fade);
                if (t >= 1f) wave.Sprite.enabled = false;
            }
        }

        public void PlaySound(Sound sound, float pitch = 1f) => feedback.Play(sound, pitch);

        private static Sprite MakeShape(Shape shape)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - size / 2f);
                float dy = Mathf.Abs(y + 0.5f - size / 2f);
                float distance;
                switch (shape)
                {
                    case Shape.Solid:
                        distance = -1f;
                        break;
                    case Shape.Circle:
                        distance = Mathf.Sqrt(dx * dx + dy * dy) - 62f;
                        break;
                    case Shape.Blob:
                        float angle = Mathf.Atan2(y + 0.5f - size / 2f, x + 0.5f - size / 2f);
                        float outline = 60f + 2.2f * Mathf.Sin(angle * 3f + 1f) + 1.05f * Mathf.Cos(angle * 5f + 0.4f);
                        distance = Mathf.Sqrt(dx * dx + dy * dy) - outline;
                        break;
                    case Shape.OpenMouth:
                        distance = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 51f) - 10f;
                        break;
                    case Shape.Ring:
                    case Shape.DashedRing:
                        distance = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 60f) - (shape == Shape.Ring ? 1.8f : 1.25f);
                        if (shape == Shape.DashedRing)
                        {
                            float arc = (Mathf.Atan2(y + 0.5f - size / 2f, x + 0.5f - size / 2f) + Mathf.PI) * (8f / Mathf.PI);
                            distance = Mathf.Max(distance, (Mathf.Abs(Mathf.Repeat(arc, 1f) - 0.5f) - 0.29f) * 22f);
                        }
                        break;
                    case Shape.Diamond:
                        distance = (dx + dy - 62f) * 0.7071f;
                        break;
                    case Shape.SpikedDiamond:
                        float diamond = (dx + dy - 57f) * 0.7071f;
                        float spur = Mathf.Min(Mathf.Abs(dx - 20f) + Mathf.Abs(dy - 37f),
                            Mathf.Abs(dy - 20f) + Mathf.Abs(dx - 37f));
                        distance = Mathf.Min(diamond, (spur - 11f) * 0.7071f);
                        break;
                    default:
                        float rx = Mathf.Max(0f, dx - 51f);
                        float ry = Mathf.Max(0f, dy - 51f);
                        distance = Mathf.Sqrt(rx * rx + ry * ry) - 12.5f;
                        break;
                }
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = shape.ToString(), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0,
                SpriteMeshType.FullRect, shape == Shape.Bar ? Vector4.one * 14f : Vector4.zero);
            sprite.name = texture.name;
            return sprite;
        }

        private void OnDestroy()
        {
            ResetShake();
            feedback?.Dispose();
            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                Destroy(sprite.texture);
                Destroy(sprite);
            }
        }
    }
}
