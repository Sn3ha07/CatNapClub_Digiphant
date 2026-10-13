using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DigiPhant
{
    // A Super Mario 64 style game layer: title screen, how-to-play, get-ready calibration, HUD with
    // Power Stars, coins and a timer, and a Course Clear screen. Coins exist only while playing;
    // the course itself is never changed. F1 shows the developer panel, Esc pauses.
    [DefaultExecutionOrder(300)]
    public class DigiPhantGame : MonoBehaviour
    {
        enum Phase { Title, HowTo, GetReady, Playing, Paused, Clear }
        enum Mission { Finish, Eat, Drink, Nap }
        static readonly string[] MissionNames = { "REACH THE FINISH", "EAT A BANANA", "DRINK SOME WATER", "TAKE A NAP" };

        [Tooltip("Distance between coins along the path, in Unity units.")]
        [Min(1)] public float coinSpacing = 3.5f;
        [Min(.5f)] public float coinPickupRadius = 2.2f;
        [Tooltip("Seconds of eating or drinking needed to earn that star.")]
        [Min(0)] public float activitySecondsForStar = 1.5f;
        [Tooltip("Background music volume. Add tracks in the DigiPhant Controller's Add your music section.")]
        [Range(0, 1)] public float musicVolume = .6f;

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        DigiPhantCameraPreview preview;
        AudioSource music, lullaby, clearJingle, calibrationMusic;
        bool lullabyActive; // the nap tune is playing (or paused) instead of the main theme
        Font font;
        Sprite rounded, starSprite, coinSprite;
        Phase phase = Phase.Title;
        bool cameraMode;
        float timer, eatSeconds, drinkSeconds, popupUntil, clearAt = -1, readyStarted = -1, goUntil;
        int coins;
        readonly bool[] stars = new bool[4];
        readonly List<Transform> coinObjects = new List<Transform>();
        Transform coinRoot, finishMarker;
        Material coinMaterial;
        Vector3 startPosition;

        // UI
        GameObject title, howTo, getReady, hud, popup, paused, clear;
        Text pressStart, bestOnTitle, readyStatus, readyCountdown, timeText, starCount, coinCount, actionText, controlsText, popupMission, clearStats, goText;
        Image popupStar;
        RawImage readyPreview, hudPreview;
        Image[] missionStars = new Image[4];
        Text[] missionLabels = new Text[4];
        Image[] clearStars = new Image[4];
        Canvas canvas;

        static readonly Color Red = new Color(.9f, .15f, .13f), Yellow = new Color(.98f, .82f, 0), Green = new Color(.26f, .69f, .28f),
            Blue = new Color(.02f, .61f, .85f), Ink = new Color(.08f, .05f, .18f), PanelBlue = new Color(.05f, .12f, .35f);

        bool started;

        void Start()
        {
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion && locomotion.travelRoot) startPosition = locomotion.travelRoot.position;
            Setup();
            started = true;
        }

        // Unity switches scripts off and on again when it recompiles during Play mode; OnDisable has removed
        // the screens by then, so rebuild them (Start only runs once).
        void OnEnable() { if (started) Setup(); }

        // Music: the clip in the controller's "Add your music" slot, else a file of that name in Resources/Music.
        // No music ships with the project, so either may be missing; then that moment is silent.
        static AudioClip Track(AudioClip chosen, string fileName) => chosen ? chosen : Resources.Load<AudioClip>("Music/" + fileName);

        // Creates whatever is missing, then shows the title screen if the screens had to be built.
        void Setup()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            preview = GetComponent<DigiPhantCameraPreview>();
            if (!font) font = Resources.Load<Font>("LuckiestGuy") ?? Font.CreateDynamicFontFromOSFont("Arial Rounded MT Bold", 32);
            if (!rounded || !starSprite || !coinSprite) { rounded = MakeRounded(); starSprite = MakeStar(); coinSprite = MakeCoin(); }
            if (!music)
            {
                music = gameObject.AddComponent<AudioSource>();
                music.clip = Track(controller ? controller.mainTheme : null, "MainTheme");
                music.loop = true; // start again from the beginning when the track ends
                music.playOnAwake = false; music.spatialBlend = 0; music.volume = musicVolume;
            }
            if (!lullaby)
            {
                lullaby = gameObject.AddComponent<AudioSource>();
                lullaby.clip = Track(controller ? controller.napMusic : null, "Lullaby"); // plays only while the elephant naps
                lullaby.loop = true; lullaby.playOnAwake = false; lullaby.spatialBlend = 0; lullaby.volume = musicVolume;
            }
            if (!clearJingle)
            {
                clearJingle = gameObject.AddComponent<AudioSource>();
                clearJingle.clip = Track(controller ? controller.courseClearMusic : null, "CourseClear"); // plays once on the Course Clear screen
                clearJingle.loop = false; clearJingle.playOnAwake = false; clearJingle.spatialBlend = 0;
            }
            if (!calibrationMusic)
            {
                calibrationMusic = gameObject.AddComponent<AudioSource>();
                calibrationMusic.clip = Track(controller ? controller.calibrationMusic : null, "StaffRoll"); // plays during the 10-second neutral pose
                calibrationMusic.loop = false; calibrationMusic.playOnAwake = false; calibrationMusic.spatialBlend = 0;
            }
            // P3's jump: use the scene's Digi Phant Jump if it has one (keeps its Inspector settings), else add it.
            if (!GetComponent<StudentWork.DigiPhantJump>())
                gameObject.AddComponent<StudentWork.DigiPhantJump>().performer = 3;
            if (canvas) return;
            BuildUi();
            SetDeveloperPanel(false);
            ShowTitle();
        }

        void OnDisable()
        {
            Time.timeScale = 1;
            StopMusic();
            if (locomotion) locomotion.holdActivities = false;
            // Clear the references now: Destroy finishes at the end of the frame, and OnEnable can run before that.
            if (coinRoot) Destroy(coinRoot.gameObject);
            coinRoot = null;
            coinObjects.Clear();
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
            SetDeveloperPanel(true);
        }

        void SetDeveloperPanel(bool on)
        {
            if (controller) controller.showControls = on;
            if (preview) preview.showPreview = on;
            // The jump's own Jump button would sit on the timer; the game uses Space instead.
            var jump = GetComponent<StudentWork.DigiPhantJump>();
            if (jump) jump.showTestButton = on;
        }

        // ---------- Flow ----------

        void ShowOnly(GameObject screen)
        {
            foreach (var s in new[] { title, howTo, getReady, hud, paused, clear }) s.SetActive(s == screen || (s == hud && screen == paused));
            var button = screen.GetComponentInChildren<Button>();
            if (button && EventSystem.current) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        void ShowTitle()
        {
            Time.timeScale = 1;
            StopMusic();
            phase = Phase.Title;
            StopElephant();
            controller.SetInputMode(InputMode.TestSliders);
            float best = PlayerPrefs.GetFloat("DigiPhantBestTime", 0);
            bestOnTitle.text = best > 0 ? "BEST TIME  " + FormatTime(best) : "";
            ShowOnly(title);
        }

        void ShowHowTo() { phase = Phase.HowTo; ShowOnly(howTo); }

        void PlayWithKeyboard()
        {
            cameraMode = false;
            controller.SetInputMode(InputMode.TestSliders);
            if (locomotion) locomotion.ResetPosition();
            StartRound();
        }

        void PlayWithCamera()
        {
            cameraMode = true;
            if (locomotion) locomotion.ResetPosition();
            controller.SetInputMode(InputMode.Camera);
            readyStarted = -1;
            phase = Phase.GetReady;
            ShowOnly(getReady);
        }

        void BeginNeutralPose()
        {
            controller.BeginCalibrationCountdown(Time.realtimeSinceStartup);
            readyStarted = Time.realtimeSinceStartup;
            // The Staff Roll plays from the start while everyone holds still.
            if (calibrationMusic && calibrationMusic.clip) { calibrationMusic.volume = musicVolume; calibrationMusic.Stop(); calibrationMusic.Play(); }
        }

        void StartRound()
        {
            timer = eatSeconds = drinkSeconds = 0; coins = 0; clearAt = -1;
            for (int i = 0; i < stars.Length; i++) stars[i] = false;
            SpawnCoins();
            phase = Phase.Playing;
            // The music starts with the round (from the beginning after a Course Clear) and loops.
            if (lullabyActive) { lullabyActive = false; lullaby.Stop(); }
            if (clearJingle) clearJingle.Stop();
            if (calibrationMusic) calibrationMusic.Stop(); // countdown over: hand over to the main theme
            if (music && music.clip && !music.isPlaying)
            {
                music.volume = musicVolume;
                if (music.time > 0) music.UnPause(); else music.Play();
            }
            goUntil = Time.unscaledTime + 1.6f;
            ShowOnly(hud);
            popup.SetActive(false);
            controlsText.text = cameraMode
                ? "P1  LEFT HAND UP = GO     RIGHT HAND UP = BACK          P2  RIGHT HAND UP = TURN RIGHT     LEFT HAND UP = TURN LEFT\n" +
                  "P3  BANANA = EAT     BOTTLE = DRINK     EYES CLOSED + PRAYING HANDS = NAP     THROW BOTH HANDS UP = JUMP          ESC = PAUSE"
                : "W / UP = GO     S / DOWN = BACK     A D / LEFT RIGHT = TURN     SHIFT = RUN\n" +
                  "HOLD E = EAT     HOLD Q = DRINK     Z = NAP / WAKE UP     SPACE = JUMP          ESC = PAUSE";
            hudPreview.gameObject.SetActive(cameraMode);
        }

        void Pause()
        {
            if (phase != Phase.Playing) return;
            phase = Phase.Paused; Time.timeScale = 0;
            if (music) music.Pause();
            if (lullaby) lullaby.Pause();
            ShowOnly(paused);
        }

        void Resume()
        {
            Time.timeScale = 1; phase = Phase.Playing;
            if (lullabyActive) lullaby.UnPause(); else if (music) music.UnPause();
            ShowOnly(hud);
        }

        void StopMusic()
        {
            lullabyActive = false;
            if (music) music.Stop();
            if (lullaby) lullaby.Stop();
            if (clearJingle) clearJingle.Stop();
            if (calibrationMusic) calibrationMusic.Stop();
        }

        // While the elephant lies down and sleeps, the main theme pauses and the lullaby plays from its start;
        // when it gets up, the lullaby stops and the main theme carries on from where it paused.
        void UpdateMusic(bool napping)
        {
            if (!music || !lullaby || !lullaby.clip) return;
            if (napping && !lullabyActive)
            {
                lullabyActive = true;
                music.Pause();
                lullaby.volume = musicVolume;
                lullaby.Play();
            }
            else if (!napping && lullabyActive)
            {
                lullabyActive = false;
                lullaby.Stop();
                music.UnPause();
            }
        }
        void Restart() { Time.timeScale = 1; if (cameraMode) PlayWithCamera(); else PlayWithKeyboard(); }

        void StopElephant()
        {
            if (!locomotion) return;
            locomotion.testForward = locomotion.testSteering = 0;
            locomotion.testEat = locomotion.testDrink = locomotion.testSleep = false;
        }

        // ---------- Update ----------

        void Update()
        {
            if (!canvas || !pressStart) return; // screens not built (yet) or being rebuilt
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) SetDeveloperPanel(!controller.showControls);
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (phase == Phase.Playing) Pause();
                else if (phase == Phase.Paused) Resume();
                else if (phase == Phase.HowTo || phase == Phase.GetReady) ShowTitle();
            }
            pressStart.color = new Color(1, 1, 1, .55f + .45f * Mathf.Sin(Time.unscaledTime * 5));
            // Sleep, eat and drink only count during a round, not on the title or Get Ready screens.
            if (locomotion) locomotion.holdActivities = phase != Phase.Playing && phase != Phase.Paused;
            foreach (var coin in coinObjects) if (coin && coin.gameObject.activeSelf) coin.Rotate(0, 180 * Time.deltaTime, 0, Space.World);

            if (phase == Phase.GetReady) UpdateGetReady();
            if (phase == Phase.Playing) UpdatePlaying(keyboard);
            if (phase == Phase.Clear || phase == Phase.Playing) UpdatePopup();
            goText.gameObject.SetActive(phase == Phase.Playing && Time.unscaledTime < goUntil);
        }

        void UpdateGetReady()
        {
            float now = Time.realtimeSinceStartup;
            var texture = preview ? preview.PreviewTexture : null;
            readyPreview.texture = texture;
            readyPreview.color = texture ? Color.white : new Color(.1f, .1f, .2f);
            var lines = new List<string>();
            for (int slot = 1; slot <= controller.performerCount; slot++)
                lines.Add(controller.IsTracked(slot, now) ? $"P{slot}  READY!" : $"P{slot}  STEP INTO YOUR ZONE");
            if (!texture) lines.Add("CAMERA STARTING...");
            readyStatus.text = string.Join("\n", lines);
            if (controller.CalibrationPending && readyStarted > 0)
                readyCountdown.text = "HOLD STILL...  " + Mathf.Max(0, Mathf.CeilToInt(10 - (now - readyStarted)));
            else if (readyStarted > 0 && !controller.IsCalibrated)
                readyCountdown.text = "TRY AGAIN: EVERYONE IN VIEW?";
            else readyCountdown.text = "EVERYONE IN YOUR ZONE? REST YOUR HANDS LOW, THEN PRESS START";
            // The countdown music only lasts as long as the countdown (it stops if calibration fails too).
            if (!controller.CalibrationPending && calibrationMusic && calibrationMusic.isPlaying) calibrationMusic.Stop();
            if (controller.IsCalibrated) StartRound();
        }

        void UpdatePlaying(Keyboard keyboard)
        {
            timer += Time.deltaTime;
            if (!cameraMode && keyboard != null && locomotion)
            {
                float forward = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                float turn = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                bool run = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                locomotion.testForward = forward > 0 ? (run ? 1 : .55f) : forward < 0 ? -.8f : 0;
                locomotion.testSteering = turn;
                locomotion.testEat = keyboard.eKey.isPressed;
                locomotion.testDrink = keyboard.qKey.isPressed;
                if (keyboard.zKey.wasPressedThisFrame) locomotion.testSleep = !locomotion.testSleep;
                var jump = GetComponent<StudentWork.DigiPhantJump>();
                if (jump && keyboard.spaceKey.wasPressedThisFrame) jump.TriggerJump(Time.realtimeSinceStartup);
            }
            if (!locomotion || !locomotion.travelRoot) return;
            var rest = locomotion.CurrentRest;
            UpdateMusic(rest == DigiPhantLocomotion.Rest.LyingDown || rest == DigiPhantLocomotion.Rest.Sleeping);
            actionText.text = rest == DigiPhantLocomotion.Rest.Eating ? "NOM NOM!" : rest == DigiPhantLocomotion.Rest.Drinking ? "GLUG GLUG!"
                : rest == DigiPhantLocomotion.Rest.LyingDown || rest == DigiPhantLocomotion.Rest.Sleeping ? "ZZZ..."
                : rest == DigiPhantLocomotion.Rest.GettingUp ? "GOOD MORNING!" : "";
            if (rest == DigiPhantLocomotion.Rest.Eating) eatSeconds += Time.deltaTime;
            if (rest == DigiPhantLocomotion.Rest.Drinking) drinkSeconds += Time.deltaTime;
            if (eatSeconds >= activitySecondsForStar) Award(Mission.Eat);
            if (drinkSeconds >= activitySecondsForStar) Award(Mission.Drink);
            if (rest == DigiPhantLocomotion.Rest.Sleeping) Award(Mission.Nap);

            Vector3 here = locomotion.travelRoot.position;
            foreach (var coin in coinObjects)
            {
                if (!coin || !coin.gameObject.activeSelf) continue;
                if (Vector2.Distance(new Vector2(coin.position.x, coin.position.z), new Vector2(here.x, here.z)) > coinPickupRadius) continue;
                coin.gameObject.SetActive(false);
                coins++;
            }
            if (finishMarker && timer > 5 && clearAt < 0 &&
                Vector2.Distance(new Vector2(finishMarker.position.x, finishMarker.position.z), new Vector2(here.x, here.z)) < 3)
            {
                Award(Mission.Finish);
                clearAt = Time.unscaledTime + 2.2f; // let the star celebration play first
            }
            if (clearAt > 0 && Time.unscaledTime >= clearAt) ShowClear();

            timeText.text = "TIME  " + FormatTime(timer);
            starCount.text = "x " + stars.Count(s => s);
            coinCount.text = "x " + coins;
            for (int i = 0; i < 4; i++)
            {
                missionStars[i].color = stars[i] ? Yellow : new Color(1, 1, 1, .25f);
                missionLabels[i].color = stars[i] ? Yellow : Color.white;
            }
            if (cameraMode) hudPreview.texture = preview ? preview.PreviewTexture : null;
        }

        void Award(Mission mission)
        {
            if (stars[(int)mission]) return;
            stars[(int)mission] = true;
            popupMission.text = MissionNames[(int)mission];
            popup.SetActive(true);
            popupUntil = Time.unscaledTime + 2.2f;
        }

        void UpdatePopup()
        {
            if (!popup.activeSelf) return;
            float left = popupUntil - Time.unscaledTime;
            popupStar.rectTransform.localRotation = Quaternion.Euler(0, Time.unscaledTime * 360 % 360, 0);
            popupStar.rectTransform.localScale = Vector3.one * (1 + .12f * Mathf.Sin(Time.unscaledTime * 10));
            if (left <= 0) popup.SetActive(false);
        }

        void ShowClear()
        {
            phase = Phase.Clear;
            clearAt = -1;
            // All music stops; the Course Clear jingle plays once and then the results screen is quiet.
            StopMusic();
            if (clearJingle && clearJingle.clip) { clearJingle.volume = musicVolume; clearJingle.Play(); }
            StopElephant();
            controller.SetInputMode(InputMode.TestSliders); // performers can relax; the elephant stays put
            float best = PlayerPrefs.GetFloat("DigiPhantBestTime", 0);
            bool record = best <= 0 || timer < best;
            if (record) { PlayerPrefs.SetFloat("DigiPhantBestTime", timer); PlayerPrefs.Save(); }
            clearStats.text = $"TIME   {FormatTime(timer)}{(record ? "   NEW RECORD!" : "")}\nCOINS  {coins} / {coinObjects.Count}\nSTARS  {stars.Count(s => s)} / 4";
            for (int i = 0; i < 4; i++) clearStars[i].color = stars[i] ? Yellow : new Color(1, 1, 1, .2f);
            ShowOnly(clear);
        }

        static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60), s = (int)(seconds % 60), c = (int)(seconds * 100 % 100);
            return $"{m:00}'{s:00}\"{c:00}";
        }

        // ---------- Coins (runtime only; the course is untouched) ----------

        void SpawnCoins()
        {
            if (coinRoot) Destroy(coinRoot.gameObject);
            coinObjects.Clear();
            coinRoot = new GameObject("Game coins (play only)").transform;
            var course = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name.StartsWith("Savannah Course") && t.gameObject.activeInHierarchy);
            if (!course) return;
            finishMarker = course.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "04 Finish marker");
            var path = course.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name == "Dirt path" || f.name == "Open path");
            if (!path) return;
            // The path mesh stores a left/right edge pair per route corner; their midpoints are the route.
            var vertices = path.sharedMesh.vertices;
            var route = new List<Vector3>();
            for (int i = 0; i + 1 < vertices.Length; i += 2) route.Add(path.transform.TransformPoint((vertices[i] + vertices[i + 1]) / 2));
            if (!coinMaterial)
            {
                coinMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                coinMaterial.SetColor("_BaseColor", new Color(1, .8f, .1f));
                coinMaterial.EnableKeyword("_EMISSION");
                coinMaterial.SetColor("_EmissionColor", new Color(.6f, .45f, 0));
                coinMaterial.SetFloat("_Smoothness", .6f);
                coinMaterial.SetFloat("_Metallic", .4f);
            }
            float travelled = 0, next = 4; // first coin a few steps from the start line
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route[i - 1], b = route[i];
                float length = Vector3.Distance(a, b);
                while (next <= travelled + length)
                {
                    Vector3 spot = Vector3.Lerp(a, b, (next - travelled) / length);
                    var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(coin.GetComponent<Collider>()); // pickups are by distance, no physics
                    coin.name = "Coin";
                    coin.transform.SetParent(coinRoot, false);
                    coin.transform.position = new Vector3(spot.x, startPosition.y + 1.2f, spot.z);
                    coin.transform.rotation = Quaternion.Euler(90, 0, 0);
                    coin.transform.localScale = new Vector3(.8f, .06f, .8f);
                    coin.GetComponent<MeshRenderer>().sharedMaterial = coinMaterial;
                    coinObjects.Add(coin.transform);
                    next += coinSpacing;
                }
                travelled += length;
            }
        }

        // ---------- UI building ----------

        void BuildUi()
        {
            if (!FindAnyObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var root = new GameObject("DigiPhant Game UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var r = root.transform;

            // Title
            title = Screen(r, "Title", new Color(0, 0, .1f, .35f));
            Label(title.transform, Rainbow("DIGIPHANT 64"), 150, new Vector2(.5f, .76f), new Vector2(1600, 200));
            Label(title.transform, "CAT NAP CLUB", 54, new Vector2(.5f, .64f), new Vector2(1000, 80)).color = Yellow;
            pressStart = Label(title.transform, "PRESS START", 48, new Vector2(.5f, .54f), new Vector2(800, 70));
            var menu = Column(title.transform, new Vector2(.5f, .33f), new Vector2(620, 300));
            MakeButton(menu, "PLAY WITH CAMERA", Red, PlayWithCamera);
            MakeButton(menu, "PLAY WITH KEYBOARD", Green, PlayWithKeyboard);
            MakeButton(menu, "HOW TO PLAY", Blue, ShowHowTo);
            bestOnTitle = Label(title.transform, "", 34, new Vector2(.5f, .1f), new Vector2(800, 50));
            bestOnTitle.color = Yellow;

            // How to play
            howTo = Screen(r, "How to play", new Color(0, 0, .1f, .55f));
            Label(howTo.transform, Rainbow("HOW TO PLAY"), 96, new Vector2(.5f, .9f), new Vector2(1400, 130));
            string[] heads = { "P1  LEFT ZONE", "P2  MIDDLE ZONE", "P3  RIGHT ZONE" };
            string[] bodies = {
                "MOVES THE ELEPHANT\n\nRAISE LEFT HAND\n= WALK FORWARD\n(HIGHER = RUN)\n\nRAISE RIGHT HAND\n= BACK UP",
                "STEERS\n\nRAISE RIGHT HAND\n= TURN RIGHT\n\nRAISE LEFT HAND\n= TURN LEFT\n\nTHE NEW DIRECTION STAYS",
                "EATS, DRINKS, NAPS AND JUMPS\n\nSHOW A BANANA = EAT\n\nSHOW A WATER BOTTLE = DRINK\n\nCLOSE EYES + PRAYING HANDS\n= NAP (OPEN EYES TO WAKE)\n\nTHROW BOTH HANDS UP FAST\n= JUMP OVER THE LOG!" };
            Color[] playerColors = { new Color(.39f, .9f, .39f), new Color(.31f, .7f, 1f), new Color(1, .63f, .39f) };
            for (int i = 0; i < 3; i++)
            {
                var card = Box(howTo.transform, new Vector2(.2f + i * .3f, .5f), new Vector2(520, 560), PanelBlue);
                Label(card, heads[i], 44, new Vector2(.5f, .9f), new Vector2(480, 60)).color = playerColors[i];
                Label(card, bodies[i], 30, new Vector2(.5f, .42f), new Vector2(480, 440));
            }
            Label(howTo.transform, "COLLECT COINS ON THE PATH AND EARN 4 POWER STARS:  REACH THE FINISH  -  EAT  -  DRINK  -  NAP\n" +
                "NO CAMERA?  PLAY WITH KEYBOARD:  W A S D / ARROWS MOVE,  SHIFT RUNS,  E EATS,  Q DRINKS,  Z NAPS,  SPACE JUMPS", 30,
                new Vector2(.5f, .14f), new Vector2(1800, 100)).color = Yellow;
            MakeButton(Column(howTo.transform, new Vector2(.5f, .05f), new Vector2(400, 80)), "BACK", Blue, ShowTitle);

            // Get ready (camera mode)
            getReady = Screen(r, "Get ready", new Color(0, 0, .1f, .55f));
            Label(getReady.transform, Rainbow("GET READY!"), 96, new Vector2(.5f, .9f), new Vector2(1400, 130));
            readyPreview = new GameObject("Camera preview", typeof(RawImage)).GetComponent<RawImage>();
            Place(readyPreview.rectTransform, getReady.transform, new Vector2(.36f, .5f), new Vector2(880, 560));
            var statusBox = Box(getReady.transform, new Vector2(.78f, .56f), new Vector2(560, 420), PanelBlue);
            readyStatus = Label(statusBox, "", 40, new Vector2(.5f, .5f), new Vector2(520, 380));
            readyCountdown = Label(getReady.transform, "", 40, new Vector2(.5f, .17f), new Vector2(1700, 60));
            readyCountdown.color = Yellow;
            var readyButtons = Column(getReady.transform, new Vector2(.78f, .3f), new Vector2(560, 180));
            MakeButton(readyButtons, "START  (HOLD STILL 10 S)", Green, BeginNeutralPose);
            MakeButton(readyButtons, "BACK", Blue, ShowTitle);

            // HUD
            hud = new GameObject("HUD", typeof(RectTransform));
            Stretch(hud.GetComponent<RectTransform>(), r);
            Icon(hud.transform, starSprite, new Vector2(.045f, .92f), 90, Yellow);
            starCount = Label(hud.transform, "x 0", 64, new Vector2(.115f, .92f), new Vector2(200, 90));
            starCount.alignment = TextAnchor.MiddleLeft;
            Icon(hud.transform, coinSprite, new Vector2(.045f, .82f), 72, Color.white);
            coinCount = Label(hud.transform, "x 0", 56, new Vector2(.115f, .82f), new Vector2(200, 80));
            coinCount.alignment = TextAnchor.MiddleLeft;
            timeText = Label(hud.transform, "TIME  00'00\"00", 56, new Vector2(.84f, .93f), new Vector2(560, 80));
            var missions = Box(hud.transform, new Vector2(.12f, .58f), new Vector2(400, 250), new Color(.05f, .12f, .35f, .6f));
            for (int i = 0; i < 4; i++)
            {
                missionStars[i] = Icon(missions, starSprite, new Vector2(.1f, .82f - i * .21f), 42, Color.white);
                missionLabels[i] = Label(missions, MissionNames[i], 28, new Vector2(.58f, .82f - i * .21f), new Vector2(320, 44));
                missionLabels[i].alignment = TextAnchor.MiddleLeft;
            }
            actionText = Label(hud.transform, "", 84, new Vector2(.5f, .78f), new Vector2(1000, 120));
            actionText.color = Yellow;
            goText = Label(hud.transform, Rainbow("LET'S GO!"), 140, new Vector2(.5f, .55f), new Vector2(1400, 200));
            var hint = Box(hud.transform, new Vector2(.5f, .055f), new Vector2(1860, 100), new Color(.05f, .12f, .35f, .7f));
            controlsText = Label(hint, "", 26, new Vector2(.5f, .5f), new Vector2(1820, 90));
            hudPreview = new GameObject("Camera preview", typeof(RawImage)).GetComponent<RawImage>();
            Place(hudPreview.rectTransform, hud.transform, new Vector2(.88f, .22f), new Vector2(400, 250));

            // Star popup
            popup = new GameObject("Star popup", typeof(RectTransform));
            Stretch(popup.GetComponent<RectTransform>(), hud.transform);
            popupStar = Icon(popup.transform, starSprite, new Vector2(.5f, .56f), 300, Yellow);
            Label(popup.transform, Rainbow("YOU GOT A STAR!"), 110, new Vector2(.5f, .33f), new Vector2(1600, 150));
            popupMission = Label(popup.transform, "", 50, new Vector2(.5f, .24f), new Vector2(1200, 70));

            // Pause
            paused = Screen(r, "Paused", new Color(0, 0, .1f, .6f));
            Label(paused.transform, Rainbow("PAUSE"), 140, new Vector2(.5f, .7f), new Vector2(1000, 200));
            var pauseMenu = Column(paused.transform, new Vector2(.5f, .38f), new Vector2(620, 400));
            MakeButton(pauseMenu, "CONTINUE", Green, Resume);
            MakeButton(pauseMenu, "RESTART", Red, Restart);
            MakeButton(pauseMenu, "TITLE SCREEN", Blue, ShowTitle);
            MakeButton(pauseMenu, "DEVELOPER PANEL (F1)", new Color(.4f, .4f, .5f), () => SetDeveloperPanel(!controller.showControls));

            // Course clear
            clear = Screen(r, "Course clear", new Color(0, 0, .1f, .55f));
            Label(clear.transform, Rainbow("COURSE CLEAR!"), 140, new Vector2(.5f, .78f), new Vector2(1700, 200));
            for (int i = 0; i < 4; i++) clearStars[i] = Icon(clear.transform, starSprite, new Vector2(.38f + i * .08f, .6f), 120, Yellow);
            clearStats = Label(clear.transform, "", 52, new Vector2(.5f, .42f), new Vector2(1200, 220));
            var clearMenu = Column(clear.transform, new Vector2(.5f, .16f), new Vector2(620, 190));
            MakeButton(clearMenu, "PLAY AGAIN", Green, Restart);
            MakeButton(clearMenu, "TITLE SCREEN", Blue, ShowTitle);
        }

        GameObject Screen(Transform parent, string name, Color dim)
        {
            var go = new GameObject(name, typeof(Image));
            Stretch(go.GetComponent<RectTransform>(), parent);
            go.GetComponent<Image>().color = dim;
            return go;
        }

        static void Stretch(RectTransform t, Transform parent)
        {
            t.SetParent(parent, false);
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.offsetMin = t.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform t, Transform parent, Vector2 anchor, Vector2 size)
        {
            t.SetParent(parent, false);
            t.anchorMin = t.anchorMax = anchor; t.sizeDelta = size; t.anchoredPosition = Vector2.zero;
        }

        Transform Box(Transform parent, Vector2 anchor, Vector2 size, Color color)
        {
            var image = new GameObject("Box", typeof(Image)).GetComponent<Image>();
            Place(image.rectTransform, parent, anchor, size);
            image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color;
            // A white rim only on solid panels: under a see-through panel it would show through as grey.
            if (color.a >= 1)
            {
                var outline = image.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white; outline.effectDistance = new Vector2(4, -4);
            }
            return image.transform;
        }

        Transform Column(Transform parent, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            Place(go.GetComponent<RectTransform>(), parent, anchor, size);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 18; layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = true;
            return go.transform;
        }

        Text Label(Transform parent, string text, int size, Vector2 anchor, Vector2 box)
        {
            var label = new GameObject("Text", typeof(Text)).GetComponent<Text>();
            Place(label.rectTransform, parent, anchor, box);
            label.font = font; label.fontSize = size; label.text = text; label.supportRichText = true;
            label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Overflow;
            // SM64-style chunky dark outline and drop shadow.
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink; outline.effectDistance = new Vector2(Mathf.Max(2, size / 22f), -Mathf.Max(2, size / 22f));
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .6f); shadow.effectDistance = new Vector2(size / 14f, -size / 14f);
            return label;
        }

        Image Icon(Transform parent, Sprite sprite, Vector2 anchor, float size, Color color)
        {
            var image = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
            Place(image.rectTransform, parent, anchor, new Vector2(size, size));
            image.sprite = sprite; image.color = color; image.preserveAspect = true;
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .55f); shadow.effectDistance = new Vector2(4, -4);
            return image;
        }

        void MakeButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var image = new GameObject(text, typeof(Image), typeof(Button)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color;
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white; outline.effectDistance = new Vector2(4, -4);
            var button = image.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.selectedColor = new Color(1.2f, 1.2f, 1.05f);
            colors.pressedColor = new Color(.8f, .8f, .8f); button.colors = colors;
            button.onClick.AddListener(onClick);
            var label = Label(image.transform, text, 40, new Vector2(.5f, .5f), Vector2.zero);
            Stretch(label.rectTransform, image.transform);
        }

        static string Rainbow(string text)
        {
            // Cycling red, yellow, green, blue letters like the Super Mario 64 logo.
            string[] palette = { "#E52521", "#FBD000", "#43B047", "#049CD8" };
            int k = 0;
            return string.Concat(text.Select(c => c == ' ' ? " " : $"<color={palette[k++ % palette.Length]}>{c}</color>"));
        }

        // ---------- Procedural sprites ----------

        static Sprite MakeRounded()
        {
            const int size = 64, radius = 20;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x, x - (size - 1 - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y, y - (size - 1 - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + .5f)));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeStar()
        {
            const int size = 128;
            var points = Enumerable.Range(0, 10).Select(i =>
            {
                float r = i % 2 == 0 ? .48f : .22f, a = Mathf.PI / 2 + i * Mathf.PI / 5;
                return new Vector2(.5f + Mathf.Cos(a) * r, .48f + Mathf.Sin(a) * r);
            }).ToArray();
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + .5f, y + .5f) / size;
                    bool inside = Inside(p, points);
                    // White fill (tinted by the Image colour) with a dark rim and two eye dots, SM64 style.
                    bool rim = inside && !Inside(.5f * Vector2.one + (p - .5f * Vector2.one) * 1.12f, points);
                    bool eye = Mathf.Abs(p.y - .52f) < .06f && (Mathf.Abs(p.x - .44f) < .022f || Mathf.Abs(p.x - .56f) < .022f);
                    texture.SetPixel(x, y, !inside ? Color.clear : rim || eye ? new Color(.25f, .15f, 0) : Color.white);
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        }

        static Sprite MakeCoin()
        {
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size - .5f, dy = (y + .5f) / size - .5f;
                    float ex = dx / .36f, ey = dy / .48f, e = Mathf.Sqrt(ex * ex + ey * ey);
                    bool slot = Mathf.Abs(dx) < .035f && Mathf.Abs(dy) < .25f;
                    texture.SetPixel(x, y, e > 1 ? Color.clear : e > .86f ? new Color(.55f, .35f, 0) : slot ? new Color(.85f, .6f, 0) : new Color(1, .82f, .1f));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        }

        static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
