using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vmmsharp;

namespace Tool
{
    public class Program
    {
        static void Main(string[] args)
        {
            // Optional: load native MemProcFS libs if they are not already on PATH.
            var memProcFsPath = Environment.GetEnvironmentVariable("MEMPROCFS_HOME");
            if (string.IsNullOrWhiteSpace(memProcFsPath))
            {
                var defaultPath = @"C:\MemProcFS";
                if (System.IO.Directory.Exists(defaultPath))
                {
                    memProcFsPath = defaultPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(memProcFsPath))
            {
                Vmm.LoadNativeLibrary(memProcFsPath);
            }

            var vmmArgs = (args != null && args.Length > 0)
                ? args
                : BuildVmmArgsFromEnv();

            try
            {
                using (var vmm = new Vmm(vmmArgs))
                {
                    var processName = Environment.GetEnvironmentVariable("VMM_PROCESS") ?? "Aion.bin";
                    var process = vmm.Process(processName);

                    if (!process.IsValid)
                    {
                        Console.Error.WriteLine("Target process not found: " + processName);
                        return;
                    }





                    Console.WriteLine("Connected to process: " + process.Name + " (PID " + process.PID + ")");

                    var moduleName = Environment.GetEnvironmentVariable("VMM_MODULE") ?? "Game.dll";
                    ulong moduleBase = process.GetModuleBase(moduleName);
                    if (moduleBase == 0)
                    {
                        Console.Error.WriteLine("Module not found: " + moduleName);
                        return;
                    }

                    var pPlayer = moduleBase + 0xD1B110;

                    while (!Console.KeyAvailable)
                    {
                        // 3. 直接读取坐标字节数组
                        byte[] zBuf = process.MemRead(pPlayer + 0x7C, 4); // 高度
                        byte[] yBuf = process.MemRead(pPlayer + 0x74, 4); // 南北   北走- 南走+
                        byte[] xBuf = process.MemRead(pPlayer + 0x78, 4); // 东西   西+ 东-
                        byte[] hpBuf = process.MemRead(pPlayer + 0x1A0, 4); //血量


                        if (zBuf != null && yBuf != null && xBuf != null)
                        {
                            float posZ = BitConverter.ToSingle(zBuf, 0);
                            float posY = BitConverter.ToSingle(yBuf, 0);
                            float posX = BitConverter.ToSingle(xBuf, 0);
                            float hp = BitConverter.ToSingle(hpBuf, 0);

                            // 使用 \r 实现单行刷新
                            Console.WriteLine($"\rX: {posX,10:F3} | Y: {posY,10:F3} | Z: {posZ,10:F3}    ");
                        }
                        else
                        {
                            Console.WriteLine("\n[-] 读取失败，请检查游戏是否闪退。");
                            break;
                        }

                        Thread.Sleep(50); // 100ms 刷新一次
                    }




                    Console.WriteLine("Module base: " + moduleName + " = 0x" + moduleBase.ToString("X"));

                    var moduleName2 = Environment.GetEnvironmentVariable("VMM_MODULE2") ?? "CryEntitySystem.dll";
                    ulong moduleBase2 = process.GetModuleBase(moduleName2);
                    if (moduleBase2 == 0)
                    {
                        Console.Error.WriteLine("Module not found: " + moduleName2);
                    }
                    else
                    {
                        Console.WriteLine("Module base: " + moduleName2 + " = 0x" + moduleBase2.ToString("X"));
                    }



















                    //4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8 0F 84 5D 06 00 00 66 66 0F 1F 84 00 00 00 00 00 48 8B 0D ?? ?? ?? ?? 48 85 C9 0F 84 E4 05 00 00 48 8B 01 0F B7 53 ?? FF 50 ?? 48 8B F0 48 85 C0 0F 84 CE 05 00 00 48 8B 10
                    //48 89 5C 24 10 56 48 83 EC 20 FF 81 ?? ?? ?? ?? 0F B7 DA 4C 8B 41 ?? 48 8B F1 49 8B D0 49 8B 40 ?? 80 78 ?? 00 75 18 66 39 58 ?? 73 06 48 8B 40 ?? EB 06 48 8B D0 48 8B 00 80 78 ?? 00 74 E8 49 3B D0 74 06 66 3B 5A ?? 73 03 49 8B D0
                    //var pattern = "48 8B C8 44 38 60 ?? 74 F1 48 3B 1D ?? ?? ?? ?? 0F 85 64 FD FF FF 4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8 0F 84 5D 06 00 00 66 66 0F 1F 84 00 00 00 00 00 48 8B 0D ?? ?? ?? ?? 48 85 C9 0F 84 E4 05 00 00 48 8B 01 0F B7 53 ?? FF 50 ?? 48 8B F0 48 85 C0 0F 84 CE 05 00 00";
                    //var pattern = "4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8 0F 84 5D 06 00 00 66 66 0F 1F 84 00 00 00 00 00 48 8B 0D ?? ?? ?? ?? 48 85 C9 0F 84 E4 05 00 00 48 8B 01 0F B7 53 ?? FF 50 ?? 48 8B F0 48 85 C0 0F 84 CE 05 00 00 48 8B 10";
                    //var pattern = "48 8B C8 44 38 60 ?? 74 ?? 48 3B 1D ?? ?? ?? ?? 0F 85 ?? ?? ?? ?? 4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8 0F 84 ?? ?? ?? ?? 66 66 0F 1F 84 00 00 00 00 00 48 8B 0D ?? ?? ?? ?? 48 85 C9 0F 84 ?? ?? ?? ?? 48 8B 01 0F B7 53 ?? FF 50 ?? 48 8B F0 48 85 C0 0F 84 ?? ?? ?? ??";
                    //var pattern = "48 89 5C 24 10 56 48 83 EC 20 FF 81 ?? ?? ?? ?? 0F B7 DA 4C 8B 41 ?? 48 8B F1";// 二叉树
                    var pattern = "48 8B C8 44 38 60 ?? 74 F1 48 3B 1D ?? ?? ?? ?? 0F 85 ?? ?? ?? ?? 4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8";// 二叉数入口
                    //var pattern = "48 8B C8 44 38 60 ?? 74 F1 48 3B 1D ?? ?? ?? ?? 0F 85 64 FD FF FF 4C 8D 35 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 18 48 3B D8 0F 84 5D 06 00 00 66 66 0F 1F 84 00 00 00 00 00 48 8B 0D ?? ?? ?? ?? 48 85 C9 0F 84 E4 05 00 00 48 8B 01 0F B7 53 ?? FF 50 ?? 48 8B F0 48 85 C0 0F 84 CE 05 00 00";// 二叉数入口

                    var scanModules = new[] { "CryEntitySystem.dll", "Aion.bin", "Game.dll" };
                    string foundModule;
                    ulong patternAddress = AddressFromParttern.FindPatternInModules(
                        process,
                        scanModules,
                        pattern,
                        out foundModule);

                    if (patternAddress == 0)
                    {
                        Console.Error.WriteLine("Pattern not found in modules: " + string.Join(", ", scanModules));
                        return;
                    }

                    Console.WriteLine("Pattern found in " + foundModule + " at 0x" + patternAddress.ToString("X"));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Vmm connection failed: " + ex.Message);
            }
        }

        private static string[] BuildVmmArgsFromEnv()
        {
            var device = Environment.GetEnvironmentVariable("VMM_DEVICE");
            if (string.IsNullOrWhiteSpace(device))
            {
                device = "fpga";
            }

            var remote = Environment.GetEnvironmentVariable("VMM_REMOTE");
            if (string.IsNullOrWhiteSpace(remote))
            {
                return new[] { "-device", device };
            }

            return new[] { "-device", device, "-remote", remote };
        }

        private static bool TryReadUInt64(VmmProcess process, ulong address, out ulong value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 8);
                if (buffer == null || buffer.Length < 8)
                {
                    return false;
                }

                value = BitConverter.ToUInt64(buffer, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadUInt32(VmmProcess process, ulong address, out uint value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 4);
                if (buffer == null || buffer.Length < 4)
                {
                    return false;
                }

                value = BitConverter.ToUInt32(buffer, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadPointer(VmmProcess process, ulong address, out ulong value)
        {
            value = 0;
            if (TryReadUInt64(process, address, out ulong v64) && IsLikelyUserPointer(v64))
            {
                value = v64;
                return true;
            }

            if (TryReadUInt32(process, address, out uint v32) && v32 != 0)
            {
                value = v32;
                return true;
            }

            return false;
        }

        private static bool IsLikelyUserPointer(ulong value)
        {
            return value != 0 && value <= 0x00007FFFFFFFFFFFUL;
        }
    }
}



//ulong pPlayer = moduleBase + 0xD1B110;

//byte[] zBuf = process.MemRead(pPlayer + 0x7C, 4); // 高度
//byte[] yBuf = process.MemRead(pPlayer + 0x74, 4); // 南北   北走- 南走+
//byte[] xBuf = process.MemRead(pPlayer + 0x78, 4); // 东西   西+ 东-


//if (zBuf.Length < 4 || yBuf.Length < 4 || xBuf.Length < 4)
//{
//    Console.Error.WriteLine("Failed to read player coordinates.");
//    return;
//}

//float z = BitConverter.ToSingle(zBuf, 0);
//float y = BitConverter.ToSingle(yBuf, 0);
//float x = BitConverter.ToSingle(xBuf, 0);


//Console.WriteLine("Player position: X=" + x + " Y=" + y + " Z=" + z);
