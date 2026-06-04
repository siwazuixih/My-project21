using System;
using System.Text;
using UnityEngine;

/// <summary>
/// 仙工（Robokit）底盘 TCP 通讯协议底层工具类
/// 专门处理 16 字节包头、大小端转换（Big-Endian）和 JSON 数据打包
/// </summary>
public static class RobokitProtocol
{
    /// <summary>
    /// 封包：将 API 编号和 JSON 字符串，打包成底盘能看懂的二进制数据流
    /// </summary>
    /// <param name="apiNum">API编号（例如：平动是3055，重定位是2002）</param>
    /// <param name="jsonPayload">具体的JSON字符串（如果没有参数传 null 或 ""）</param>
    /// <param name="seqNumber">流水序号（默认1，其实底盘不强校验这个，但会原样返回）</param>
    /// <returns>可以直接扔给 networkStream.Write 的字节数组</returns>
    public static byte[] Pack(ushort apiNum, string jsonPayload, ushort seqNumber = 1)
    {
        // 1. 处理 JSON 数据体
        byte[] jsonBytes = string.IsNullOrEmpty(jsonPayload) ? new byte[0] : Encoding.UTF8.GetBytes(jsonPayload);
        uint dataLen = (uint)jsonBytes.Length;

        // 2. 初始化 16 字节的包头
        byte[] header = new byte[16];
        
        // 第 0 字节：同步头 (固定 0x5A)
        header[0] = 0x5A; 
        
        // 第 1 字节：版本号 (通常 0x01)
        header[1] = 0x01; 

        // 第 2-3 字节：序号 (转大端)
        byte[] seqBytes = BitConverter.GetBytes(seqNumber);
        if (BitConverter.IsLittleEndian) Array.Reverse(seqBytes); // C# 默认小端，必须反转！
        Array.Copy(seqBytes, 0, header, 2, 2);

        // 第 4-7 字节：数据长度 (转大端) —— 这是最容易掉坑的地方！
        byte[] lenBytes = BitConverter.GetBytes(dataLen);
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
        Array.Copy(lenBytes, 0, header, 4, 4);

        // 第 8-9 字节：API 编号 (转大端)
        byte[] apiBytes = BitConverter.GetBytes(apiNum);
        if (BitConverter.IsLittleEndian) Array.Reverse(apiBytes);
        Array.Copy(apiBytes, 0, header, 8, 2);

        // 第 10-15 字节：保留区 6 个字节，默认全是 0。由于 new byte[16] 初始就是 0，这里不用写。

        // 3. 拼接包头和包体
        byte[] fullPacket = new byte[16 + dataLen];
        Array.Copy(header, 0, fullPacket, 0, 16); // 先塞入 16 字节包头
        if (dataLen > 0)
        {
            Array.Copy(jsonBytes, 0, fullPacket, 16, dataLen); // 再塞入 JSON 数据
        }

        return fullPacket;
    }

    /// <summary>
    /// 解包第一步：读取 16 字节包头，解析出后面 JSON 数据的长度
    /// </summary>
    /// <param name="header">从 TCP 流中硬读出的前 16 个字节</param>
    /// <returns>后面跟着的 JSON 数据体字节数</returns>
    public static uint ParsePayloadLength(byte[] header)
    {
        if (header.Length < 16) return 0;
        
        if (header[0] != 0x5A)
        {
            Debug.LogError("收到错误包头：同步帧不是 0x5A，数据可能已错位！");
            return 0;
        }

        // 提取第 4-7 字节，反转回小端，转成 uint
        byte[] lenBytes = new byte[4];
        Array.Copy(header, 4, lenBytes, 0, 4);
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);

        return BitConverter.ToUInt32(lenBytes, 0);
    }
}