using Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace BLL
{
    /// <summary>
    /// 冲孔核心状态机控制类
    /// </summary>
    public class PunchStateMachine
    {
        // 声明暴露给UI的事件
        public event EventHandler<StateChangedEventArgs>? StateChanged;
        public event EventHandler<MessageEventArgs>? MessageReported;
        public event EventHandler<CompensationSelectedEventArgs>? CompensationSelected;

        private PunchState _currentState = PunchState.Finished;
        private PunchState _stateBeforePause = PunchState.Finished;
        private IHardwareController Hardware;
        private bool _firstHoleSurfaceDetected;
        private bool _hasBaselineSurfaceProbe;
        private bool _hasFirstPunchSurfaceSample;
        private bool _hasSurfaceReference;
        private bool _hasBottomReference;
        private double _referenceSurfaceZ;
        private double _bottomReferenceZ;
        private double _latestSurfaceZ;
        private readonly List<SurfaceSample> _surfaceSamples = new();
        private double _currentTargetX;
        private double _currentTargetY;
        private double _rawTargetX;
        private double _rawTargetY;
        private readonly ILogger _logger;

        private readonly record struct SurfaceSample(double X, double Y, double SurfaceZ);
        private readonly record struct CompensationResult(
            double Compensation,
            bool HasNearestSample,
            double NearestSampleX,
            double NearestSampleY,
            double NearestSampleSurfaceZ,
            double NearestDistance,
            int SampleCount);
        private readonly record struct PunchStagePlan(
            bool HasFastApproachStage,
            double FastApproachTargetZ,
            double FinalTargetZ,
            double FastApproachDistance,
            double SlowApproachDistance);

        // 由外部提供孔位索引对应的目标坐标
        public Func<int, (double X, double Y)>? HoleCoordinateResolver { get; set; }
        public Func<double, double, (double X, double Y)>? HoleCoordinateTransformer { get; set; }

        public PunchStateMachine(IHardwareController hardware)
            : this(hardware, global::Serilog.Log.Logger)
        {
        }

        public PunchStateMachine(IHardwareController hardware, ILogger logger)
        {
            Hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
            _logger = (logger ?? global::Serilog.Log.Logger).ForContext<PunchStateMachine>();
            _logger.Information("冲孔状态机已创建");
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
                    _logger.Information("状态切换: {OldState} -> {NewState}, 当前孔位索引: {HoleIndex}", oldState, _currentState, CurrentHoleIndex);
                }
            }
        }

        public PunchProcessType ProcessType { get; set; }
        public Recipe? CurrentRecipe { get; private set; }
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
        public double ReferenceProbeOffsetX { get; set; } = -1.0;
        public double ReferenceProbeOffsetY { get; set; } = 0.0;
        public double FastApproachDistance { get; set; } = 0.0;
        public double FastApproachSpeed { get; set; } = 0.0;
        public double SlowDetectDistance { get; set; } = 0.0;
        public double SlowDetectSpeed { get; set; } = 0.0;
        public double SafeZ { get; set; }
        public double FastToSafeZSpeed { get; set; }
        public double PunchDownSpeed { get; set; }

        /// <summary>
        /// 冲针 Z 轴偏移量（用于断针换针后的校准补偿）。
        /// 正值表示新针比原针更长（少冲），负值表示新针更短（多冲）。
        /// 由外部校准流程设置，默认 0。
        /// </summary>
        public double NeedleOffsetZ { get; set; }

        public SurfaceDetectionOptions SurfaceDetectionOptions { get; set; } = new();

        public bool HasInitialSurfaceReference { get; set; }

        public double InitialSurfaceReferenceZ { get; set; }

        /// <summary>
        /// 是否已探测底面（工装）基准。
        /// 安装工件前由用户触发探测，记录工装底面 Z 坐标。
        /// </summary>
        public bool HasBottomReference { get; set; }

        /// <summary>
        /// 底面（工装）基准 Z 坐标，由外部探测后传入。
        /// </summary>
        public double BottomReferenceZ { get; set; }

        /// <summary>
        /// 底面基准模式下，孔底距离工装基准的厚度（正值）。
        /// 例：160 表示孔底位于工装基准上方 160μm。
        /// </summary>
        public double BottomHoleDepth { get; set; }

        /// <summary>
        /// 启动冲孔流程
        /// </summary>
        public void StartProcess(PunchProcessType type, Recipe recipe)
        {
            CurrentRecipe = recipe ?? throw new ArgumentNullException(nameof(recipe));

            // 断针补偿仅在单次冲孔流程中有效，新流程启动时偏移必须为 0
            if (Math.Abs(NeedleOffsetZ) > 0.0001d)
            {
                _logger.Error("[系统] 启动冲孔流程失败: NeedleOffsetZ={Offset:F4} ≠ 0，断针补偿不应跨流程携带。请在启动前重新校准归零。", NeedleOffsetZ);
                ShowWarningDialog($"冲孔流程无法启动！\n\n当前断针补偿 NeedleOffsetZ = {NeedleOffsetZ:F4} mm ≠ 0。\n断针补偿仅在上次冲孔中途换针时有效，新工件应使用新针重新探测表面。\n\n请重新执行测试冲孔校准以归零 NeedleOffsetZ。");
                return;
            }

            ProcessType = type;
            CurrentHoleIndex = 1;
            _stateBeforePause = PunchState.Finished;
            CompletionStatus = PunchCompletionStatus.None;
            _firstHoleSurfaceDetected = false;
            _hasBaselineSurfaceProbe = false;
            _hasFirstPunchSurfaceSample = false;
            _hasSurfaceReference = HasInitialSurfaceReference;
            _referenceSurfaceZ = HasInitialSurfaceReference ? InitialSurfaceReferenceZ : 0.0;
            _latestSurfaceZ = HasInitialSurfaceReference ? InitialSurfaceReferenceZ : 0.0;
            _hasBottomReference = HasBottomReference;
            _bottomReferenceZ = HasBottomReference ? BottomReferenceZ : 0.0;
            _surfaceSamples.Clear();
            _currentTargetX = 0.0;
            _currentTargetY = 0.0;
            _rawTargetX = 0.0;
            _rawTargetY = 0.0;
            if (HasInitialSurfaceReference)
            {
                Log($"[系统] 已加载表面参考 Z={InitialSurfaceReferenceZ:F4} 作为冲孔补偿基准。");
            }
            Log($"[系统] 配方[{CurrentRecipe.RecipeName}]开始{(type == PunchProcessType.FirstPass ? "头道" : "二道")}冲孔流程...");
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
        public void SkipCurrentHole()
        {
            if (CurrentState == PunchState.Finished || CurrentState == PunchState.Paused)
            {
                return;
            }

            Log($"[系统] 跳过未检测到的孔位: 第 {CurrentHoleIndex} 孔");

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

                    Log($"首孔左侧预探：先移动到左侧测试点 (offsetX={ReferenceProbeOffsetX:F4}, offsetY={ReferenceProbeOffsetY:F4})...");
                    Hardware.MoveXYToOffset(ReferenceProbeOffsetX, ReferenceProbeOffsetY);

                    Log($"首孔左侧预探：先移动到 SafeZ={SafeZ:F4}，再按 FastDistance={FastApproachDistance:F4}、SlowDistance={SlowDetectDistance:F4} 探测，Mode={SurfaceDetectionOptions.Mode}...");
                    SurfaceDetectionResult probeResult;
                    try
                    {
                        probeResult = Hardware.ProbeSurface(SafeZ, FastToSafeZSpeed, FastApproachDistance, FastApproachSpeed, SlowDetectDistance, SlowDetectSpeed, SurfaceDetectionOptions);
                    }
                    catch (Exception ex)
                    {
                        ShowAlarm(ex.Message);
                        EndProcess();
                        break;
                    }

                    if (!probeResult.Detected)
                    {
                        ShowAlarm("首孔左侧预探未检测到工件表面，请检查工作台和冲针是否正常。");
                        EndProcess();
                        break;
                    }

                    UpdateSurfaceReference(probeResult.SurfaceZ);
                    _hasBaselineSurfaceProbe = true;
                    _firstHoleSurfaceDetected = true;

                    Log($"首孔左侧预探成功，基准表面Z={probeResult.SurfaceZ:F4}。第一个正式孔先使用该测试点Z作为参考，首刀冲孔过程中再记录当前点SurfaceZ。");
                    CurrentState = PunchState.LiftToHeightSafe;
                    break;

                case PunchState.LiftToHeightSafe:
                    Log("Z轴抬起到安全高度...");
                    Hardware.LiftZ(SafeZ, FastToSafeZSpeed);

                    if (CurrentHoleIndex == 1 && _firstHoleSurfaceDetected)
                    {
                        Log("首孔探测完成，回到首孔目标位置准备冲孔...");
                        Hardware.MoveXY(_currentTargetX, _currentTargetY);
                        _firstHoleSurfaceDetected = false;
                    }

                    CurrentState++;
                    break;

                case PunchState.PunchAction:
                    if (ProcessType == PunchProcessType.FirstPass)
                    {
                        bool isBottomReference = "Bottom".Equals(
                            CurrentRecipe?.ProcessParameters?.FirstPassReference,
                            StringComparison.OrdinalIgnoreCase);

                        if (isBottomReference)
                        {
                            ExecutePunchActionBottomReference();
                        }
                        else
                        {
                            ExecutePunchActionSurfaceReference();
                        }

                        if (CompletionStatus is PunchCompletionStatus.AbnormalFinished or PunchCompletionStatus.Cancelled)
                        {
                            break;
                        }
                    }
                    else
                    {
                        double secondPassDepth = CurrentRecipe?.ProcessParameters?.SecondPunchDepth ?? 0.0;
                        if (!ExecutePunchAction(secondPassDepth, secondPassDepth))
                        {
                            break;
                        }
                    }

                    CurrentState++;
                    break;

                case PunchState.LiftToHeight2:
                    Log("冲孔结束，Z轴抬起...");
                    Hardware.LiftZ(SafeZ, FastToSafeZSpeed);
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
            for (int i = 0; i < _surfaceSamples.Count; i++)
            {
                SurfaceSample sample = _surfaceSamples[i];
                if (AreSameCoordinate(sample.X, x) && AreSameCoordinate(sample.Y, y))
                {
                    _surfaceSamples[i] = new SurfaceSample(x, y, surfaceZ);
                    return;
                }
            }

            _surfaceSamples.Add(new SurfaceSample(x, y, surfaceZ));
        }

        private List<RecipeDepthItem> GetFirstPassPunchDepths()
        {
            return CurrentRecipe?.ProcessParameters?.PunchDepths?
                .Where(x => x.Value > 0)
                .Select(x => new RecipeDepthItem
                {
                    Label = x.Label,
                    Value = x.Value
                })
                .ToList()
                ?? new List<RecipeDepthItem>();
        }

        /// <summary>
        /// 底面基准（Bottom）头道冲孔逻辑（待实现）。
        /// 与 Surface 基准不同，Bottom 基准以工件底面为参考面计算冲孔目标位置。
        /// </summary>
        /// <summary>
        /// 表面基准（Surface）头道冲孔逻辑。
        /// 以工件表面为参考面，按配方 PunchDepths 逐层下压冲孔。
        /// </summary>
        private void ExecutePunchActionSurfaceReference()
        {
            List<RecipeDepthItem> punchDepths = GetFirstPassPunchDepths();

            if (punchDepths.Count == 0)
            {
                punchDepths.Add(new RecipeDepthItem
                {
                    Label = "No.1",
                    Value = 0
                });
            }

            double accumulatedDepth = 0.0;
            for (int i = 0; i < punchDepths.Count; i++)
            {
                RecipeDepthItem punchDepth = punchDepths[i];
                accumulatedDepth += punchDepth.Value;
                if (!ExecutePunchAction(accumulatedDepth, punchDepth.Value, punchDepth.Label, i + 1, punchDepths.Count))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 底面基准（Bottom）头道冲孔逻辑。
        /// 以工件底面（工装基准）为参考面，冲孔深度受底面约束：
        ///   targetZ = max(surfaceZ - accumulatedDepth, bottomReferenceZ + BottomHoleDepth)
        /// 即向下冲孔时不得低于工装基准 + 孔底厚度。
        /// </summary>
        private void ExecutePunchActionBottomReference()
        {
            if (!_hasBottomReference)
            {
                Log("[底面基准] 未设置底面基准，回退为表面基准冲孔。");
                ExecutePunchActionSurfaceReference();
                return;
            }

            if (!_hasSurfaceReference)
            {
                Log("[底面基准] 缺少表面参考，无法执行底面基准冲孔。");
                return;
            }

            double bottomLimitZ = _bottomReferenceZ + BottomHoleDepth;
            Log($"[底面基准] 底面基准Z={_bottomReferenceZ:F3}, 孔底厚度={BottomHoleDepth:F3}, 底面限制Z={bottomLimitZ:F3}, 表面Z={_referenceSurfaceZ:F3}");

            List<RecipeDepthItem> punchDepths = GetFirstPassPunchDepths();
            if (punchDepths.Count == 0)
            {
                punchDepths.Add(new RecipeDepthItem { Label = "No.1", Value = 0 });
            }

            // 预计算补偿值（与 ExecutePunchAction 内部 ResolvePunchCompensation 一致）
            CompensationResult compensationResult = ResolvePunchCompensation(_currentTargetX, _currentTargetY);
            double comp = compensationResult.Compensation;
            double surfaceZ = _referenceSurfaceZ + comp;

            // 从表面到底面限制的最大允许累计深度
            double maxAllowedDepth = surfaceZ - bottomLimitZ;
            Log($"[底面基准] 补偿后表面Z={surfaceZ:F3}, 最大允许累计深度={maxAllowedDepth:F3}");

            double accumulatedDepth = 0.0;
            for (int i = 0; i < punchDepths.Count; i++)
            {
                RecipeDepthItem punchDepth = punchDepths[i];
                accumulatedDepth += punchDepth.Value;

                // 若本步累计深度超出底面限制，截断到限制深度
                bool isLimitedByBottom = accumulatedDepth > maxAllowedDepth;
                double effectiveDepth = isLimitedByBottom ? maxAllowedDepth : accumulatedDepth;

                if (isLimitedByBottom)
                {
                    Log($"[底面基准] 第{i + 1}步触底截断: 期望累计={accumulatedDepth:F3}, 实际累计={effectiveDepth:F3}, 目标Z=surfaceZ-effectiveDepth={surfaceZ - effectiveDepth:F3}");
                }

                if (!ExecutePunchAction(effectiveDepth, punchDepth.Value, punchDepth.Label, i + 1, punchDepths.Count))
                {
                    break;
                }

                if (isLimitedByBottom)
                {
                    Log("[底面基准] 已达底面限制深度，停止后续步进。");
                    break;
                }
            }
        }

        private bool ExecutePunchAction(double configuredDepth = 0.0, double stepDepth = 0.0, string? punchLabel = null, int stepIndex = 1, int totalSteps = 1)
        {
            CompensationResult compensationResult = ResolvePunchCompensation(_currentTargetX, _currentTargetY);
            double compensation = compensationResult.Compensation;
            double? localSurfaceZ = TryResolvePunchSurfaceZ(compensation);
            double? absoluteTargetZ = localSurfaceZ.HasValue ? localSurfaceZ.Value - configuredDepth : null;
            double fallbackRelativeDepth = -stepDepth;
            bool detectSurface = ShouldDetectSurfaceDuringThisPunch();
            string punchStepTitle = BuildPunchStepTitle(punchLabel, stepIndex, totalSteps);

            double finalTargetZ = (absoluteTargetZ ?? (SafeZ + fallbackRelativeDepth)) + NeedleOffsetZ;
            PunchStagePlan punchStagePlan = BuildPunchStagePlan(finalTargetZ);
            LogPunchPreparation(
                punchStepTitle,
                configuredDepth,
                stepDepth,
                compensation,
                localSurfaceZ,
                absoluteTargetZ,
                fallbackRelativeDepth,
                finalTargetZ,
                punchStagePlan,
                detectSurface);

            SurfaceDetectionResult punchResult = ExecutePunchDownByStage(
                punchStagePlan,
                detectSurface,
                punchStepTitle,
                configuredDepth,
                stepDepth,
                compensation,
                finalTargetZ);
            TryRecordPunchSurfaceSample(punchResult, detectSurface, punchStepTitle);
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

            if (ProcessType == PunchProcessType.FirstPass)
            {
                _logger.Information("孔位#{HoleIndex}第{StepIndex}/{StepCount}次头道冲孔{PunchLabel}: 累计深度={PunchDepth}, 本次步进={StepDepth}, 补偿={Compensation}, 表面Z={SurfaceZ}, 目标Z={TargetZ}, 最近邻距离={Distance}, 采样点数量={SampleCount}",
                    CurrentHoleIndex,
                    stepIndex,
                    totalSteps,
                    FormatPunchLabel(punchLabel),
                    configuredDepth,
                    stepDepth,
                    compensationResult.Compensation,
                    localSurfaceZ,
                    absoluteTargetZ,
                    compensationResult.NearestDistance,
                    compensationResult.SampleCount);
            }
            else
            {
                _logger.Information("孔位#{HoleIndex}二道冲孔: 深度={PunchDepth}, 补偿={Compensation}, 表面Z={SurfaceZ}, 目标Z={TargetZ}, 最近邻距离={Distance}, 采样点数量={SampleCount}",
                    CurrentHoleIndex,
                    configuredDepth,
                    compensationResult.Compensation,
                    localSurfaceZ,
                    absoluteTargetZ,
                    compensationResult.NearestDistance,
                    compensationResult.SampleCount);
            }

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
                    return false;
                }
            }
            else if (contactDiff > BreakPinThreshold)
            {
                HandleBrokenPin();
                return false;
            }

            Log("等待Z轴运动结束...");
            Hardware.WaitForZStop();
            Log($"{punchStepTitle} 结束回退: SafeZ={SafeZ:F4}, 回退速度={FastToSafeZSpeed:F4}...");
            Hardware.LiftZ(SafeZ, FastToSafeZSpeed);

            if (ProcessType == PunchProcessType.FirstPass)
            {
                Log($"{punchStepTitle} 完成。");
            }
            else
            {
                Log($"{punchStepTitle} 完成。");
            }

            return true;
        }

        private void LogPunchPreparation(
            string punchStepTitle,
            double configuredDepth,
            double stepDepth,
            double compensation,
            double? localSurfaceZ,
            double? absoluteTargetZ,
            double fallbackRelativeDepth,
            double finalTargetZ,
            PunchStagePlan punchStagePlan,
            bool detectSurface)
        {
            string surfaceText = localSurfaceZ.HasValue
                ? localSurfaceZ.Value.ToString("F4")
                : "未建立参考Z";

            string targetText = absoluteTargetZ.HasValue
                ? absoluteTargetZ.Value.ToString("F4")
                : $"按相对下压 {fallbackRelativeDepth:F4}";

            Log($"{punchStepTitle} 准备开始: 点位(X={_currentTargetX:F4}, Y={_currentTargetY:F4}), StartSafeZ={SafeZ:F4}, 本次步进深度={stepDepth:F4}, 累计下压深度={configuredDepth:F4}, 补偿值={compensation:F4}, 当前表面Z={surfaceText}, 目标Z={targetText}, FinalTargetZ={finalTargetZ:F4}, 首刀表面探测={(detectSurface ? "启用" : "关闭")}");

            if (punchStagePlan.HasFastApproachStage)
            {
                Log($"{punchStepTitle} 快速下探计划: 起点SafeZ={SafeZ:F4}, 距离={punchStagePlan.FastApproachDistance:F4}, 速度={FastToSafeZSpeed:F4}, FastTargetZ={punchStagePlan.FastApproachTargetZ:F4}");
            }
            else
            {
                Log($"{punchStepTitle} 快速下探计划: 已跳过，直接从 SafeZ={SafeZ:F4} 进入慢速冲孔段。");
            }

            Log($"{punchStepTitle} 慢速冲孔计划: 距离={punchStagePlan.SlowApproachDistance:F4}, 速度={PunchDownSpeed:F4}, 累计下压深度={configuredDepth:F4}, 本次步进深度={stepDepth:F4}, 补偿值={compensation:F4}, FinalTargetZ={finalTargetZ:F4}");
        }

        private PunchStagePlan BuildPunchStagePlan(double finalTargetZ)
        {
            if (finalTargetZ >= SafeZ)
            {
                return new PunchStagePlan(
                    HasFastApproachStage: false,
                    FastApproachTargetZ: finalTargetZ,
                    FinalTargetZ: finalTargetZ,
                    FastApproachDistance: 0d,
                    SlowApproachDistance: 0d);
            }

            double desiredFastTargetZ = SafeZ + FastApproachDistance;
            double desiredSlowStageStartZ = finalTargetZ + Math.Abs(SlowDetectDistance);
            double fastApproachTargetZ = Math.Min(desiredFastTargetZ, desiredSlowStageStartZ);
            fastApproachTargetZ = Math.Max(finalTargetZ, Math.Min(SafeZ, fastApproachTargetZ));

            bool hasFastApproachStage = fastApproachTargetZ < SafeZ && fastApproachTargetZ > finalTargetZ;
            double fastApproachDistance = hasFastApproachStage ? fastApproachTargetZ - SafeZ : 0d;
            double slowApproachDistance = finalTargetZ - (hasFastApproachStage ? fastApproachTargetZ : SafeZ);

            return new PunchStagePlan(
                HasFastApproachStage: hasFastApproachStage,
                FastApproachTargetZ: fastApproachTargetZ,
                FinalTargetZ: finalTargetZ,
                FastApproachDistance: fastApproachDistance,
                SlowApproachDistance: slowApproachDistance);
        }

        private SurfaceDetectionResult ExecutePunchDownByStage(
            PunchStagePlan punchStagePlan,
            bool detectSurface,
            string punchStepTitle,
            double configuredDepth,
            double stepDepth,
            double compensation,
            double finalTargetZ)
        {
            if (punchStagePlan.HasFastApproachStage)
            {
                Log($"{punchStepTitle} 快速下探执行: StartSafeZ={SafeZ:F4} -> FastTargetZ={punchStagePlan.FastApproachTargetZ:F4}, 距离={punchStagePlan.FastApproachDistance:F4}, 速度={FastToSafeZSpeed:F4}");
                Hardware.PunchDown(punchStagePlan.FastApproachTargetZ, isAbsoluteTarget: true, detectSurface: false, detectionOptions: null, speed: FastToSafeZSpeed);
            }
            else
            {
                Log($"{punchStepTitle} 快速下探执行: 已跳过，直接慢速下压到目标Z={punchStagePlan.FinalTargetZ:F4}");
            }

            Log($"{punchStepTitle} 慢速冲孔执行: Distance={punchStagePlan.SlowApproachDistance:F4}, Speed={PunchDownSpeed:F4}, 累计下压深度={configuredDepth:F4}, 本次步进深度={stepDepth:F4}, 补偿值={compensation:F4}, FinalTargetZ={finalTargetZ:F4}, 首刀表面探测={(detectSurface ? "启用" : "关闭")}");
            return Hardware.PunchDown(
                punchStagePlan.FinalTargetZ,
                isAbsoluteTarget: true,
                detectSurface: detectSurface,
                detectionOptions: detectSurface ? SurfaceDetectionOptions : null,
                speed: PunchDownSpeed);
        }

        private string BuildPunchStepTitle(string? punchLabel, int stepIndex, int totalSteps)
        {
            return ProcessType == PunchProcessType.FirstPass
                ? $"孔#{CurrentHoleIndex} 头道第 {stepIndex}/{totalSteps} 次冲孔{FormatPunchLabel(punchLabel)}"
                : $"孔#{CurrentHoleIndex} 二道冲孔";
        }

        private double? TryResolvePunchSurfaceZ(double compensation)
        {
            if (!_hasSurfaceReference)
            {
                return null;
            }

            return _referenceSurfaceZ + compensation;
        }

        private bool ShouldDetectSurfaceDuringThisPunch()
        {
            return CurrentHoleIndex == 1 && !_hasFirstPunchSurfaceSample && _hasBaselineSurfaceProbe;
        }

        private void TryRecordPunchSurfaceSample(SurfaceDetectionResult result, bool wasDetectionRequested, string punchStepTitle)
        {
            if (!wasDetectionRequested)
            {
                return;
            }

            if (!result.Detected)
            {
                Log($"{punchStepTitle} 首次当前点表面探测未命中，继续沿用首孔左侧测试点 SurfaceZ 作为补偿参考。");
                _hasFirstPunchSurfaceSample = true;
                return;
            }

            UpdateLatestSurface(result.SurfaceZ);
            AddSurfaceSample(_currentTargetX, _currentTargetY, result.SurfaceZ);
            _hasFirstPunchSurfaceSample = true;
            Log($"{punchStepTitle} 首次探测到当前点表面值 SurfaceZ={result.SurfaceZ:F4}，后续同点多次冲孔继续沿用该点补偿；后续孔位按最近邻 SurfaceZ 样本补偿。");
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

        private static bool AreSameCoordinate(double left, double right)
        {
            return Math.Abs(left - right) < 0.000001;
        }

        private static string FormatPunchLabel(string? punchLabel)
        {
            return string.IsNullOrWhiteSpace(punchLabel) ? string.Empty : $"[{punchLabel}]";
        }

        private void HandleBrokenPin()
        {
            Log("检测到断针！停止Z轴！");
            Hardware.StopZ();
            CurrentHoleIndex++;
            CompletionStatus = PunchCompletionStatus.AbnormalFinished;
            ShowAlarm("断针报警！请检查冲针。");
            Hardware.LiftZ(SafeZ, FastToSafeZSpeed);
            EndProcess();
        }

        private void EndProcess()
        {
            _stateBeforePause = PunchState.Finished;
            CurrentState = PunchState.Finished;

            // 冲孔流程完整结束后归零断针补偿，新工件将使用新针重新探测表面
            if (Math.Abs(NeedleOffsetZ) > 0.0001d)
            {
                _logger.Information("[系统] 冲孔流程结束，归零 NeedleOffsetZ（原值={OldOffset:F4}）。新工件将使用新针探测表面。", NeedleOffsetZ);
                NeedleOffsetZ = 0d;
            }

            Log("冲孔流程已结束/就绪。");
        }

        // 以下方法全部改为触发事件，交由UI处理
        private void Log(string msg)
        {
            _logger.Information("{Message}", msg);
            MessageReported?.Invoke(this, new MessageEventArgs(msg));
        }

        private void ShowAlarm(string msg)
        {
            if (CompletionStatus == PunchCompletionStatus.None)
            {
                CompletionStatus = PunchCompletionStatus.AbnormalFinished;
            }

            _logger.Warning("{Message}", msg);
            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: true));
        }

        private void ShowWarningDialog(string msg)
        {
            _logger.Warning("{Message}", msg);
            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: true, requiresUserAction: true));
        }

        private void ShowAdjustmentDialog(string msg)
        {
            _logger.Information("{Message}", msg);
            MessageReported?.Invoke(this, new MessageEventArgs(msg, isAlarm: false, requiresUserAction: true));
        }

        #endregion
    }
}
