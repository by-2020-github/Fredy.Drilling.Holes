using System;

namespace BLL
{
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
}
