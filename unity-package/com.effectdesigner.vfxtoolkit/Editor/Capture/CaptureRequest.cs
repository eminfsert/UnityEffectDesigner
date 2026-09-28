using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>A camera direction around the effect, in degrees.</summary>
    public struct CaptureView
    {
        public string Name;
        /// <summary>Rotation around world Y. 0 = camera on the -Z side looking toward +Z.</summary>
        public float Azimuth;
        /// <summary>Angle above the horizon. 90 = straight down.</summary>
        public float Elevation;

        public CaptureView(string name, float azimuth, float elevation)
        {
            Name = name;
            Azimuth = azimuth;
            Elevation = elevation;
        }
    }

    public struct CaptureBackground
    {
        public string Name;
        public Color Color;

        public CaptureBackground(string name, Color color)
        {
            Name = name;
            Color = color;
        }
    }

    /// <summary>Parsed, validated parameters for a timeline capture.</summary>
    public sealed class CaptureRequest
    {
        public const int MinFrameSize = 64;
        public const int MaxFrameSize = 1024;
        public const int MaxTimes = 24;
        public const int MaxFrames = 192;

        public static readonly float[] DefaultTimes = { 0f, 0.05f, 0.1f, 0.2f, 0.35f, 0.5f, 0.75f, 1f, 1.5f };

        static readonly Dictionary<string, CaptureView> ViewPresets = new Dictionary<string, CaptureView>(StringComparer.OrdinalIgnoreCase)
        {
            { "front", new CaptureView("front", 180f, 0f) },
            { "back", new CaptureView("back", 0f, 0f) },
            { "side", new CaptureView("side", 90f, 0f) },
            { "top", new CaptureView("top", 0f, 89f) },
            { "three_quarter", new CaptureView("three_quarter", 145f, 25f) },
            { "low", new CaptureView("low", 160f, 8f) },
        };

        static readonly Dictionary<string, Color> BackgroundPresets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "dark", new Color(0.07f, 0.07f, 0.09f, 1f) },
            { "mid", new Color(0.32f, 0.33f, 0.36f, 1f) },
            { "light", new Color(0.78f, 0.80f, 0.82f, 1f) },
        };

        public string Target;
        public float[] Times = DefaultTimes;
        public List<CaptureView> Views = new List<CaptureView> { ViewPresets["three_quarter"] };
        public List<CaptureBackground> Backgrounds = new List<CaptureBackground> { new CaptureBackground("dark", BackgroundPresets["dark"]) };
        public int FrameSize = 320;
        public int Seed = 1234;
        /// <summary>Framing radius in meters; 0 = fit the effect's bounds over all sampled times.</summary>
        public float FramingRadius;
        public float FieldOfView = 35f;
        public bool AddLight = true;
        public bool PostProcessing = true;
        /// <summary>
        /// VolumeProfile asset applied as a top-priority global volume during the capture, so the
        /// effect is judged under the game scene's post-processing. Defaults to the project's
        /// ProjectSettings/EffectDesigner.json volume_profile; "none" opts out (pipeline defaults only).
        /// Scene volumes never reach the capture.
        /// </summary>
        public string VolumeProfile;
        /// <summary>Re-frame each view on the visible pixels (ignored when FramingRadius is set).</summary>
        public bool AutoFrame = true;
        /// <summary>
        /// Camera placements to reuse, by view name (a previous result's viewFraming), so iterations
        /// are compared at the same scale and position. Views listed here skip auto framing.
        /// </summary>
        public Dictionary<string, (Vector3 lookAt, float distance)> ViewFraming =
            new Dictionary<string, (Vector3, float)>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Also render each system alone (first view, first background) and measure its colors.</summary>
        public bool SystemColorStats = true;
        /// <summary>Unlit ground plane color under the effect (at the effect's pivot height); null = none.</summary>
        public Color? Ground;
        public string GroundName;
        public float GroundHeight;
        /// <summary>Also save each system-alone render as a PNG (to compare a layer alone with the composite).</summary>
        public bool SystemFrames;
        public string OutputFolder = "Library/VFXToolkit/Captures";
        public string Label;

        public static IEnumerable<string> ViewPresetNames => ViewPresets.Keys;

        public static CaptureRequest FromJson(JObject json, out string error)
        {
            error = null;
            var request = new CaptureRequest();
            if (json == null)
            {
                error = "Parameters are required.";
                return null;
            }

            request.Target = (string)json["target"];
            if (string.IsNullOrWhiteSpace(request.Target))
            {
                error = "'target' is required: a scene GameObject name/path or a prefab asset path (Assets/...prefab).";
                return null;
            }

            if (json["times"] is JArray times && times.Count > 0)
            {
                var parsed = new List<float>();
                foreach (var t in times)
                {
                    float value = t.Value<float>();
                    if (value < 0f || float.IsNaN(value))
                    {
                        error = $"Invalid time {value}; times must be >= 0 seconds.";
                        return null;
                    }
                    parsed.Add(value);
                }
                parsed.Sort();
                if (parsed.Count > MaxTimes)
                {
                    error = $"At most {MaxTimes} times per capture.";
                    return null;
                }
                request.Times = parsed.ToArray();
            }

            if (json["views"] is JArray views && views.Count > 0)
            {
                request.Views = new List<CaptureView>();
                foreach (var v in views)
                {
                    if (!TryParseView(v, out var view, out error))
                        return null;
                    request.Views.Add(view);
                }
            }

            if (json["backgrounds"] is JArray backgrounds && backgrounds.Count > 0)
            {
                request.Backgrounds = new List<CaptureBackground>();
                foreach (var b in backgrounds)
                {
                    if (!TryParseBackground((string)b, out var bg, out error))
                        return null;
                    request.Backgrounds.Add(bg);
                }
            }

            if (json["frame_size"] != null)
                request.FrameSize = Mathf.Clamp(json["frame_size"].Value<int>(), MinFrameSize, MaxFrameSize);
            if (json["seed"] != null)
                request.Seed = json["seed"].Value<int>();
            if (json["framing_radius"] != null)
                request.FramingRadius = Mathf.Max(0f, json["framing_radius"].Value<float>());
            if (json["fov"] != null)
                request.FieldOfView = Mathf.Clamp(json["fov"].Value<float>(), 5f, 120f);
            if (json["add_light"] != null)
                request.AddLight = json["add_light"].Value<bool>();
            if (json["auto_frame"] != null)
                request.AutoFrame = json["auto_frame"].Value<bool>();
            if (json["system_color_stats"] != null)
                request.SystemColorStats = json["system_color_stats"].Value<bool>();
            var groundText = (string)json["ground"];
            if (!string.IsNullOrWhiteSpace(groundText) && !string.Equals(groundText, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseBackground(groundText, out var ground, out error))
                {
                    error = $"ground: {error}";
                    return null;
                }
                request.Ground = ground.Color;
                request.GroundName = ground.Name;
            }
            if (json["ground_height"] != null)
                request.GroundHeight = json["ground_height"].Value<float>();
            if (json["system_frames"] != null)
                request.SystemFrames = json["system_frames"].Value<bool>();
            if (json["view_framing"] != null && !TryParseViewFraming(json["view_framing"], request.ViewFraming, out error))
                return null;
            request.VolumeProfile = (string)json["volume_profile"];
            if (json["post_processing"] != null)
                request.PostProcessing = json["post_processing"].Value<bool>();
            if (!string.IsNullOrWhiteSpace((string)json["output_folder"]))
                request.OutputFolder = (string)json["output_folder"];
            request.Label = (string)json["label"];

            int frameCount = request.Times.Length * request.Views.Count * request.Backgrounds.Count;
            if (frameCount > MaxFrames)
            {
                error = $"Capture would produce {frameCount} frames (times x views x backgrounds); the limit is {MaxFrames}.";
                return null;
            }

            return request;
        }

        /// <summary>Accepts the result's own shape: [{view, lookAt | look_at: [x,y,z], distance}].</summary>
        static bool TryParseViewFraming(JToken token, Dictionary<string, (Vector3, float)> framing, out string error)
        {
            error = null;
            // Parameters may arrive as a JSON string when the client serializes nested values.
            if (token.Type == JTokenType.String)
            {
                try { token = JToken.Parse((string)token); }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    error = $"view_framing is not valid JSON: {ex.Message}";
                    return false;
                }
            }
            if (!(token is JArray entries))
            {
                error = "view_framing must be an array of {view, lookAt: [x,y,z], distance}, e.g. a previous capture's viewFraming.";
                return false;
            }
            foreach (var entry in entries)
            {
                var view = (string)entry["view"];
                var lookAt = (entry["lookAt"] ?? entry["look_at"]) as JArray;
                float distance = entry["distance"]?.Value<float>() ?? 0f;
                if (string.IsNullOrWhiteSpace(view) || lookAt == null || lookAt.Count != 3 || !(distance > 0f))
                {
                    error = $"Invalid view_framing entry {entry.ToString(Newtonsoft.Json.Formatting.None)}: needs view, lookAt [x,y,z] and distance > 0.";
                    return false;
                }
                framing[view] = (new Vector3(lookAt[0].Value<float>(), lookAt[1].Value<float>(), lookAt[2].Value<float>()), distance);
            }
            return true;
        }

        static bool TryParseView(JToken token, out CaptureView view, out string error)
        {
            error = null;
            view = default;
            if (token.Type == JTokenType.String)
            {
                if (ViewPresets.TryGetValue((string)token, out view))
                    return true;
                error = $"Unknown view '{token}'. Presets: {string.Join(", ", ViewPresets.Keys)}; or pass {{\"name\", \"azimuth\", \"elevation\"}}.";
                return false;
            }
            if (token is JObject obj)
            {
                float azimuth = obj["azimuth"]?.Value<float>() ?? 0f;
                float elevation = Mathf.Clamp(obj["elevation"]?.Value<float>() ?? 0f, -89f, 89f);
                string name = (string)obj["name"] ?? $"az{azimuth.ToString("0", CultureInfo.InvariantCulture)}_el{elevation.ToString("0", CultureInfo.InvariantCulture)}";
                view = new CaptureView(name, azimuth, elevation);
                return true;
            }
            error = "Each view must be a preset name or an object with azimuth/elevation.";
            return false;
        }

        static bool TryParseBackground(string value, out CaptureBackground background, out string error)
        {
            error = null;
            background = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Empty background value.";
                return false;
            }
            if (BackgroundPresets.TryGetValue(value, out var preset))
            {
                background = new CaptureBackground(value.ToLowerInvariant(), preset);
                return true;
            }
            if (ColorUtility.TryParseHtmlString(value, out var color))
            {
                color.a = 1f;
                background = new CaptureBackground(value.TrimStart('#').ToLowerInvariant(), color);
                return true;
            }
            error = $"Unknown background '{value}'. Use dark, mid, light or a hex color like #202030.";
            return false;
        }
    }
}
