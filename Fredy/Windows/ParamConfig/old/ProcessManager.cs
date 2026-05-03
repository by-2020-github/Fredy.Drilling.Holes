using System;
using System.IO;
using System.Windows.Forms;

namespace visonCAM.Class_config
{
    /// <summary>
    /// 头道冲孔工艺状态枚举
    /// </summary>
    public enum FirstPunchState
    {
        /// <summary>
        /// 初始状态
        /// </summary>
        Initial,
        /// <summary>
        /// 读取加工坐标
        /// </summary>
        ReadCoordinates,
        /// <summary>
        /// 移动工作台到坐标位置
        /// </summary>
        MoveToPosition,
        /// <summary>
        /// 操作Z轴进行冲孔
        /// </summary>
        PunchOperation,
        /// <summary>
        /// 更新索引并准备下一个孔
        /// </summary>
        UpdateIndex,
        /// <summary>
        /// 工艺完成
        /// </summary>
        Completed,
        /// <summary>
        /// 错误状态
        /// </summary>
        Error,
        /// <summary>
        /// 停止状态
        /// </summary>
        Stop
    }
    
    /// <summary>
    /// 二道冲孔工艺状态枚举
    /// </summary>
    public enum SecondPunchState
    {
        /// <summary>
        /// 初始状态
        /// </summary>
        Initial,
        /// <summary>
        /// 读取加工坐标
        /// </summary>
        ReadCoordinates,
        /// <summary>
        /// 移动工作台到坐标位置
        /// </summary>
        MoveToPosition,
        /// <summary>
        /// 操作Z轴进行冲孔
        /// </summary>
        PunchOperation,
        /// <summary>
        /// 更新索引并准备下一个孔
        /// </summary>
        UpdateIndex,
        /// <summary>
        /// 工艺完成
        /// </summary>
        Completed,
        /// <summary>
        /// 错误状态
        /// </summary>
        Error,
        /// <summary>
        /// 停止状态
        /// </summary>
        Stop
    }

    /// <summary>
    /// 工艺管理器类，用于管理工艺相关的业务逻辑
    /// </summary>
    public class ProcessManager
    {
        /// <summary>
        /// 单例实例
        /// </summary>
        private static ProcessManager _instance;
        
        /// <summary>
        /// 日志记录器
        /// </summary>
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(ProcessManager));
        
        /// <summary>
        /// 单例实例访问属性
        /// </summary>
        public static ProcessManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProcessManager();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 当前头道冲孔工艺状态
        /// </summary>
        public FirstPunchState CurrentFirstPunchState { get; private set; } = FirstPunchState.Initial;
        
        /// <summary>
        /// 当前二道冲孔工艺状态
        /// </summary>
        public SecondPunchState CurrentSecondPunchState { get; private set; } = SecondPunchState.Initial;
        
        /// <summary>
        /// 私有构造函数
        /// </summary>
        private ProcessManager() { }
        
        /// <summary>
        /// 回零方法
        /// </summary>
        /// <param name="axis">轴号（1: X轴, 2: Y轴, 3: Z轴, 0: 所有轴）</param>
        /// <returns>回零是否成功</returns>
        public bool HomeAxes(int axis = 0)
        {
            try
            {
                // 创建运动控制卡实例
                visonCAM.Class_Motion.CtrlCard ctrlCard = new visonCAM.Class_Motion.CtrlCard();
                ctrlCard.m_cardno = visonCAM.Class_Motion.Motion_diver.m_cardno;
                
                // 执行回零操作
                string axisName = axis switch
                {
                    1 => "X轴",
                    2 => "Y轴",
                    3 => "Z轴",
                    _ => "所有轴"
                };
                
                // 记录日志
                log.Info($"开始{axisName}回零操作");
                if (visonCAM.GlobalParams.Instance.DebugMode)
                {
                    log.Debug($"回零轴号: {axis}, 轴名称: {axisName}");
                }
                
                // 处理所有轴回零
                if (axis == 0)
                {
                    // 依次回零X、Y、Z轴
                    bool xHomed = HomeSingleAxis(1, ctrlCard);
                    bool yHomed = HomeSingleAxis(2, ctrlCard);
                    bool zHomed = HomeSingleAxis(3, ctrlCard);
                    
                    // 设置所有轴回零标志位
                    visonCAM.GlobalParams.Instance.FlaghomeX = xHomed;
                    visonCAM.GlobalParams.Instance.FlaghomeY = yHomed;
                    visonCAM.GlobalParams.Instance.FlaghomeZ = zHomed;
                    
                    // 提示回零完成
                    MessageBox.Show($"所有轴回零操作完成", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    log.Info("所有轴回零操作完成");
                    
                    return xHomed && yHomed && zHomed;
                }
                else
                {
                    // 回零单个轴
                    bool homed = HomeSingleAxis(axis, ctrlCard);
                    
                    // 设置回零标志位
                    switch (axis)
                    {
                        case 1: // X轴
                            visonCAM.GlobalParams.Instance.FlaghomeX = homed;
                            break;
                        case 2: // Y轴
                            visonCAM.GlobalParams.Instance.FlaghomeY = homed;
                            break;
                        case 3: // Z轴
                            visonCAM.GlobalParams.Instance.FlaghomeZ = homed;
                            break;
                    }
                    
                    // 提示回零完成
                    MessageBox.Show($"{axisName}回零操作完成", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    log.Info($"{axisName}回零操作完成");
                    
                    return homed;
                }
            }
            catch (Exception ex)
            {
                // 回零失败
                log.Error("回零操作失败: " + ex.Message);
                MessageBox.Show("回零操作失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        
        /// <summary>
        /// 回零单个轴
        /// </summary>
        /// <param name="axis">轴号（1: X轴, 2: Y轴, 3: Z轴）</param>
        /// <param name="ctrlCard">运动控制卡实例</param>
        /// <returns>回零是否成功</returns>
        private bool HomeSingleAxis(int axis, visonCAM.Class_Motion.CtrlCard ctrlCard)
        {
            try
            {
                string axisName = axis switch
                {
                    1 => "X轴",
                    2 => "Y轴",
                    3 => "Z轴",
                    _ => "未知轴"
                };
                
                // Z轴回零逻辑
                if (axis == 3)
                {
                    return HomeZAxis(ctrlCard, axisName);
                }
                // XY轴回零逻辑
                else
                {
                    return HomeXYAxis(axis, ctrlCard, axisName);
                }
            }
            catch (Exception ex)
            {
                string axisName = axis switch
                {
                    1 => "X轴",
                    2 => "Y轴",
                    3 => "Z轴",
                    _ => "未知轴"
                };
                log.Error($"{axisName}回零操作失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Z轴回零
        /// </summary>
        /// <param name="ctrlCard">运动控制卡实例</param>
        /// <param name="axisName">轴名称</param>
        /// <returns>回零是否成功</returns>
        private bool HomeZAxis(visonCAM.Class_Motion.CtrlCard ctrlCard, string axisName)
        {
            try
            {
                log.Info($"开始{axisName}回零过程");
                
                // 读取机械零位开关电平参数
                // 默认不勾选，默认值为1，则Z轴在负方向电平为0，如果勾选，则Z轴在正方向电平为0
                // 使用ZSTOP0作为Z轴机械零位开关的IO端口
                int mechanicalZeroSwitchPort = visonCAM.GlobalParams.Instance.ZSTOP0;
                bool isCheckStop0 = visonCAM.GlobalParams.Instance.check_ZSTOP0;
                // 根据checkbox状态调整电平参数：不勾选时为1，勾选时为0
                int a = isCheckStop0 ? 0 : 1;
                int mechanicalZeroSwitchLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                log.Info($"{axisName}机械零位开关端口: {mechanicalZeroSwitchPort}, 电平: {mechanicalZeroSwitchLevel}, 电平参数a: {a}, checkbox状态: {isCheckStop0}");
                
                // 读取Z轴复位后抬起高度的值
                int homeZLocation = visonCAM.GlobalParams.Instance.HomeZLocation;
                log.Info($"{axisName}复位后抬起高度: {homeZLocation}");
                
                // 机械回零过程
                if (mechanicalZeroSwitchLevel == 1) // 机械零位开关电平为1，Z轴此时在零位之上
                {
                    log.Info($"{axisName}机械零位开关电平为1，开始向负方向移动");
                    
                    // 向负方向移动Z轴，使用连续运动方式
                    ctrlCard.Manu_Continue(3);
                    
                    // 检测机械零位开关电平变为1，阻塞式检测
                    log.Info($"{axisName}正在向负方向移动，等待机械零位开关电平变为1");
                    while (true)
                    {
                        int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                        if (currentLevel == 1)
                        {
                            log.Info($"{axisName}检测到机械零位开关电平变为1，停止运动");
                            break;
                        }
                        // 短暂延迟，避免CPU占用过高
                        System.Threading.Thread.Sleep(10);
                    }
                    
                    // 立即停止Z轴运动
                    ctrlCard.StopRun(3, 0);
                    
                    // 设置Z轴位置为0
                    log.Info($"{axisName}设置位置为0");
                    ctrlCard.Setup_Pos(3, 0, 0);
                }
                else // 机械零位开关电平为0
                {
                    log.Info($"{axisName}机械零位开关电平为0，开始向正方向移动");
                    
                    // 向正方向移动Z轴，使用连续运动方式
                    ctrlCard.Manu_Continue(3);
                    
                    // 检测机械零位开关电平变为!a（即1），阻塞式检测
                    log.Info($"{axisName}正在向正方向移动，等待机械零位开关电平变为1");
                    while (true)
                    {
                        int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                        if (currentLevel == 1)
                        {
                            log.Info($"{axisName}检测到机械零位开关电平变为1，停止运动");
                            break;
                        }
                        System.Threading.Thread.Sleep(10);
                    }
                    
                    // 立即停止Z轴运动
                    ctrlCard.StopRun(3, 0);
                    
                    // 设置Z轴位置为0
                    log.Info($"{axisName}设置位置为0");
                    ctrlCard.Setup_Pos(3, 0, 0);
                }
                
                // 移动向正方向移动抬起高度个脉冲
                if (homeZLocation > 0)
                {
                    log.Info($"{axisName}向正方向移动抬起高度{homeZLocation}个脉冲");
                    ctrlCard.Axis_Pmove(3, homeZLocation);
                    
                    // 等待运动完成
                    System.Threading.Thread.Sleep(1000);
                }
                else
                {
                    log.Info($"{axisName}抬起高度为0，不执行抬起操作");
                }
                
                // 设置Z轴回零标志位为置位
                visonCAM.GlobalParams.Instance.FlaghomeZ = true;
                log.Info($"{axisName}设置回零标志位为置位");
                
                log.Info($"{axisName}回零流程完成");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"{axisName}回零操作失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 检查紧急停止标志
        /// </summary>
        /// <returns>是否需要紧急停止</returns>
        private bool CheckEmergencyStop()
        {
            // 检查GlobalParams中的紧急停止标志
            return visonCAM.GlobalParams.Instance.FlagEmergencyStop;
        }
        
        /// <summary>
        /// 紧急停止处理
        /// </summary>
        /// <param name="ctrlCard">运动控制卡实例</param>
        /// <param name="axisName">轴名称</param>
        private void HandleEmergencyStop(visonCAM.Class_Motion.CtrlCard ctrlCard, string axisName)
        {
            log.Info($"{axisName}执行紧急停止处理");
            
            // 立即停止所有轴运动
            ctrlCard.StopRun(1, 0);
            ctrlCard.StopRun(2, 0);
            ctrlCard.StopRun(3, 0);
            
            // 复位所有回零标志
            visonCAM.GlobalParams.Instance.FlaghomeX = false;
            visonCAM.GlobalParams.Instance.FlaghomeY = false;
            visonCAM.GlobalParams.Instance.FlaghomeZ = false;
            
            // 禁用锁存功能
            try
            {
                ctrlCard.Setup_LockPosition(1, 0, 0, 0);
                ctrlCard.Setup_LockPosition(2, 0, 0, 0);
            }
            catch (Exception ex)
            {
                log.Error($"禁用锁存功能失败: {ex.Message}");
            }
            
            // 记录紧急停止日志
            log.Info($"{axisName}紧急停止完成");
        }
        
        /// <summary>
        /// XY轴回零
        /// </summary>
        /// <param name="axis">轴号（1: X轴, 2: Y轴）</param>
        /// <param name="ctrlCard">运动控制卡实例</param>
        /// <param name="axisName">轴名称</param>
        /// <returns>回零是否成功</returns>
        private bool HomeXYAxis(int axis, visonCAM.Class_Motion.CtrlCard ctrlCard, string axisName)
        {
            const int MAX_RETRY_COUNT = 3;
            int retryCount = 0;
            
            while (retryCount < MAX_RETRY_COUNT)
            {
                try
                {
                    log.Info($"开始{axisName}回零流程，第{retryCount + 1}次尝试");
                    
                    // 检查紧急停止标志
                    if (CheckEmergencyStop())
                    {
                        HandleEmergencyStop(ctrlCard, axisName);
                        return false;
                    }
                    
                    // 记录日志/初始化参数
                    log.Info($"{axisName}初始化回零参数");
                    
                    // 读取机械零位开关电平参数
                    int mechanicalZeroSwitchPort = axis == 1 ? visonCAM.GlobalParams.Instance.XSTOP0 : visonCAM.GlobalParams.Instance.YSTOP0;
                    bool isCheckStop0 = axis == 1 ? visonCAM.GlobalParams.Instance.check_XSTOP0 : visonCAM.GlobalParams.Instance.check_YSTOP0;
                    int mechanicalZeroSwitchLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                    int a = isCheckStop0 ? 0 : 1;
                    log.Info($"{axisName}机械零位开关端口: {mechanicalZeroSwitchPort}, 原始电平: {mechanicalZeroSwitchLevel}, 电平参数a: {a}, checkbox状态: {isCheckStop0}");
                    
                    // 检查紧急停止标志
                    if (CheckEmergencyStop())
                    {
                        HandleEmergencyStop(ctrlCard, axisName);
                        return false;
                    }
                    
                    bool mechanicalHomeSuccess = false;
                    
                    // 机械回零过程
                    if (mechanicalZeroSwitchLevel == a) // 开关电平=设定值（向左运动）
                    {
                        log.Info($"{axisName}开关电平=设定值，开始向左运动分支");
                        
                        // 向负方向连续运动
                        log.Info($"{axisName}向负方向连续运动");
                        ctrlCard.Manu_Continue(axis);
                        
                        // 检测开关信号变为0，带超时检查
                        const int TIMEOUT_MS = 10000;
                        int elapsedTime = 0;
                        bool signalDetected = false;
                        
                        while (elapsedTime < TIMEOUT_MS)
                        {
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                ctrlCard.StopRun(axis, 0);
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                            if (currentLevel == 0)
                            {
                                signalDetected = true;
                                log.Info($"{axisName}检测到开关信号变为0，停止运动");
                                break;
                            }
                            
                            System.Threading.Thread.Sleep(10);
                            elapsedTime += 10;
                        }
                        
                        // 停止电机
                        ctrlCard.StopRun(axis, 0);
                        
                        if (!signalDetected)
                        {
                            log.Error($"{axisName}向左运动超时，未检测到开关信号");
                            retryCount++;
                            continue;
                        }
                        
                        // 检查紧急停止标志
                        if (CheckEmergencyStop())
                        {
                            HandleEmergencyStop(ctrlCard, axisName);
                            return false;
                        }
                        
                        // 向正方向移动200脉冲
                        log.Info($"{axisName}向正方向移动200脉冲");
                        ctrlCard.Axis_Pmove(axis, 200);
                        
                        // 等待运动完成
                        System.Threading.Thread.Sleep(1000);
                        
                        // 检查紧急停止标志
                        if (CheckEmergencyStop())
                        {
                            HandleEmergencyStop(ctrlCard, axisName);
                            return false;
                        }
                        
                        // 慢速向负方向运动
                        log.Info($"{axisName}慢速向负方向运动");
                        ctrlCard.Setup_Speed(axis, 100, 500, 1000);
                        ctrlCard.Manu_Continue(axis);
                        
                        // 检测开关信号变为0，带超时检查
                        elapsedTime = 0;
                        signalDetected = false;
                        
                        while (elapsedTime < TIMEOUT_MS)
                        {
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                ctrlCard.StopRun(axis, 0);
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                            if (currentLevel == 0)
                            {
                                signalDetected = true;
                                log.Info($"{axisName}检测到开关信号变为0，停止运动");
                                break;
                            }
                            
                            System.Threading.Thread.Sleep(10);
                            elapsedTime += 10;
                        }
                        
                        // 停止电机
                        ctrlCard.StopRun(axis, 0);
                        
                        if (!signalDetected)
                        {
                            log.Error($"{axisName}慢速向左运动超时，未检测到开关信号");
                            retryCount++;
                            continue;
                        }
                        
                        // 设置位置=0
                        log.Info($"{axisName}设置位置为0");
                        ctrlCard.Setup_Pos(axis, 0, 0);
                        mechanicalHomeSuccess = true;
                    }
                    else // 开关电平≠设定值（向右运动）
                    {
                        log.Info($"{axisName}开关电平≠设定值，开始向右运动分支");
                        
                        // 向正方向连续运动
                        log.Info($"{axisName}向正方向连续运动");
                        ctrlCard.Manu_Continue(axis);
                        
                        // 检测开关信号变为1，带超时检查
                        const int TIMEOUT_MS = 10000;
                        int elapsedTime = 0;
                        bool signalDetected = false;
                        
                        while (elapsedTime < TIMEOUT_MS)
                        {
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                ctrlCard.StopRun(axis, 0);
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                            if (currentLevel == 1)
                            {
                                signalDetected = true;
                                log.Info($"{axisName}检测到开关信号变为1，停止运动");
                                break;
                            }
                            
                            System.Threading.Thread.Sleep(10);
                            elapsedTime += 10;
                        }
                        
                        // 停止电机
                        ctrlCard.StopRun(axis, 0);
                        
                        if (!signalDetected)
                        {
                            log.Error($"{axisName}向右运动超时，未检测到开关信号");
                            retryCount++;
                            continue;
                        }
                        
                        // 检查紧急停止标志
                        if (CheckEmergencyStop())
                        {
                            HandleEmergencyStop(ctrlCard, axisName);
                            return false;
                        }
                        
                        // 慢速向负方向运动
                        log.Info($"{axisName}慢速向负方向运动");
                        ctrlCard.Setup_Speed(axis, 100, 500, 1000);
                        ctrlCard.Manu_Continue(axis);
                        
                        // 检测开关信号变为0，带超时检查
                        elapsedTime = 0;
                        signalDetected = false;
                        
                        while (elapsedTime < TIMEOUT_MS)
                        {
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                ctrlCard.StopRun(axis, 0);
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            int currentLevel = ctrlCard.Read_Input(mechanicalZeroSwitchPort);
                            if (currentLevel == 0)
                            {
                                signalDetected = true;
                                log.Info($"{axisName}检测到开关信号变为0，停止运动");
                                break;
                            }
                            
                            System.Threading.Thread.Sleep(10);
                            elapsedTime += 10;
                        }
                        
                        // 停止电机
                        ctrlCard.StopRun(axis, 0);
                        
                        if (!signalDetected)
                        {
                            log.Error($"{axisName}慢速向右运动超时，未检测到开关信号");
                            retryCount++;
                            continue;
                        }
                        
                        // 设置位置=0
                        log.Info($"{axisName}设置位置为0");
                        ctrlCard.Setup_Pos(axis, 0, 0);
                        mechanicalHomeSuccess = true;
                    }
                    
                    if (!mechanicalHomeSuccess)
                    {
                        retryCount++;
                        continue;
                    }
                    
                    // 检查是否需要执行光栅尺回零
                    if (visonCAM.GlobalParams.Instance.Home_rule) // 如果勾选了光栅尺回零选项
                    {
                        log.Info($"{axisName}开始光栅尺回零过程");
                        
                        // 检查紧急停止标志
                        if (CheckEmergencyStop())
                        {
                            HandleEmergencyStop(ctrlCard, axisName);
                            return false;
                        }
                        
                        bool ioMode = visonCAM.GlobalParams.Instance.Home_IO;
                        bool latchMode = visonCAM.GlobalParams.Instance.Home_latch;
                        bool encoderHomeSuccess = false;
                        
                        if (ioMode && !latchMode) // IO检测模式
                        {
                            log.Info($"{axisName}使用IO模式进行光栅尺回零");
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 光栅尺零位信号电平常时为1，当触发时电平为0
                            int gratingZeroSwitchPort = axis == 1 ? visonCAM.GlobalParams.Instance.XSTOPrule : visonCAM.GlobalParams.Instance.YSTOPrule;
                            log.Info($"{axisName}正在移动，等待光栅尺零位信号触发，端口: {gratingZeroSwitchPort}");
                            
                            // 使用光栅尺回零参数移动
                            ctrlCard.Setup_Speed(axis, 500, 2000, 2000);
                            ctrlCard.Manu_Continue(axis);
                            
                            // 检测光栅零位信号变为0，带超时检查
                            const int TIMEOUT_MS = 10000;
                            int elapsedTime = 0;
                            bool signalDetected = false;
                            
                            while (elapsedTime < TIMEOUT_MS)
                            {
                                // 检查紧急停止标志
                                if (CheckEmergencyStop())
                                {
                                    ctrlCard.StopRun(axis, 0);
                                    HandleEmergencyStop(ctrlCard, axisName);
                                    return false;
                                }
                                
                                int currentLevel = ctrlCard.Read_Input(gratingZeroSwitchPort);
                                if (currentLevel == 0)
                                {
                                    signalDetected = true;
                                    log.Info($"{axisName}检测到光栅尺零位信号触发，停止运动");
                                    break;
                                }
                                
                                System.Threading.Thread.Sleep(10);
                                elapsedTime += 10;
                            }
                            
                            // 停止电机
                            ctrlCard.StopRun(axis, 0);
                            
                            if (!signalDetected)
                            {
                                log.Error($"{axisName}光栅尺IO模式回零超时，未检测到零位信号");
                                retryCount++;
                                continue;
                            }
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 设置位置为0
                            log.Info($"{axisName}设置位置为0");
                            ctrlCard.Setup_Pos(axis, 0, 0);
                            encoderHomeSuccess = true;
                        }
                        else if (!ioMode && latchMode) // 锁存模式
                        {
                            log.Info($"{axisName}使用锁存模式进行光栅尺回零");
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 配置锁存参数
                            log.Info($"{axisName}设置锁存参数");
                            ctrlCard.Setup_LockPosition(axis, 1, 0, 1);
                            
                            // 启用锁存
                            log.Info($"{axisName}启用锁存");
                            
                            // 启动运动
                            ctrlCard.Setup_Speed(axis, 500, 2000, 2000);
                            ctrlCard.Manu_Continue(axis);
                            
                            // 等待锁存完成，带超时检查
                            const int TIMEOUT_MS = 10000;
                            int elapsedTime = 0;
                            bool latchCompleted = false;
                            int lockStatus = 0;
                            
                            while (elapsedTime < TIMEOUT_MS)
                            {
                                // 检查紧急停止标志
                                if (CheckEmergencyStop())
                                {
                                    ctrlCard.StopRun(axis, 0);
                                    HandleEmergencyStop(ctrlCard, axisName);
                                    return false;
                                }
                                
                                int result = ctrlCard.Get_LockStatus(axis, out lockStatus);
                                if (lockStatus == 1)
                                {
                                    latchCompleted = true;
                                    log.Info($"{axisName}锁存完成，停止运动");
                                    break;
                                }
                                
                                System.Threading.Thread.Sleep(10);
                                elapsedTime += 10;
                            }
                            
                            // 停止电机
                            ctrlCard.StopRun(axis, 0);
                            
                            if (!latchCompleted)
                            {
                                log.Error($"{axisName}光栅尺锁存模式回零超时，锁存未完成");
                                retryCount++;
                                continue;
                            }
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 读取锁存位置
                            int lockPosition = 0;
                            ctrlCard.Get_LockPosition(axis, out lockPosition);
                            log.Info($"{axisName}锁存位置: {lockPosition}");
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 移动到锁存位置
                            log.Info($"{axisName}移动到锁存位置");
                            ctrlCard.Axis_Pmove(axis, lockPosition);
                            
                            // 等待移动完成
                            System.Threading.Thread.Sleep(1000);
                            
                            // 检查紧急停止标志
                            if (CheckEmergencyStop())
                            {
                                HandleEmergencyStop(ctrlCard, axisName);
                                return false;
                            }
                            
                            // 设置位置为0
                            log.Info($"{axisName}设置位置为0");
                            ctrlCard.Setup_Pos(axis, 0, 0);
                            encoderHomeSuccess = true;
                        }
                        
                        if (!encoderHomeSuccess)
                        {
                            retryCount++;
                            continue;
                        }
                    }
                    
                    // 设置回零标志位
                    if (axis == 1)
                    {
                        visonCAM.GlobalParams.Instance.FlaghomeX = true;
                    }
                    else if (axis == 2)
                    {
                        visonCAM.GlobalParams.Instance.FlaghomeY = true;
                    }
                    
                    log.Info($"{axisName}回零流程完成");
                    return true;
                }
                catch (Exception ex)
                {
                    log.Error($"{axisName}回零操作失败: " + ex.Message);
                    retryCount++;
                }
            }
            
            // 达到最大重试次数
            log.Error($"{axisName}回零操作失败，已达到最大重试次数{MAX_RETRY_COUNT}");
            
            // 安全停止所有轴
            try
            {
                ctrlCard.StopRun(1, 0);
                ctrlCard.StopRun(2, 0);
                ctrlCard.StopRun(3, 0);
            }
            catch (Exception ex)
            {
                log.Error($"停止轴运动失败: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// 启动头道冲孔工艺
        /// </summary>
        public void StartFirstPunchProcess()
        {
            try
            {
                // 重置状态
                CurrentFirstPunchState = FirstPunchState.Initial;
                log.Info("启动头道冲孔工艺");
                
                // 在后台线程中启动状态机，避免阻塞UI线程
                Task.Run(() => ProcessFirstPunchStateAsync());
            }
            catch (Exception ex)
            {
                CurrentFirstPunchState = FirstPunchState.Error;
                log.Error("启动头道冲孔工艺失败: " + ex.Message);
                MessageBox.Show("启动头道冲孔工艺失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// 异步处理头道冲孔工艺状态
        /// </summary>
        private async Task ProcessFirstPunchStateAsync()
        {
            
                while (CurrentFirstPunchState != FirstPunchState.Completed && CurrentFirstPunchState != FirstPunchState.Error && CurrentFirstPunchState != FirstPunchState.Stop)
                {
                    switch (CurrentFirstPunchState)
                    {
                        case FirstPunchState.Initial:
                            // 初始状态，准备开始工艺
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("头道冲孔工艺: 初始状态，准备开始工艺");
                                log.Debug("当前状态: Initial，准备进入ReadCoordinates状态");
                                log.Debug("firstStepStart been called!");
                            }
                            CurrentFirstPunchState = FirstPunchState.ReadCoordinates;
                            break;
                            
                        case FirstPunchState.ReadCoordinates:
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 读取当前索引位置的加工坐标
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("头道冲孔工艺: 读取当前索引位置的加工坐标");
                                log.Debug("当前状态: ReadCoordinates，准备读取加工坐标");
                                log.Debug("firstPuchProcess_Step1 been called!");
                            }
                            
                            
                            // 直接使用已加载的坐标数据
                            try
                            {
                                // 检查是否需要停止
                                if (CurrentFirstPunchState == FirstPunchState.Stop)
                                {
                                    log.Info("头道冲孔工艺已停止");
                                    break;
                                }
                                
                                // 检查WorkpieceType是否为空
                                if (string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    log.Error("WorkpieceType为空，无法构建坐标文件路径");
                                    CurrentFirstPunchState = FirstPunchState.Error;
                                    continue;
                                }
                                
                                // 记录当前索引位置
                                int currentManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                                int currentCircle = visonCAM.GlobalParams.Instance.Indexcircle;
                                int currentHole = visonCAM.GlobalParams.Instance.Indexhole;
                                
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug($"使用已加载的坐标数据: 加工索引={currentManufacindex}, 圈={currentCircle}, 孔={currentHole}");
                                    log.Debug($"坐标详情: 圈索引={currentCircle}, 孔索引={currentHole}, 加工索引={currentManufacindex}");
                                    log.Debug("saveCurHoleIndex been called!");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("读取坐标数据失败: " + ex.Message);
                                CurrentFirstPunchState = FirstPunchState.Error;
                                continue;
                            }
                            
                            CurrentFirstPunchState = FirstPunchState.MoveToPosition;
                            break;
                            
                        case FirstPunchState.MoveToPosition:
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 移动工作台到坐标位置
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("头道冲孔工艺: 移动工作台到坐标位置");
                                log.Debug("当前状态: MoveToPosition，准备移动工作台");
                                log.Debug("firstPuchProcess_Step21 been called!");
                                log.Debug("aMoveToHole been called!");
                            }
                            
                            
                           
                            
                            // 模拟移动操作的延迟，避免处理过快
                            await Task.Delay(50);
                            
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("aMoveCallBack been called!");
                            }
                            
                            CurrentFirstPunchState = FirstPunchState.PunchOperation;
                            break;
                            
                        case FirstPunchState.PunchOperation:
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 操作Z轴进行冲孔
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("头道冲孔工艺: 操作Z轴进行冲孔");
                                log.Debug("当前状态: PunchOperation，准备执行冲孔操作");
                                log.Debug("firstPuchProcess_Step3 been called!");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("操作Z轴进行冲孔...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // 模拟冲孔操作
                            // 实际实现中，这里应该调用Z轴控制相关的方法进行冲孔
                            
                            // 模拟Z轴脉冲数
                            int pulseZ = -1600;
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug($"firstPuchProcess_Step3 been called! pulseZ={pulseZ}");
                                log.Debug("firstPuchProcess_Step3CallBack begin!");
                            }
                            
                            // 模拟冲孔操作的延迟，避免处理过快
                            await Task.Delay(50);
                            
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 读取当前索引位置的加工坐标和参数
                            int manufacIndex = visonCAM.GlobalParams.Instance.Manufacindex;
                            int circleIndex = visonCAM.GlobalParams.Instance.Indexcircle;
                            int holeIndex = visonCAM.GlobalParams.Instance.Indexhole;
                            int enterDepth = -8; // 进入深度，实际应该从参数中获取
                            int punchDepth = visonCAM.GlobalParams.Instance.FirstPunchDepth; // 冲孔深度，实际应该从参数中获取
                            int contactDistance = 208892; // 接触距离
                            int startDistance = 205908; // 起始距离
                            int endDistance = 211292; // 结束距离
                            
                            // 按照指定格式输出info级别的日志
                            log.Info($"头道 第{manufacIndex}孔({circleIndex},{holeIndex}),进入深度为：{enterDepth},冲孔深度为：{punchDepth},接触距离为：{contactDistance},起始距离：{startDistance},结束距离：{endDistance}");
                            
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("firstPuchProcess_Step4 been called!");
                                log.Debug($"firstPuchProcess_Step4：pulseZ={Math.Abs(pulseZ)}当前z轴位置：{-endDistance}");
                            }
                            
                            // 模拟Z轴抬起
                            await Task.Delay(30);
                            
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("firstPuchProcess_Step3CallBack end!");
                                log.Debug("firstPuchProcess_Step4CallBack been called!");
                            }
                            
                            CurrentFirstPunchState = FirstPunchState.UpdateIndex;
                            break;
                            
                        case FirstPunchState.UpdateIndex:
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 更新索引并准备下一个孔
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("头道冲孔工艺: 更新索引并准备下一个孔");
                                log.Debug("当前状态: UpdateIndex，准备更新索引");
                            }
                            
                            // 更新加工索引
                            int oldManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                            visonCAM.GlobalParams.Instance.Manufacindex++;
                            int newManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug($"加工索引已更新: {oldManufacindex} → {newManufacindex}");
                                log.Debug($"旧加工索引: {oldManufacindex}, 新加工索引: {newManufacindex}");
                            }
                            
                            // 根据新的加工索引更新圈索引和孔索引
                            try
                            {
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug("开始更新圈索引和孔索引");
                                }
                                // 检查WorkpieceType是否为空
                                if (!string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"WorkpieceType: {visonCAM.GlobalParams.Instance.WorkpieceType}");
                                    }
                                    // 获取坐标文件路径
                                    string coordinateFolder = Path.Combine(AppContext.BaseDirectory, "coordinate");
                                    string typeFolder = Path.Combine(coordinateFolder, visonCAM.GlobalParams.Instance.WorkpieceType);
                                    string coordinateFile = Path.Combine(typeFolder, "Coordinate.txt");
                                    
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"坐标文件路径: {coordinateFile}");
                                    }
                                    
                                    // 检查文件夹和文件是否存在
                                    if (Directory.Exists(coordinateFolder) && Directory.Exists(typeFolder) && File.Exists(coordinateFile))
                                    {
                                        if (visonCAM.GlobalParams.Instance.DebugMode)
                                        {
                                            log.Debug("坐标文件存在");
                                        }
                                        // 读取文件内容
                                        string[] lines = File.ReadAllLines(coordinateFile);
                                        if (visonCAM.GlobalParams.Instance.DebugMode)
                                        {
                                            log.Debug($"坐标文件行数: {lines.Length}");
                                        }
                                        
                                        // 检查新的加工索引是否有效
                                        if (newManufacindex > 0 && newManufacindex <= lines.Length)
                                        {
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"加工索引有效: {newManufacindex}");
                                            }
                                            // 使用新的加工索引读取坐标（注意：数组索引从0开始，所以需要减1）
                                            string line = lines[newManufacindex - 1];
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"读取坐标文件第{newManufacindex}行: {line}");
                                            }
                                            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"分割后的数据部分数量: {parts.Length}");
                                            }
                                            
                                            if (parts.Length >= 4)
                                            {
                                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                                {
                                                    log.Debug("数据部分数量足够");
                                                }
                                                // 清理并解析坐标和索引
                                                string circleStr = parts[2].Trim().TrimEnd(',');
                                                string holeStr = parts[3].Trim().TrimEnd(',');
                                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                                {
                                                    log.Debug($"圈索引字符串: {circleStr}, 孔索引字符串: {holeStr}");
                                                }
                                                
                                                if (int.TryParse(circleStr, out int circle) && int.TryParse(holeStr, out int hole))
                                                {
                                                    // 同步到Indexcircle和Indexhole参数
                                                    visonCAM.GlobalParams.Instance.Indexcircle = circle;
                                                    visonCAM.GlobalParams.Instance.Indexhole = hole;
                                                    
                                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                                    {
                                                        log.Debug($"圈索引和孔索引已更新: 圈={circle}, 孔={hole}");
                                                        log.Debug($"新圈索引: {circle}, 新孔索引: {hole}, 加工索引: {newManufacindex}");
                                                    }
                                                }
                                                else
                                                {
                                                    log.Error($"解析圈索引或孔索引失败: 圈索引={circleStr}, 孔索引={holeStr}");
                                                }
                                            }
                                            else
                                            {
                                                log.Error($"坐标文件格式错误，数据部分数量不足: {parts.Length}");
                                            }
                                        }
                                        else
                                        {
                                            log.Error($"加工索引无效: {newManufacindex}，超出文件行数范围: 1-{lines.Length}");
                                        }
                                    }
                                    else
                                    {
                                        log.Error($"坐标文件或文件夹不存在: coordinateFolder={Directory.Exists(coordinateFolder)}, typeFolder={Directory.Exists(typeFolder)}, coordinateFile={File.Exists(coordinateFile)}");
                                    }
                                }
                                else
                                {
                                    log.Error("WorkpieceType为空，无法更新圈索引和孔索引");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("更新圈索引和孔索引失败: " + ex.Message);
                            }
                            
                            // 保存索引参数到配置文件
                            try
                            {
                                visonCAM.GlobalParams.Instance.SaveUserConfig();
                                // 同时更新ConfigManager中的值，确保Main页面加载时能正确读取
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Manufacindex", newManufacindex);
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Indexcircle", visonCAM.GlobalParams.Instance.Indexcircle);
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Indexhole", visonCAM.GlobalParams.Instance.Indexhole);
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug($"索引参数已保存到配置文件: Indexcircle={visonCAM.GlobalParams.Instance.Indexcircle}, Indexhole={visonCAM.GlobalParams.Instance.Indexhole}, Manufacindex={newManufacindex}");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("保存索引参数失败: " + ex.Message);
                            }
                            
                            // 检查是否所有孔都已加工完成
                            bool hasMoreHoles = true;
                            try
                            {
                                // 检查是否需要停止
                                if (CurrentFirstPunchState == FirstPunchState.Stop)
                                {
                                    log.Info("头道冲孔工艺已停止");
                                    break;
                                }
                                
                                // 检查WorkpieceType是否为空
                                if (string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    log.Error("WorkpieceType为空，无法构建坐标文件路径");
                                    hasMoreHoles = false;
                                }
                                else
                                {
                                    // 直接使用已加载的坐标数据判断是否还有更多孔
                                    // 这里简化处理，假设只要加工索引递增，就认为还有更多孔
                                    // 实际应用中可能需要更复杂的逻辑
                                    hasMoreHoles = true;
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"继续加工下一个孔: 新加工索引={newManufacindex}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("检查坐标数据失败: " + ex.Message);
                                hasMoreHoles = false;
                            }
                            
                            // 检查是否需要停止
                            if (CurrentFirstPunchState == FirstPunchState.Stop)
                            {
                                log.Info("头道冲孔工艺已停止");
                                break;
                            }
                            
                            // 根据检查结果设置下一个状态
                            if (hasMoreHoles)
                            {
                                // 还有更多孔，回到读取坐标状态继续加工
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug("还有更多孔需要加工，继续循环");
                                }
                                CurrentFirstPunchState = FirstPunchState.ReadCoordinates;
                            }
                            else
                            {
                                // 所有孔都已加工完成，设置为完成状态
                                log.Info("所有孔都已加工完成，工艺结束");
                                CurrentFirstPunchState = FirstPunchState.Completed;
                            }
                            break;
                    }
                    
                    // 每次状态转换后短暂延迟，避免CPU占用过高
                    await Task.Delay(100);
                }
                
                // 工艺完成或出错时的处理
                if (CurrentFirstPunchState == FirstPunchState.Completed)
                {
                    log.Info("头道冲孔工艺完成");
                    if (visonCAM.GlobalParams.Instance.DebugMode)
                    {
                        log.Debug("当前状态: Completed，工艺已完成");
                    }
                }
                else if (CurrentFirstPunchState == FirstPunchState.Error)
                {
                    log.Error("头道冲孔工艺出现错误");
                }
                else if (CurrentFirstPunchState == FirstPunchState.Stop)
                {
                    log.Info("头道冲孔工艺已停止");
                }
          
          
        }
        
        /// <summary>
        /// 停止头道冲孔工艺
        /// </summary>
        public void StopFirstPunchProcess()
        {
            try
            {
                CurrentFirstPunchState = FirstPunchState.Stop;
                log.Info("头道冲孔工艺已停止，状态设置为Stop");
                // 移除MessageBox，改为log输出
                // MessageBox.Show("头道冲孔工艺已停止", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                log.Error("停止头道冲孔工艺失败: " + ex.Message);
                // 移除MessageBox，改为log输出
                // MessageBox.Show("停止头道冲孔工艺失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// 获取当前头道冲孔工艺状态
        /// </summary>
        /// <returns>当前状态的字符串描述</returns>
        public string GetFirstPunchStateDescription()
        {
            return CurrentFirstPunchState switch
            {
                FirstPunchState.Initial => "初始状态",
                FirstPunchState.ReadCoordinates => "读取坐标",
                FirstPunchState.MoveToPosition => "移动到位置",
                FirstPunchState.PunchOperation => "冲孔操作",
                FirstPunchState.UpdateIndex => "更新索引",
                FirstPunchState.Completed => "工艺完成",
                FirstPunchState.Error => "错误状态",
                FirstPunchState.Stop => "停止状态",
                _ => "未知状态"
            };
        }
        
        /// <summary>
        /// 启动二道冲孔工艺
        /// </summary>
        public void StartSecondPunchProcess()
        {
            try
            {
                // 重置状态
                CurrentSecondPunchState = SecondPunchState.Initial;
                log.Info("启动二道冲孔工艺，重置状态为Initial");
                
                // 在后台线程中启动状态机，避免阻塞UI线程
                Task.Run(() => ProcessSecondPunchStateAsync());
            }
            catch (Exception ex)
            {
                CurrentSecondPunchState = SecondPunchState.Error;
                log.Error("启动二道冲孔工艺失败: " + ex.Message);
                MessageBox.Show("启动二道冲孔工艺失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// 异步处理二道冲孔工艺状态
        /// </summary>
        private async Task ProcessSecondPunchStateAsync()
        {
            try
            {
                while (CurrentSecondPunchState != SecondPunchState.Completed && CurrentSecondPunchState != SecondPunchState.Error && CurrentSecondPunchState != SecondPunchState.Stop)
                {
                    switch (CurrentSecondPunchState)
                    {
                        case SecondPunchState.Initial:
                            // 初始状态，准备开始工艺
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("二道冲孔工艺: 初始状态，准备开始工艺");
                                log.Debug("当前状态: Initial，准备进入ReadCoordinates状态");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("开始二道冲孔工艺...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            CurrentSecondPunchState = SecondPunchState.ReadCoordinates;
                            break;
                            
                        case SecondPunchState.ReadCoordinates:
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 读取当前索引位置的加工坐标
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("二道冲孔工艺: 读取当前索引位置的加工坐标");
                                log.Debug("当前状态: ReadCoordinates，准备读取加工坐标");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("读取当前索引位置的加工坐标...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // 直接使用已加载的坐标数据
                            try
                            {
                                // 检查是否需要停止
                                if (CurrentSecondPunchState == SecondPunchState.Stop)
                                {
                                    log.Info("二道冲孔工艺已停止");
                                    break;
                                }
                                
                                // 检查WorkpieceType是否为空
                                if (string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    log.Error("WorkpieceType为空，无法构建坐标文件路径");
                                    CurrentSecondPunchState = SecondPunchState.Error;
                                    continue;
                                }
                                
                                // 记录当前索引位置
                                int currentManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                                int currentCircle = visonCAM.GlobalParams.Instance.Indexcircle;
                                int currentHole = visonCAM.GlobalParams.Instance.Indexhole;
                                
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug($"使用已加载的坐标数据: 加工索引={currentManufacindex}, 圈={currentCircle}, 孔={currentHole}");
                                    log.Debug($"坐标详情: 圈索引={currentCircle}, 孔索引={currentHole}, 加工索引={currentManufacindex}");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("读取坐标数据失败: " + ex.Message);
                                CurrentSecondPunchState = SecondPunchState.Error;
                                continue;
                            }
                            
                            CurrentSecondPunchState = SecondPunchState.MoveToPosition;
                            break;
                            
                        case SecondPunchState.MoveToPosition:
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 移动工作台到坐标位置
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("二道冲孔工艺: 移动工作台到坐标位置");
                                log.Debug("当前状态: MoveToPosition，准备移动工作台");
                                log.Debug("模拟移动工作台到指定坐标位置");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("移动工作台到坐标位置...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // 模拟移动操作
                            // 实际实现中，这里应该调用运动控制相关的方法移动到指定坐标
                            
                            // 模拟移动操作的延迟，避免处理过快
                            await Task.Delay(500);
                            
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            CurrentSecondPunchState = SecondPunchState.PunchOperation;
                            break;
                            
                        case SecondPunchState.PunchOperation:
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 操作Z轴进行冲孔
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("二道冲孔工艺: 操作Z轴进行冲孔");
                                log.Debug("当前状态: PunchOperation，准备执行冲孔操作");
                                log.Debug("模拟Z轴冲孔操作");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("操作Z轴进行冲孔...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // 模拟冲孔操作
                            // 实际实现中，这里应该调用Z轴控制相关的方法进行冲孔
                            
                            // 模拟冲孔操作的延迟，避免处理过快
                            await Task.Delay(1000);
                            
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 读取当前索引位置的加工坐标和参数
                            int manufacIndex = visonCAM.GlobalParams.Instance.Manufacindex;
                            int circleIndex = visonCAM.GlobalParams.Instance.Indexcircle;
                            int holeIndex = visonCAM.GlobalParams.Instance.Indexhole;
                            int enterDepth = -8; // 进入深度，实际应该从参数中获取
                            int punchDepth = 1000; // 冲孔深度，实际应该从参数中获取
                            
                            // 按照指定格式输出info级别的日志
                            log.Info($"二道 第{manufacIndex}孔({circleIndex},{holeIndex}),进入深度为：{enterDepth},冲孔深度为：{punchDepth}");
                            
                            CurrentSecondPunchState = SecondPunchState.UpdateIndex;
                            break;
                            
                        case SecondPunchState.UpdateIndex:
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 更新索引并准备下一个孔
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug("二道冲孔工艺: 更新索引并准备下一个孔");
                                log.Debug("当前状态: UpdateIndex，准备更新索引");
                            }
                            // 移除MessageBox，改为log输出
                            // MessageBox.Show("更新索引并准备下一个孔...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // 更新加工索引
                            int oldManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                            visonCAM.GlobalParams.Instance.Manufacindex++;
                            int newManufacindex = visonCAM.GlobalParams.Instance.Manufacindex;
                            if (visonCAM.GlobalParams.Instance.DebugMode)
                            {
                                log.Debug($"加工索引已更新: {oldManufacindex} → {newManufacindex}");
                                log.Debug($"旧加工索引: {oldManufacindex}, 新加工索引: {newManufacindex}");
                            }
                            
                            // 根据新的加工索引更新圈索引和孔索引
                            try
                            {
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug("开始更新圈索引和孔索引");
                                }
                                // 检查WorkpieceType是否为空
                                if (!string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"WorkpieceType: {visonCAM.GlobalParams.Instance.WorkpieceType}");
                                    }
                                    // 获取坐标文件路径
                                    string coordinateFolder = Path.Combine(AppContext.BaseDirectory, "coordinate");
                                    string typeFolder = Path.Combine(coordinateFolder, visonCAM.GlobalParams.Instance.WorkpieceType);
                                    string coordinateFile = Path.Combine(typeFolder, "Coordinate.txt");
                                    
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"坐标文件路径: {coordinateFile}");
                                    }
                                    
                                    // 检查文件夹和文件是否存在
                                    if (Directory.Exists(coordinateFolder) && Directory.Exists(typeFolder) && File.Exists(coordinateFile))
                                    {
                                        if (visonCAM.GlobalParams.Instance.DebugMode)
                                        {
                                            log.Debug("坐标文件存在");
                                        }
                                        // 读取文件内容
                                        string[] lines = File.ReadAllLines(coordinateFile);
                                        if (visonCAM.GlobalParams.Instance.DebugMode)
                                        {
                                            log.Debug($"坐标文件行数: {lines.Length}");
                                        }
                                        
                                        // 检查新的加工索引是否有效
                                        if (newManufacindex > 0 && newManufacindex <= lines.Length)
                                        {
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"加工索引有效: {newManufacindex}");
                                            }
                                            // 使用新的加工索引读取坐标（注意：数组索引从0开始，所以需要减1）
                                            string line = lines[newManufacindex - 1];
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"读取坐标文件第{newManufacindex}行: {line}");
                                            }
                                            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                            if (visonCAM.GlobalParams.Instance.DebugMode)
                                            {
                                                log.Debug($"分割后的数据部分数量: {parts.Length}");
                                            }
                                            
                                            if (parts.Length >= 4)
                                            {
                                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                                {
                                                    log.Debug("数据部分数量足够");
                                                }
                                                // 清理并解析坐标和索引
                                                string circleStr = parts[2].Trim().TrimEnd(',');
                                                string holeStr = parts[3].Trim().TrimEnd(',');
                                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                                {
                                                    log.Debug($"圈索引字符串: {circleStr}, 孔索引字符串: {holeStr}");
                                                }
                                                
                                                if (int.TryParse(circleStr, out int circle) && int.TryParse(holeStr, out int hole))
                                                {
                                                    // 同步到Indexcircle和Indexhole参数
                                                    visonCAM.GlobalParams.Instance.Indexcircle = circle;
                                                    visonCAM.GlobalParams.Instance.Indexhole = hole;
                                                    
                                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                                    {
                                                        log.Debug($"圈索引和孔索引已更新: 圈={circle}, 孔={hole}");
                                                        log.Debug($"新圈索引: {circle}, 新孔索引: {hole}, 加工索引: {newManufacindex}");
                                                    }
                                                }
                                                else
                                                {
                                                    log.Error($"解析圈索引或孔索引失败: 圈索引={circleStr}, 孔索引={holeStr}");
                                                }
                                            }
                                            else
                                            {
                                                log.Error($"坐标文件格式错误，数据部分数量不足: {parts.Length}");
                                            }
                                        }
                                        else
                                        {
                                            log.Error($"加工索引无效: {newManufacindex}，超出文件行数范围: 1-{lines.Length}");
                                        }
                                    }
                                    else
                                    {
                                        log.Error($"坐标文件或文件夹不存在: coordinateFolder={Directory.Exists(coordinateFolder)}, typeFolder={Directory.Exists(typeFolder)}, coordinateFile={File.Exists(coordinateFile)}");
                                    }
                                }
                                else
                                {
                                    log.Error("WorkpieceType为空，无法更新圈索引和孔索引");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("更新圈索引和孔索引失败: " + ex.Message);
                            }
                            
                            // 保存索引参数到配置文件
                            try
                            {
                                visonCAM.GlobalParams.Instance.SaveUserConfig();
                                // 同时更新ConfigManager中的值，确保Main页面加载时能正确读取
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Manufacindex", newManufacindex);
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Indexcircle", visonCAM.GlobalParams.Instance.Indexcircle);
                                visonCAM.ConfigManager.Instance.SetValue("索引参数.Indexhole", visonCAM.GlobalParams.Instance.Indexhole);
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug($"索引参数已保存到配置文件: Indexcircle={visonCAM.GlobalParams.Instance.Indexcircle}, Indexhole={visonCAM.GlobalParams.Instance.Indexhole}, Manufacindex={newManufacindex}");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("保存索引参数失败: " + ex.Message);
                            }
                            
                            // 检查是否所有孔都已加工完成
                            bool hasMoreHoles = true;
                            try
                            {
                                // 检查是否需要停止
                                if (CurrentSecondPunchState == SecondPunchState.Stop)
                                {
                                    log.Info("二道冲孔工艺已停止");
                                    break;
                                }
                                
                                // 检查WorkpieceType是否为空
                                if (string.IsNullOrEmpty(visonCAM.GlobalParams.Instance.WorkpieceType))
                                {
                                    log.Error("WorkpieceType为空，无法构建坐标文件路径");
                                    hasMoreHoles = false;
                                }
                                else
                                {
                                    // 直接使用已加载的坐标数据判断是否还有更多孔
                                    // 这里简化处理，假设只要加工索引递增，就认为还有更多孔
                                    // 实际应用中可能需要更复杂的逻辑
                                    hasMoreHoles = true;
                                    if (visonCAM.GlobalParams.Instance.DebugMode)
                                    {
                                        log.Debug($"继续加工下一个孔: 新加工索引={newManufacindex}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("检查坐标数据失败: " + ex.Message);
                                hasMoreHoles = false;
                            }
                            
                            // 检查是否需要停止
                            if (CurrentSecondPunchState == SecondPunchState.Stop)
                            {
                                log.Info("二道冲孔工艺已停止");
                                break;
                            }
                            
                            // 根据检查结果设置下一个状态
                            if (hasMoreHoles)
                            {
                                // 还有更多孔，回到读取坐标状态继续加工
                                if (visonCAM.GlobalParams.Instance.DebugMode)
                                {
                                    log.Debug("还有更多孔需要加工，继续循环");
                                }
                                CurrentSecondPunchState = SecondPunchState.ReadCoordinates;
                            }
                            else
                            {
                                // 所有孔都已加工完成，设置为完成状态
                                log.Info("所有孔都已加工完成，工艺结束");
                                CurrentSecondPunchState = SecondPunchState.Completed;
                            }
                            break;
                    }
                    
                    // 每次状态转换后短暂延迟，避免CPU占用过高
                    await Task.Delay(100);
                }
                
                // 工艺完成或出错时的处理
                if (CurrentSecondPunchState == SecondPunchState.Completed)
                {
                    log.Info("二道冲孔工艺完成");
                    if (visonCAM.GlobalParams.Instance.DebugMode)
                    {
                        log.Debug("当前状态: Completed，工艺已完成");
                    }
                }
                else if (CurrentSecondPunchState == SecondPunchState.Error)
                {
                    log.Error("二道冲孔工艺出现错误");
                }
                else if (CurrentSecondPunchState == SecondPunchState.Stop)
                {
                    log.Info("二道冲孔工艺已停止");
                }
            }
            catch (Exception ex)
            {
                CurrentSecondPunchState = SecondPunchState.Error;
                log.Error("二道冲孔工艺处理失败: " + ex.Message);
            }
        }
        
        /// <summary>
        /// 停止二道冲孔工艺
        /// </summary>
        public void StopSecondPunchProcess()
        {
            try
            {
                CurrentSecondPunchState = SecondPunchState.Stop;
                log.Info("二道冲孔工艺已停止，状态设置为Stop");
                // 移除MessageBox，改为log输出
                // MessageBox.Show("二道冲孔工艺已停止", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                log.Error("停止二道冲孔工艺失败: " + ex.Message);
                // 移除MessageBox，改为log输出
                // MessageBox.Show("停止二道冲孔工艺失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// 获取当前二道冲孔工艺状态
        /// </summary>
        /// <returns>当前状态的字符串描述</returns>
        public string GetSecondPunchStateDescription()
        {
            return CurrentSecondPunchState switch
            {
                SecondPunchState.Initial => "初始状态",
                SecondPunchState.ReadCoordinates => "读取坐标",
                SecondPunchState.MoveToPosition => "移动到位置",
                SecondPunchState.PunchOperation => "冲孔操作",
                SecondPunchState.UpdateIndex => "更新索引",
                SecondPunchState.Completed => "工艺完成",
                SecondPunchState.Error => "错误状态",
                SecondPunchState.Stop => "停止状态",
                _ => "未知状态"
            };
        }
    }
}