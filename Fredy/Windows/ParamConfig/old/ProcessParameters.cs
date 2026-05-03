using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Fredy.Drilling.Holes.Windows.ParamConfig.old
{
    /// <summary>
    /// 工艺类型枚举
    /// </summary>
    public enum ProcessType
    {
        /// <summary>
        /// 校针工艺
        /// </summary>
        NeedleCalibration = 0,
        
        /// <summary>
        /// 工件基准工艺
        /// </summary>
        WorkpieceReference = 1,
        
        /// <summary>
        /// 头道冲孔工艺
        /// </summary>
        FirstPunch = 2,
        
        /// <summary>
        /// 二道冲孔工艺
        /// </summary>
        SecondPunch = 3
    }

    /// <summary>
    /// 工艺参数数据类
    /// </summary>
    public class ProcessParameters
    {
        /// <summary>
        /// 当前工艺类型
        /// </summary>
        public ProcessType CurrentProcessType { get; set; } = ProcessType.NeedleCalibration;

        // 工件参数
        public string WorkpieceType { get; set; } = string.Empty;
        
        // 校针工艺参数
        public int NeedleCalibrationCameraX { get; set; }
        public int NeedleCalibrationCameraY { get; set; }
        public int NeedleCalibrationNeedleX { get; set; }
        public int NeedleCalibrationNeedleY { get; set; }
        public int NeedleCalibrationOffsetX { get; set; }
        public int NeedleCalibrationOffsetY { get; set; }
        public bool NeedleCalibrationStatus { get; set; } = false;
        
        // 工件基准工艺参数
        public int WorkpieceReferenceX { get; set; }
        public int WorkpieceReferenceY { get; set; }
        public int WorkpieceReferenceZ { get; set; }
        public int WorkpieceReferenceTolerance { get; set; }
        public bool WorkpieceReferenceAutoCalibrate { get; set; } = true;
        
        // 头道冲孔工艺参数
        public int FirstPunchDepth { get; set; }
        public int FirstPunchBrokenNeedleAlarmDepth { get; set; }
        public int FirstPunchLiftHeight { get; set; }
        public int FirstPunchPeckDepth { get; set; }
        public int FirstPunchPeckSingleDepth { get; set; }
        
        // 二道冲孔工艺参数
        public int SecondPunchDepth { get; set; }
        public int SecondPunchBrokenNeedleAlarmDepth { get; set; }
        public int SecondPunchLiftHeight { get; set; }
        public int SecondPunchMinSafeDepth { get; set; }
        public bool SecondPunchDetection { get; set; }
        
        /// <summary>
        /// 参数文件路径
        /// </summary>
        private static readonly string ConfigFolder = Path.Combine(AppContext.BaseDirectory, "config");
        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "ProcessParameters.json");
        
        /// <summary>
        /// 保存参数到文件
        /// </summary>
        public void SaveToFile()
        {
            // 确保配置文件夹存在
            if (!Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(ConfigFolder);
            }
            
            // 序列化并保存到文件
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigPath, json);
        }
        
        /// <summary>
        /// 从文件加载参数
        /// </summary>
        /// <returns>工艺参数实例</returns>
        public static ProcessParameters LoadFromFile()
        {
            // 确保配置文件夹存在
            if (!Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(ConfigFolder);
            }
            
            // 如果文件存在，加载文件内容
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<ProcessParameters>(json) ?? new ProcessParameters();
                }
                catch
                {
                    // 文件读取失败，返回默认值
                    return new ProcessParameters();
                }
            }
            
            // 文件不存在，返回默认值
            return new ProcessParameters();
        }
    }
}