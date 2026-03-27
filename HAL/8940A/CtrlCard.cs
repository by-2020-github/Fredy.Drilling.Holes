using System;
using System.Collections.Generic;
using System.Text;



namespace Demo
{   
    class CtrlCard
    {
        Int32 Result = -1;
        const Int32 MAXAXIS=4;
        public  Int32 m_cardno = 0;
/*******************初始化函数************************

    该函数中包含了控制卡初始化常用的库函数，这是调用
    
    其他函数的基础，所以必须在示例程序中最先调用
    
    返回值<=0表示初始化失败，返回值>0表示初始化成功

*****************************************************/
 public  Int32 Init_Board()
{

    Result = adt8940a1.adt8940a1_initial();         //卡初始化函数    

	if (Result <= 0) return Result;
 

    return Result;

}


/**********************设置速度模块***********************

    依据参数的值，判断是匀速还是加减速
	
	设置轴的初始速度、驱动速度和加速度

    参数：axis   -轴号

	      startv -初始速度

		  speed  -驱动速度

          add    -加速度
		  
		 tacc   -加速时间
    
    返回值=0正确，返回值=1错误

*********************************************************/
public  Int32 Setup_Speed(Int32 axis, Int32 startv, Int32 speed, Int32 add )
{	
	if (startv - speed >= 0) //匀速运动
	{
        Result = adt8940a1.adt8940a1_set_startv(m_cardno, axis, startv);

        adt8940a1.adt8940a1_set_speed(m_cardno, axis, startv);

	}
	else                    //加减速运动
	{
        Result = adt8940a1.adt8940a1_set_startv(m_cardno, axis, startv);

        adt8940a1.adt8940a1_set_speed(m_cardno, axis, speed);

        adt8940a1.adt8940a1_set_acc(m_cardno, axis, add / 125);  
		
	}
	
	return Result;

}
/*********************单轴驱动函数**********************

    该函数用于驱动单个运动轴运动

    参数： axis-轴号，value-输出脉冲数
    
    返回值=0正确，返回值=1错误

*******************************************************/
public  Int32 Axis_Pmove(Int32 axis, Int32 value)
{
    Result = adt8940a1.adt8940a1_pmove(m_cardno, axis, value);
	
	return Result;

}

/*******************任意两轴插补函数********************

    该函数用于驱动任意两轴进行插补运动

    参数：axis1,axis2-轴号、value1,value2-脉冲数
    
    返回值=0正确，返回值=1错误

*******************************************************/
public  Int32 Int32erp_Move2(Int32 axis1, Int32 axis2, Int32 value1, Int32 value2)
{
    Result = adt8940a1.adt8940a1_inp_move2(m_cardno, axis1, axis2, value1, value2);

	return Result;

}
/*******************任意三轴插补函数********************

    该函数用于驱动任意三轴进行插补运动

    参数：axis1,axis2,axis3-轴号、value1,value2,value3-脉冲数
    
    返回值=0正确，返回值=1错误

*******************************************************/
public  Int32 Int32erp_Move3(Int32 axis1, Int32 axis2, Int32 axis3, Int32 value1, Int32 value2, Int32 value3)
{
    Result = adt8940a1.adt8940a1_inp_move3(m_cardno, axis1, axis2, axis3, value1, value2, value3);

	return Result;

}

/*******************四轴插补函数****************************

    该函数用于驱动XYZW四轴进行插补运动
    
	参数：value1,value2,value3,value4-输出脉冲数

    返回值=0正确，返回值=1错误

***********************************************************/
public  Int32 Int32erp_Move4(Int32 value1, Int32 value2, Int32 value3, Int32 value4)
{
    Result = adt8940a1.adt8940a1_inp_move4(m_cardno, value1, value2, value3, value4);

	return Result;

}

/************************停止轴驱动***********************

    该函数用于立即或减速停止轴的驱动

	参数：axis-轴号、mode-减速方式(0－立即停止, 1－减速停止)
    
    返回值=0正确，返回值=1错误

************************************************************/
public  Int32 StopRun(Int32 axis, Int32 mode)
{
	if (mode == 0)        //立即停止

        Result = adt8940a1.adt8940a1_sudden_stop(m_cardno, axis);
        
    else                 //减速停止

        Result = adt8940a1.adt8940a1_dec_stop(m_cardno, axis);
        
    return Result;

}
/*****************获取轴的驱动状态**************************

    该函数用于获取单轴的驱动状态或插补驱动状态

    参数：axis-轴号，value-状态指针(0-驱动结束，非0－正在驱动)
	  
		  mode(0-获取单轴驱动状态，1－获取插补驱动状态)
    
    返回值=0正确，返回值=1错误

************************************************************/
public  Int32 Get_Status(Int32 axis,  out Int32  value, Int32 mode)
{
	if (mode==0)          //获取单轴驱动状态

        Result = adt8940a1.adt8940a1_get_status(m_cardno, axis, out value);

	else                  //获取插补驱动状态

        Result = adt8940a1.adt8940a1_get_inp_status(m_cardno, out value);

	return Result;

}

/*****************获取运动信息******************************

    该函数用于反馈轴当前的逻辑位置，实际位置和运行速度

    参数：axis-轴号,LogPos-逻辑位置,ActPos-实际位置,Speed-运行速度
    
    返回值=0正确，返回值=1错误

************************************************************/
public  Int32 Get_CurrentInf(Int32 axis, out Int32 LogPos, out Int32 ActPos, out Int32 Speed )
{
    Result = adt8940a1.adt8940a1_get_command_pos(m_cardno, axis, out LogPos);

    adt8940a1.adt8940a1_get_actual_pos(m_cardno, axis, out ActPos);

    adt8940a1.adt8940a1_get_speed(m_cardno, axis, out Speed);	

	return Result;

}

/***********************读取输入点*******************************

     该函数用于读取单个输入点

     参数：number-输入点(0 ~ 39)

     返回值：0 － 低电平，1 － 高电平，-1 － 错误

****************************************************************/
public  Int32 Read_Input(Int32 number)
{
    Result = adt8940a1.adt8940a1_read_bit(m_cardno, number);
    
	return Result;
}

/*********************输出单点函数******************************

    该函数用于输出单点信号

    参数： number-输出点(0 ~ 15),value 0-低电平、1－高电平

    返回值=0正确，返回值=1错误
****************************************************************/

public  Int32 Write_Output(Int32 number, Int32 value)
{
    Result = adt8940a1.adt8940a1_write_bit(m_cardno, number, value);

	return Result;

}
/*******************设置位置计数器*******************************

     该函数用于设置逻辑位置和实际位置

     参数：axis-轴号,pos-设置的位置值
	       mode 0－设置逻辑位置,非0－设置实际位置

     返回值=0正确，返回值=1错误
****************************************************************/
	 
public  Int32 Setup_Pos(Int32 axis, Int32 pos, Int32 mode)
{
	if(mode==0)
	{
        Result = adt8940a1.adt8940a1_set_command_pos(m_cardno, axis, pos);
	}
	else
	{
        Result = adt8940a1.adt8940a1_set_actual_pos(m_cardno, axis, pos);
	}

	return Result;

}

/********************获取版本信息************************
      
	该函数用于获取硬件版本和函数库版本

	参数：LibVer－库版本号,HardwareVer - 硬件版本号

*********************************************************/
public  void Get_Version(out float LibVer, out float HardwareVer)
{
	Int32  Ver;
    Ver = adt8940a1.adt8940a1_get_lib_version(m_cardno);	
    LibVer=Convert.ToSingle(Ver);
    HardwareVer = adt8940a1.adt8940a1_get_hardware_ver(m_cardno);
}

/********************设置脉冲输出方式**********************
      
	该函数用于设置脉冲的工作方式

	参数：axis-轴号， value-脉冲方式 0－脉冲＋脉冲方式 1－脉冲＋方向方式

    返回值=0正确，返回值=1错误

    默认脉冲方式为脉冲＋方向方式

    本程序采用默认的正逻辑脉冲和方向输出信号正逻辑

*********************************************************/
public  Int32 Setup_PulseMode(Int32 axis, Int32 value)
{
    Result = adt8940a1.adt8940a1_set_pulse_mode(m_cardno, axis, value, 0, 0);

	return Result;

}
/********************设置限位信号方式**********************

   该函数用于设定正/负方向限位输入nLMT信号的模式

   参数： axis－轴号
          value1   0－正限位有效  1－正限位无效
		  value2   0－负限位有效  1－负限位无效
		  logic    0－低电平有效  1－高电平有效
   默认模式为：正限位有效、负限位有效、低电平有效

   返回值=0正确，返回值=1错误
  *********************************************************/
public  Int32 Setup_LimitMode(Int32 axis, Int32 value1, Int32 value2, Int32 logic)
{
    Result = adt8940a1.adt8940a1_set_limit_mode(m_cardno, axis, value1, value2, logic);

	return Result;

}
/********************设置stop0信号方式**********************

   该函数用于设定stop0信号的模式

   参数： axis－轴号
          value   0－无效  1－有效
		  logic   0－低电平有效  1－高电平有效
   默认模式为：无效

   返回值=0正确，返回值=1错误
  *********************************************************/
public  Int32 Setup_Stop0Mode(Int32 axis, Int32 value, Int32 logic)
{
    Result = adt8940a1.adt8940a1_set_stop0_mode(m_cardno, axis, value, logic);

	return Result;

}
/********************设置stop1信号方式**********************

   该函数用于设定stop1信号的模式

   参数： axis－轴号
          value   0－无效  1－有效
		  logic   0－低电平有效  1－高电平有效
   默认模式为：无效

   返回值=0正确，返回值=1错误
  *********************************************************/
public  Int32 Setup_Stop1Mode(Int32 axis, Int32 value, Int32 logic)
{
    Result = adt8940a1.adt8940a1_set_stop1_mode(m_cardno, axis, value, logic);

	return Result;

}

/********************设置硬件停止模式**********************

   该函数用于设定硬件停止信号的模式

   参数： value   0－无效		 1－有效
		  logic   0－低电平有效  1－高电平有效
   默认模式为：无效

   返回值=0正确，返回值=1错误

   硬件停止信号固定使用P3端子板34引脚(IN31)
  *********************************************************/
public  Int32 Setup_HardStop(Int32 value, Int32 logic)
{
    Result = adt8940a1.adt8940a1_set_suddenstop_mode(m_cardno, value, logic);

	return Result;

}
/********************设置延时**********************

   该函数用于设定延时

   参数： time - 延时时间(单位为us)

   返回值=0正确，返回值=1错误

  *********************************************************/
public  Int32 Setup_Delay(Int32 time)
{
    Result = adt8940a1.adt8940a1_set_delay_time(m_cardno, time * 8);

	return Result;

}
/********************获取延时状态**********************

   该函数获取延时状态

   返回值   0－延时结束    1－延时进行中

  *********************************************************/
public  Int32 Get_DelayStatus()
{
    Result = adt8940a1.adt8940a1_get_delay_status(m_cardno);

	return Result;
}


/*****************************单轴相对运动*********************
*功能:参照当前位置,以加减速进行定量移动
*参数:
      cardno-卡号
	  axis---轴号
	  pulse--脉冲
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)	  
返回值         0：正确          1：错误
*******************************************************************/
public  Int32 Sym_RelativeMove(Int32 axis, Int32 pulse, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_relative_move(m_cardno, axis, pulse, lspd, hspd, tacc);

    return Result;
}

/***************************单轴绝对移动************************
*功能:参照零点位置,以加减速进行定量移动
*参数:
      cardno-卡号
	  axis---轴号
	  pulse--脉冲
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)  
返回值         0：正确          1：错误
********************************************************************/
public  Int32 Sym_AbsoluteMove(Int32 axis, Int32 pulse, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_absolute_move(m_cardno, axis, pulse, lspd, hspd, tacc);

    return Result;
}
/**********************两轴直线插补相对移动********************
*功能:参照当前位置,以加减速进行直线插补
*参数:
      cardno-卡号
	  axis1---轴号1
	  axis2---轴号2	
	  pulse1--脉冲1
	  pulse2--脉冲2
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)	  
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Sym_RelativeLine2(Int32 axis1, Int32 axis2, Int32 pulse1, Int32 pulse2, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_relative_line2(m_cardno, axis1, axis2, pulse1, pulse2, lspd, hspd, tacc);

    return Result;
}

/********************两轴直线插补绝对移动**********************
*功能:参照零点位置,以加减速进行直线插补
*参数:
      cardno-卡号
	  axis1---轴号1
	  axis2---轴号2	
	  pulse1--脉冲1
	  pulse2--脉冲2
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)	  
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Sym_AbsoluteLine2(Int32 axis1, Int32 axis2, Int32 pulse1, Int32 pulse2, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_absolute_line2(m_cardno, axis1, axis2, pulse1, pulse2, lspd, hspd, tacc);

    return Result;
}

/**********************三轴直线插补相对运动********************
*功能:参照当前位置,以加减速进行直线插补
*参数:
      cardno-卡号
	  axis1---轴号1
	  axis2---轴号2	
	  axis3---轴号3	
	  pulse1--脉冲1
	  pulse2--脉冲2
	  pulse3--脉冲3
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒) 
	  
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Sym_RelativeLine3(Int32 axis1, Int32 axis2, Int32 axis3, Int32 pulse1, Int32 pulse2, Int32 pulse3, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_relative_line3(m_cardno, axis1, axis2, axis3, pulse1, pulse2, pulse3, lspd, hspd, tacc);

    return Result;
}


/*********************三轴直线插补绝对运动*********************
功能:参照零点位置,以加减速进行直线插补
参数:
      cardno-卡号
	  axis1---轴号1
	  axis2---轴号2	
	  axis3---轴号3
	  pulse1--脉冲1
	  pulse2--脉冲2
	  pulse3--脉冲3
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)	  
	  
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Sym_AbsoluteLine3(Int32 axis1, Int32 axis2, Int32 axis3, Int32 pulse1, Int32 pulse2, Int32 pulse3, Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_absolute_line3(m_cardno, axis1, axis2, axis3, pulse1, pulse2, pulse3, lspd, hspd, tacc);

    return Result;
}

/*****************四轴直线插补相对运动****************
*功能:参照当前位置,以加减速进行直线插补
*参数:
      cardno-卡号	  
	  pulse1--脉冲1
	  pulse2--脉冲2
	  pulse3--脉冲3
	  pulse4--脉冲4
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)
******************************************************/
public  Int32  Sym_RelativeLine4(Int32 pulse1, Int32 pulse2, Int32 pulse3,  Int32 pulse4,Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_relative_line4(m_cardno, pulse1, pulse2, pulse3, pulse4, lspd, hspd, tacc);
    
	return Result; 
}

/*****************四轴对称直线插补绝对运动****************
*功能:参照零点位置,以对称加减速进行直线插补
*参数:
      cardno-卡号	 
	  pulse1--脉冲1
	  pulse2--脉冲2
	  pulse3--脉冲3
	  pulse4--脉冲4
	  lspd---低速
	  hspd---高速
      tacc---加速时间(单位:秒)
******************************************************/
public  Int32 Sym_AbsoluteLine4(Int32 pulse1, Int32 pulse2, Int32 pulse3, Int32 pulse4,Int32 lspd ,Int32 hspd, double tacc)
{
    Result = adt8940a1.adt8940a1_symmetry_absolute_line4(m_cardno, pulse1, pulse2, pulse3, pulse4, lspd, hspd, tacc);
	
	return Result; 
}

/*****************获取输出点************************************
功能：获取输出点
参数：
	cardno	    卡号
	number		输出点
返回值			返回值:指定端口的当前状态,-1表示参数错误  
*****************************************************/
public  Int32 Get_OutNum(Int32 number)
{
    Result = adt8940a1.adt8940a1_get_out(m_cardno, number);

	return Result; 
}

/************************手动定量驱动***********************
功能:启用手动定量驱动功能
参数:
	axis         轴号
	pulse        脉冲
返回值         0：正确          1：错误
***********************************************************/
public  Int32 Manu_Pmove(Int32 axis, Int32 pulse)
{
    Result = adt8940a1.adt8940a1_manual_pmove(m_cardno, axis, pulse);

	return Result;
}

/************************手动连续驱动***********************
功能:启用手动连续驱动功能
参数:
	axis         轴号
返回值         0：正确          1：错误
***********************************************************/
public  Int32 Manu_Continue(Int32 axis)
{
    Result = adt8940a1.adt8940a1_manual_continue(m_cardno, axis);

	return Result;
}

/************************关闭手动驱动***********************
功能:关闭外部信号驱动
参数:
	axis         轴号
返回值         0：正确          1：错误
***********************************************************/
public  Int32 Manu_Disable(Int32 axis)
{
    Result = adt8940a1.adt8940a1_manual_disable(m_cardno, axis);

	return Result;
}

/****************************位置锁存设置函数**********************
功能:设置到位信号功能,锁定所有轴的逻辑位置和实际位置
参数:
	axis—参照轴
	mode--锁存模式    |0:无效
					  |1:有效 
	regi—计数器模式  |0:逻辑位置
					  |1:实际位置 
	logical—电平信号 |0:上升沿 
				      |1:下降沿
返回值         0：正确          1：错误
	说明:使用指定轴axis的IN信号作为触发信号						  
*******************************************************************/
public  Int32 Setup_LockPosition(Int32 axis,Int32 mode,Int32 regi,Int32 logical)
{
    Result = adt8940a1.adt8940a1_set_lock_position(m_cardno, axis, mode, regi, logical);
	
	return Result;
} 

/*************************获取锁存状态***********************
功能:获取同步操作的状态
参数:
	axis         轴号
	v           0|未执行同步操作
			    1|执行过同步操作
返回值         0：正确          1：错误
	说明:利用该函数可以捕捉位置锁存是否执行		
******************************************************************/
public  Int32 Get_LockStatus(Int32 axis,out Int32 v)
{
    Result = adt8940a1.adt8940a1_get_lock_status(m_cardno, axis, out v);

	  return Result;
}

/**************************获取锁定的位置**************************
功能:获取锁定的位置
参数:
	axis         轴号
	pos         锁存的位置
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Get_LockPosition(Int32 axis,out Int32 pos)
{
    Result = adt8940a1.adt8940a1_get_lock_position(m_cardno, axis, out pos);
	
    return Result;
}

/**************************清除锁存状态**************************
功能:清除锁存状态
参数:
	axis         轴号(1-4)
返回值         0：正确          1：错误
******************************************************************/
public  Int32 Clr_LockPosition(Int32 axis)
{
    Result = adt8940a1.adt8940a1_clr_lock_status(m_cardno, axis);
	
    return Result;
}
    }
}
