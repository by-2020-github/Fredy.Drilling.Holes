using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fredy.Drilling.Holes.Windows.ParamConfig.old
{
    /// <summary>
    /// 配置管理器，用于加载和保存软件运行参数
    /// </summary>
    public class ConfigManager
    {
        /// <summary>
        /// 用户配置文件路径
        /// </summary>
        private readonly string _configFilePath;
        
        /// <summary>
        /// 配置数据字典
        /// </summary>
        private Dictionary<string, object> _configData = new Dictionary<string, object>();
        
        /// <summary>
        /// 配置管理器实例（单例模式）
        /// </summary>
        private static ConfigManager? _instance;
        
        /// <summary>
        /// 配置管理器实例（单例模式）
        /// </summary>
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        private ConfigManager()
        {
            // 配置文件夹路径
            string configFolder = Path.Combine(AppContext.BaseDirectory, "config");
            
            // 确保配置文件夹存在
            if (!Directory.Exists(configFolder))
            {
                Directory.CreateDirectory(configFolder);
            }
            
            // 设置配置文件路径
            _configFilePath = Path.Combine(configFolder, "config.json");
            
            // 调试日志
            Console.WriteLine($"ConfigManager initialized with config file: {_configFilePath}");
            
            // 加载用户配置
            LoadConfig();
        }
        
        /// <summary>
        /// 加载用户配置文件
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string jsonContent = File.ReadAllText(_configFilePath);
                    var options = new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    var loadedData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent, options);
                    _configData = ConvertJsonElementsToObjects(loadedData ?? new Dictionary<string, JsonElement>());
                }
                else
                {
                    // 用户配置文件不存在，使用空字典
                    _configData = new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载用户配置文件失败: {ex.Message}");
                _configData = new Dictionary<string, object>();
            }
        }
        
        /// <summary>
        /// 将JsonElement字典转换为普通对象字典
        /// </summary>
        /// <param name="jsonElements">JsonElement字典</param>
        /// <returns>普通对象字典</returns>
        private Dictionary<string, object> ConvertJsonElementsToObjects(Dictionary<string, JsonElement> jsonElements)
        {
            var result = new Dictionary<string, object>();
            
            foreach (var kvp in jsonElements)
            {
                result[kvp.Key] = ConvertJsonElementToObject(kvp.Value);
            }
            
            return result;
        }
        
        /// <summary>
        /// 将JsonElement转换为普通对象
        /// </summary>
        /// <param name="element">JsonElement</param>
        /// <returns>转换后的对象</returns>
        private object ConvertJsonElementToObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    // 尝试转换为整数，如果失败则转换为double
                    if (element.TryGetInt32(out int intVal))
                    {
                        return intVal;
                    }
                    else if (element.TryGetInt64(out long longVal))
                    {
                        return longVal;
                    }
                    else if (element.TryGetDouble(out double doubleVal))
                    {
                        return doubleVal;
                    }
                    else
                    {
                        return element.GetRawText();
                    }
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    // 处理嵌套对象
                    var nestedDict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        nestedDict[property.Name] = ConvertJsonElementToObject(property.Value);
                    }
                    return nestedDict;
                default:
                    return element.GetRawText();
            }
        }
        
        /// <summary>
        /// 保存用户配置文件
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonContent = JsonSerializer.Serialize(_configData, options);
                File.WriteAllText(_configFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存用户配置文件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键，支持点号分隔的嵌套路径</param>
        /// <param name="defaultValue">默认值（当配置文件中没有该值时使用）</param>
        /// <returns>配置值</returns>
        public T GetValue<T>(string key, T defaultValue)
            where T : notnull
        {
            try
            {
                // 解析键路径
                string[] keys = key.Split('.');
                Dictionary<string, object> currentDict = _configData;
                object? value = null;
                
                // 遍历键路径，查找对应的值
                for (int i = 0; i < keys.Length; i++)
                {
                    if (currentDict.ContainsKey(keys[i]))
                    {
                        if (i == keys.Length - 1)
                        {
                            // 找到最终值
                            value = currentDict[keys[i]];
                            break;
                        }
                        else
                        {
                            // 继续深入嵌套层级
                            if (currentDict[keys[i]] is Dictionary<string, object> nestedDict)
                            {
                                currentDict = nestedDict;
                            }
                            else
                            {
                                // 中间层级不是字典，说明路径不存在
                                return defaultValue;
                            }
                        }
                    }
                    else
                    {
                        // 键不存在
                        return defaultValue;
                    }
                }
                
                // 处理找到的值
                if (value is not null)
                {
                    // 处理数值类型转换
                    if (typeof(T) == typeof(int))
                    {
                        if (value is int intVal)
                        {
                            return (T)(object)intVal;
                        }
                        else if (value is long longVal)
                        {
                            return (T)(object)(int)longVal;
                        }
                        else if (value is double doubleVal)
                        {
                            return (T)(object)(int)doubleVal;
                        }
                        else if (value is float floatVal)
                        {
                            return (T)(object)(int)floatVal;
                        }
                        return (T)Convert.ChangeType(value, typeof(T))!;
                    }
                    // 处理布尔类型转换
                    else if (typeof(T) == typeof(bool))
                    {
                        if (value is bool boolVal)
                        {
                            return (T)(object)boolVal;
                        }
                        return (T)Convert.ChangeType(value, typeof(T))!;
                    }
                    // 处理字符串类型转换
                    else if (typeof(T) == typeof(string))
                    {
                        return (T)Convert.ChangeType(value, typeof(T))!;
                    }
                    // 其他类型
                    else
                    {
                        return (T)Convert.ChangeType(value, typeof(T))!;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取用户配置值失败: {ex.Message}");
            }
            
            // 如果都没有，使用传入的默认值
            return defaultValue;
        }
        
        /// <summary>
        /// 设置配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键，支持点号分隔的嵌套路径</param>
        /// <param name="value">配置值</param>
        public void SetValue<T>(string key, T value)
            where T : notnull
        {
            try
            {
                // 解析键路径
                string[] keys = key.Split('.');
                Dictionary<string, object> currentDict = _configData;
                
                // 遍历键路径，创建必要的嵌套层级
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (!currentDict.ContainsKey(keys[i]))
                    {
                        // 创建新的嵌套层级
                        var newDict = new Dictionary<string, object>();
                        currentDict[keys[i]] = newDict;
                        currentDict = newDict;
                    }
                    else
                    {
                        // 检查现有值是否为字典
                        if (currentDict[keys[i]] is Dictionary<string, object> nestedDict)
                        {
                            currentDict = nestedDict;
                        }
                        else
                        {
                            // 现有值不是字典，替换为字典
                            var newDict = new Dictionary<string, object>();
                            currentDict[keys[i]] = newDict;
                            currentDict = newDict;
                        }
                    }
                }
                
                // 设置最终值
                string finalKey = keys[keys.Length - 1];
                if (currentDict.ContainsKey(finalKey))
                {
                    currentDict[finalKey] = value;
                }
                else
                {
                    currentDict.Add(finalKey, value);
                }
                
                SaveConfig(); // 自动保存
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置用户配置值失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 删除配置项
        /// </summary>
        /// <param name="key">配置键，支持点号分隔的嵌套路径</param>
        public void RemoveValue(string key)
        {
            try
            {
                // 解析键路径
                string[] keys = key.Split('.');
                Dictionary<string, object> currentDict = _configData;
                
                // 遍历键路径，找到目标值的父级字典
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (currentDict.ContainsKey(keys[i]) && currentDict[keys[i]] is Dictionary<string, object> nestedDict)
                    {
                        currentDict = nestedDict;
                    }
                    else
                    {
                        // 路径不存在
                        return;
                    }
                }
                
                // 删除最终值
                string finalKey = keys[keys.Length - 1];
                if (currentDict.ContainsKey(finalKey))
                {
                    currentDict.Remove(finalKey);
                    SaveConfig(); // 自动保存
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除用户配置值失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 检查配置项是否存在
        /// </summary>
        /// <param name="key">配置键，支持点号分隔的嵌套路径</param>
        /// <returns>是否存在</returns>
        public bool ContainsKey(string key)
        {
            try
            {
                // 解析键路径
                string[] keys = key.Split('.');
                Dictionary<string, object> currentDict = _configData;
                
                // 遍历键路径，查找对应的值
                for (int i = 0; i < keys.Length; i++)
                {
                    if (currentDict.ContainsKey(keys[i]))
                    {
                        if (i == keys.Length - 1)
                        {
                            // 找到最终值
                            return true;
                        }
                        else
                        {
                            // 继续深入嵌套层级
                            if (currentDict[keys[i]] is Dictionary<string, object> nestedDict)
                            {
                                currentDict = nestedDict;
                            }
                            else
                            {
                                // 中间层级不是字典，说明路径不存在
                                return false;
                            }
                        }
                    }
                    else
                    {
                        // 键不存在
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查用户配置值失败: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取所有配置项
        /// </summary>
        /// <returns>配置项字典</returns>
        public Dictionary<string, object> GetAllConfig()
        {
            return new Dictionary<string, object>(_configData);
        }
        
        /// <summary>
        /// 清除所有配置项
        /// </summary>
        public void ClearAllConfig()
        {
            _configData.Clear();
            SaveConfig(); // 自动保存
        }
    }
    
    /// <summary>
    /// 配置键常量类
    /// </summary>
    public static class ConfigKeys
    {
        // 示例配置键
        public const string APP_VERSION = "应用设置.Version";
        public const string WINDOW_WIDTH = "应用设置.Window.Width";
        public const string WINDOW_HEIGHT = "应用设置.Window.Height";
        public const string WINDOW_POSITION_X = "应用设置.Window.PositionX";
        public const string WINDOW_POSITION_Y = "应用设置.Window.PositionY";
        public const string LOG_LEVEL = "应用设置.Log.Level";
        public const string LAST_OPENED_FILE = "应用设置.File.LastOpened";
        
        // 运动控制相关配置键
        public const string MOTION_CONTROL_ENABLED = "运动控制.Enabled";
        public const string MOTION_SPEED = "运动控制.Speed";
        public const string MOTION_ACCELERATION = "运动控制.Acceleration";
        public const string MOTION_DECELERATION = "运动控制.Deceleration";
        
        // 相机相关配置键
        public const string CAMERA_IP = "相机设置.IP";
        public const string CAMERA_PORT = "相机设置.Port";
        public const string CAMERA_EXPOSURE = "相机设置.Exposure";
        public const string CAMERA_GAIN = "相机设置.Gain";
        
        // XY轴参数
        public const string START_V_XY = "运动控制.XY轴.StartV_XY";
        public const string SPEED_XY = "运动控制.XY轴.Speed_XY";
        public const string ACC_XY = "运动控制.XY轴.Acc_XY";
        public const string DELAY_XY = "运动控制.XY轴.Delay_XY";
        public const string PULSE_EQ = "运动控制.XY轴.Pulse_Eq";
        
        // Z轴参数
        public const string START_V_Z = "运动控制.Z轴.StartV_Z";
        public const string SPEED_Z = "运动控制.Z轴.Speed_Z";
        public const string ACC_Z = "运动控制.Z轴.Acc_Z";
        public const string DELAY_Z = "运动控制.Z轴.Delay_Z";
        
        // Z1轴参数
        public const string START_V_Z1 = "运动控制.Z1轴.StartV_Z1";
        public const string SPEED_Z1 = "运动控制.Z1轴.Speed_Z1";
        public const string ACC_Z1 = "运动控制.Z1轴.Acc_Z1";
        
        // Z2轴参数
        public const string START_V_Z2 = "运动控制.Z2轴.StartV_Z2";
        public const string SPEED_Z2 = "运动控制.Z2轴.Speed_Z2";
        public const string ACC_Z2 = "运动控制.Z2轴.Acc_Z2";
        
        // 快速慢速参数
        public const string SPEED_SLOW = "运动控制.探测参数.Speed_slow";
        public const string POS_SLOW = "运动控制.探测参数.Pos_slow";
        public const string POS_QUICK = "运动控制.探测参数.Pos_quick";
        public const string SPEED_QUICK = "运动控制.探测参数.Speed_quick";
        
        // 回零参数
        public const string HOME_START_V = "运动控制.回零参数.HomeStartv";
        public const string HOME_SEARCH_V = "运动控制.回零参数.HomeSearchv";
        public const string HOME_SPEED = "运动控制.回零参数.HomeSpeed";
        public const string HOME_Z_SPEED = "运动控制.回零参数.HomeZSpeed";
        public const string HOME_ACC = "运动控制.回零参数.HomeAcc";
        public const string HOME_Z_LOCATION = "运动控制.回零参数.Zlocation";
        public const string HOME_RULE = "运动控制.回零参数.Home_rule";
        public const string HOME_LATCH = "运动控制.回零参数.Home_latch";
        public const string HOME_IO = "运动控制.回零参数.Home_IO";
        public const string XSTOP0 = "XSTOP0";
        public const string YSTOP0 = "YSTOP0";
        public const string ZSTOP0 = "ZSTOP0";
        public const string XSTOPRULE = "XSTOPrule";
        public const string YSTOPRULE = "YSTOPrule";
        public const string CHECK_XSTOP0 = "check_XSTOP0";
        public const string CHECK_YSTOP0 = "check_YSTOP0";
        public const string CHECK_ZSTOP0 = "check_ZSTOP0";



        // GPIO输入相关参数
        public const string PROBE = "系统设置.GPIO输入.Probe";
        public const string CHECK_PROBE = "系统设置.GPIO输入.checkProbe";
        public const string E_STOP = "系统设置.GPIO输入.E_Stop";
        public const string CHECK_E_STOP = "系统设置.GPIO输入.checkE_Stop";
        public const string START_UP = "系统设置.GPIO输入.StartUp";
        public const string CHECK_START_UP = "系统设置.GPIO输入.checkStartUp";
        
        // 机台坐标相关参数
        public const string MACHINE_COORDINATE_X = "机台坐标.Coordinate.X";
        public const string MACHINE_COORDINATE_Y = "机台坐标.Coordinate.Y";
        public const string MACHINE_COORDINATE_Z = "机台坐标.Coordinate.Z";
        public const string MACHINE_COORDINATE_Z1 = "机台坐标.Coordinate.Z1";
        public const string MACHINE_COORDINATE_Z2 = "机台坐标.Coordinate.Z2";
        
        // 分区扫描相关参数
        public const string SCAN_SQUARE_SIZE = "分区扫描.SquareSize";
        public const string SCAN_EXPAND = "分区扫描.Expand";
        public const string SCAN_DELAY = "分区扫描.Delay";
        
        // 索引参数
        public const string INDEX_CIRCLE = "索引参数.Indexcircle";
        public const string INDEX_HOLE = "索引参数.Indexhole";
        public const string MANUFAC_INDEX = "索引参数.Manufacindex";
        
        // 冲孔状态参数
        public const string PUNCH_STATUS = "工艺参数.PunchStatus";
    }
}