using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LensHH.StockMcp.Glass
{
    /// <summary>
    /// One glass from an AGF catalog: its dispersion formula (the AGF numbering, 1 to 13), its
    /// coefficients and the wavelength range its data covers.
    /// </summary>
    public class GlassData
    {
        public string Name { get; set; } = string.Empty;
        public string Catalog { get; set; } = string.Empty;
        public int DispersionFormula { get; set; }
        public double[] Coefficients { get; set; } = Array.Empty<double>();
        public double WavelengthMin { get; set; }
        public double WavelengthMax { get; set; }
        public double Nd { get; set; }
        public double Vd { get; set; }

        private double C(int i) => i < Coefficients.Length ? Coefficients[i] : 0.0;

        /// <summary>
        /// The refractive index at <paramref name="um"/> micrometres, for all thirteen AGF
        /// dispersion formulas: 1 Schott, 2 Sellmeier 1, 3 Herzberger, 4 Sellmeier 2, 5 Conrady,
        /// 6 Sellmeier 3, 7 and 8 Handbook of Optics 1 and 2, 9 Sellmeier 4, 10 Extended,
        /// 11 Sellmeier 5, 12 Extended 2, 13 Extended 3. The same definitions as LensHH-LT's engine.
        /// </summary>
        public double GetIndex(double um)
        {
            double l2 = um * um;
            double n2;
            switch (DispersionFormula)
            {
                case 1:
                    n2 = C(0) + C(1) * l2 + C(2) / l2 + C(3) / (l2 * l2) + C(4) / (l2 * l2 * l2) + C(5) / (l2 * l2 * l2 * l2);
                    break;
                case 2:
                    n2 = 1.0;
                    for (int i = 0; i < 3; i++) n2 += C(2 * i) * l2 / (l2 - C(2 * i + 1));
                    break;
                case 3:
                {
                    double L = 1.0 / (l2 - 0.028);
                    return C(0) + C(1) * L + C(2) * L * L + C(3) * l2 + C(4) * l2 * l2 + C(5) * l2 * l2 * l2;
                }
                case 4:
                    n2 = 1.0 + C(0) + C(1) * l2 / (l2 - C(2) * C(2)) + C(3) / (l2 - C(4) * C(4));
                    break;
                case 5:
                    return C(0) + C(1) / um + C(2) / Math.Pow(um, 3.5);
                case 6:
                    n2 = 1.0;
                    for (int i = 0; i < 4; i++) n2 += C(2 * i) * l2 / (l2 - C(2 * i + 1));
                    break;
                case 7:
                    n2 = C(0) + C(1) / (l2 - C(2)) - C(3) * l2;
                    break;
                case 8:
                    n2 = C(0) + C(1) * l2 / (l2 - C(2)) - C(3) * l2;
                    break;
                case 9:
                    n2 = C(0) + C(1) * l2 / (l2 - C(2)) + C(3) * l2 / (l2 - C(4));
                    break;
                case 10:
                    n2 = C(0) + C(1) * l2 + C(2) / l2 + C(3) / Math.Pow(l2, 2) + C(4) / Math.Pow(l2, 3)
                         + C(5) / Math.Pow(l2, 4) + C(6) / Math.Pow(l2, 5) + C(7) / Math.Pow(l2, 6);
                    break;
                case 11:
                    n2 = 1.0;
                    for (int i = 0; i < 5; i++) n2 += C(2 * i) * l2 / (l2 - C(2 * i + 1));
                    break;
                case 12:
                    n2 = C(0) + C(1) * l2 + C(2) / l2 + C(3) / Math.Pow(l2, 2) + C(4) / Math.Pow(l2, 3)
                         + C(5) / Math.Pow(l2, 4) + C(6) * Math.Pow(l2, 2) + C(7) * Math.Pow(l2, 3);
                    break;
                case 13:
                    n2 = C(0) + C(1) * l2 + C(2) * l2 * l2 + C(3) / l2 + C(4) / Math.Pow(l2, 2)
                         + C(5) / Math.Pow(l2, 3) + C(6) / Math.Pow(l2, 4) + C(7) / Math.Pow(l2, 5) + C(8) / Math.Pow(l2, 6);
                    break;
                default:
                    return Nd > 0 ? Nd : double.NaN;
            }
            return Math.Sqrt(n2);
        }
    }

    /// <summary>
    /// The AGF glass catalogs bundled with the stock-lens catalog, in <c>catalogs/Glass</c>. A
    /// glass is found by <c>CATALOG:NAME</c>, or by name in a preferred catalog order (a lens's own
    /// GlassCatalogs) and then in every catalog - the order LensHH-LT resolves a name in.
    /// </summary>
    public class GlassCatalogManager
    {
        private readonly Dictionary<string, GlassData> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _catalogs = new();

        public IReadOnlyList<string> LoadedCatalogs => _catalogs;

        /// <summary>Loads every .AGF in a folder, in name order. A missing folder loads nothing.</summary>
        public void LoadCatalogsFromFolder(string folder)
        {
            if (!Directory.Exists(folder)) return;
            var files = Directory.GetFiles(folder);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                if (Path.GetExtension(f).Equals(".agf", StringComparison.OrdinalIgnoreCase))
                    LoadCatalog(f);
        }

        /// <summary>Loads one AGF file; the catalog name is the file name, in capitals.</summary>
        public void LoadCatalog(string path)
        {
            string catalog = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            GlassData? g = null;
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in ReadLines(path))
            {
                var p = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0].ToUpperInvariant())
                {
                    case "NM":
                        g = null;
                        if (p.Length < 3) break;
                        g = new GlassData { Name = p[1], Catalog = catalog };
                        if (double.TryParse(p[2], NumberStyles.Float, inv, out var f)) g.DispersionFormula = (int)f;
                        if (p.Length > 4 && double.TryParse(p[4], NumberStyles.Float, inv, out var nd)) g.Nd = nd;
                        if (p.Length > 5 && double.TryParse(p[5], NumberStyles.Float, inv, out var vd)) g.Vd = vd;
                        _byKey[catalog + ":" + g.Name] = g;
                        break;
                    case "CD":
                        if (g == null) break;
                        var c = new double[Math.Max(10, p.Length - 1)];
                        for (int i = 1; i < p.Length; i++)
                            double.TryParse(p[i], NumberStyles.Float, inv, out c[i - 1]);
                        g.Coefficients = c;
                        break;
                    case "LD":
                        if (g == null) break;
                        if (p.Length > 1 && double.TryParse(p[1], NumberStyles.Float, inv, out var lo)) g.WavelengthMin = lo;
                        if (p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, inv, out var hi)) g.WavelengthMax = hi;
                        break;
                }
            }
            if (!_catalogs.Contains(catalog)) _catalogs.Add(catalog);
        }

        // AGF files come as UTF-16 (OpticStudio's own) or 8-bit text.
        private static IEnumerable<string> ReadLines(string path)
        {
            var bytes = File.ReadAllBytes(path);
            bool utf16 = bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
            string text = utf16 ? System.Text.Encoding.Unicode.GetString(bytes) : System.Text.Encoding.Latin1.GetString(bytes);
            if (utf16 && bytes[0] == 0xFE) text = System.Text.Encoding.BigEndianUnicode.GetString(bytes);
            return text.Replace("\r\n", "\n").Split('\n');
        }

        /// <summary>
        /// A glass by <c>CATALOG:NAME</c>, or by name in <paramref name="preferredCatalogs"/> order
        /// and then in every loaded catalog. Null when no catalog has it.
        /// </summary>
        public GlassData? GetGlass(string name, IList<string>? preferredCatalogs = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_byKey.TryGetValue(name, out var exact)) return exact;
            if (preferredCatalogs != null)
                foreach (var cat in preferredCatalogs)
                    if (_byKey.TryGetValue(cat + ":" + name, out var pg)) return pg;
            foreach (var cat in _catalogs)
                if (_byKey.TryGetValue(cat + ":" + name, out var any)) return any;
            return null;
        }

        /// <summary>
        /// The index of the medium after each surface at <paramref name="um"/> micrometres: air is
        /// 1, a mirror keeps the medium before it. Throws for a glass no catalog has, since a
        /// conversion made with a wrong index would be a wrong lens.
        /// </summary>
        public double[] BuildRefractiveIndexArray(LhltFile system, double um)
        {
            var n = new double[system.Surfaces.Count];
            IList<string>? pref = system.GlassCatalogs.Count > 0 ? system.GlassCatalogs : null;
            for (int i = 0; i < n.Length; i++)
            {
                var s = system.Surfaces[i];
                if (string.IsNullOrEmpty(s.Material) || s.Material.Equals("AIR", StringComparison.OrdinalIgnoreCase))
                    n[i] = 1.0;
                else if (s.IsMirror)
                    n[i] = i > 0 ? n[i - 1] : 1.0;
                else
                {
                    var g = GetGlass(s.Material, pref)
                        ?? throw new InvalidOperationException(
                            $"Glass {s.Material} (surface {i}) is not in the bundled glass catalogs, so its refractive index is not known.");
                    n[i] = g.GetIndex(um);
                }
            }
            return n;
        }

        /// <summary>
        /// The bundled catalogs: <c>catalogs/Glass</c> beside the stock-lens database, loaded once.
        /// </summary>
        public static GlassCatalogManager Bundled
        {
            get
            {
                if (_bundled == null)
                {
                    var m = new GlassCatalogManager();
                    string root = Path.GetDirectoryName(StockCatalog.ResolveDbPath())!;
                    m.LoadCatalogsFromFolder(Path.Combine(root, "Glass"));
                    _bundled = m;
                }
                return _bundled;
            }
        }
        private static GlassCatalogManager? _bundled;
    }
}
