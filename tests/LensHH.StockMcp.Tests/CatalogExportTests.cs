using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LensHH.StockMcp;
using LensHH.StockMcp.Glass;
using LensHH.StockMcp.Writers;
using Xunit;
using Xunit.Abstractions;

namespace LensHH.StockMcp.Tests
{
    // Every lens in the stock catalog, in every format. The catalog is not in this repository: it
    // is LENSHH_CATALOGS_DIR, or the sibling LensHH-LT tree build-release.ps1 takes it from. Where
    // neither is present the test says so and passes.
    public class CatalogExportTests
    {
        private readonly ITestOutputHelper _out;
        public CatalogExportTests(ITestOutputHelper output) => _out = output;

        private static string? CatalogDir()
        {
            var env = Environment.GetEnvironmentVariable("LENSHH_CATALOGS_DIR");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "stock-lens-catalog.sqlite"))) return env;
            string dir = AppContext.BaseDirectory;
            for (int up = 0; up < 8 && dir != null; up++, dir = Path.GetDirectoryName(dir)!)
            {
                var c = Path.Combine(dir, "..", "SynapseLensHH-LT", "LensHH-LT", "catalogs");
                if (File.Exists(Path.Combine(c, "stock-lens-catalog.sqlite"))) return Path.GetFullPath(c);
            }
            return null;
        }

        [Fact]
        public void EveryCatalogLensExportsToEveryFormatByItsRules()
        {
            var root = CatalogDir();
            if (root == null)
            {
                _out.WriteLine("No stock-lens catalog found (set LENSHH_CATALOGS_DIR); nothing to check.");
                return;
            }
            var glass = new GlassCatalogManager();
            glass.LoadCatalogsFromFolder(Path.Combine(root, "Glass"));
            string tmp = Path.Combine(Path.GetTempPath(), "stockcatalog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var failures = new List<string>();
            var refused = new Dictionary<string, int>();
            int lenses = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Lenses"), "*.lhlt", SearchOption.AllDirectories))
                {
                    var lens = LhltReader.Read(file);
                    lenses++;
                    bool r2 = lens.Surfaces.Any(s => s.AsphericCoefficients is { Length: > 0 } a && a[0] != 0);
                    int primary = lens.PrimaryWavelengthIndex;
                    foreach (var format in new[] { "oslo", "codev", "optalix", "optiland", "zemax" })
                    {
                        string path = Path.Combine(tmp, "x" + LensExport.Formats[format].Ext);
                        try
                        {
                            LensExport.Write(lens, format, path, glass, installOptilandGlasses: false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            refused[format + ": " + (ex.Message.Contains("r²") ? "r² term" : ex.Message)] =
                                refused.GetValueOrDefault(format + ": " + (ex.Message.Contains("r²") ? "r² term" : ex.Message)) + 1;
                            if (!(r2 && format is "oslo" or "codev" or "optalix") && !ex.Message.Contains("not in the bundled glass catalogs"))
                                failures.Add($"{file} {format}: {ex.Message}");
                            continue;
                        }
                        string text = File.ReadAllText(path);
                        var t = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToArray();
                        if (format == "oslo")
                        {
                            string wv = t.FirstOrDefault(l => l.StartsWith("WV ")) ?? "";
                            string first = wv.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
                            if (lens.Wavelengths.Count > 0 && first != lens.Wavelengths[primary].Value.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                                failures.Add($"{file} oslo: primary not first ({wv})");
                            string name = t.First(l => l.StartsWith("LEN NEW")).Substring(8).Trim('"');
                            if (name.Split(' ').Any(w => double.TryParse(w, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
                                failures.Add($"{file} oslo: a number in LEN NEW \"{name}\"");
                        }
                        if (format == "codev" && Array.IndexOf(t, "ASP") >= 0 && t.Contains("CON"))
                        {
                            // CON may belong to another surface; it must not follow an ASP directly.
                            for (int i = 0; i < t.Length - 1; i++)
                                if (t[i] == "ASP" && t.Skip(i + 1).TakeWhile(l => !l.StartsWith("S ") && !l.StartsWith("SI ")).Contains("CON"))
                                    failures.Add($"{file} codev: CON after ASP");
                        }
                        if (format == "optalix" && t.Count(l => l.StartsWith("RAIM")) != 1)
                            failures.Add($"{file} optalix: RAIM written {t.Count(l => l.StartsWith("RAIM"))} times");
                        if (format == "optiland" && text.Contains("\"robust_search\": true"))
                            failures.Add($"{file} optiland: robust_search");
                    }
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            _out.WriteLine($"{lenses} lenses exported to five formats.");
            foreach (var kv in refused.OrderBy(k => k.Key))
                _out.WriteLine($"  refused, {kv.Key}: {kv.Value}");
            foreach (var f in failures.Take(40))
                _out.WriteLine("FAIL " + f);
            Assert.Empty(failures);
        }
    }
}
