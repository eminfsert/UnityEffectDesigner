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

        Console.WriteLine(failures == 0 ? "All recipe mapping checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    static string[] Run(string file)
    {
        var recipe = JObject.Parse(File.ReadAllText(file));
        recipe["dry_run"] = true;
        ParticleRecipeBuilder.Apply(recipe, out var errors);
        return errors.Where(e => !IsNative(e)).ToArray();
    }
}
