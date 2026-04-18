using System;

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
        LiftToHeightSafe = 4,   // Z轴起到抬起高度
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
}
