using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Models
{
    public class RecipePunchParameters
    {
        public List<PunchPoint> PunchPoints { get; set; } = new();

        public double Radius { get; set; }

        public int Rings { get; set; }

        public static RecipePunchParameters CreateDefault(List<PunchPoint>? punchPoints = null, double radius = 0, int rings = 0)
        {
            return new RecipePunchParameters
            {
                PunchPoints = punchPoints ?? new List<PunchPoint>(),
                Radius = radius,
                Rings = rings
            };
        }
    }

    public class RecipeDepthItem
    {
        public string Label { get; set; } = string.Empty;

        public int Value { get; set; }
    }

    public class RecipeProcessParameters
    {
        public string WorkpieceType { get; set; } = "PS60-6X500-0.05X0.075X10";

        public int FirstPunchDepth { get; set; } = 186;

        public int FirstAlarmDepth { get; set; } = 50;

        public int FirstLiftHeight { get; set; } = 260;

        public int FirstPeckDepth { get; set; } = 400;

        public int FirstPeckSingleDepth { get; set; } = 200;

        public int SecondPunchDepth { get; set; } = 200;

        public int SecondAlarmDepth { get; set; } = 120;

        public int SecondLiftHeight { get; set; } = 260;

        public int MinSafeDepth { get; set; } = 20;

        public bool IsSecondDetectionActive { get; set; }

        public List<RecipeDepthItem> PunchDepths { get; set; } = CreateDefaultPunchDepths();

        public static RecipeProcessParameters CreateDefault(string? workpieceType = null)
        {
            return new RecipeProcessParameters
            {
                WorkpieceType = string.IsNullOrWhiteSpace(workpieceType) ? "PS60-6X500-0.05X0.075X10" : workpieceType
            };
        }

        private static List<RecipeDepthItem> CreateDefaultPunchDepths()
        {
            int[] defaultValues = { 400, 200, 300, 200, 100, 0, 0, 0 };

            return defaultValues
                .Select((value, index) => new RecipeDepthItem
                {
                    Label = $"No.{index + 1}",
                    Value = value
                })
                .ToList();
        }
    }

    public class Recipe
    {
        public required string RecipeName { get; set; }
        public required string TypeName { get; set; }

        public RecipePunchParameters PunchParameters { get; set; } = new();

        public RecipeProcessParameters ProcessParameters { get; set; } = new();

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
                PunchParameters = RecipePunchParameters.CreateDefault(points, radius, rings),
                ProcessParameters = RecipeProcessParameters.CreateDefault(recipeName)
            };
        }
    }
}
