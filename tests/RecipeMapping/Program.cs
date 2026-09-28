using System;
using System.IO;
using System.Linq;
using EffectDesigner.VFXToolkit.Editor.Recipes;
using Newtonsoft.Json.Linq;

static class Program
{
    // Conversions that call into the engine (AnimationCurve, Gradient, ColorUtility...) cannot run
    // outside the Unity editor. Those errors are expected here; only mapping errors count.
    static bool IsNative(string e) => e.Contains("ECall methods");

    static int Main(string[] args)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "recipes");
        int failures = 0;

        var sample = Run(Path.Combine(dir, "sample_recipe.json"));
        if (sample.Length > 0)
        {
            Console.WriteLine("FAIL sample_recipe.json should map cleanly:");
            foreach (var e in sample) Console.WriteLine("   " + e);
            failures++;
        }
        else Console.WriteLine("PASS sample_recipe.json maps every key to a writable Unity property");

        var bad = Run(Path.Combine(dir, "bad_recipe.json"));
        string[] expected =
        {
            "main.start_lifetim: unknown property. Did you mean: start_lifetime",
            "'wordl' is not a valid ParticleSystemSimulationSpace",
            "main.particle_count: unknown property. Did you mean: max_particles",
            "emision: unknown module. Did you mean: emission",
            "shape.radius:",
            "shape.angle_deg: 'angle' is already in degrees",
            "sub_emitters[0].system: 'Missing' is not a system in this recipe",
            "'stretched' is not a valid ParticleSystemRenderMode",
            "Parent cycle between systems: B, C",
            "renderer.material.path: an inline material needs",
        };
        foreach (var e in expected)
        {
            if (bad.Any(b => b.Contains(e))) Console.WriteLine($"PASS bad_recipe.json reports: {e}");
            else { Console.WriteLine($"FAIL bad_recipe.json should report: {e}"); failures++; }
        }
        if (bad.Length != expected.Length)
        {
            Console.WriteLine($"FAIL bad_recipe.json reported {bad.Length} errors, expected {expected.Length}:");
            foreach (var e in bad) Console.WriteLine("   " + e);
            failures++;
        }

        // HDR on particle colors: Shuriken stores 8-bit colors, so the tool must rescale and warn.
        var hdr = JObject.Parse(File.ReadAllText(Path.Combine(dir, "hdr_recipe.json")));
        hdr["dry_run"] = true;
        var hdrResult = ParticleRecipeBuilder.Apply(hdr, out var hdrErrors);
        int hdrWarnings = hdrResult?.warnings.Count(w => w.Contains("HDR color")) ?? 0;
        if (hdrErrors.Count == 0 && hdrWarnings == 2)
            Console.WriteLine("PASS hdr_recipe.json warns about HDR particle colors (start_color, color_over_trail[0])");
        else
        {
            Console.WriteLine($"FAIL hdr_recipe.json: expected 2 HDR warnings and no errors, got {hdrWarnings} warning(s), errors: {string.Join("; ", hdrErrors)}");
            failures++;
        }

        failures += CheckColorStats();
        failures += CheckHdrIntensity();
        failures += CheckCaptureLogCulture();
        failures += CheckReadabilityWarning();
        failures += CheckEasingKeys();
        failures += CheckToolkitVersion();
        failures += CheckFrameTimes();
        failures += CheckMeshShapes();
        failures += CheckGroundStats();

        Console.WriteLine(failures == 0 ? "All recipe mapping checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    // Capture color metric: a gold effect must read as colored, the same effect washed to white must not.
    static int CheckColorStats()
    {
        const int size = 64;
        var background = Enumerable.Repeat(new UnityEngine.Color32(20, 20, 26, 255), size * size).ToArray();
        UnityEngine.Color32[] Frame(UnityEngine.Color32 effect)
        {
            var f = (UnityEngine.Color32[])background.Clone();
            for (int i = 0; i < size * size / 4; i++) f[i] = effect;
            return f;
        }
        var gold = EffectDesigner.VFXToolkit.Editor.Capture.ColorStats.Measure(Frame(new UnityEngine.Color32(255, 190, 70, 255)), background, 0f);
        var washed = EffectDesigner.VFXToolkit.Editor.Capture.ColorStats.Measure(Frame(new UnityEngine.Color32(236, 233, 224, 255)), background, 0f);
        bool ok = gold.washedOut == 0f && washed.washedOut == 1f && gold.hue > 30f && gold.hue < 45f && Math.Abs(gold.coverage - 0.25f) < 0.001f;
        Console.WriteLine(ok
            ? "PASS color stats: gold washedOut 0 (hue " + gold.hue.ToString("0") + "), near-white washedOut 1"
            : $"FAIL color stats: gold washedOut {gold.washedOut} hue {gold.hue} coverage {gold.coverage}, near-white washedOut {washed.washedOut}");
        return ok ? 0 : 1;
    }

    // HDR intensity is in stops of linear light (Unity's HDR picker convention): +1 stop doubles the linear value.
    static int CheckHdrIntensity()
    {
        var baseColor = new UnityEngine.Color(1f, 0.69f, 0.188f, 1f); // #FFB030
        var hdr = ColorSpaceMath.WithIntensity(baseColor, 1f);
        float ratioR = ColorSpaceMath.SrgbToLinear(hdr.r) / ColorSpaceMath.SrgbToLinear(baseColor.r);
        float ratioB = ColorSpaceMath.SrgbToLinear(hdr.b) / ColorSpaceMath.SrgbToLinear(baseColor.b);
        float roundTrip = ColorSpaceMath.LinearToSrgb(ColorSpaceMath.SrgbToLinear(0.42f));
        // [HDR] material properties get the linear value: #5A3000 @0 -> (0.102, 0.030, 0), white @1 -> 2.
        var dark = ColorSpaceMath.ForHdrProperty(new UnityEngine.Color(0x5A / 255f, 0x30 / 255f, 0f, 1f), true);
        var whiteUp = ColorSpaceMath.ForHdrProperty(ColorSpaceMath.WithIntensity(new UnityEngine.Color(1f, 1f, 1f, 1f), 1f), true);
        var gammaProject = ColorSpaceMath.ForHdrProperty(new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f), false);
        bool ok = Math.Abs(ratioR - 2f) < 1e-3f && Math.Abs(ratioB - 2f) < 1e-3f && Math.Abs(roundTrip - 0.42f) < 1e-5f && hdr.a == 1f
                  && Math.Abs(dark.r - 0.102f) < 0.001f && Math.Abs(dark.g - 0.030f) < 0.001f && dark.b == 0f
                  && Math.Abs(whiteUp.r - 2f) < 1e-3f && Math.Abs(ColorSpaceMath.ForHdrProperty(hdr, true).r / ColorSpaceMath.SrgbToLinear(baseColor.r) - 2f) < 1e-3f
                  && gammaProject.r == 0.5f;
        Console.WriteLine(ok
            ? $"PASS HDR intensity: +1 stop doubles linear light; [HDR] properties get linear values (#5A3000 -> {dark.r:0.###}, {dark.g:0.###}, {dark.b:0.###}; white @1 -> {whiteUp.r:0.###})"
            : $"FAIL HDR intensity: linear ratios R {ratioR}, B {ratioB}, round trip {roundTrip}");
        return ok ? 0 : 1;
    }

    // Capture menu log lines must not depend on the editor's culture (a Turkish editor printed "0,95").
    static int CheckCaptureLogCulture()
    {
        var result = new EffectDesigner.VFXToolkit.Editor.Capture.TimelineCaptureResult
        {
            times = new[] { 0f, 0.35f },
            colorStatsFor = "three_quarter/dark",
            postProcessing = new EffectDesigner.VFXToolkit.Editor.Capture.PostProcessInfo
            {
                volumeProfile = "Assets/Settings/Profile.asset", volumeProfileSource = "volume_profile parameter",
                tonemapping = "Neutral", bloom = true, bloomThreshold = 0.95f, bloomIntensity = 0.85f,
            },
        };
        result.systemParticleCounts["Sparks"] = new System.Collections.Generic.List<int> { 0, 40 };
        result.colorStats.Add(new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { time = 0.35f, coverage = 0.0321f, washedOut = 0.45f, saturation = 0.52f, hue = 41.1f });
        result.colorStatsByBackground["dark"] = result.colorStats;
        result.systemColorStats["Sparks"] = new System.Collections.Generic.List<EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats>
        {
            new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { time = 0.35f, coverage = 0.0105f, washedOut = -1f, saturation = 0.71f, hue = 35.4f },
        };

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var lines = EffectDesigner.VFXToolkit.Editor.Capture.CaptureMenu.SummaryLines(result).ToList();
            var numeric = lines.Where(l => l.StartsWith("Post-processing") || l.StartsWith("Color") || l.StartsWith("Times")).ToList();
            bool ok = numeric.Count == 4 && numeric.All(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"\d,\d") && !l.Contains("%0") && !l.Contains("% "))
                      && numeric.Any(l => l.Contains("threshold 0.95 intensity 0.85")) && numeric.Any(l => l.StartsWith("Color three_quarter/dark") && l.Contains("saturation 0.52") && l.Contains("washedOut 45%"))
                      && numeric.Any(l => l.StartsWith("Color Sparks alone 0.35") && l.Contains("washedOut -") && l.Contains("hue 35deg"));
            Console.WriteLine(ok ? "PASS capture log lines are culture-invariant under tr-TR" : "FAIL capture log lines under tr-TR:\n   " + string.Join("\n   ", numeric));
            return ok ? 0 : 1;
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // Readability gate: much lower coverage on a light background than on dark is flagged, in an invariant format.
    static int CheckReadabilityWarning()
    {
        EffectDesigner.VFXToolkit.Editor.Capture.TimelineCaptureResult Make(float light)
        {
            var r = new EffectDesigner.VFXToolkit.Editor.Capture.TimelineCaptureResult();
            r.colorStatsByBackground["dark"] = new System.Collections.Generic.List<EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats>
                { new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = 0.10f }, new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = 0.06f } };
            r.colorStatsByBackground["light"] = new System.Collections.Generic.List<EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats>
                { new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = light }, new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = light } };
            EffectDesigner.VFXToolkit.Editor.Capture.TimelineCapture.WarnAboutReadability(r);
            return r;
        }
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var faint = Make(0.02f);
            var fine = Make(0.07f);
            bool ok = faint.warnings.Count == 1 && faint.warnings[0].Contains("'light'") && faint.warnings[0].Contains("25%") && faint.warnings[0].Contains("8.0%")
                      && fine.warnings.Count == 0;
            Console.WriteLine(ok ? "PASS readability: low coverage on light vs dark is flagged (culture-invariant), comparable coverage is not"
                                 : $"FAIL readability: faint -> [{string.Join(" | ", faint.warnings)}], fine -> [{string.Join(" | ", fine.warnings)}]");
            return ok ? 0 : 1;
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // Named curves: a spike is visible from its first frame, a pop leaves its start fast (tangents follow the ease, not ClampedAuto).
    static int CheckEasingKeys()
    {
        var spike = Easing.Keys("spike", 0f, 1f);
        var pop = Easing.Keys("pop", 0f, 1f);
        var outExpo = Easing.Keys("ease_out_expo", 2f, 0f);
        bool ok = spike[0].value >= 0.8f && spike[spike.Length - 1].value <= 0.001f
                  && pop[0].value == 0f && pop[0].outTangent > 10f && pop.Max(k => k.value) >= 1.15f
                  && outExpo[0].value == 2f && outExpo[0].outTangent < -10f && Math.Abs(outExpo[outExpo.Length - 1].value) < 0.01f;
        Console.WriteLine(ok
            ? $"PASS easing keys: spike starts at {spike[0].value:0.00}, pop start slope {pop[0].outTangent:0}, ease_out_expo start slope {outExpo[0].outTangent:0}"
            : $"FAIL easing keys: spike[0] {spike[0].value}, pop[0] {pop[0].value}/{pop[0].outTangent}, expo[0] {outExpo[0].value}/{outExpo[0].outTangent}");
        return ok ? 0 : 1;
    }

    // Tool results report ToolkitInfo.Version; it must match the package agents install.
    static int CheckToolkitVersion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "unity-package")))
            dir = dir.Parent;
        string version = dir == null ? null
            : (string)JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "unity-package", "com.effectdesigner.vfxtoolkit", "package.json")))["version"];
        bool ok = version == EffectDesigner.VFXToolkit.Editor.ToolkitInfo.Version;
        Console.WriteLine(ok ? $"PASS toolkit version {version} matches package.json"
                             : $"FAIL ToolkitInfo.Version {EffectDesigner.VFXToolkit.Editor.ToolkitInfo.Version} != package.json {version}");
        return ok ? 0 : 1;
    }

    // Capture times: t = 0 is the first frame, each 1/60 s adds one; times on the same frame are captured once.
    static int CheckFrameTimes()
    {
        int F(float t) => EffectDesigner.VFXToolkit.Editor.Capture.EffectSampler.FrameIndex(t);
        var request = new EffectDesigner.VFXToolkit.Editor.Capture.CaptureRequest { Times = new[] { 0f, 0.008f, 0.017f, 0.05f, 0.1f, 0.105f, 0.5f } };
        string note = EffectDesigner.VFXToolkit.Editor.Capture.TimelineCapture.MergeSameFrameTimes(request);
        var clean = new EffectDesigner.VFXToolkit.Editor.Capture.CaptureRequest { Times = new[] { 0f, 0.017f, 0.034f } };
        bool ok = F(0f) == 1 && F(0.008f) == 1 && F(0.017f) == 2 && F(0.05f) == 4 && F(0.5f) == 31
                  && request.Times.SequenceEqual(new[] { 0f, 0.017f, 0.05f, 0.1f, 0.5f })
                  && note != null && note.Contains("0.008 (same frame as 0)") && note.Contains("0.105 (same frame as 0.1)")
                  && EffectDesigner.VFXToolkit.Editor.Capture.TimelineCapture.MergeSameFrameTimes(clean) == null && clean.Times.Length == 3;
        Console.WriteLine(ok ? "PASS capture times: 60 fps frame index, same-frame times merged with a note"
                             : $"FAIL capture times: frames {F(0f)} {F(0.008f)} {F(0.017f)} {F(0.05f)} {F(0.5f)}, kept [{string.Join(", ", request.Times)}], note: {note}");
        return ok ? 0 : 1;
    }

    // Procedural meshes: every triangle faces along its normals, UVs in 0..1, domes rest on y = 0.
    static int CheckMeshShapes()
    {
        var problems = new System.Collections.Generic.List<string>();
        foreach (var shape in EffectDesigner.VFXToolkit.Editor.Meshes.MeshShapes.Names)
        {
            var m = EffectDesigner.VFXToolkit.Editor.Meshes.MeshShapes.Build(shape, new JObject(), out var error);
            if (m == null) { problems.Add($"{shape}: {error}"); continue; }
            int wrong = 0;
            for (int t = 0; t < m.Triangles.Count; t += 3)
            {
                var a = m.Vertices[m.Triangles[t]]; var b = m.Vertices[m.Triangles[t + 1]]; var c = m.Vertices[m.Triangles[t + 2]];
                var face = UnityEngine.Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f) continue;
                var n = m.Normals[m.Triangles[t]] + m.Normals[m.Triangles[t + 1]] + m.Normals[m.Triangles[t + 2]];
                if (UnityEngine.Vector3.Dot(face, n) <= 0) wrong++;
            }
            if (wrong > 0) problems.Add($"{shape}: {wrong} of {m.Triangles.Count / 3} triangles face away from their normals");
            if (m.Uv.Any(uv => uv.x < -1e-5f || uv.x > 1.00001f || uv.y < -1e-5f || uv.y > 1.00001f)) problems.Add($"{shape}: UV outside 0..1");
            if (m.Triangles.Any(i => i < 0 || i >= m.Vertices.Count)) problems.Add($"{shape}: index out of range");
            if (shape == "dome" || shape == "sphere" || shape == "ring" || shape == "cylinder")
            {
                float minY = m.Vertices.Min(v => v.y);
                if (Math.Abs(minY) > 1e-4f) problems.Add($"{shape}: lowest point at y = {minY}, expected 0");
            }
            if (shape == "dome" && Math.Abs(m.Vertices.Max(v => v.y) - 0.5f) > 1e-4f) problems.Add("dome: top should be at y = radius (0.5)");
        }
        var cone = EffectDesigner.VFXToolkit.Editor.Meshes.MeshShapes.Build("dome", new JObject { ["angle"] = 60, ["radius"] = 2 }, out _);
        if (Math.Abs(cone.Vertices.Min(v => v.y)) > 1e-4f) problems.Add("dome angle 60: rim not on y = 0");
        var bad = EffectDesigner.VFXToolkit.Editor.Meshes.MeshShapes.Build("donut", new JObject(), out var badError);
        if (bad != null || badError == null || !badError.Contains("dome")) problems.Add("unknown shape should list the shapes");
        Console.WriteLine(problems.Count == 0 ? "PASS meshes: dome, sphere, ring, cylinder, arc face outward with 0..1 UVs, grounded pivots"
                                              : "FAIL meshes: " + string.Join("; ", problems));
        return problems.Count == 0 ? 0 : 1;
    }

    // On a saturated ground: edge pixels in the ground's hue do not set the effect's hue; value contrast
    // separates dark ink (readable) from pale glow on a light background (not); identical backgrounds skip the ratio warning.
    static int CheckGroundStats()
    {
        const int size = 64;
        UnityEngine.Color32[] Fill(UnityEngine.Color32 c) => Enumerable.Repeat(c, size * size).ToArray();
        var grass = new UnityEngine.Color32(78, 138, 58, 255);
        var frame = Fill(grass);
        for (int i = 0; i < 600; i++) frame[i] = new UnityEngine.Color32(250, 246, 235, 255);          // white dome
        for (int i = 600; i < 900; i++) frame[i] = new UnityEngine.Color32(150, 190, 110, 255);        // edges blended with grass
        for (int i = 900; i < 1000; i++) frame[i] = new UnityEngine.Color32(255, 170, 60, 255);        // orange rim
        var dome = EffectDesigner.VFXToolkit.Editor.Capture.ColorStats.Measure(frame, Fill(grass), 0f);
        var ink = Fill(grass); for (int i = 0; i < 500; i++) ink[i] = new UnityEngine.Color32(14, 12, 10, 255);
        var inkStats = EffectDesigner.VFXToolkit.Editor.Capture.ColorStats.Measure(ink, Fill(grass), 0f);
        var light = new UnityEngine.Color32(199, 204, 209, 255);
        var glow = Fill(light); for (int i = 0; i < 500; i++) glow[i] = new UnityEngine.Color32(235, 222, 190, 255);
        var glowStats = EffectDesigner.VFXToolkit.Editor.Capture.ColorStats.Measure(glow, Fill(light), 0f);

        var same = new EffectDesigner.VFXToolkit.Editor.Capture.TimelineCaptureResult();
        var list = new System.Collections.Generic.List<EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats> { new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = 0.3f } };
        same.colorStatsByBackground["dark"] = list;
        same.colorStatsByBackground["light"] = new System.Collections.Generic.List<EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats> { new EffectDesigner.VFXToolkit.Editor.Capture.FrameColorStats { coverage = 0.3f } };
        EffectDesigner.VFXToolkit.Editor.Capture.TimelineCapture.WarnAboutReadability(same);

        bool ok = dome.hue > 20f && dome.hue < 45f && inkStats.valueContrast > 0.95f && glowStats.valueContrast < 0.05f
                  && same.warnings.Count == 0 && same.notes.Count == 1;
        Console.WriteLine(ok
            ? $"PASS ground stats: hue ignores ground-colored edges ({dome.hue:0}deg), contrast ink {inkStats.valueContrast:0.00} vs pale glow on light {glowStats.valueContrast:0.00}, identical backgrounds noted"
            : $"FAIL ground stats: dome hue {dome.hue}, ink contrast {inkStats.valueContrast}, glow contrast {glowStats.valueContrast}, warnings {same.warnings.Count}, notes {same.notes.Count}");
        return ok ? 0 : 1;
    }

    static string[] Run(string file)
    {
        var recipe = JObject.Parse(File.ReadAllText(file));
        recipe["dry_run"] = true;
        ParticleRecipeBuilder.Apply(recipe, out var errors);
        return errors.Where(e => !IsNative(e)).ToArray();
    }
}
