using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Models
{
    /// <summary>
    ///     冲孔坐标
    /// </summary>
    public class PunchPoint
    {
        /// <summary>
        /// X 物理坐标
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y 物理坐标
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 孔所属的圈号
        /// </summary>
        public int RingNumber { get; set; }

        /// <summary>
        /// 该孔在当前圈内的序号
        /// </summary>
        public int SequenceIndex { get; set; }

        public override string ToString()
        {
            return $"圈:{RingNumber}, 序号:{SequenceIndex}, 坐标:({X}, {Y})";
        }

    }
}
