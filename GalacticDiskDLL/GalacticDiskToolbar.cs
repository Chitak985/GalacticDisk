using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GalacticDisk
{
    /// <summary>
    /// The in-game toolbar GUI to edit GalacticDisk objects.
    /// </summary>
    public class GalacticDiskToolbar
    {
        private bool showWindow = false;
        private Rect windowRect = new Rect(20, 20, 560, 560);

        /// <summary>
        /// <see cref="FlightGlobals.Bodies"/> index of the currently selected body.
        /// Because of how they are stored, these are in the same order as in the console.
        /// </summary>
        private int selectedBodyIndex = 0;

        /// <summary>
        /// The config for the current object stored as a <see cref="string"/>.
        /// </summary>
        private string configText = "";
        
        private bool configInitialized;
        private string lastConfigText, lastFieldFingerprint;
        private ConfigEditorResult textValidation, fieldValidation;
        private readonly List<string> editorErrors = new List<string>();
        private readonly List<string> editorWarnings = new List<string>();
        private Vector2 diagnosticsScroll;
        private string operationMessage = "";
        private DiskRenderMode renderMode = DiskRenderMode.Particles;
        private bool isRing;
        private string innerRingRadius = "0";
        private GalaxySettings volumeFields = new GalaxySettings();
        private readonly Dictionary<string, string> volumeInputs = new Dictionary<string, string>();
        private bool showVolumeOptions = false;
        private Vector2 configScroll = Vector2.zero;

        /// <summary>
        /// Position for the scrollbar of the config value list.
        /// </summary>
        private Vector2 fieldsScroll = Vector2.zero;

        ///// Config fields stored as strings and booleans /////
        private string starCount = "50000";
        private string galaxyRadius = "100";
        private string diskThickness = "2";
        private string minStarSize = "0.015";
        private string maxStarSize = "0.09";

        private string armCount = "4";
        private string armTightness = "4";
        private string armWidth = "0.25";
        private string interarmFraction = "0.25";
        private string armConcentration = "0.35";

        private string bulgeFraction = "0.15";
        private string bulgeScaleX = "15", bulgeScaleY = "2.25", bulgeScaleZ = "15";

        private string colorBulgeR = "1.0", colorBulgeG = "0.92", colorBulgeB = "0.65", colorBulgeA = "1.0";
        private string colorArmsR = "1.0", colorArmsG = "0.92", colorArmsB = "0.65", colorArmsA = "1.0";
        private string colorInterarmR = "0.45", colorInterarmG = "0.65", colorInterarmB = "1.0", colorInterarmA = "1.0";

        private string armHighlightColorR = "1.0", armHighlightColorG = "0.55", armHighlightColorB = "0.6", armHighlightColorA = "1.0";
        private string armHighlightChance = "0";

        private string rotationX = "0", rotationY = "0", rotationZ = "0";
        private bool alignRotationToOrbit = false;
        private bool spinAroundDiskAxis = false;
        private string rotationPeriod = "0";
        private string diskScaleLengthFraction = "0.25";

        private string objectScale = "1";
        private string renderScale = "1";
        private bool usesTexture;
        private string texturePath = "";
        ////////////////////////////////////////////////////////

        ///// Colapsible config value sections state /////
        private bool showBasic = true;
        private bool showArms = false;
        private bool showBulge = false;
        private bool showColors = false;
        private bool showRotation = false;
        private bool showScale = false;
        ///////////////////////////////////////////////////

        /// <summary>
        /// Called from GalacticDiskGenerator.OnGUI() every frame.
        /// This only draws the window while the toolbar button has it open.
        /// </summary>
        public void OnClick()
        {
            if (!showWindow) return;

            windowRect = GUILayout.Window(985123, windowRect, DrawWindow, "Galactic Disk Config");
        }

        public bool IsVisible => showWindow;

        public void SetVisible(bool visible)
        {
            showWindow = visible;
        }

        private static bool IsUsableScene()
            => HighLogic.LoadedScene == GameScenes.SPACECENTER
            || HighLogic.LoadedScene == GameScenes.FLIGHT
            || HighLogic.LoadedScene == GameScenes.TRACKSTATION;

        private const float LabelWidth = 140f;

        private static string LabeledField(string label, string value, float width = LabelWidth)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(width));
            string result = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
            return result;
        }

        // A clickable section header that toggles a bool — used to fold groups
        // of fields away so the window doesn't have to show everything at once.
        private static bool Foldout(string label, bool expanded)
        {
            string arrow = expanded ? "\u25bc " : "\u25b6 ";
            if (GUILayout.Button(arrow + label, GUILayout.Height(22f)))
                expanded = !expanded;
            return expanded;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            if (!IsUsableScene())
            {
                GUILayout.Label("Galactic Disk config is only available in the Space Center, Flight, or Tracking Station scenes.");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            EnsureConfigText();
            GUILayout.Label(isRing ? "Ring Config (edit values here)" : "Galaxy Config (edit values here)", GUILayout.Height(20f));
            isRing = GUILayout.Toolbar(isRing ? 1 : 0, new[] { "Galaxy", "Ring" }) == 1;
            renderMode = (DiskRenderMode)GUILayout.Toolbar((int)renderMode, new[] { "Particles", "Volumetric" });
            if (!isRing && renderMode == DiskRenderMode.Volumetric && GUILayout.Button("Load Milky Way preset into fields"))
            {
                RefreshEditorDiagnostics();
                if (fieldValidation.IsValid)
                {
                    GalaxyConfig preset = fieldValidation.config;
                    VolumeConfig.ApplyMilkyWayPreset(preset);
                    PopulateFieldsFromConfig(preset);
                }
                else operationMessage = "Error: fix the field errors before loading a preset.";
            }

            GUILayout.Space(10f);
            GUILayout.Label("Text edits update fields. Save to config copies fields into the text.");
            GUILayout.Label("Apply rebuilds from the fields. Save the .cfg file manually.");

            fieldsScroll = GUILayout.BeginScrollView(fieldsScroll, GUILayout.Height(Mathf.Clamp(Screen.height - 540f, 80f, 240f)));

            GUILayout.Space(10f);
            showBasic = Foldout("Basic", showBasic);
            if (showBasic)
            {
                starCount = LabeledField(isRing ? "Particle count" : "Star count", starCount);
                if (isRing) innerRingRadius = LabeledField("Inner ring radius", innerRingRadius);
                galaxyRadius = LabeledField(isRing ? "Ring radius" : "Galaxy radius", galaxyRadius);
                diskThickness = LabeledField(isRing ? "Ring thickness" : "Disk thickness", diskThickness);
                minStarSize = LabeledField(isRing ? "Min particle size" : "Min star size", minStarSize);
                maxStarSize = LabeledField(isRing ? "Max particle size" : "Max star size", maxStarSize);
                diskScaleLengthFraction = LabeledField("Falloff fraction", diskScaleLengthFraction);
            }

            if (!isRing)
            {
                showArms = Foldout("Arms", showArms);
                if (showArms)
                {
                    armCount = LabeledField("Arm count", armCount);
                    armTightness = LabeledField("Arm tightness", armTightness);
                    armWidth = LabeledField("Arm width", armWidth);
                    interarmFraction = LabeledField("Interarm fraction", interarmFraction);
                    armConcentration = LabeledField("Arm concentration", armConcentration);
                }

                showBulge = Foldout("Bulge", showBulge);
                if (showBulge)
                {
                    bulgeFraction = LabeledField("Bulge fraction", bulgeFraction);
                    bulgeScaleX = LabeledField("Bulge scale X", bulgeScaleX);
                    bulgeScaleY = LabeledField("Bulge scale Y", bulgeScaleY);
                    bulgeScaleZ = LabeledField("Bulge scale Z", bulgeScaleZ);
                }
            }

            showColors = Foldout("Colors", showColors);
            if (showColors)
            {
                if (isRing)
                {
                    colorInterarmR = LabeledField("Ring color R", colorInterarmR);
                    colorInterarmG = LabeledField("Ring color G", colorInterarmG);
                    colorInterarmB = LabeledField("Ring color B", colorInterarmB);
                    colorInterarmA = LabeledField("Ring opacity A", colorInterarmA);
                }
                else
                {
                    colorBulgeR = LabeledField("Bulge color R", colorBulgeR);
                    colorBulgeG = LabeledField("Bulge color G", colorBulgeG);
                    colorBulgeB = LabeledField("Bulge color B", colorBulgeB);
                    colorBulgeA = LabeledField("Bulge opacity A", colorBulgeA);

                    GUILayout.Space(6f);
                    colorArmsR = LabeledField("Arms color R", colorArmsR);
                    colorArmsG = LabeledField("Arms color G", colorArmsG);
                    colorArmsB = LabeledField("Arms color B", colorArmsB);
                    colorArmsA = LabeledField("Arms opacity A", colorArmsA);

                    GUILayout.Space(6f);
                    colorInterarmR = LabeledField("Interarm color R", colorInterarmR);
                    colorInterarmG = LabeledField("Interarm color G", colorInterarmG);
                    colorInterarmB = LabeledField("Interarm color B", colorInterarmB);
                    colorInterarmA = LabeledField("Interarm opacity A", colorInterarmA);

                    GUILayout.Space(6f);
                    armHighlightColorR = LabeledField("Highlight color R", armHighlightColorR);
                    armHighlightColorG = LabeledField("Highlight color G", armHighlightColorG);
                    armHighlightColorB = LabeledField("Highlight color B", armHighlightColorB);
                    armHighlightColorA = LabeledField("Highlight opacity A", armHighlightColorA);
                    armHighlightChance = LabeledField("Highlight chance", armHighlightChance);
                }
            }

            showVolumeOptions = Foldout(isRing ? "Particles, fog and dust" : "Volume, dust, nebulae and bar", showVolumeOptions);
            if (showVolumeOptions) DrawVolumeFields();

            showRotation = Foldout("Rotation", showRotation);
            if (showRotation)
            {
                alignRotationToOrbit = GUILayout.Toggle(alignRotationToOrbit, " Align rotation to planet's orbit plane");

                GUI.enabled = !alignRotationToOrbit;
                rotationX = LabeledField("Rotation X", rotationX);
                GUI.enabled = true;
                rotationY = LabeledField("Rotation Y", rotationY);
                GUI.enabled = !alignRotationToOrbit;
                rotationZ = LabeledField("Rotation Z", rotationZ);
                GUI.enabled = true;
                if (alignRotationToOrbit)
                    GUILayout.Label("(X/Z ignored while aligned; Y still applies as spin)");

                GUILayout.Space(6f);
                rotationPeriod = LabeledField("Rotation period (s)", rotationPeriod);
                spinAroundDiskAxis = GUILayout.Toggle(spinAroundDiskAxis, " Spin around the disk's tilted axis");
                GUILayout.Label("(Seconds per full spin, on top of Rotation Y. 0 = no spin. Supports");
                GUILayout.Label("scientific notation, e.g. 6.3E+7 for a year.)");
            }

            showScale = Foldout("Scale", showScale);
            if (showScale)
            {
                objectScale = LabeledField("Object scale", objectScale);
                renderScale = LabeledField("Render scale", renderScale);
            }
            if (renderMode == DiskRenderMode.Particles)
            {
                usesTexture = GUILayout.Toggle(usesTexture, " Use custom particle texture");
                if (usesTexture) texturePath = LabeledField("Texture path", texturePath);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10f);

            // Body selector — cycles through whatever bodies are currently loaded,
            // in whatever order FlightGlobals.Bodies happens to have them in.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30f)))
            {
                CycleBody(-1);
            }
            GUILayout.Label(GetSelectedBody()?.name ?? "(no bodies loaded)");
            if (GUILayout.Button(">", GUILayout.Width(30f)))
            {
                CycleBody(1);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to config"))
            {
                SaveToConfig();
            }
            if (GUILayout.Button("Load"))
            {
                LoadSelectedDisk();
            }
            if (GUILayout.Button("Apply"))
            {
                Apply();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Remove disks on selected body"))
            {
                RemoveDisks();
            }

            GUILayout.Space(10f);
            RefreshEditorDiagnostics();
            DrawEditorDiagnostics();
            configScroll = GUILayout.BeginScrollView(configScroll, GUILayout.Height(160f));
            string edited = GUILayout.TextArea(configText, GUILayout.MinHeight(150f));
            if (edited != configText) EditConfigText(edited);
            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private void DrawVolumeFields()
        {
            foreach (FieldInfo field in VolumeConfig.OptionFields)
            {
                if (isRing && !RingConfig.IsVolumeOption(field.Name)) continue;
                object[] headers = field.GetCustomAttributes(typeof(HeaderAttribute), false);
                if (headers.Length > 0) { GUILayout.Space(8f); GUILayout.Label(((HeaderAttribute)headers[0]).header); }
                if (!volumeInputs.TryGetValue(field.Name, out string value)) value = VolumeConfig.Format(field.GetValue(volumeFields));
                string name = isRing ? RingConfig.OptionName(field.Name) : field.Name;
                string label = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
                if (field.FieldType == typeof(bool))
                {
                    bool.TryParse(value, out bool enabled);
                    value = GUILayout.Toggle(enabled, label) ? "true" : "false";
                }
                else
                    value = LabeledField(label, value, 220f);
                volumeInputs[field.Name] = value;
            }
            GUILayout.Label("Colors accept R, G, B or R, G, B, A. Missing alpha = 1.");
        }

        private CelestialBody GetSelectedBody()
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0) return null;

            if (selectedBodyIndex < 0) selectedBodyIndex = 0;
            if (selectedBodyIndex >= bodies.Count) selectedBodyIndex = bodies.Count - 1;

            return bodies[selectedBodyIndex];
        }

        private void CycleBody(int direction)
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0) return;

            selectedBodyIndex = (selectedBodyIndex + direction + bodies.Count) % bodies.Count;
        }

        private Dictionary<string, string> CaptureFieldValues()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            foreach (KeyValuePair<string,Type> entry in ConfigEditor.Schema(isRing))
            {
                if (entry.Key == "color1" || entry.Key == "color2" || entry.Key == "bulgeRadius") continue;
                if (entry.Key == "planet") { values[entry.Key] = GetSelectedBody()?.name ?? ""; continue; }
                string shared = isRing ? RingConfig.SharedFieldName(entry.Key) : entry.Key;
                if (entry.Key == "renderMode") shared = "renderMode";
                if (shared != null && typeof(GalaxyConfig).GetField(shared) != null && entry.Value == typeof(Color))
                {
                    List<string> components = new List<string>();
                    foreach (string suffix in new[] { "R", "G", "B", "A" })
                        components.Add((string)GetType().GetField(shared + suffix, flags).GetValue(this));
                    values[entry.Key] = string.Join(", ", components.ToArray());
                }
                else
                {
                    FieldInfo field = shared == null ? null : GetType().GetField(shared, flags);
                    if (field != null) values[entry.Key] = VolumeConfig.Format(field.GetValue(this));
                    else
                    {
                        string option = entry.Key == "particleBrightness" ? "starBrightness" : entry.Key;
                        values[entry.Key] = volumeInputs.TryGetValue(option, out string raw) ? raw :
                            VolumeConfig.Format(typeof(GalaxySettings).GetField(option).GetValue(volumeFields));
                    }
                }
            }
            return values;
        }

        private void EnsureConfigText()
        {
            if (configInitialized) return;
            configInitialized = true;
            StringBuilder text = new StringBuilder((isRing ? RingConfig.NodeName : "GalacticDiskDefinition") + "\n{\n");
            foreach (KeyValuePair<string,string> entry in CaptureFieldValues())
                text.Append("    ").Append(entry.Key).Append(" = ").Append(entry.Value).Append('\n');
            configText = text.Append('}').ToString();
        }

        private void EditConfigText(string edited)
        {
            configInitialized = true;
            configText = edited;
            operationMessage = "";
            textValidation = ConfigEditor.Read(configText);
            lastConfigText = configText;
            if (textValidation.IsValid)
            {
                int index = FlightGlobals.Bodies.FindIndex(body => body != null && body.name == textValidation.config.planet);
                if (index >= 0) selectedBodyIndex = index;
                PopulateFieldsFromConfig(textValidation.config);
            }
            lastFieldFingerprint = null;
            RefreshEditorDiagnostics();
        }

        private bool SaveToConfig()
        {
            EnsureConfigText();
            RefreshEditorDiagnostics();
            if (!fieldValidation.IsValid)
            {
                operationMessage = "Error: fields contain invalid values; the config text was preserved.";
                return false;
            }
            configText = GenerateConfigText(fieldValidation.config, GetSelectedBody()?.name ?? "");
            lastConfigText = null;
            operationMessage = "Fields saved to the config text. Save the .cfg file manually.";
            RefreshEditorDiagnostics();
            return true;
        }

        private void RefreshEditorDiagnostics()
        {
            EnsureConfigText();
            bool changed = false;
            if (lastConfigText != configText || textValidation == null)
            {
                textValidation = ConfigEditor.Read(configText);
                lastConfigText = configText; changed = true;
            }
            Dictionary<string,string> inputs = CaptureFieldValues();
            StringBuilder fingerprint = new StringBuilder(isRing ? "Ring\n" : "Galaxy\n");
            foreach (KeyValuePair<string,string> entry in inputs)
                fingerprint.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');
            string current = fingerprint.ToString();
            if (lastFieldFingerprint != current || fieldValidation == null)
            {
                if (lastFieldFingerprint != null) operationMessage = "";
                fieldValidation = ConfigEditor.ReadValues(isRing, inputs, true);
                lastFieldFingerprint = current; changed = true;
            }
            if (!changed) return;
            editorErrors.Clear(); editorWarnings.Clear();
            foreach (string error in textValidation.errors) editorErrors.Add("Config: " + error);
            foreach (string error in fieldValidation.errors) editorErrors.Add("Fields: " + error);
            foreach (string warning in textValidation.warnings) editorWarnings.Add(warning);
            foreach (string warning in fieldValidation.warnings)
                if (!editorWarnings.Contains(warning)) editorWarnings.Add(warning);
            if (textValidation.IsValid && fieldValidation.IsValid)
                foreach (string difference in ConfigEditor.Differences(fieldValidation.config, textValidation.config))
                    editorWarnings.Add("Fields/config mismatch: " + difference);
        }

        private void DrawEditorDiagnostics()
        {
            int count = editorErrors.Count + editorWarnings.Count + (operationMessage.Length > 0 ? 1 : 0);
            diagnosticsScroll = GUILayout.BeginScrollView(diagnosticsScroll, GUILayout.Height(Mathf.Clamp(count * 24f, 24f, 90f)));
            Color previous = GUI.contentColor;
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true
            };
            if (operationMessage.Length > 0)
            {
                GUI.contentColor = operationMessage.StartsWith("Error:") ? new Color(1f, 0.45f, 0.4f) : previous;
                GUILayout.Label(operationMessage, style);
            }
            GUI.contentColor = new Color(1f, 0.45f, 0.4f);
            foreach (string error in editorErrors)
                GUILayout.Label("Error: " + error, style);
            GUI.contentColor = new Color(1f, 0.8f, 0.35f);
            foreach (string warning in editorWarnings)
                GUILayout.Label("Warning: " + warning, style);
            GUI.contentColor = previous;
            if (count == 0)
                GUILayout.Label("Fields and config match.", style);
            GUILayout.EndScrollView();
        }

        private string GenerateConfigText(GalaxyConfig cfg, string planetName)
        {
            if (cfg.isRing) return RingConfig.Export(cfg, planetName);
            CultureInfo ci = CultureInfo.InvariantCulture;

            return
                "GalacticDiskDefinition\n" +
                "{\n" +
                $"    planet = {planetName}\n" +
                $"    renderMode = {cfg.renderMode}\n" +
                $"    starCount = {cfg.starCount.ToString(ci)}\n" +
                $"    galaxyRadius = {cfg.galaxyRadius.ToString("R", ci)}\n" +
                $"    diskThickness = {cfg.diskThickness.ToString("R", ci)}\n" +
                $"    minStarSize = {cfg.minStarSize.ToString("R", ci)}\n" +
                $"    maxStarSize = {cfg.maxStarSize.ToString("R", ci)}\n" +
                $"    armCount = {cfg.armCount.ToString(ci)}\n" +
                $"    armTightness = {cfg.armTightness.ToString("R", ci)}\n" +
                $"    armWidth = {cfg.armWidth.ToString("R", ci)}\n" +
                $"    interarmFraction = {cfg.interarmFraction.ToString("R", ci)}\n" +
                $"    armConcentration = {cfg.armConcentration.ToString("R", ci)}\n" +
                $"    bulgeFraction = {cfg.bulgeFraction.ToString("R", ci)}\n" +
                $"    bulgeScaleX = {cfg.bulgeScaleX.ToString("R", ci)}\n" +
                $"    bulgeScaleY = {cfg.bulgeScaleY.ToString("R", ci)}\n" +
                $"    bulgeScaleZ = {cfg.bulgeScaleZ.ToString("R", ci)}\n" +
                $"    colorBulge = {cfg.colorBulge.r.ToString("R", ci)}, {cfg.colorBulge.g.ToString("R", ci)}, {cfg.colorBulge.b.ToString("R", ci)}, {cfg.colorBulge.a.ToString("R", ci)}\n" +
                $"    colorArms = {cfg.colorArms.r.ToString("R", ci)}, {cfg.colorArms.g.ToString("R", ci)}, {cfg.colorArms.b.ToString("R", ci)}, {cfg.colorArms.a.ToString("R", ci)}\n" +
                $"    colorInterarm = {cfg.colorInterarm.r.ToString("R", ci)}, {cfg.colorInterarm.g.ToString("R", ci)}, {cfg.colorInterarm.b.ToString("R", ci)}, {cfg.colorInterarm.a.ToString("R", ci)}\n" +
                $"    armHighlightColor = {cfg.armHighlightColor.r.ToString("R", ci)}, {cfg.armHighlightColor.g.ToString("R", ci)}, {cfg.armHighlightColor.b.ToString("R", ci)}, {cfg.armHighlightColor.a.ToString("R", ci)}\n" +
                $"    armHighlightChance = {cfg.armHighlightChance.ToString("R", ci)}\n" +
                $"    rotationX = {cfg.rotationX.ToString("R", ci)}\n" +
                $"    rotationY = {cfg.rotationY.ToString("R", ci)}\n" +
                $"    rotationZ = {cfg.rotationZ.ToString("R", ci)}\n" +
                $"    alignRotationToOrbit = {cfg.alignRotationToOrbit}\n" +
                $"    rotationPeriod = {cfg.rotationPeriod.ToString("R", ci)}\n" +
                $"    spinAroundDiskAxis = {VolumeConfig.Format(cfg.spinAroundDiskAxis)}\n" +
                $"    diskScaleLengthFraction = {cfg.diskScaleLengthFraction.ToString("R", ci)}\n" +
                $"    objectScale = {cfg.objectScale.ToString("R", ci)}\n" +
                $"    renderScale = {cfg.renderScale.ToString("R", ci)}\n" +
                $"    usesTexture = {VolumeConfig.Format(cfg.usesTexture)}\n" +
                $"    texturePath = {cfg.texturePath}\n" +
                VolumeConfig.Export(cfg, true) +
                "}";
        }

        // Fills every editable field from an existing config — the reverse of
        // BuildConfigFromFields — so "Load" can pull a disk's current values in
        // instead of the user re-typing everything from scratch.
        private void PopulateFieldsFromConfig(GalaxyConfig cfg)
        {
            operationMessage = "";
            lastFieldFingerprint = null;
            isRing = cfg.isRing;
            innerRingRadius = cfg.innerRingRadius.ToString("R", CultureInfo.InvariantCulture);
            renderMode = cfg.renderMode;
            volumeFields = cfg.volume;
            volumeInputs.Clear();
            foreach (FieldInfo field in VolumeConfig.OptionFields)
                volumeInputs[field.Name] = VolumeConfig.Format(field.GetValue(volumeFields));
            CultureInfo ci = CultureInfo.InvariantCulture;

            starCount = cfg.starCount.ToString(ci);
            galaxyRadius = cfg.galaxyRadius.ToString("R", ci);
            diskThickness = cfg.diskThickness.ToString("R", ci);
            minStarSize = cfg.minStarSize.ToString("R", ci);
            maxStarSize = cfg.maxStarSize.ToString("R", ci);

            armCount = cfg.armCount.ToString(ci);
            armTightness = cfg.armTightness.ToString("R", ci);
            armWidth = cfg.armWidth.ToString("R", ci);
            interarmFraction = cfg.interarmFraction.ToString("R", ci);
            armConcentration = cfg.armConcentration.ToString("R", ci);

            bulgeFraction = cfg.bulgeFraction.ToString("R", ci);
            bulgeScaleX = cfg.bulgeScaleX.ToString("R", ci);
            bulgeScaleY = cfg.bulgeScaleY.ToString("R", ci);
            bulgeScaleZ = cfg.bulgeScaleZ.ToString("R", ci);

            colorBulgeR = cfg.colorBulge.r.ToString("R", ci);
            colorBulgeG = cfg.colorBulge.g.ToString("R", ci);
            colorBulgeB = cfg.colorBulge.b.ToString("R", ci);
            colorBulgeA = cfg.colorBulge.a.ToString("R", ci);

            colorArmsR = cfg.colorArms.r.ToString("R", ci);
            colorArmsG = cfg.colorArms.g.ToString("R", ci);
            colorArmsB = cfg.colorArms.b.ToString("R", ci);
            colorArmsA = cfg.colorArms.a.ToString("R", ci);

            colorInterarmR = cfg.colorInterarm.r.ToString("R", ci);
            colorInterarmG = cfg.colorInterarm.g.ToString("R", ci);
            colorInterarmB = cfg.colorInterarm.b.ToString("R", ci);
            colorInterarmA = cfg.colorInterarm.a.ToString("R", ci);

            armHighlightColorR = cfg.armHighlightColor.r.ToString("R", ci);
            armHighlightColorG = cfg.armHighlightColor.g.ToString("R", ci);
            armHighlightColorB = cfg.armHighlightColor.b.ToString("R", ci);
            armHighlightColorA = cfg.armHighlightColor.a.ToString("R", ci);
            armHighlightChance = cfg.armHighlightChance.ToString("R", ci);

            rotationX = cfg.rotationX.ToString("R", ci);
            rotationY = cfg.rotationY.ToString("R", ci);
            rotationZ = cfg.rotationZ.ToString("R", ci);
            alignRotationToOrbit = cfg.alignRotationToOrbit;
            spinAroundDiskAxis = cfg.spinAroundDiskAxis;
            rotationPeriod = cfg.rotationPeriod.ToString("R", ci);
            diskScaleLengthFraction = cfg.diskScaleLengthFraction.ToString("R", ci);

            objectScale = cfg.objectScale.ToString("R", ci);
            renderScale = cfg.renderScale.ToString("R", ci);
            usesTexture = cfg.usesTexture;
            texturePath = cfg.texturePath;
        }

        // Reads the selected body's currently-applied disk config, if it has one,
        // into the fields. Does nothing (and leaves fields untouched) if the body
        // has no disk yet — there's nothing to load in that case.
        private void LoadSelectedDisk()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            GalaxyConfig cfg = GalacticDiskGenerator.GetConfig(body.name);
            if (cfg == null)
            {
                operationMessage = $"Warning: {body.name} has no disk to load. The config text was preserved.";
                return;
            }

            PopulateFieldsFromConfig(cfg);
            configInitialized = true;
            configText = GenerateConfigText(cfg, body.name);
            lastConfigText = null;
            operationMessage = "Loaded the selected disk into the fields and config text.";
        }

        // Replaces the selected body's disk in place with the current field
        // values — creates one if it doesn't have one yet. No more temporary
        // preview objects; this is the real disk, updated live.
        private void Apply()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            RefreshEditorDiagnostics();
            if (!fieldValidation.IsValid || !textValidation.IsValid)
            {
                operationMessage = "Error: fix the field/config errors before applying, or save valid fields to config.";
                return;
            }
            GalaxyConfig cfg = fieldValidation.config;
            try
            {
                GalacticDiskGenerator.ApplyDisk(body, cfg);
                operationMessage = "Applied the fields to the selected disk.";
            }
            catch (Exception ex)
            {
                operationMessage = "Error: could not apply disk: " + ex.Message;
                Debug.LogError("[GalacticDisk] " + ex);
            }
        }

        private void RemoveDisks()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            GalacticDiskGenerator.RemoveDisk(body.name);
        }
    }
}
