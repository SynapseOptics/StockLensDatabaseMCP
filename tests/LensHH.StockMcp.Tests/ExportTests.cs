using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LensHH.StockMcp;
using LensHH.StockMcp.Glass;
using LensHH.StockMcp.Writers;
using Xunit;

namespace LensHH.StockMcp.Tests
{
    // The format writers, against what each target program itself writes - the rules LensHH-LT's
    // exporters were corrected to in 1.0.158, which these writers had not received. Each test names
    // what the old writer did.
    public class ExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "stockexport_" + Guid.NewGuid().ToString("N"));

        public ExportTests()
        {
            Directory.CreateDirectory(_dir);
            OptilandGlass.UserCatalogsFolderOverride = Path.Combine(_dir, "home", ".optiland", "catalogs");
        }

        public void Dispose()
        {
            OptilandGlass.UserCatalogsFolderOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private GlassCatalogManager Glass()
        {
            File.WriteAllText(Path.Combine(_dir, "SCHOTT.AGF"),
                "NM N-BK7 2 517642 1.5168 64.17 0 1 0\r\n" +
                "CD 1.03961212 0.00600069867 0.231792344 0.0200179144 1.01046945 103.560653 0 0 0 0\r\n" +
                "LD 0.3 2.5\r\n" +
                "NM N-SF5 2 673323 1.67271 32.25 0 1 0\r\n" +
                "CD 1.52481889 0.011254756 0.187085527 0.0588995392 1.42729015 129.141675 0 0 0 0\r\n" +
                "LD 0.37 2.5\r\n");
            var g = new GlassCatalogManager();
            g.LoadCatalog(Path.Combine(_dir, "SCHOTT.AGF"));
            return g;
        }

        // A cemented doublet, F d C with d primary, an aspheric front, a fixed and an automatic
        // semi-diameter; a title with a number in it, as a third of the catalog's have.
        private static LhltFile Doublet(double objectDistance = double.PositiveInfinity)
        {
            var lens = new LhltFile
            {
                Title = "62478 Test Achromat",
                Aperture = new LhltAperture { Type = ApertureType.EPD, Value = 12.5 },
                FieldType = FieldType.ObjectAngle,
                GlassCatalogs = new List<string> { "SCHOTT" },
            };
            lens.Wavelengths.Add(new LhltWavelength { Value = 0.4861327, Weight = 1 });
            lens.Wavelengths.Add(new LhltWavelength { Value = 0.5875618, Weight = 1, IsPrimary = true });
            lens.Wavelengths.Add(new LhltWavelength { Value = 0.6562725, Weight = 1 });
            lens.Fields.Add(new LhltField { Y = 0 });
            lens.Fields.Add(new LhltField { Y = 2 });
            lens.Surfaces.Add(new LhltSurface { Index = 0, Thickness = objectDistance });
            lens.Surfaces.Add(new LhltSurface { Index = 1, Radius = 61.47, Thickness = 6, Material = "N-BK7", IsStop = true,
                                                Type = SurfaceType.EvenAsphere, Conic = -0.5,
                                                AsphericCoefficients = new[] { 0.0, 2e-6, -3e-9, 0, 0, 0, 0, 0 },
                                                SemiDiameter = 12.7, SemiDiameterMode = SemiDiameterMode.Fixed });
            lens.Surfaces.Add(new LhltSurface { Index = 2, Radius = -44.64, Thickness = 2.5, Material = "N-SF5",
                                                SemiDiameter = 12.5, SemiDiameterMode = SemiDiameterMode.Auto });
            lens.Surfaces.Add(new LhltSurface { Index = 3, Radius = -129.94, Thickness = 97, SemiDiameter = 12.4 });
            lens.Surfaces.Add(new LhltSurface { Index = 4 });
            return lens;
        }

        private string Export(LhltFile lens, string format)
        {
            string path = Path.Combine(_dir, "lens" + LensExport.Formats[format].Ext);
            LensExport.Write(lens, format, path, Glass(), installOptilandGlasses: false);
            return File.ReadAllText(path);
        }

        private static string[] Lines(string t) => t.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToArray();

        [Fact]
        public void OsloPutsThePrimaryFirstAndKeepsNumbersOutOfTheName()
        {
            var t = Lines(Export(Doublet(), "oslo"));
            // OSLO's primary is wavelength 1. (Stored order made an F d C lens an F-line lens.)
            Assert.Contains("WV  0.58756 0.48613 0.65627", t);
            // A number standing as a word is read as the surface count, and OSLO refuses the file.
            Assert.Contains("LEN NEW \"Test Achromat\"", t);
            Assert.Contains("SNO1 \"62478 Test Achromat\"", t);
        }

        [Fact]
        public void OsloNameCutToLengthLeavesNoNumberAtTheEnd()
        {
            // Cut at 32 characters this title ends "DIA; 10" - a number OSLO would read as the
            // surface count. (Ross Optical's L-AOC doublets, 41 of them.)
            var lens = Doublet();
            lens.Title = "POSITIVE DOUBLET; 6.00MM DIA; 100.00MM EFL";
            var t = Lines(Export(lens, "oslo"));
            Assert.Contains("LEN NEW \"POSITIVE DOUBLET; 6.00MM DIA;\"", t);
        }

        [Fact]
        public void OsloChecksOnlyTheAperturesThatClip()
        {
            var t = Lines(Export(Doublet(), "oslo"));
            Assert.Contains("AP CHK 12.7", t);                    // fixed
            Assert.Contains("AP 12.5", t);                        // automatic: drawn, never blocks
            Assert.DoesNotContain("AP CHK 12.5", t);
        }

        [Fact]
        public void OsloGivesAFiniteObjectAnNaAndAnObjectHeight()
        {
            var t = Lines(Export(Doublet(objectDistance: 300), "oslo"));
            Assert.Contains(t, l => l.StartsWith("NAO "));
            Assert.Contains(t, l => l.StartsWith("OBH "));
            Assert.DoesNotContain(t, l => l.StartsWith("EBR ") || l.StartsWith("ANG "));
            // The object height is the field angle carried to the object: tan(2 deg) times the
            // distance from the object to the entrance pupil, which here is at surface 1 (the stop).
            double obh = double.Parse(t.First(l => l.StartsWith("OBH ")).Substring(4), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(300 * Math.Tan(2 * Math.PI / 180), obh, 9);
        }

        [Fact]
        public void CodeVWritesTheAsphereAsCodeVDoesAndQualifiesTheGlass()
        {
            var t = Lines(Export(Doublet(), "codev"));
            int asp = Array.IndexOf(t, "ASP");
            Assert.True(asp > 0, "ASP");
            Assert.Equal("K -0.5", t[asp + 1]);
            Assert.StartsWith("A 2.00000000000000E-006 ; B -3.00000000000000E-009", t[asp + 2]);
            // CON after ASP made the surface a plain conic in Code V.
            Assert.DoesNotContain("CON", t);
            Assert.Contains(t, l => l.StartsWith("S 61.47 6 NBK7_SCHOTT"));
            Assert.Contains(t, l => l.StartsWith("S -44.64 2.5 NSF5_SCHOTT"));
            Assert.Contains("REF 2", t);
        }

        [Fact]
        public void CodeVWritesAGlassFromACatalogItLacksAsAPrivateGlass()
        {
            // LightPath's D-ZLAF52LA_M, stripped to DZLAF52LAM, is LightPath's other glass
            // D-ZLAF52LAM - and Code V has no LightPath catalog to qualify it with.
            var glass = Glass();
            File.WriteAllText(Path.Combine(_dir, "LIGHTPATH.AGF"),
                "NM D-ZLAF52LA_M 2 000000 1.8 40 0 1 0\r\n" +
                "CD 1.03961212 0.00600069867 0.231792344 0.0200179144 1.01046945 103.560653 0 0 0 0\r\n" +
                "LD 0.3 2.5\r\n");
            glass.LoadCatalog(Path.Combine(_dir, "LIGHTPATH.AGF"));
            var lens = Doublet();
            lens.GlassCatalogs = new List<string> { "SCHOTT", "LIGHTPATH" };
            lens.Surfaces[2].Material = "D-ZLAF52LA_M";
            string path = Path.Combine(_dir, "prv.seq");
            LensExport.Write(lens, "codev", path, glass, installOptilandGlasses: false);
            var t = Lines(File.ReadAllText(path));

            int prv = Array.IndexOf(t, "PRV");
            Assert.True(prv > 0, "PRV");
            Assert.Equal("PWL 486.1327 587.5618 656.2725", t[prv + 1]);
            var row = t[prv + 2].Split(' ');
            Assert.Equal("'DZLAF52LAM'", row[0]);
            Assert.Equal(glass.GetGlass("D-ZLAF52LA_M", null)!.GetIndex(0.5875618), double.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture), 12);
            Assert.Equal("END", t[prv + 3]);
            Assert.Contains(t, l => l.StartsWith("S -44.64 2.5 DZLAF52LAM"));
            Assert.DoesNotContain(t, l => l.Contains("DZLAF52LAM_"));
            Assert.Contains(t, l => l.StartsWith("S 61.47 6 NBK7_SCHOTT"));   // a Code V catalog's glass still by name
        }

        [Fact]
        public void OptalixWritesWhatOptalixWrites()
        {
            var t = Lines(Export(Doublet(), "optalix"));
            Assert.Single(t, l => l.StartsWith("RAIM"));           // was RAIM 0 then RAIM 2
            Assert.Contains("FTYP 1", t);                         // angles
            Assert.Contains("PIM 0", t);
            Assert.Single(t, l => l == "FH 1 1");                 // only the fixed aperture clips
            Assert.Contains(t, l => Regex.IsMatch(l, @"^ASP -0\.5 2\.0+E-006 -3\.0+E-009( 0\.0+E\+000){6} 0$"));
        }

        [Fact]
        public void OptilandNamesEveryGlassStrictlyWithItsOwnData()
        {
            string json = Export(Doublet(), "optiland");
            Assert.Contains("\"name\": \"N-BK7\", \"reference\": null, \"catalog\": \"lenshh-schott\", \"match_policy\": \"strict\"", json);
            Assert.DoesNotContain("\"robust_search\": true", json);
            Assert.True(File.Exists(Path.Combine(_dir, "lens_glass", "lenshh-schott", "N-SF5.yml")));
        }

        [Theory]
        [InlineData("oslo")]
        [InlineData("codev")]
        [InlineData("optalix")]
        public void AnR2TermIsRefusedWhereTheFormatHasNoPlaceForIt(string format)
        {
            var lens = Doublet();
            lens.Surfaces[1].AsphericCoefficients![0] = 1e-4;
            var ex = Assert.Throws<InvalidOperationException>(() => Export(lens, format));
            Assert.Contains("r²", ex.Message);
        }

        [Fact]
        public void OptilandCarriesAnR2Term()
        {
            // Optiland's even asphere starts at r^2 (coefficients[0] is r^2), so it can.
            var lens = Doublet();
            lens.Surfaces[1].AsphericCoefficients![0] = 1e-4;
            Assert.Contains("\"coefficients\": [0.0001, 2E-06, -3E-09", Export(lens, "optiland"));
        }

        [Fact]
        public void AGlassTheCatalogsLackIsReported()
        {
            var lens = Doublet();
            lens.Surfaces[2].Material = "D-LAK6M";
            var notes = LensExport.Write(lens, "codev", Path.Combine(_dir, "x.seq"), Glass());
            Assert.Contains(notes, n => n.Contains("D-LAK6M"));
        }
    }
}
