using System;

namespace LensHH.StockMcp.Writers
{
    /// <summary>
    /// The paraxial quantities the format writers need for their conversions - an F-number into an
    /// entrance pupil, a field angle at a finite object into an object height - from the lens and
    /// its refractive indices (index i is the medium after surface i). The same recurrence as
    /// LensHH-LT's paraxial trace: y at surface 1 and slope u in object space, refracted by
    /// (n' - n) c at each surface and transferred by the thickness after it.
    /// </summary>
    internal static class Paraxial
    {
        // The optical surfaces are 1 .. Count - 2; the last is the image.
        private static int Last(LhltFile s) => s.Surfaces.Count - 2;

        // Height and slope after surface `through` of the ray with height y1 at surface 1 and
        // slope u0 in object space. Mirrors reverse the sign of the index from there on.
        private static (double y, double u) Trace(LhltFile s, double[] n, double y1, double u0, int through)
        {
            double sign = 1.0, nPrev = Math.Abs(n[0]);
            double y = y1, omega = nPrev * u0, u = u0;
            for (int i = 1; i <= through; i++)
            {
                var surf = s.Surfaces[i];
                if (surf.IsMirror) sign = -sign;
                double nNext = sign * Math.Abs(n[i]);
                omega -= y * (nNext - nPrev) * surf.Curvature;
                u = omega / nNext;
                nPrev = nNext;
                if (i < through)
                {
                    double t = surf.Thickness;
                    if (double.IsInfinity(t) || double.IsNaN(t)) t = 0.0;
                    y += t * u;
                }
            }
            return (y, u);
        }

        /// <summary>The effective focal length, -n_object / (n' u') for a ray entering at height 1.</summary>
        public static double Efl(LhltFile s, double[] n)
        {
            int last = Last(s);
            var (_, u) = Trace(s, n, 1.0, 0.0, last);
            double sign = 1.0;
            for (int i = 1; i <= last; i++) if (s.Surfaces[i].IsMirror) sign = -sign;
            double omega = sign * Math.Abs(n[last]) * u;
            if (Math.Abs(omega) < 1e-300)
                throw new InvalidOperationException("The lens is afocal, so an F-number gives it no entrance pupil.");
            return -Math.Abs(n[0]) / omega;
        }

        /// <summary>
        /// Where the entrance pupil is, measured from surface 1 (positive to the right): where the
        /// object-space ray that crosses the axis at the stop meets the axis.
        /// </summary>
        public static double EntrancePupilPosition(LhltFile s, double[] n)
        {
            int stop = s.Surfaces.FindIndex(x => x.IsStop);
            if (stop < 1 || stop > Last(s)) stop = 1;
            // Heights at the stop of two basis rays: (y1 = 1, u0 = 0) and (y1 = 0, u0 = 1). The ray
            // y1 = -u0 * p crosses the axis at the stop when p = b / a.
            double a = StopHeight(s, n, 1.0, 0.0, stop);
            double b = StopHeight(s, n, 0.0, 1.0, stop);
            return Math.Abs(a) > 1e-15 ? b / a : 0.0;
        }

        // Height at the stop surface itself: traced up to the surface before it, then carried
        // across that surface's thickness.
        private static double StopHeight(LhltFile s, double[] n, double y1, double u0, int stop)
        {
            if (stop == 1) return y1;
            var (y, u) = Trace(s, n, y1, u0, stop - 1);
            double t = s.Surfaces[stop - 1].Thickness;
            if (double.IsInfinity(t) || double.IsNaN(t)) t = 0.0;
            return y + t * u;
        }
    }
}
