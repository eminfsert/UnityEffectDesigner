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

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var lines = EffectDesigner.VFXToolkit.Editor.Capture.CaptureMenu.SummaryLines(result).ToList();
            var numeric = lines.Where(l => l.StartsWith("Post-processing") || l.StartsWith("Color") || l.StartsWith("Times")).ToList();
            bool ok = numeric.Count == 3 && numeric.All(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"\d,\d") && !l.Contains("%0") && !l.Contains("% "))
                      && numeric.Any(l => l.Contains("threshold 0.95 intensity 0.85")) && numeric.Any(l => l.Contains("saturation 0.52") && l.Contains("washedOut 45%"));
            Console.WriteLine(ok ? "PASS capture log lines are culture-invariant under tr-TR" : "FAIL capture log lines under tr-TR:\n   " + string.Join("\n   ", numeric));
            return ok ? 0 : 1;
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    static string[] Run(string file)
    {
        var recipe = JObject.Parse(File.ReadAllText(file));
        recipe["dry_run"] = true;
        ParticleRecipeBuilder.Apply(recipe, out var errors);
        return errors.Where(e => !IsNative(e)).ToArray();
    }
}
