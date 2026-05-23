using System;
using System.Collections.Generic;
using System.Linq;

namespace LensHH.StockMcp
{
    // Reverses a lens prescription front-to-back. Operates on the refractive
    // run only — OBJ, IMG, and any separate stop planes outside the refractive
    // run stay where they are. The algebra for the refractive run:
    //
    //   Original positions A..B (light travels A → B):
    //       S_k has Radius_k, Thickness_k (medium between S_k and S_{k+1}),
    //                  Material_k (the medium itself).
    //
    //   Reversed positions 0..(B-A):
    //       new[i] = original[B-i] with:
    //           Radius        → -Radius          (Infinity stays Infinity)
    //           Thickness     → Thickness from original[B-i-1] for i < B-A,
    //                            or from original[B] (BFL) for the last surface
    //           Material      → same index shift as Thickness
    //           Everything else (Conic, AsphericCoefficients, SemiDiameter,
    //           Comment, IsStop, …) travels with the surface unchanged.
    //
    // Pickups become meaningless after reordering and are dropped. The engine
    // .lhlt fields we don't model (MeritFunction, ConfigurationEditor) are
    // also dropped — fine for stock-catalog inputs which never have them.
    public static class LensReversal
    {
        public static LhltFile Reverse(LhltFile input)
        {
            int n = input.Surfaces.Count;
            if (n < 3)
                throw new InvalidOperationException(
                    "Cannot reverse a lens prescription with fewer than 3 surfaces.");

            (int A, int B) = FindRefractiveRun(input);
            if (A < 0)
                throw new InvalidOperationException(
                    "No refractive surfaces found to reverse.");

            var output = new LhltFile
            {
                FormatVersion = input.FormatVersion,
                Title = string.IsNullOrEmpty(input.Title)
                    ? "(reversed)"
                    : input.Title + " (reversed)",
                Notes = input.Notes,
                Designer = input.Designer,
                Aperture = new LhltAperture { Type = input.Aperture.Type, Value = input.Aperture.Value },
                FieldType = input.FieldType,
                Wavelengths = input.Wavelengths
                    .Select(w => new LhltWavelength { Value = w.Value, Weight = w.Weight, IsPrimary = w.IsPrimary })
                    .ToList(),
                Fields = input.Fields
                    .Select(f => new LhltField { Y = f.Y, Weight = f.Weight })
                    .ToList(),
                Pickups = new List<LhltPickup>(),
                RayAiming = input.RayAiming,
                IsAfocal = input.IsAfocal,
                PenalizeVignetting = input.PenalizeVignetting,
                GlassCatalogs = new List<string>(input.GlassCatalogs),
            };

            var surfaces = new List<LhltSurface>(n);

            for (int i = 0; i < A; i++)
                surfaces.Add(Clone(input.Surfaces[i]));

            int runLen = B - A + 1;
            for (int k = 0; k < runLen; k++)
            {
                var rev = Clone(input.Surfaces[B - k]);

                if (!double.IsInfinity(rev.Radius))
                    rev.Radius = -rev.Radius;

                int sourceIdx = k < runLen - 1 ? (B - k - 1) : B;
                rev.Thickness = input.Surfaces[sourceIdx].Thickness;
                rev.Material  = input.Surfaces[sourceIdx].Material;

                surfaces.Add(rev);
            }

            for (int i = B + 1; i < n; i++)
                surfaces.Add(Clone(input.Surfaces[i]));

            for (int i = 0; i < surfaces.Count; i++)
                surfaces[i].Index = i;

            output.Surfaces = surfaces;
            return output;
        }

        // Refractive run = surfaces between OBJ (index 0) and IMG (index n-1)
        // that either have curvature OR bound a non-air medium. This skips
        // separate stop planes (R=Infinity, no adjacent glass) but keeps
        // is_stop=true rides that sit on a curved surface (they travel with
        // their surface through the reversal).
        private static (int A, int B) FindRefractiveRun(LhltFile f)
        {
            int n = f.Surfaces.Count;
            int A = -1, B = -1;
            for (int i = 1; i < n - 1; i++)
            {
                var s = f.Surfaces[i];
                bool curved = !double.IsInfinity(s.Radius);
                bool boundsGlass = !string.IsNullOrEmpty(s.Material)
                                || !string.IsNullOrEmpty(f.Surfaces[i - 1].Material);
                if (curved || boundsGlass)
                {
                    if (A == -1) A = i;
                    B = i;
                }
            }
            return (A, B);
        }

        private static LhltSurface Clone(LhltSurface s) => new LhltSurface
        {
            Index = s.Index,
            Type = s.Type,
            Comment = s.Comment,
            Radius = s.Radius,
            Thickness = s.Thickness,
            Material = s.Material,
            SemiDiameter = s.SemiDiameter,
            SemiDiameterMode = s.SemiDiameterMode,
            ClearAperturePercent = s.ClearAperturePercent,
            Conic = s.Conic,
            IsStop = s.IsStop,
            InnerRadius = s.InnerRadius,
            ObscurationRadius = s.ObscurationRadius,
            FloatingApertureRadius = s.FloatingApertureRadius,
            AsphericCoefficients = s.AsphericCoefficients?.ToArray(),
            CurvatureVariable = s.CurvatureVariable,
            ThicknessVariable = s.ThicknessVariable,
            ConicVariable = s.ConicVariable,
            AsphericVariable = s.AsphericVariable?.ToArray(),
            HasMarginalRaySolve = s.HasMarginalRaySolve,
        };
    }
}
