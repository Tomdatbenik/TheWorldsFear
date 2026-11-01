using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TWF
{
    // The character creation scene: a large animated preview you can turn around, a turnaround strip
    // with all four directions, and the look options. It's also the test bench for new character art.
    public class CharacterCreator : MonoBehaviour
    {
        // The look picked here, carried to the title screen's weapon pick and into the new game.
        public static Appearance Pending;

        Appearance look;
        readonly BodyRenderer.Gear gear = new BodyRenderer.Gear();
        bool showBones;
        static readonly WeaponType?[] WeaponChoices = { null, WeaponType.Sword, WeaponType.Axe, WeaponType.Hammer, WeaponType.Daggers, WeaponType.Spear, WeaponType.Bow, WeaponType.Crossbow, WeaponType.Staff, WeaponType.Wand };
        RawImage preview;
        readonly RawImage[] turnaround = new RawImage[4];
        readonly Dictionary<string, Texture2D> frames = new Dictionary<string, Texture2D>();
        RectTransform options;
        int facing;          // 0 front, 1 right, 2 back, 3 left
        bool walking = true;
        float animT;
        int frame;

        static readonly string[] FacingNames = { "Front", "Right", "Back", "Left" };

        // Live lighting test: the character is drawn from albedo + normal maps by a preview camera
        // with a key light you can turn and an optional torch.
        bool dynamicLight = true;
        float lightAngle = 135f;     // degrees around the character, seen from the front
        bool torch;
        Camera previewCam;
        RenderTexture previewRT;
        SpriteRenderer previewSprite;
        Material previewMat;
        Light keyLight, torchLight;
        readonly Dictionary<string, (Texture2D albedo, Texture2D normal)> mapCache = new Dictionary<string, (Texture2D, Texture2D)>();
        TextMeshProUGUI lightLabel;

        // Reference board: images from <project>/ArtReference, shown beside the character (editor only, never shipped).
        readonly List<Texture2D> references = new List<Texture2D>();
        int refIndex = -1;
        RawImage refImage;

        void Start()
        {
            Time.timeScale = 1f;
            look = Pending ?? new Appearance { body = 1, skin = 1 };
            var root = (RectTransform)UIFactory.CreateCanvas("Creator", 10).transform;
            var bg = UIFactory.Image("Background", root, GameAssets.Pixel, new Color(0.11f, 0.1f, 0.12f));
            bg.rectTransform.Stretch();

            var title = UIFactory.Text("Title", root, "Create your hero", 56, Palette.Gold);
            title.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(1200, 80));

            // Stage: a lit floor and the big preview.
            var stage = UIFactory.Panel("Stage", root, new Color(0.16f, 0.15f, 0.17f, 1f));
            stage.rectTransform.anchorMin = new Vector2(0.03f, 0.2f);
            stage.rectTransform.anchorMax = new Vector2(0.52f, 0.88f);
            stage.rectTransform.offsetMin = stage.rectTransform.offsetMax = Vector2.zero;
            var floor = UIFactory.Image("Floor", stage.transform, GameAssets.SoftCircle, new Color(0, 0, 0, 0.45f));
            floor.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 60));
            preview = UIFactory.Rect("Preview", stage.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(64 * 6, 96 * 6)).gameObject.AddComponent<RawImage>();
            refImage = UIFactory.Rect("Reference", stage.transform).Place(new Vector2(0.75f, 0), new Vector2(0, 80), new Vector2(64 * 6, 96 * 6)).gameObject.AddComponent<RawImage>();
            refImage.rectTransform.pivot = new Vector2(0.5f, 0);
            refImage.gameObject.SetActive(false);
            BuildLightRig();
            LoadReferences();
            var lightL = UIFactory.Button("LightLeft", stage.transform, "Light <", () => { lightAngle += 30f; UpdateLights(); }, null, 22);
            ((RectTransform)lightL.transform).Place(new Vector2(0, 0), new Vector2(16, 16), new Vector2(150, 56));
            var lightR = UIFactory.Button("LightRight", stage.transform, "Light >", () => { lightAngle -= 30f; UpdateLights(); }, null, 22);
            ((RectTransform)lightR.transform).Place(new Vector2(0, 0), new Vector2(176, 16), new Vector2(150, 56));
            var torchB = UIFactory.Button("Torch", stage.transform, "Torch", () => { torch = !torch; UpdateLights(); }, null, 22);
            ((RectTransform)torchB.transform).Place(new Vector2(0, 0), new Vector2(336, 16), new Vector2(120, 56));
            var mode = UIFactory.Button("Mode", stage.transform, "Dynamic / Baked", () => { dynamicLight = !dynamicLight; Redraw(); }, null, 22);
            ((RectTransform)mode.transform).Place(new Vector2(1, 0), new Vector2(-16, 16), new Vector2(230, 56));
            ((RectTransform)mode.transform).pivot = new Vector2(1, 0);
            var refB = UIFactory.Button("Ref", stage.transform, "Reference", NextReference, null, 22);
            ((RectTransform)refB.transform).Place(new Vector2(1, 1), new Vector2(-16, -16), new Vector2(180, 56));
            ((RectTransform)refB.transform).pivot = new Vector2(1, 1);
            lightLabel = UIFactory.Text("LightInfo", stage.transform, "", 20, Palette.MutedText, TextAlignmentOptions.Left);
            lightLabel.rectTransform.Place(new Vector2(0, 1), new Vector2(16, -16), new Vector2(420, 40));
            var turnL = UIFactory.Button("TurnLeft", stage.transform, "<", () => { facing = (facing + 3) % 4; Redraw(); }, null, 44);
            ((RectTransform)turnL.transform).Place(new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(90, 110));
            var turnR = UIFactory.Button("TurnRight", stage.transform, ">", () => { facing = (facing + 1) % 4; Redraw(); }, null, 44);
            ((RectTransform)turnR.transform).Place(new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(90, 110));
            var walk = UIFactory.Button("Walk", stage.transform, "Walk / Idle", () => { walking = !walking; }, null, 26);
            ((RectTransform)walk.transform).Place(new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(240, 60));

            // Turnaround strip.
            var strip = UIFactory.Panel("Turnaround", root, new Color(0.14f, 0.13f, 0.15f, 1f));
            strip.rectTransform.anchorMin = new Vector2(0.03f, 0.02f);
            strip.rectTransform.anchorMax = new Vector2(0.52f, 0.185f);
            strip.rectTransform.offsetMin = strip.rectTransform.offsetMax = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                turnaround[i] = UIFactory.Rect("Dir" + i, strip.transform).gameObject.AddComponent<RawImage>();
                var rt = turnaround[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2((i + 0.5f) / 4f, 0.5f);
                rt.sizeDelta = new Vector2(64 * 1.6f, 96 * 1.6f);
            }

            // Options.
            var panel = UIFactory.Panel("Options", root, new Color(0.14f, 0.12f, 0.15f, 0.98f));
            panel.rectTransform.anchorMin = new Vector2(0.55f, 0.2f);
            panel.rectTransform.anchorMax = new Vector2(0.97f, 0.88f);
            panel.rectTransform.offsetMin = panel.rectTransform.offsetMax = Vector2.zero;
            var optView = UIFactory.Rect("View", panel.transform).Stretch(20);
            optView.gameObject.AddComponent<RectMask2D>();
            var optScroll = optView.gameObject.AddComponent<ScrollRect>();
            optScroll.horizontal = false;
            options = UIFactory.Rect("List", optView);
            options.anchorMin = new Vector2(0, 1);
            options.anchorMax = new Vector2(1, 1);
            options.pivot = new Vector2(0.5f, 1);
            options.sizeDelta = Vector2.zero;
            options.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            optScroll.content = options;
            var v = options.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4;
            v.childForceExpandHeight = false;
            v.childControlHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            var back = UIFactory.Button("Back", root, "Back", () => SceneManager.LoadScene("Title"), null, 32);
            ((RectTransform)back.transform).Place(new Vector2(0.55f, 0.04f), Vector2.zero, new Vector2(240, 90));
            ((RectTransform)back.transform).pivot = new Vector2(0, 0);
            var random = UIFactory.Button("Random", root, "Randomize", () => { look = Appearance.Random(new System.Random()); RefreshOptions(); Redraw(); }, null, 32);
            ((RectTransform)random.transform).Place(new Vector2(0.55f, 0.04f), new Vector2(260, 0), new Vector2(260, 90));
            ((RectTransform)random.transform).pivot = new Vector2(0, 0);
            var next = UIFactory.Button("Next", root, "Choose weapon >", () =>
            {
                Pending = look;
                TitleScreen.OpenNewHero = true;
                SceneManager.LoadScene("Title");
            }, new Color(0.2f, 0.4f, 0.15f, 1f), 32);
            ((RectTransform)next.transform).Place(new Vector2(0.97f, 0.04f), Vector2.zero, new Vector2(340, 90));
            ((RectTransform)next.transform).pivot = new Vector2(1, 0);

            RefreshOptions();
            Redraw();
        }

        void RefreshOptions()
        {
            foreach (Transform c in options) Destroy(c.gameObject);
            Option("Body", look.Female ? "Female" : "Male", d => { look.body = 1 - look.body; if (look.Female) look.beard = 0; });
            Option("Skin", $"Tone {look.skin + 1}", d => look.skin = Wrap(look.skin + d, Appearance.SkinTones.Length));
            Option("Hair", (look.Female ? Appearance.FemaleHair : Appearance.MaleHair)[look.hair], d => look.hair = Wrap(look.hair + d, 5));
            Option("Hair color", $"Color {look.hairColor + 1}", d => look.hairColor = Wrap(look.hairColor + d, Appearance.HairColors.Length));
            Header("Equipment test");
            Option("Chest", gear.chest.ToString(), d => gear.chest = (ChestStyle)Wrap((int)gear.chest + d, 6));
            Option("Skirt", gear.skirt ? "Yes" : "No", d => gear.skirt = !gear.skirt);
            Option("Helm", gear.helm.ToString(), d => gear.helm = (HelmStyle)Wrap((int)gear.helm + d, 7));
            Option("Gloves", gear.gloves.ToString(), d => gear.gloves = (GloveStyle)Wrap((int)gear.gloves + d, 5));
            Option("Boots", gear.boots.ToString(), d => gear.boots = (BootStyle)Wrap((int)gear.boots + d, 5));
            Option("Cape", gear.cape ? "Yes" : "No", d => gear.cape = !gear.cape);
            Header("Weapon slots");
            WeaponOption("Main hand", gear.weapon, w => gear.weapon = w);
            Option("Off hand", gear.offHand == BodyRenderer.OffHand.Weapon ? (gear.offWeapon.HasValue ? gear.offWeapon.Value.ToString() : "Weapon") : gear.offHand.ToString(), d =>
            {
                // Cycle: none, shields, then each weapon.
                var order = new List<(BodyRenderer.OffHand, WeaponType?)> { (BodyRenderer.OffHand.None, null), (BodyRenderer.OffHand.RoundShield, null), (BodyRenderer.OffHand.KiteShield, null),
                    (BodyRenderer.OffHand.Orb, null), (BodyRenderer.OffHand.Tome, null), (BodyRenderer.OffHand.Quiver, null) };
                foreach (var w in WeaponChoices) if (w.HasValue) order.Add((BodyRenderer.OffHand.Weapon, w));
                int cur = order.FindIndex(o => o.Item1 == gear.offHand && (o.Item1 != BodyRenderer.OffHand.Weapon || o.Item2 == gear.offWeapon));
                var next = order[Wrap(cur + d, order.Count)];
                gear.offHand = next.Item1;
                gear.offWeapon = next.Item2;
            });
            WeaponOption("Back", gear.backWeapon, w => gear.backWeapon = w);
            WeaponOption("Hip", gear.hipWeapon, w => gear.hipWeapon = w);
            Option("Bones", showBones ? "Shown" : "Hidden", d => showBones = !showBones);
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        void WeaponOption(string label, WeaponType? current, System.Action<WeaponType?> set)
        {
            int i = System.Array.IndexOf(WeaponChoices, current);
            Option(label, current.HasValue ? current.Value.ToString() : "None", d => set(WeaponChoices[Wrap(i + d, WeaponChoices.Length)]));
        }

        void Header(string text)
        {
            var t = UIFactory.Text("Header", options, text, 28, Palette.MutedText, TextAlignmentOptions.Left);
            t.rectTransform.sizeDelta = new Vector2(0, 46);
        }

        void Option(string label, string value, System.Action<int> change)
        {
            var row = UIFactory.Rect(label, options);
            row.sizeDelta = new Vector2(0, 46);
            var t = UIFactory.Text("Label", row, label, 24, Palette.Gold, TextAlignmentOptions.Left);
            t.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(220, 44));
            var val = UIFactory.Text("Value", row, value, 24, Palette.Text);
            val.rectTransform.Place(new Vector2(0.62f, 0.5f), Vector2.zero, new Vector2(300, 44));
            var l = UIFactory.Button("Prev", row, "<", () => { change(-1); RefreshOptions(); Redraw(); }, null, 26);
            ((RectTransform)l.transform).Place(new Vector2(0.62f, 0.5f), new Vector2(-190, 0), new Vector2(60, 42));
            var r = UIFactory.Button("Next", row, ">", () => { change(1); RefreshOptions(); Redraw(); }, null, 26);
            ((RectTransform)r.transform).Place(new Vector2(0.62f, 0.5f), new Vector2(190, 0), new Vector2(60, 42));
        }

        void Update()
        {
            animT += Time.deltaTime;
            if (animT > (walking ? 0.15f : 0.5f))
            {
                animT = 0f;
                frame = (frame + 1) % 4;
                ShowPreview();
            }
            if (torch && torchLight != null)
            {
                // The torch circles the character so the shading visibly follows it.
                float t = Time.time * 1.2f;
                torchLight.transform.position = previewSprite.transform.position + new Vector3(Mathf.Cos(t) * 1.4f, 1f + Mathf.Sin(t * 0.7f) * 0.4f, -1.2f);
            }
        }

        void Redraw()
        {
            ShowPreview();
            for (int i = 0; i < 4; i++) turnaround[i].texture = Frame(i, -1);
        }

        void ShowPreview()
        {
            int f = walking ? frame : frame % 2;
            if (!dynamicLight) { preview.texture = Frame(facing, f); return; }
            var maps = MapFrame(facing, f);
            previewSprite.sprite = Sprite.Create(maps.albedo, new Rect(0, 0, BodyRenderer.W, BodyRenderer.H), new Vector2(0.5f, 0f), 48f);
            previewMat.SetTexture("_NormalMap", maps.normal);
            preview.texture = previewRT;
        }

        // Walking cycles the legs; standing still breathes (chest and shoulders rise and fall).
        BodyRenderer.Pose PoseFor(int dir, int f)
        {
            var facingEnum = dir == 0 ? BodyRenderer.Facing.Front : dir == 2 ? BodyRenderer.Facing.Back : BodyRenderer.Facing.Side;
            if (f < 0) return new BodyRenderer.Pose { facing = facingEnum };
            if (walking) return new BodyRenderer.Pose { facing = facingEnum, step = new[] { 1, 0, -1, 0 }[f], bob = f % 2 == 1 ? 1 : 0 };
            return new BodyRenderer.Pose { facing = facingEnum, breath = f };
        }

        // Bones as lines with joint dots, over the figure.
        void DrawBones(Color[,] px, BodyRenderer.Pose pose)
        {
            foreach (var b in BodyRenderer.Bones(look.Female, pose))
            {
                bool joint = b.a == b.b;
                var col = joint ? new Color(1f, 0.3f, 0.3f) : b.name.Contains("_L") || b.name.Contains("_far") ? new Color(0.3f, 0.8f, 1f) : b.name.Contains("_R") || b.name.Contains("_near") ? new Color(1f, 0.8f, 0.2f) : new Color(0.5f, 1f, 0.5f);
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(b.a, b.b)));
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector2.Lerp(b.a, b.b, i / (float)n);
                    int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
                    if (x >= 0 && y >= 0 && x < BodyRenderer.W && y < BodyRenderer.H) px[x, y] = col;
                }
            }
        }

        // ---------- Live lighting rig ----------

        void BuildLightRig()
        {
            var rig = new GameObject("PreviewRig").transform;
            rig.position = new Vector3(1000f, 0f, 0f);   // far from everything else
            previewCam = new GameObject("PreviewCam").AddComponent<Camera>();
            previewCam.transform.SetParent(rig, false);
            previewCam.transform.localPosition = new Vector3(0f, 1f, -10f);
            previewCam.orthographic = true;
            previewCam.orthographicSize = 1.02f;
            previewCam.clearFlags = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0, 0, 0, 0);
            previewRT = new RenderTexture(64 * 6, 96 * 6, 16) { filterMode = FilterMode.Point };
            previewCam.targetTexture = previewRT;
            previewSprite = new GameObject("Hero").AddComponent<SpriteRenderer>();
            previewSprite.transform.SetParent(rig, false);
            previewMat = new Material(RenderAssets.Instance.litSprite);
            previewMat.SetFloat("_UseNormalMap", 1f);
            previewMat.SetFloat("_Cutoff", 0.5f);
            previewSprite.sharedMaterial = previewMat;
            keyLight = new GameObject("KeyLight").AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.95f, 0.85f);
            keyLight.intensity = 1.1f;
            keyLight.shadows = LightShadows.None;
            torchLight = new GameObject("Torch").AddComponent<Light>();
            torchLight.transform.SetParent(rig, false);
            torchLight.type = LightType.Point;
            torchLight.color = new Color(1f, 0.55f, 0.25f);
            torchLight.range = 3f;
            torchLight.intensity = 2.5f;
            Shader.SetGlobalVector("_TWF_Ambient", new Vector4(0.28f, 0.27f, 0.32f, 1f));
            UpdateLights();
        }

        void UpdateLights()
        {
            // The key light shines from the viewer's side, from an angle around the character.
            float a = lightAngle * Mathf.Deg2Rad;
            var dir = new Vector3(-Mathf.Cos(a) * 0.7f, -Mathf.Sin(a) * 0.7f, 0.7f).normalized;
            keyLight.transform.rotation = Quaternion.LookRotation(dir);
            torchLight.enabled = torch;
            if (lightLabel != null) lightLabel.text = $"Light from {Mathf.Repeat(lightAngle, 360f):0}°{(torch ? "  ·  torch on" : "")}  ·  {(dynamicLight ? "dynamic" : "baked")}";
        }

        (Texture2D albedo, Texture2D normal) MapFrame(int dir, int f)
        {
            var pose = PoseFor(dir, f);
            string key = $"{look.Key}_{gear.Key}_{showBones}_{dir}_{pose.step}_{pose.bob}_{pose.breath}";
            if (mapCache.TryGetValue(key, out var m) && m.albedo) return m;
            var maps = BodyRenderer.FigureMaps(look, gear, pose);
            if (showBones) DrawBones(maps.albedo, pose);
            bool mirror = dir == 3;
            var albedo = GameAssets.NewTexture(BodyRenderer.W, BodyRenderer.H);
            var normal = GameAssets.NewTexture(BodyRenderer.W, BodyRenderer.H);
            for (int x = 0; x < BodyRenderer.W; x++)
            for (int y = 0; y < BodyRenderer.H; y++)
            {
                int sx = mirror ? BodyRenderer.W - 1 - x : x;
                albedo.SetPixel(x, y, maps.albedo[sx, y]);
                var n = maps.normal[sx, y];
                if (mirror) n.r = 1f - n.r;   // a mirrored sprite faces the other way
                normal.SetPixel(x, y, n);
            }
            albedo.Apply();
            normal.Apply();
            m = (albedo, normal);
            mapCache[key] = m;
            return m;
        }

        // ---------- Reference board ----------

        void LoadReferences()
        {
            var folder = System.IO.Path.Combine(Application.dataPath, "..", "ArtReference");
            if (!System.IO.Directory.Exists(folder)) return;
            foreach (var file in System.IO.Directory.GetFiles(folder))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(System.IO.File.ReadAllBytes(file))) references.Add(tex);
            }
        }

        void NextReference()
        {
            if (references.Count == 0) { refImage.gameObject.SetActive(false); return; }
            refIndex = refIndex + 1 >= references.Count ? -1 : refIndex + 1;
            refImage.gameObject.SetActive(refIndex >= 0);
            // With a reference showing, the character moves to the left half and the reference takes the right.
            var prt = preview.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(refIndex >= 0 ? 0.27f : 0.5f, 0f);
            if (refIndex < 0) return;
            var tex = references[refIndex];
            refImage.texture = tex;
            // Fit inside the right half at most the preview's height, so proportions can be compared.
            var stageRect = ((RectTransform)refImage.transform.parent).rect;
            float maxW = stageRect.width * 0.46f, maxH = 96 * 6 - 40;
            float scale = Mathf.Min(maxW / tex.width, maxH / tex.height);
            refImage.rectTransform.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
        }

        // facing 0 front, 1 right, 2 back, 3 left; f = walk frame (or -1 standing).
        Texture2D Frame(int dir, int f)
        {
            var pose = PoseFor(dir, f);
            string key = $"{look.Key}_{gear.Key}_{showBones}_{dir}_{pose.step}_{pose.bob}_{pose.breath}";
            if (frames.TryGetValue(key, out var tex) && tex) return tex;
            var px = BodyRenderer.Figure(look, gear, pose);
            if (showBones) DrawBones(px, pose);
            tex = GameAssets.NewTexture(BodyRenderer.W, BodyRenderer.H);
            bool mirror = dir == 3;
            for (int x = 0; x < BodyRenderer.W; x++)
            for (int y = 0; y < BodyRenderer.H; y++)
                tex.SetPixel(x, y, px[mirror ? BodyRenderer.W - 1 - x : x, y]);
            tex.Apply();
            frames[key] = tex;
            return tex;
        }
    }
}
