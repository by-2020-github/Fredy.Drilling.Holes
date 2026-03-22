using Common.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Common.Tools
{
    public class CoordinateLoader
    {
        /// <summary>
        /// 加载并解析坐标文件
        /// </summary>
        /// <param name="filePath">Coordinate.txt 的完整路径</param>
        /// <returns>实体对象列表</returns>
        public static List<PunchPoint> LoadFromFile(string filePath)
        {
            var points = new List<PunchPoint>();

            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"找不到坐标文件: {filePath}");
                }

                // 读取所有行
                string[] lines = File.ReadAllLines(filePath);

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 根据文件内容，数据是用逗号分隔的，可能带有空格或制表符
                    // 例如: 68.388657, 30.789472, 1, 1
                    string[] parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(p => p.Trim())
                                         .ToArray();

                    if (parts.Length >= 4)
                    {
                        var point = new PunchPoint
                        {
                            X = double.Parse(parts[0]),
                            Y = double.Parse(parts[1]),
                            RingNumber = int.Parse(parts[2]),
                            SequenceIndex = int.Parse(parts[3])
                        };
                        points.Add(point);
                    }
                }
            }
            catch (Exception ex)
            {
                // 此处可接入您的日志系统 [日常模式/调试模式]
                Console.WriteLine($"加载坐标文件出错: {ex.Message}");
                throw;
            }

            return points;
        }
    }
}
