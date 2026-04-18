using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.Tools
{
    public static class CircleFitter
    {
        /// <summary>
        /// 使用最小二乘法拟合圆心坐标和半径
        /// 至少需要 3 个点
        /// </summary>
        /// <param name="points">包含 X, Y 坐标的点集</param>
        /// <param name="centerX">拟合出的圆心 X 坐标</param>
        /// <param name="centerY">拟合出的圆心 Y 坐标</param>
        /// <param name="radius">拟合出的圆半径</param>
        /// <returns>是否拟合成功</returns>
        public static bool FitCircle(IEnumerable<(double X, double Y)> points, out double centerX, out double centerY, out double radius)
        {
            centerX = 0;
            centerY = 0;
            radius = 0;

            var pointList = points.ToList();
            if (pointList.Count < 3)
            {
                return false;
            }

            double sumX = 0, sumY = 0;
            double sumXX = 0, sumYY = 0, sumXY = 0;
            double sumXXX = 0, sumYYY = 0, sumXYY = 0, sumXXY = 0;

            int n = pointList.Count;
            foreach (var p in pointList)
            {
                double x = p.X;
                double y = p.Y;
                double xx = x * x;
                double yy = y * y;

                sumX += x;
                sumY += y;
                sumXX += xx;
                sumYY += yy;
                sumXY += x * y;
                sumXXX += x * xx;
                sumYYY += y * yy;
                sumXYY += x * yy;
                sumXXY += xx * y;
            }

            double c = n * sumXX - sumX * sumX;
            double d = n * sumXY - sumX * sumY;
            double e = n * sumXXX + n * sumXYY - (sumXX + sumYY) * sumX;
            double g = n * sumYY - sumY * sumY;
            double h = n * sumXXY + n * sumYYY - (sumXX + sumYY) * sumY;

            double denominator = (c * g - d * d);
            if (Math.Abs(denominator) < 1e-10)
            {
                return false; // 点共线，无法拟合圆
            }

            double a = (e * g - d * h) / denominator;
            double b = (c * h - d * e) / denominator;

            centerX = a / 2;
            centerY = b / 2;
            radius = Math.Sqrt(centerX * centerX + centerY * centerY + (sumXX + sumYY - a * sumX - b * sumY) / n);

            return true;
        }
    }
}