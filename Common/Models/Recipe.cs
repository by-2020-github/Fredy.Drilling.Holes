using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Models
{
    public class Recipe
    {
        public required string RecipeName { get; set; }
        public required string TypeName { get; set; }
        public required List<PunchPoint> PunchPoints { get; set; }

        public required double Radius { get; set; }

        public required int Rings { get; set; }

        /// <summary>
        /// 创建一个虚拟的环形分布 Recipe
        /// </summary>
        /// <param name="recipeName">配方名称</param>
        /// <param name="rings">总圈数</param>
        /// <param name="pointsPerRing">每圈的孔数</param>
        /// <param name="spacing">间距(半径增量)</param>
        /// <returns>生成的 Recipe 实例</returns>
        public static Recipe CreateVirtualRingRecipe(string recipeName, int rings, int pointsPerRing, double spacing, double radius = 150)
        {
            var points = new List<PunchPoint>();

            for (int r = 1; r <= rings; r++)
            {
                double ringRadius = r * spacing; // 当前半径
                for (int i = 0; i < pointsPerRing; i++)
                {
                    // 计算角度 (弧度)
                    double angle = (2 * Math.PI / pointsPerRing) * i;

                    points.Add(new PunchPoint
                    {
                        X = Math.Round(ringRadius * Math.Cos(angle), 3),
                        Y = Math.Round(ringRadius * Math.Sin(angle), 3),
                        RingNumber = r,
                        SequenceIndex = i + 1
                    });
                }
            }

            return new Recipe
            {
                RecipeName = recipeName,
                TypeName = "Virtual_Circular",
                PunchPoints = points,
                Radius = radius,
                Rings = rings
            };
        }
    }
}
