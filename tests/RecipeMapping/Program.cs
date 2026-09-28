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
        bool ok = Math.Abs(ratioR - 2f) < 1e-3f && Math.Abs(ratioB - 2f) < 1e-3f && Math.Abs(roundTrip - 0.42f) < 1e-5f && hdr.a == 1f;
        Console.WriteLine(ok
            ? $"PASS HDR intensity: +1 stop doubles linear light (#FFB030 @1 stored as {hdr.r:0.###}, {hdr.g:0.###}, {hdr.b:0.###})"
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

    static string[] Run(string file)
    {
        var recipe = JObject.Parse(File.ReadAllText(file));
        recipe["dry_run"] = true;
        ParticleRecipeBuilder.Apply(recipe, out var errors);
        return errors.Where(e => !IsNative(e)).ToArray();
    }
}
