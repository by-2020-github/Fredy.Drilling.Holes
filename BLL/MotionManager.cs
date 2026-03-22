using Common.Models;
using HAL;
using System;

namespace BLL
{
    public class MotionManager
    {
        public AxisParam XAxis { get; set; }
        public AxisParam YAxis { get; set; }
        public AxisParam ZAxis { get; set; }

        public required IMoton Moton { get; set; }

        public MotionManager(IMoton motion)
        {
            Moton = motion ?? throw new ArgumentNullException(nameof(motion));
        }

        public void HomeAll(bool wait)
        {
        
        }

        public void MoveX(double position, double velocity,bool wait = true)
        {
        }
   

        public void MoveY(double position, double velocity, bool wait = true)
        {
        }
     

        public void MoveZ(double position, double velocity, bool wait = true)
        {
        }

        public void MoveToPunchPoint(PunchPoint punchPoint)
        {
            
        }

    }
}
