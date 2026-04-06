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
        LiftToHeightSafe = 4,      // Z轴起到抬起高度
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

    /// <summary>
    /// 补偿选择事件参数（按XY最近邻选点）
    /// </summary>
    public class CompensationSelectedEventArgs : EventArgs
    {
        public int HoleIndex { get; }
        public double TargetX { get; }
        public double TargetY { get; }
        public double Compensation { get; }
        public bool HasNearestSample { get; }
        public double NearestSampleX { get; }
        public double NearestSampleY { get; }
        public double NearestSampleSurfaceZ { get; }
        public double NearestDistance { get; }
        public int SampleCount { get; }

        public CompensationSelectedEventArgs(
            int holeIndex,
            double targetX,
            double targetY,
            double compensation,
            bool hasNearestSample,
            double nearestSampleX,
            double nearestSampleY,
            double nearestSampleSurfaceZ,
            double nearestDistance,
            int sampleCount)
        {
            HoleIndex = holeIndex;
            TargetX = targetX;
            TargetY = targetY;
            Compensation = compensation;
            HasNearestSample = hasNearestSample;
            NearestSampleX = nearestSampleX;
            NearestSampleY = nearestSampleY;
            NearestSampleSurfaceZ = nearestSampleSurfaceZ;
            NearestDistance = nearestDistance;
            SampleCount = sampleCount;
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
        public event EventHandler<CompensationSelectedEventArgs> CompensationSelected;

        private PunchState _currentState = PunchState.Finished;
        private PunchState _stateBeforePause = PunchState.Finished;
        private IHardwareController Hardware;
        private bool _firstHoleSurfaceDetected;
        private bool _hasSurfaceReference;
        private double _referenceSurfaceZ;
        private double _latestSurfaceZ;
        private readonly List<SurfaceSample> _surfaceSamples = new();
        private double _currentTargetX;
        private double _currentTargetY;
        private double _rawTargetX;
        private double _rawTargetY;

        private readonly record struct SurfaceSample(double X, double Y, double SurfaceZ);
        private readonly record struct CompensationResult(
            double Compensation,
            bool HasNearestSample,
            double NearestSampleX,
            double NearestSampleY,
            double NearestSampleSurfaceZ,
            double NearestDistance,
            int SampleCount);

        // 由外部提供孔位索引对应的目标坐标
        public Func<int, (double X, double Y)>? HoleCoordinateResolver { get; set; }
        public Func<double, double, (double X, double Y)>? HoleCoordinateTransformer { get; set; }

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

        // 首孔探测参数
        public double DetectOffsetX { get; set; } = 0.0;
        public double DetectOffsetY { get; set; } = 0.0;
        public double FastApproachDistance { get; set; } = 0.0;
        public double SlowDetectDistance { get; set; } = 0.0;

        /// <summary>
        /// 启动冲孔流程
        /// </summary>
        public void StartProcess(PunchProcessType type)
        {
            ProcessType = type;
            CurrentHoleIndex = 1;
            _stateBeforePause = PunchState.Finished;
            CompletionStatus = PunchCompletionStatus.None;
            _firstHoleSurfaceDetected = false;
            _hasSurfaceReference = false;
            _referenceSurfaceZ = 0.0;
            _latestSurfaceZ = 0.0;
            _surfaceSamples.Clear();
            _currentTargetX = 0.0;
            _currentTargetY = 0.0;
            _rawTargetX = 0.0;
            _rawTargetY = 0.0;
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
                    if (HoleCoordinateResolver != null)
                    {
                        var point = HoleCoordinateResolver(CurrentHoleIndex);
                        _rawTargetX = point.X;
                        _rawTargetY = point.Y;

                        var transformed = point;
                        if (ProcessType == PunchProcessType.SecondPass && HoleCoordinateTransformer is not null)
                        {
                            transformed = HoleCoordinateTransformer(point.X, point.Y);
                        }

                        _currentTargetX = transformed.X;
                        _currentTargetY = transformed.Y;

                        if (ProcessType == PunchProcessType.SecondPass)
                        {
                            Log($"第 {CurrentHoleIndex} 孔二道坐标: 原始(X={_rawTargetX:F4}, Y={_rawTargetY:F4}) -> 变换后(X={_currentTargetX:F4}, Y={_currentTargetY:F4})");
                        }
                        else
                        {
                            Log($"第 {CurrentHoleIndex} 孔目标坐标: X={_currentTargetX:F4}, Y={_currentTargetY:F4}");
                        }
                    }
                    else
                    {
                        _rawTargetX = 0.0;
                        _rawTargetY = 0.0;
                        _currentTargetX = 0.0;
                        _currentTargetY = 0.0;
                    }
                    // TODO: 调用坐标读取接口
                    CurrentState++;
                    break;

                case PunchState.CheckSimulation:
                    if (IsSimulationChecked)
                    {
                        Log("模拟冲孔模式：移动到该孔，跳过实际冲孔。");
                        Hardware.MoveXY(_currentTargetX, _currentTargetY);
                        CurrentState = PunchState.CheckFinish;
                    }
                    else
                    {
                        Log("移动到该孔...");
                        Hardware.MoveXY(_currentTargetX, _currentTargetY);
                        CurrentState++;
                    }
                    break;

                case PunchState.DetectSurface:
                    if (!IsDetectionEnabled)
                    {
                        Log("跳过探测工件表面。");
                        CurrentState = PunchState.LiftToHeightSafe;
                        break;
                    }

                    if (CurrentHoleIndex != 1)
                    {
                        Log("非首孔，跳过慢速探测，沿用最近点补偿。");
                        CurrentState = PunchState.LiftToHeightSafe;
                        break;
                    }

                    Log($"首孔探测：先移动到偏移位置 (offsetX={DetectOffsetX}, offsetY={DetectOffsetY})...");
                    Hardware.MoveXYToOffset(DetectOffsetX, DetectOffsetY);

                    Log("首孔探测：Z轴快速下降到接近位置...");
                    Hardware.FastMoveZ(FastApproachDistance);
                    if (Hardware.CheckContactSignal())
                    {
                        ShowAlarm("提前检测到接触信号，请检查工作台是否有异物！");
                        EndProcess();
                        break;
                    }

                    Log("首孔探测：Z轴慢速下降固定距离进行探测...");
                    Hardware.SlowMoveZ(SlowDetectDistance);
                    if (!Hardware.CheckContactSignal())
                    {
                        ShowAlarm("未检测到接触信号，请检查工作台和冲针是否正常！");
                        EndProcess();
                        break;
                    }

                    double diff = Hardware.CalculateDifference();
                    double detectedSurfaceZ = Hardware.ReadRecordedSurfaceZ();
                    UpdateSurfaceReference(detectedSurfaceZ);
                    AddSurfaceSample(_currentTargetX, _currentTargetY, detectedSurfaceZ);
                    _firstHoleSurfaceDetected = true;

                    if (diff < DetectThreshold)
                    {
                        Log($"首孔探测成功，表面Z={detectedSurfaceZ:F4}，建立补偿基准。");
                        CurrentState = PunchState.LiftToHeightSafe;
                    }
                    else
                    {
                        ShowWarningDialog($"表面高度偏差超出预期(diff={diff:F4})，请确认是否继续？");
                        CurrentState = PunchState.LiftToHeightSafe;
                    }
                    break;

                case PunchState.LiftToHeightSafe:
                    Log("Z轴抬起到安全高度...");
                    Hardware.LiftZ();

                    if (CurrentHoleIndex == 1 && _firstHoleSurfaceDetected)
                    {
                        Log("首孔探测完成，回到首孔目标位置准备冲孔...");
                        Hardware.MoveXY(_currentTargetX, _currentTargetY);
                        _firstHoleSurfaceDetected = false;
                    }

                    CurrentState++;
                    break;

                case PunchState.PunchAction:
                    CompensationResult compensationResult = ResolvePunchCompensation(_currentTargetX, _currentTargetY);
                    double compensation = compensationResult.Compensation;
                    Log($"执行冲孔动作(补偿={compensation:F4})...");
                    Hardware.PunchDown(compensation);
                    CompensationSelected?.Invoke(this, new CompensationSelectedEventArgs(
                        CurrentHoleIndex,
                        _currentTargetX,
                        _currentTargetY,
                        compensationResult.Compensation,
                        compensationResult.HasNearestSample,
                        compensationResult.NearestSampleX,
                        compensationResult.NearestSampleY,
                        compensationResult.NearestSampleSurfaceZ,
                        compensationResult.NearestDistance,
                        compensationResult.SampleCount));
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

                    double punchedSurfaceZ = Hardware.ReadRecordedSurfaceZ();
                    UpdateLatestSurface(punchedSurfaceZ);
                    AddSurfaceSample(_currentTargetX, _currentTargetY, punchedSurfaceZ);
                    Log($"读取当前孔表面Z={punchedSurfaceZ:F4}，更新后续补偿参考。");

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

        private void UpdateSurfaceReference(double surfaceZ)
        {
            _referenceSurfaceZ = surfaceZ;
            _latestSurfaceZ = surfaceZ;
            _hasSurfaceReference = true;
        }

        private void UpdateLatestSurface(double surfaceZ)
        {
            if (!_hasSurfaceReference)
            {
                UpdateSurfaceReference(surfaceZ);
                return;
            }

            _latestSurfaceZ = surfaceZ;
        }

        private void AddSurfaceSample(double x, double y, double surfaceZ)
        {
            _surfaceSamples.Add(new SurfaceSample(x, y, surfaceZ));
        }

        private CompensationResult ResolvePunchCompensation(double targetX, double targetY)
        {
            if (!_hasSurfaceReference)
            {
                return new CompensationResult(0.0, false, 0.0, 0.0, 0.0, 0.0, _surfaceSamples.Count);
            }

            if (_surfaceSamples.Count == 0)
            {
                return new CompensationResult(_latestSurfaceZ - _referenceSurfaceZ, false, 0.0, 0.0, 0.0, 0.0, 0);
            }

            SurfaceSample nearest = _surfaceSamples[0];
            double minDistSq = GetDistanceSquare(targetX, targetY, nearest.X, nearest.Y);

            for (int i = 1; i < _surfaceSamples.Count; i++)
            {
                SurfaceSample sample = _surfaceSamples[i];
                double distSq = GetDistanceSquare(targetX, targetY, sample.X, sample.Y);
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearest = sample;
                }
            }

            return new CompensationResult(
                nearest.SurfaceZ - _referenceSurfaceZ,
                true,
                nearest.X,
                nearest.Y,
                nearest.SurfaceZ,
                Math.Sqrt(minDistSq),
                _surfaceSamples.Count);
        }

        private static double GetDistanceSquare(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return dx * dx + dy * dy;
        }

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
        void MoveXY(double targetX, double targetY);
        void MoveXYToOffset(double offsetX, double offsetY);
        void FastMoveZ(double distance = 0.0);
        void SlowMoveZ(double distance = 0.0);
        void LiftZ();
        void PunchDown(double compensation = 0.0);
        void StopZ();
        void WaitForZStop();
        bool CheckContactSignal();
        double CalculateDifference();
        double ReadRecordedSurfaceZ();

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

        public void MoveXY(double targetX, double targetY)
        {
            // 封装底层 adt8940_move(x, y) 等函数
            Console.WriteLine($"ADT8940A1: 发送XY运动指令 X={targetX:F4}, Y={targetY:F4}");
        }

        public void MoveXYToOffset(double offsetX, double offsetY)
        {
            Console.WriteLine($"ADT8940A1: 发送XY偏移运动指令 offsetX={offsetX}, offsetY={offsetY}");
        }

        public void FastMoveZ(double distance = 0.0) { }
        public void SlowMoveZ(double distance = 0.0) { }
        public void LiftZ() { }
        public void PunchDown(double compensation = 0.0) { }
        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => false;
        public double CalculateDifference() => 0.0;
        public double ReadRecordedSurfaceZ() => 0.0;

        public void Close()
        {
        }
    }

    // 模拟控制器（用于离线测试或纯UI调试）
    public class MockHardwareController : IHardwareController
    {
        private bool _contactSignal;
        private double _surfaceZ;

        public bool Initialize() => true;

        public void MoveXY(double targetX, double targetY)
            => Console.WriteLine($"Mock: 模拟XY走位 X={targetX:F4}, Y={targetY:F4}");

        public void MoveXYToOffset(double offsetX, double offsetY)
            => Console.WriteLine($"Mock: 模拟XY偏移走位 offsetX={offsetX}, offsetY={offsetY}");

        public void FastMoveZ(double distance = 0.0)
        {
            // 快速接近阶段，默认未接触
            _contactSignal = false;
        }

        public void SlowMoveZ(double distance = 0.0)
        {
            // 慢速探测阶段，模拟触发表面接触
            _contactSignal = true;
            _surfaceZ += 0.01;
        }

        public void LiftZ()
        {
            _contactSignal = false;
        }

        public void PunchDown(double compensation = 0.0)
        {
            // 模拟冲孔后记录该点表面值（带补偿）
            _surfaceZ += compensation * 0.1;
        }

        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => _contactSignal;
        public double CalculateDifference() => 0.1;
        public double ReadRecordedSurfaceZ() => _surfaceZ;

        public void Close()
        {
        }
    }

    /// <summary>
    /// 模拟硬件操作接口 (根据实际控制卡API替换)
    /// </summary>
    public class HardwareSimulation : IHardwareController
    {
        public void MoveXY(double targetX, double targetY) { /* 调用 ADT8940A1 走位 */ }
        public void MoveXYToOffset(double offsetX, double offsetY) { /* 调用 ADT8940A1 偏移走位 */ }
        public void FastMoveZ(double distance = 0.0) { /* Z轴快速下探 */ }
        public void SlowMoveZ(double distance = 0.0) { /* Z轴慢速下探 */ }
        public void LiftZ() { /* Z轴抬起 */ }
        public void PunchDown(double compensation = 0.0) { /* Z轴冲孔 */ }
        public void StopZ() { /* 急停Z轴 */ }
        public void WaitForZStop() { /* 阻塞或轮询等待轴停止 */ }
        public bool CheckContactSignal() => false; // 模拟读取接触IO
        public double CalculateDifference() => 0.0; // 模拟计算当前位置与预期表面位置差值
        public double ReadRecordedSurfaceZ() => 0.0; // 模拟读取控制器记录的表面高度

        public bool Initialize()
        {
            return true;
        }

        public void Close()
        {
        }
    }
}
