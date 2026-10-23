using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace pk3DS.Core.NX;
public static class SwitchSeed
{
    public static ulong New64()=>BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8))&long.MaxValue;
    public static uint New32()=>BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(4));
}
