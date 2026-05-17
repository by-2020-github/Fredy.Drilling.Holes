using Common.Tools;
using Fredy.Drilling.Holes.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace Fredy.Drilling.Holes.Services
{
    /// <summary>
    /// 二维平面点，用于坐标系之间传递。
    /// </summary>
    public readonly record struct Point2D(double X, double Y)
    {
        public override string ToString() => $"({X:F3}, {Y:F3})";
    }

    /// <summary>
    /// 坐标系校准数据。
    /// <para>
    /// 本系统涉及三个坐标概念：<br/>
    /// · <b>机械坐标系</b>：运动控制卡物理坐标，由回零确定，是所有位置的底层表示。<br/>
    /// · <b>相机中心机械坐标</b>：相机和冲针共用同一套 XY 轴，只是安装位置存在固定偏移
    ///   （<see cref="CameraToPunchOffsetX"/> / <see cref="CameraToPunchOffsetY"/>）。
    ///   相机采集到的点位（边缘点、圆心）均为"相机中心所在的机械坐标"，
    ///   需减去该偏移才能换算为冲针等效的机械坐标。<br/>
    /// · <b>工件/冲针坐标系</b>：以冲针对准工件圆心时的机械坐标为原点，Recipe 孔位坐标使用此坐标系。
    /// </para>
    /// </summary>
    public class CoordinateCalibration
    {
        // ─── 相机 → 冲针偏移（机械坐标系下的固定偏移量，校准1）────────────────
        /// <summary>
        /// 相机视野中心相对于冲针在机械坐标系下的 X 偏移量。<br/>
        /// 校准方法：冲一个孔（记录冲孔时机械坐标 P_punch），
        /// 然后移动平台使相机视野中心与该孔重合（记录机械坐标 P_camera），
        /// OffsetX = P_punch.X - P_camera.X。
        /// </summary>
        public double CameraToPunchOffsetX { get; set; }

        /// <summary>相机视野中心相对于冲针在机械坐标系下的 Y 偏移量。</summary>
        public double CameraToPunchOffsetY { get; set; }

        // ─── 相机对准工件圆心时的机械坐标（原始测量值，校准2）────────────────
        /// <summary>
        /// 相机视野中心对准工件圆心时，平台的机械坐标 X。<br/>
        /// 这是相机边缘点拟合圆心后得到的原始测量值，
        /// 每次换工件后由 <see cref="CoordinateService.CalibrateWorkpieceByEdgePoints"/> 更新。<br/>
        /// 保留此原始值的意义：若日后重新校准1（更新 CameraToPunchOffset），
        /// 可直接由本字段重新派生 <see cref="WorkpieceCenterX"/>，无需重新上工件。
        /// </summary>
        public double CameraAtWorkpieceCenterX { get; set; }

        /// <summary>
        /// 相机视野中心对准工件圆心时，平台的机械坐标 Y。<br/>
        /// 原始测量值，含义同 <see cref="CameraAtWorkpieceCenterX"/>。
        /// </summary>
        public double CameraAtWorkpieceCenterY { get; set; }

        // ─── 工件坐标系原点（派生值）─────────────────────────────────────────
        /// <summary>
        /// 冲针对准工件圆心时的机械坐标 X（工件坐标系原点）。<br/>
        /// 派生自：CameraAtWorkpieceCenter − CameraToPunchOffset，无需手动赋值。
        /// </summary>
        public double WorkpieceCenterX => CameraAtWorkpieceCenterX + CameraToPunchOffsetX;

        /// <summary>
        /// 冲针对准工件圆心时的机械坐标 Y（工件坐标系原点）。<br/>
        /// 派生自：CameraAtWorkpieceCenter − CameraToPunchOffset，无需手动赋值。
        /// </summary>
        public double WorkpieceCenterY => CameraAtWorkpieceCenterY + CameraToPunchOffsetY;

        // ─── 旋转修正 ─────────────────────────────────────────────────────────
        /// <summary>
        /// 相机轴向与机械轴向的旋转修正角（弧度）。默认为 0（无旋转）。
        /// </summary>
        public double CameraToWorkpieceRotationRad { get; set; } = 0;

        /// <summary>工件圆心原始测量值是否已完成（校准2已执行）。</summary>
        public bool IsWorkpieceCenterCalibrated =>
            CameraAtWorkpieceCenterX != 0.0 || CameraAtWorkpieceCenterY != 0.0;

        /// <summary>相机偏移是否已校准（校准1已执行）。</summary>
        public bool IsCameraOffsetCalibrated =>
            CameraToPunchOffsetX != 0.0 || CameraToPunchOffsetY != 0.0;
    }

    /// <summary>
    /// 坐标转换服务。
    /// <para>
    /// 三个坐标概念与转换链：<br/>
    /// <code>
    /// [相机中心机械坐标] --减去CameraToPunchOffset--> [冲针等效机械坐标] --减去WorkpieceCenter--> [工件/Recipe坐标]
    /// </code>
    /// 职责：<br/>
    /// 1. 维护当前校准参数（<see cref="Calibration"/>）。<br/>
    /// 2. 提供机械坐标系与工件坐标系之间的互转方法（逻辑层最常用）。<br/>
    /// 3. 提供两种校准计算入口：<br/>
    ///    · <see cref="CalibrateCameraToPunchOffset"/> — 冲一个孔后相机对准，求相机相对冲针的固定偏移。<br/>
    ///    · <see cref="CalibrateWorkpieceByEdgePoints"/> — 相机采集工件边缘点拟合圆心，
    ///      再减去相机偏移，得到冲针坐标系原点在机械坐标系中的位置。
    /// </para>
    /// </summary>
    public class CoordinateService
    {
        private readonly ConfigService _configService;
        private readonly ILogger _logger;

        /// <summary>当前校准参数。启动时从 AppConfig 加载，校准后自动持久化。</summary>
        public CoordinateCalibration Calibration { get; private set; }

        public CoordinateService(ConfigService configService, ILogger logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = (logger ?? Log.Logger).ForContext<CoordinateService>();

            Calibration = LoadFromConfig(configService.CurrentConfig.CoordinateCalibration);
            _logger.Information(
                "坐标服务已初始化：CameraToPunchOffset=({OffX:F3}, {OffY:F3}), " +
                "CameraAtWorkpieceCenter=({CWX:F3}, {CWY:F3})",
                Calibration.CameraToPunchOffsetX, Calibration.CameraToPunchOffsetY,
                Calibration.CameraAtWorkpieceCenterX, Calibration.CameraAtWorkpieceCenterY);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 1. 工件坐标系  ↔  机械坐标系
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 将冲针/工件坐标系中的点转换为机械坐标系坐标。
        /// <para>这是逻辑层最常用的转换：指定工件坐标 → 获取运动控制目标位置。</para>
        /// </summary>
        /// <param name="workpiecePoint">工件坐标系中的点（Recipe 孔位坐标）。</param>
        /// <returns>机械坐标系下的目标坐标。</returns>
        /// <exception cref="InvalidOperationException">工件圆心尚未校准。</exception>
        public Point2D WorkpieceToMachine(Point2D workpiecePoint)
        {
            EnsureWorkpieceCalibrated();
            return new Point2D(
                workpiecePoint.X + Calibration.WorkpieceCenterX,
                workpiecePoint.Y + Calibration.WorkpieceCenterY);
        }

        /// <summary>将机械坐标系中的点转换为冲针/工件坐标系坐标。</summary>
        /// <param name="machinePoint">机械坐标系下的当前位置。</param>
        /// <returns>工件坐标系下对应的点。</returns>
        /// <exception cref="InvalidOperationException">工件圆心尚未校准。</exception>
        public Point2D MachineToWorkpiece(Point2D machinePoint)
        {
            EnsureWorkpieceCalibrated();
            return new Point2D(
                machinePoint.X - Calibration.WorkpieceCenterX,
                machinePoint.Y - Calibration.WorkpieceCenterY);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. 相机坐标系  ↔  机械坐标系
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 将相机坐标系中的点转换为机械坐标系坐标。
        /// <para>
        /// 相机坐标系与机械坐标系之间仅有平移关系（忽略镜头畸变），
        /// 偏移量 = (CameraToPunchOffsetX, CameraToPunchOffsetY)。
        /// </para>
        /// </summary>
        /// <param name="cameraPoint">相机坐标系中的点（与机械坐标系同方向，单位 mm）。</param>
        /// <returns>机械坐标系下对应的坐标。</returns>
        /// <exception cref="InvalidOperationException">相机偏移尚未校准。</exception>
        public Point2D CameraToMachine(Point2D cameraPoint)
        {
            EnsureCameraCalibrated();
            return new Point2D(
                cameraPoint.X - Calibration.CameraToPunchOffsetX,
                cameraPoint.Y - Calibration.CameraToPunchOffsetY);
        }

        /// <summary>将机械坐标系中的点转换为相机坐标系坐标。</summary>
        /// <param name="machinePoint">机械坐标系下的点。</param>
        /// <returns>相机坐标系下对应的点。</returns>
        /// <exception cref="InvalidOperationException">相机偏移尚未校准。</exception>
        public Point2D MachineToCamera(Point2D machinePoint)
        {
            EnsureCameraCalibrated();
            return new Point2D(
                machinePoint.X + Calibration.CameraToPunchOffsetX,
                machinePoint.Y + Calibration.CameraToPunchOffsetY);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 3. 相机坐标系  ↔  工件坐标系
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 将相机坐标系中的点转换为工件坐标系坐标。
        /// <para>先转换到机械坐标系，再转换到工件坐标系。
        /// 若存在旋转修正角，则在机械坐标系完成旋转后再做平移。</para>
        /// </summary>
        /// <param name="cameraPoint">相机坐标系中的点。</param>
        /// <returns>工件坐标系下对应的点。</returns>
        public Point2D CameraToWorkpiece(Point2D cameraPoint)
        {
            var machine = CameraToMachine(cameraPoint);
            // 若存在旋转修正，绕工件圆心旋转
            machine = ApplyRotation(machine,
                Calibration.WorkpieceCenterX,
                Calibration.WorkpieceCenterY,
                -Calibration.CameraToWorkpieceRotationRad);
            return MachineToWorkpiece(machine);
        }

        /// <summary>将工件坐标系中的点转换为相机坐标系坐标。</summary>
        /// <param name="workpiecePoint">工件坐标系中的点。</param>
        /// <returns>相机坐标系下对应的点。</returns>
        public Point2D WorkpieceToCamera(Point2D workpiecePoint)
        {
            var machine = WorkpieceToMachine(workpiecePoint);
            machine = ApplyRotation(machine,
                Calibration.WorkpieceCenterX,
                Calibration.WorkpieceCenterY,
                Calibration.CameraToWorkpieceRotationRad);
            return MachineToCamera(machine);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 4. 校准方法
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 【校准1】根据冲孔位置和相机对准位置计算相机相对于冲针的偏移量。
        /// <para>
        /// 操作步骤：<br/>
        /// 1. 在机械坐标系某点冲一个孔，记录此时机械坐标 <paramref name="punchMachinePosition"/>。<br/>
        /// 2. 移动平台，直至相机视野中心与该孔重合，记录此时机械坐标 <paramref name="cameraMachinePosition"/>。<br/>
        /// 3. 调用本方法，结果写入 <see cref="CoordinateCalibration.CameraToPunchOffsetX"/> 等字段。
        /// </para>
        /// </summary>
        /// <param name="punchMachinePosition">冲针冲孔时的机械坐标。</param>
        /// <param name="cameraMachinePosition">相机对准孔位时的机械坐标。</param>
        /// <returns>计算得到的相机偏移量（OffsetX, OffsetY）。</returns>
        public Point2D CalibrateCameraToPunchOffset(Point2D punchMachinePosition, Point2D cameraMachinePosition)
        {
            var offset = new Point2D(
                cameraMachinePosition.X - punchMachinePosition.X,
                cameraMachinePosition.Y - punchMachinePosition.Y);

            Calibration.CameraToPunchOffsetX = offset.X;
            Calibration.CameraToPunchOffsetY = offset.Y;

            _logger.Information(
                "【校准1完成】相机偏移已更新：OffsetX={OffX:F3}, OffsetY={OffY:F3}（冲孔位={Punch}, 相机对准位={Camera}）",
                offset.X, offset.Y, punchMachinePosition, cameraMachinePosition);

            SaveToConfig();
            return offset;
        }

        /// <summary>
        /// 【校准2】通过相机在不同位置对准工件边缘时采集的机械坐标（≥3个），
        /// 拟合出相机对准工件圆心时的机械坐标，再减去 <see cref="CoordinateCalibration.CameraToPunchOffsetX"/>
        /// 得到冲针对准工件圆心时的机械坐标，从而确定工件坐标系原点。
        /// <para>
        /// 关键点：<br/>
        /// · 边缘点采集时记录的是<b>相机中心</b>所在的机械坐标，而非冲针位置。<br/>
        /// · 拟合圆心 = 相机中心对准工件圆心时的机械坐标。<br/>
        /// · WorkpieceCenter（冲针等效） = 拟合圆心 − CameraToPunchOffset。<br/>
        /// · 因此校准1（相机偏移）必须先于校准2完成。
        /// </para>
        /// <para>
        /// 操作步骤：<br/>
        /// 1. 先完成校准1，确保 CameraToPunchOffset 已知。<br/>
        /// 2. 移动平台使相机视野中心对准工件圆形边缘不同位置（≥3点），每次记录机械坐标。<br/>
        /// 3. 调用本方法，圆心坐标写入 <see cref="CoordinateCalibration.WorkpieceCenterX"/> 等字段。
        /// </para>
        /// </summary>
        /// <param name="edgePointsInMachineAtCamera">
        /// 相机视野中心对准工件边缘时的机械坐标列表（即相机中心的机械坐标，不是冲针坐标）。
        /// </param>
        /// <param name="fittedRadius">拟合出的工件半径（mm），仅用于验证，不参与坐标转换。</param>
        /// <returns>工件圆心在机械坐标系下的坐标（冲针等效，即 WorkpieceCenter）。</returns>
        /// <exception cref="InvalidOperationException">相机偏移尚未校准，无法完成工件圆心校准。</exception>
        /// <exception cref="ArgumentException">点数不足或点共线导致拟合失败。</exception>
        public Point2D CalibrateWorkpieceByEdgePoints(
            IEnumerable<Point2D> edgePointsInMachineAtCamera,
            out double fittedRadius)
        {
            EnsureCameraCalibrated();

            var tuples = new List<(double X, double Y)>();
            foreach (var p in edgePointsInMachineAtCamera)
            {
                tuples.Add((p.X, p.Y));
            }

            if (!CircleFitter.FitCircle(tuples, out double cx, out double cy, out double r))
            {
                throw new ArgumentException("边缘点拟合失败：点数不足 3 个或点共线，无法确定工件圆心。");
            }

            // 拟合圆心是"相机中心对准工件圆心时"的机械坐标，保存为原始测量值。
            // WorkpieceCenterX/Y 作为派生属性，自动等于 CameraAtWorkpieceCenter − CameraToPunchOffset。
            Calibration.CameraAtWorkpieceCenterX = cx;
            Calibration.CameraAtWorkpieceCenterY = cy;

            Calibration.CameraToWorkpieceRotationRad = 0.0;

            _logger.Information(
                "【校准2完成】工件圆心已更新：CameraAtCenter=({CX:F3}, {CY:F3}), " +
                "WorkpieceCenter(冲针等效)=({WX:F3}, {WY:F3}), 拟合半径={R:F3} mm，采样点数={Count}",
                cx, cy,
                Calibration.WorkpieceCenterX, Calibration.WorkpieceCenterY,
                r, tuples.Count);

            SaveToConfig();

            fittedRadius = r;
            return new Point2D(Calibration.WorkpieceCenterX, Calibration.WorkpieceCenterY);
        }

        /// <summary>
        /// 直接设置工件圆心（冲针等效机械坐标），内部换算为相机原始测量值存储。-->实际上不应该存在 2026.05.17 暂时屏蔽
        /// 适用于已知冲针圆心坐标时跳过拟合直接赋值的场景。
        /// </summary>
        /// <param name="centerInMachine">冲针对准工件圆心时的机械坐标。</param>
        private void SetWorkpieceCenter(Point2D centerInMachine)
        {
            // 反向换算：存储相机视野对准圆心时的机械坐标，保持字段语义一致
            Calibration.CameraAtWorkpieceCenterX = centerInMachine.X + Calibration.CameraToPunchOffsetX;
            Calibration.CameraAtWorkpieceCenterY = centerInMachine.Y + Calibration.CameraToPunchOffsetY;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 私有辅助
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>绕指定轴心旋转平面点。</summary>
        private static Point2D ApplyRotation(Point2D point, double pivotX, double pivotY, double angleRad)
        {
            if (Math.Abs(angleRad) < 1e-9) return point;

            double dx = point.X - pivotX;
            double dy = point.Y - pivotY;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            return new Point2D(
                pivotX + dx * cos - dy * sin,
                pivotY + dx * sin + dy * cos);
        }

        private void EnsureWorkpieceCalibrated()
        {
            if (!Calibration.IsWorkpieceCenterCalibrated)
            {
                throw new InvalidOperationException(
                    "工件圆心尚未校准，请先调用 CalibrateWorkpieceByEdgePoints 或 SetWorkpieceCenter。");
            }
        }

        private void EnsureCameraCalibrated()
        {
            if (!Calibration.IsCameraOffsetCalibrated)
            {
                throw new InvalidOperationException(
                    "相机偏移尚未校准，请先调用 CalibrateCameraToPunchOffset。");
            }
        }

        private void SaveToConfig()
        {
            try
            {
                var config = _configService.CurrentConfig;
                config.CoordinateCalibration.CameraToPunchOffsetX = Calibration.CameraToPunchOffsetX;
                config.CoordinateCalibration.CameraToPunchOffsetY = Calibration.CameraToPunchOffsetY;
                config.CoordinateCalibration.CameraAtWorkpieceCenterX = Calibration.CameraAtWorkpieceCenterX;
                config.CoordinateCalibration.CameraAtWorkpieceCenterY = Calibration.CameraAtWorkpieceCenterY;
                config.CoordinateCalibration.CameraToWorkpieceRotationRad = Calibration.CameraToWorkpieceRotationRad;
                _configService.SaveWithArchive(config);
                _logger.Debug("坐标校准参数已持久化到 config.json");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "坐标校准参数持久化失败");
            }
        }

        private static CoordinateCalibration LoadFromConfig(CoordinateCalibrationData data)
        {
            return new CoordinateCalibration
            {
                CameraToPunchOffsetX = data.CameraToPunchOffsetX,
                CameraToPunchOffsetY = data.CameraToPunchOffsetY,
                CameraAtWorkpieceCenterX = data.CameraAtWorkpieceCenterX,
                CameraAtWorkpieceCenterY = data.CameraAtWorkpieceCenterY,
                CameraToWorkpieceRotationRad = data.CameraToWorkpieceRotationRad,
            };
        }
    }
}
