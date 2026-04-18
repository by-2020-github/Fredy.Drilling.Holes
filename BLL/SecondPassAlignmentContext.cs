using System;
using System.Collections.Generic;

namespace BLL
{
    public readonly record struct AffineTransform2D(
        double M11,
        double M12,
        double M21,
        double M22,
        double OffsetX,
        double OffsetY)
    {
        public static AffineTransform2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Transform(double x, double y)
        {
            var tx = (M11 * x) + (M12 * y) + OffsetX;
            var ty = (M21 * x) + (M22 * y) + OffsetY;
            return (tx, ty);
        }
    }

    public interface ISecondPassAlignmentContext
    {
        bool IsReady { get; }
        AffineTransform2D Transform { get; }
        IReadOnlyDictionary<int, (double X, double Y)> MatchedPoints { get; }
        event EventHandler? AlignmentChanged;
        void SetTransform(AffineTransform2D transform, IReadOnlyDictionary<int, (double X, double Y)> matchedPoints);
        void Clear();
    }

    public sealed class SecondPassAlignmentContext : ISecondPassAlignmentContext
    {
        public bool IsReady { get; private set; }

        public AffineTransform2D Transform { get; private set; } = AffineTransform2D.Identity;

        public IReadOnlyDictionary<int, (double X, double Y)> MatchedPoints { get; private set; } = new Dictionary<int, (double X, double Y)>();

        public event EventHandler? AlignmentChanged;

        public void SetTransform(AffineTransform2D transform, IReadOnlyDictionary<int, (double X, double Y)> matchedPoints)
        {
            Transform = transform;
            MatchedPoints = matchedPoints;
            IsReady = true;
            AlignmentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            Transform = AffineTransform2D.Identity;
            MatchedPoints = new Dictionary<int, (double X, double Y)>();
            IsReady = false;
            AlignmentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
