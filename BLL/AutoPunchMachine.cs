using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLL
{
    /// <summary>
    /// 冲孔工序类型
    /// </summary>
    public enum PunchProcessType
    {
        FirstPass,  // 头道
        SecondPass  // 二道
    }

    /// <summary>
    /// 冲孔状态位 (1-8)
    /// </summary>
    public enum PunchState
    {
        ReadCoordinate = 1,     // 读取索引值孔坐标
        CheckSimulation = 2,    // 判断是否需要模拟冲孔
        DetectSurface = 3,      // 探测工件表面
        LiftToHeight1 = 4,      // Z轴起到抬起高度
        PunchAction = 5,        // Z轴冲孔动作
        LiftToHeight2 = 6,      // Z轴抬起到抬起高度
        CheckFinish = 7,        // 判断是否结束冲孔
        Finished = 8,           // 结束冲孔状态
        Paused = 9              // 暂停状态
    }

    /// <summary>
    /// 流程结束状态
    /// </summary>
    public enum PunchCompletionStatus
    {
        None = 0,
        NormalFinished = 1,
        AbnormalFinished = 2,
        Cancelled = 3
    }

    #region 事件参数定义

    /// <summary>
    /// 状态改变事件参数
    /// </summary>
    public class StateChangedEventArgs : EventArgs
    {
        public PunchState OldState { get; }
        public PunchState NewState { get; }
        public int CurrentHoleIndex { get; }

        public StateChangedEventArgs(PunchState oldState, PunchState newState, int holeIndex)
        {
            OldState = oldState;
            NewState = newState;
            CurrentHoleIndex = holeIndex;
        }
    }

    /// <summary>
    /// 消息与报警事件参数（用于UI弹窗或日志更新）
    /// </summary>
    public class MessageEventArgs : EventArgs
    {
        public string Message { get; }
        public bool IsAlarm { get; }
        public bool RequiresUserAction { get; }

        public MessageEventArgs(string message, bool isAlarm = false, bool requiresUserAction = false)
        {
            Message = message;
            IsAlarm = isAlarm;
            RequiresUserAction = requiresUserAction;
        }
    }

    #endregion

    /// <summary>
    /// 冲孔核心状态机控制类
    /// </summary>
    public class PunchStateMachine
    {
        // 声明暴露给UI的事件
        public event EventHandler<StateChangedEventArgs> StateChanged;
        public event EventHandler<MessageEventArgs> MessageReported;

        private PunchState _currentState = PunchState.Finished;
        private PunchState _stateBeforePause = PunchState.Finished;
        private IHardwareController Hardware;

        public PunchStateMachine(IHardwareController hardware)
        {
            Hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        }

        /// <summary>
        /// 当前冲孔状态（赋值时自动触发事件）
        /// </summary>
        public PunchState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    PunchState oldState = _currentState;
                    _currentState = value;
                    // 触发状态改变事件
                    StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, _currentState, CurrentHoleIndex));
                }
            }
        }

        public PunchProcessType ProcessType { get; set; }
        public int CurrentHoleIndex { get; private set; } = 1;
        public PunchCompletionStatus CompletionStatus { get; private set; } = PunchCompletionStatus.None;

        // UI或配置项映射标志位
        public bool IsSimulationChecked { get; set; } = false;
        public bool IsDetectionEnabled { get; set; } = true;
        public bool IsLastHole { get; set; } = false; // 需外部或坐标系统判断赋值

        // 阈值配置
        public double DetectThreshold { get; set; } = 0.5;   // 探测偏差阈值
        public double BreakPinThreshold { get; set; } = 2.0; // 断针报警阈值
        public double DevThreshold { get; set; } = 1.0;      // 二道偏差报警阈值

        /// <summary>
        /// 启动冲孔流程
        /// </summary>
        public void StartProcess(PunchProcessType type)
        {
            ProcessType = type;
            CurrentHoleIndex = 1;
            _stateBeforePause = PunchState.Finished;
            CompletionStatus = PunchCompletionStatus.None;
            Log($"[系统] 开始{(type == PunchProcessType.FirstPass ? "头道" : "二道")}冲孔流程...");
            CurrentState = PunchState.ReadCoordinate;
        }

        public void PauseProcess()
        {
            if (CurrentState == PunchState.Finished || CurrentState == PunchState.Paused)
            {
                return;
            }

            _stateBeforePause = CurrentState;
            CurrentState = PunchState.Paused;
            Log($"[系统] 冲孔流程已暂停，当前孔位: {CurrentHoleIndex}");
        }

        public void ResumeProcess()
        {
            if (CurrentState != PunchState.Paused)
            {
                return;
            }

            var resumeState = _stateBeforePause == PunchState.Finished ? PunchState.ReadCoordinate : _stateBeforePause;
            CurrentState = resumeState;
            Log($"[系统] 冲孔流程继续，当前孔位: {CurrentHoleIndex}");
        }

        public void CancelProcess()
        {
            if (CurrentState == PunchState.Finished)
            {
                return;
            }

            CompletionStatus = PunchCompletionStatus.Cancelled;
            Log($"[系统] 冲孔流程已取消，停止于第 {CurrentHoleIndex} 孔");
            EndProcess();
        }

        /// <summary>
        /// 执行状态机下一步 (通常在控制线程的 while 循环或 Timer 中调用)
        /// </summary>
        public void ExecuteNextStep(PunchProcessType processType)
        {
            ProcessType = processType;

            if (CurrentState == PunchState.Finished || CurrentState == PunchState.Paused) return;

            switch (CurrentState)
            {
                case PunchState.ReadCoordinate:
                    Log($"读取第 {CurrentHoleIndex} 孔坐标...");
                    // TODO: 调用坐标读取接口
                    CurrentState++;
                    break;

                case PunchState.CheckSimulation:
                    if (IsSimulationChecked)
                    {
                        Log("模拟冲孔模式：移动到该孔，跳过实际冲孔。");
                        Hardware.MoveXY();
                        CurrentState = PunchState.CheckFinish;
                    }
                    else
                    {
                        Log("移动到该孔...");
                        Hardware.MoveXY();
                        CurrentState++;
                    }
                    break;

                case PunchState.DetectSurface:
                    if (IsDetectionEnabled)
                    {
                        Log("开始探测工件表面...");
                        Hardware.FastMoveZ();
                        if (Hardware.CheckContactSignal())
                        {
                            ShowAlarm("提前检测到接触信号，请检查工作台是否有异物！");
                            EndProcess();
                            break;
                        }

                        Hardware.SlowMoveZ();
                        if (Hardware.CheckContactSignal())
                        {
                            double diff = Hardware.CalculateDifference();
                            if (diff < DetectThreshold)
                            {
                                Log("探测成功，建立工件坐标系。");
                                CurrentState++;
                            }
                            else
                            {
                                // 触发需要用户确认的警告事件
                                ShowWarningDialog("表面高度偏差超出预期，请确认是否继续？");
                                // 实际开发中，这里状态机应挂起，等待UI反馈后再决定是执行 CurrentState++ 还是 EndProcess()
                                // 为演示流畅，这里默认用户同意
                                CurrentState++;
                            }
                        }
                        else
                        {
                            ShowAlarm("未检测到接触信号，请检查工作台和冲针是否正常！");
                            EndProcess();
                        }
                    }
                    else
                    {
                        Log("跳过探测工件表面。");
                        CurrentState += 2;
                    }
                    break;

                case PunchState.LiftToHeight1:
                    Log("Z轴抬起到安全高度...");
                    Hardware.LiftZ();
                    CurrentState++;
                    break;

                case PunchState.PunchAction:
                    Log("执行冲孔动作...");
                    Hardware.PunchDown();
                    double contactDiff = Hardware.CalculateDifference();

                    if (ProcessType == PunchProcessType.SecondPass)
                    {
                        if (contactDiff < DevThreshold)
                        {
                            ShowAdjustmentDialog("当前孔位置偏差过大，请手动微调。");
                            // 实际开发中这里应等待UI反馈完成
                        }
                        else if (contactDiff > BreakPinThreshold)
                        {
                            HandleBrokenPin();
                            break;
                        }
                    }
                    else
                    {
                        if (contactDiff > BreakPinThreshold)
                        {
                            HandleBrokenPin();
                            break;
                        }
                    }

                    Log("等待Z轴运动结束...");
                    Hardware.WaitForZStop();
                    CurrentState++;
                    break;

                case PunchState.LiftToHeight2:
                    Log("冲孔结束，Z轴抬起...");
                    Hardware.LiftZ();
                    CurrentState++;
                    break;

                case PunchState.CheckFinish:
                    if (IsLastHole)
                    {
                        Log("所有孔位加工完毕！");
                        CurrentHoleIndex = 1;
                        CompletionStatus = PunchCompletionStatus.NormalFinished;
                        EndProcess();
                    }
                    else
                    {
                        CurrentHoleIndex++;
                        CurrentState = PunchState.ReadCoordinate;
                    }
                    break;
            }
        }

        #region 内部辅助方法

        private void HandleBrokenPin()
        {
            Log("检测到断针！停止Z轴！");
            Hardware.StopZ();
            CurrentHoleIndex++;
            CompletionStatus = PunchCompletionStatus.AbnormalFinished;
            ShowAlarm("断针报警！请检查冲针。");
            Hardware.LiftZ();
            EndProcess();
        }

        private void EndProcess()
        {
            _stateBeforePause = PunchState.Finished;
            CurrentState = PunchState.Finished;
            Log("冲孔流程已结束/就绪。");
        }

        // 以下方法全部改为触发事件，交由UI处理
        private void Log(string msg)
        {
            MessageReported?.Invoke(this, new MessageEventArgs(msg));
        }

        private void ShowAlarm(string msg)
        {
            if (CompletionStatus == PunchCompletionStatus.None)
            {
                CompletionStatus = PunchCompletionStatus.AbnormalFinished;
            }

            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: true));
        }

        private void ShowWarningDialog(string msg)
        {
            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: true, requiresUserAction: true));
        }

        private void ShowAdjustmentDialog(string msg)
        {
            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: false, requiresUserAction: true));
        }

        #endregion
    }
    /*
     move xy  fast

    move z slow  lift z  punch down  stop z  wait for z stop  check contact signal  calculate difference

    检测表面
        1 IO  read... slow speed: 200~500 -->  1plus/um 
        2 接触 On  , 分离 Off 10um ,  


    1分钟150~160个孔，平均每孔约0.375秒，考虑到加速、减速和等待时间，实际冲孔动作可能在0.2-0.3秒之间，剩余时间用于XY移动和其他操作。

    2ms-->  6000um/s  --> 6um/ms  12um

     
     */

    public interface IHardwareController
    {
        void MoveXY();
        void FastMoveZ();
        void SlowMoveZ();
        void LiftZ();
        void PunchDown();
        void StopZ();
        void WaitForZStop();
        bool CheckContactSignal();
        double CalculateDifference();

        // 还可以扩展连接和断开等公共生命周期方法
        bool Initialize();
        void Close();
    }

    // 众为兴 ADT8940A1 控制卡实现
    public class Adt8940Controller : IHardwareController
    {
        private int _cardHandle; // 维护硬件状态（句柄）

        public bool Initialize()
        {
            // 调用众为兴 SDK 初始化
            return true;
        }

        public void MoveXY()
        {
            // 封装底层 adt8940_move(x, y) 等函数
            Console.WriteLine("ADT8940A1: 发送XY运动指令");
        }

        public void FastMoveZ() { }
        public void SlowMoveZ() { }
        public void LiftZ() { }
        public void PunchDown() { }
        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => false;
        public double CalculateDifference() => 0.0;

        public void Close()
        {
        }
    }

    // 模拟控制器（用于离线测试或纯UI调试）
    public class MockHardwareController : IHardwareController
    {
        public bool Initialize() => true;
        public void MoveXY() => Console.WriteLine("Mock: 模拟XY走位...");
        public void FastMoveZ() { }
        public void SlowMoveZ() { }
        public void LiftZ() { }
        public void PunchDown() { }
        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => true;
        public double CalculateDifference() => 0.0;

        public void Close()
        {

        }
    }

    /// <summary>
    /// 模拟硬件操作接口 (根据实际控制卡API替换)
    /// </summary>
    public class HardwareSimulation : IHardwareController
    {
        public void MoveXY() { /* 调用 ADT8940A1 走位 */ }
        public void FastMoveZ() { /* Z轴快速下探 */ }
        public void SlowMoveZ() { /* Z轴慢速下探 */ }
        public void LiftZ() { /* Z轴抬起 */ }
        public void PunchDown() { /* Z轴冲孔 */ }
        public void StopZ() { /* 急停Z轴 */ }
        public void WaitForZStop() { /* 阻塞或轮询等待轴停止 */ }
        public bool CheckContactSignal() => false; // 模拟读取接触IO
        public double CalculateDifference() => 0.0; // 模拟计算当前位置与预期表面位置差值

        public bool Initialize()
        {
            return true;
        }

        public void Close()
        {
        }
    }
}
