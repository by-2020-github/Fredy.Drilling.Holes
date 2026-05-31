# 冲孔参考Z与下压公式调整说明

## 1. 背景

- 机台坐标系约定为：Z 轴向下为负方向。
- 全部工件类型共用一个绝对安全位 `AppConfig.PunchSafeZ`，回安全位时直接移动到该绝对坐标。
- 自动冲孔开始前，会移动到第一个孔左侧 1mm 的测试点进行表面预探，并把该 Z 作为本轮冲孔的基准表面 Z。
- 原实现中，状态机先计算 `commandDepth = compensation + configuredDepth`，硬件层再执行 `targetZ = currentZ + commandDepth`。
- 在“向下为负方向”的坐标系下，如果 `configuredDepth` 采用正值表示下压深度，则上述做法会天然偏向把目标 Z 算向更大的值，语义不稳定。

## 2. 原公式问题

### 2.1 原流程

1. 参考表面 Z：`referenceSurfaceZ`
2. 当前孔位补偿：`compensation = localSurfaceZ - referenceSurfaceZ`
3. 状态机给出：`commandDepth = compensation + configuredDepth`
4. 硬件层执行：`targetZ = currentZ + commandDepth`

### 2.2 问题点

- `referenceSurfaceZ` 只参与补偿差值计算，没有直接参与最终冲孔终点计算。
- 硬件层按“当前 Z + 深度值”二次换算，导致状态机和硬件层对同一个量的语义不一致。
- 在向下为负方向时，`configuredDepth > 0` 并不自然对应“目标 Z 更负”。
- 二道冲孔原先未使用 `SecondPunchDepth`，实际下压量不完整。
- 头道多段冲孔原先逐段直接使用单段值，没有按累计深度计算绝对终点，不符合“第一次 400，第二次 400+200”这类啄钻语义。

## 3. 建议公式

在保留补偿定义不变的前提下：

1. `compensation = localSurfaceZ - referenceSurfaceZ`
2. `localSurfaceZ = referenceSurfaceZ + compensation`
3. `punchTargetZ = localSurfaceZ - configuredDepth`

其中：

- `configuredDepth` 使用正值表示“向下的深度量”。
- 因为向下为负方向，所以真正的绝对冲孔终点要用 `localSurfaceZ - configuredDepth`。
- 硬件层应直接接收 `punchTargetZ`，执行绝对定位，不再自行做 `currentZ + depth` 二次换算。

## 4. 本次代码调整

### 4.1 全局 safeZ 与参考 Z

- 全局安全位保存到：
  - `AppConfig.PunchSafeZ`
- 校准页测试冲孔成功后，将触发 Z 保存到：
  - `AppConfig.HasWorkpieceReferenceZ`
  - `AppConfig.WorkpieceReferenceZ`
- 自动冲孔启动时仍会进行首孔左侧预探；预探得到的 Z 会覆盖本轮流程内的基准表面 Z。
- 主界面创建 `PunchStateMachine` 时，会注入：
  - `SafeZ`
  - `FastToSafeZSpeed`
  - `PunchDownSpeed`
  - `HasInitialSurfaceReference`
  - `InitialSurfaceReferenceZ`

### 4.2 状态机公式调整

- `PunchStateMachine` 现在在状态机内计算绝对冲孔终点 Z。
- 自动冲孔正式开始前，在第一个孔左侧测试点进行表面预探：
  - 默认偏移：`SurfaceProbeOffsetX = -1mm`
  - `SurfaceProbeOffsetY = 0mm`
  - 预探结果只作为基准表面 Z，不作为最近邻 SurfaceZ 样本。
- 当已建立参考 Z 时：
  - 先根据最近邻采样或最新表面值得到 `localSurfaceZ`
  - 再计算 `absoluteTargetZ = localSurfaceZ - configuredDepth`
  - 调用硬件层按绝对目标位执行
- 第一个正式孔的第一刀冲孔过程中会探测并记录当前点 `SurfaceZ`；后续孔位根据欧式距离寻找最近的 `SurfaceZ` 样本进行补偿。
- 每一刀冲孔完成后都会按 `FastToSafeZSpeed` 回到全局绝对 `SafeZ`。
- 头道和二道正式冲孔下压速度统一使用 `PunchDownSpeed`。
- 当尚未建立参考 Z 时：
  - 保留兜底相对下压模式
  - 相对下压量按负方向处理：`-stepDepth`

### 4.3 头道与二道深度修正

- 头道多段冲孔改为累计深度执行：
  - 第 1 次：`depth1`
  - 第 2 次：`depth1 + depth2`
  - 第 3 次：`depth1 + depth2 + depth3`
- 二道冲孔改为使用 `RecipeProcessParameters.SecondPunchDepth` 作为下压深度。

### 4.4 首孔探测参数接入

- 自动冲孔流程启动时，从配置注入：
  - `FastMovePos -> FastApproachDistance`
  - `FastMoveSpeed -> FastApproachSpeed`
  - `SlowMoveDist -> SlowDetectDistance`
  - `SlowMoveSpeed -> SlowDetectSpeed`
  - `SurfaceProbeOffsetX / SurfaceProbeOffsetY -> 首孔左侧预探偏移`
  - `SurfaceDetectionMode -> Latch 或 IoPolling`
  - `SurfaceDetectInputPort / SurfaceDetectInputLowActive -> IO 轮询探测参数`

### 4.5 硬件层职责调整

- `IHardwareController` 新增了探测速度参数：
  - `FastMoveZ(distance, speed)`
  - `SlowMoveZ(distance, speed)`
- `IHardwareController` 新增了表面探测方法：
  - `ProbeSurface(...)`：开工前测试点探面，检测到后立即停止 Z。
- `SurfaceDetectionService` 统一承接锁存与 IO 轮询探测逻辑，自动冲孔硬件层与相机冲孔偏移校准页共用该实现。
- `PunchDown(commandValue, isAbsoluteTarget, speed)` 现支持两种模式：
  - `isAbsoluteTarget = true`：按绝对目标 Z 执行
  - `isAbsoluteTarget = false`：按相对位移执行兜底逻辑
- `PunchDown(..., detectSurface: true, ...)` 用于正式冲孔过程中记录首点首刀 SurfaceZ：
  - 锁存模式：根据锁存状态和锁存值确定 `SurfaceZ`。
  - IO 模式：移动过程中轮询 IO，首次触发时记录当前 Z，但不停止本次冲孔。

## 5. 当前实现后的实际语义

### 5.1 本轮已有预探基准 Z

- 当前孔位表面估计值来自：
  - 最近邻表面采样，或
  - 最新表面值
- 冲孔终点计算为：
  - `targetZ = localSurfaceZ - configuredDepth`
- 这时：
  - 表面越低，目标 Z 越低
  - 设定深度越大，目标 Z 越负

### 5.2 没有参考 Z

- 如果首孔探测成功，会建立新的参考表面 Z。
- 如果没有参考 Z 且流程仍继续执行，则临时退化为相对下压模式。
- 该兜底路径仅用于兼容旧流程，不建议作为长期工艺依赖。

## 6. 涉及文件

- `BLL/AutoPunchMachine.cs`
- `BLL/Hardware/IHardwareController.cs`
- `BLL/Hardware/Adt8940Controller.cs`
- `BLL/Hardware/MockHardwareController.cs`
- `Fredy/ViewModels/MainViewModel.cs`
- `Fredy/Windows/CameraPunchOffsetCalibration/CameraPunchOffsetCalibrationViewModel.cs`
- `Fredy/Windows/ParamConfig/ConfigModels.cs`

## 7. 后续建议

- 参数配置窗口已增加“冲孔表面探测”页签，可编辑 `SurfaceDetectionMode`、`SurfaceDetectInputPort`、`SurfaceDetectInputLowActive`、全局安全位、预探偏移、回 `SafeZ` 速度和正式冲孔下压速度。
- 如果二道冲孔需要独立速度参数，可继续把 `RecipeProcessParameters` 或 `AppConfig.SecondPass` 中的速度配置接入 `PunchStateMachine`。
- 当全局参考 Z 已稳定可用后，可考虑去掉“无参考 Z 时的兜底相对下压”，避免工艺语义分叉。