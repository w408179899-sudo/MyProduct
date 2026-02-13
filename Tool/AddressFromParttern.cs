using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool
{
    public class AddressFromParttern
    {
        public struct Pattern
        {
            public byte[] Bytes;
            public bool[] Wildcards;
        }

        public static Pattern ParsePattern(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            var tokens = pattern
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var bytes = new List<byte>(tokens.Length);
            var wildcards = new List<bool>(tokens.Length);

            foreach (var token in tokens)
            {
                var t = token.Trim();
                if (t == "?" || t == "??")
                {
                    bytes.Add(0);
                    wildcards.Add(true);
                    continue;
                }

                if (t.Length != 2)
                {
                    throw new FormatException("Invalid pattern token: " + token);
                }

                bytes.Add(Convert.ToByte(t, 16));
                wildcards.Add(false);
            }

            return new Pattern
            {
                Bytes = bytes.ToArray(),
                Wildcards = wildcards.ToArray()
            };
        }

        public static int FindPattern(byte[] data, Pattern pattern, int startIndex = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (pattern.Bytes == null || pattern.Wildcards == null)
            {
                throw new ArgumentException("Pattern is not initialized.", nameof(pattern));
            }

            int patternLength = pattern.Bytes.Length;
            if (patternLength == 0 || data.Length < patternLength) return -1;
            if (startIndex < 0) startIndex = 0;

            int lastStart = data.Length - patternLength;
            for (int i = startIndex; i <= lastStart; i++)
            {
                bool match = true;
                for (int j = 0; j < patternLength; j++)
                {
                    if (!pattern.Wildcards[j] && data[i + j] != pattern.Bytes[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return i;
            }

            return -1;
        }

        // Scan a 64-bit address range using a caller-provided read function.
        // read: function that returns up to "size" bytes starting at "address".
        // Returns 0 if not found.
        public static ulong FindPatternInRange(
            Func<ulong, int, byte[]> read,
            ulong startAddress,
            ulong endAddressExclusive,
            string patternString,
            int chunkSize = 0x10000)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (startAddress >= endAddressExclusive) return 0;
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            var pattern = ParsePattern(patternString);
            int patternLength = pattern.Bytes.Length;
            if (patternLength == 0) return 0;
            if (chunkSize < patternLength)
            {
                chunkSize = patternLength;
            }

            ulong address = startAddress;
            int overlap = patternLength - 1;

            while (address < endAddressExclusive)
            {
                int toRead = (int)Math.Min((ulong)chunkSize, endAddressExclusive - address);
                var buffer = read(address, toRead);
                if (buffer == null || buffer.Length == 0)
                {
                    address += (ulong)toRead;
                    continue;
                }

                int index = FindPattern(buffer, pattern);
                if (index >= 0)
                {
                    return address + (ulong)index;
                }

                ulong advance = (ulong)buffer.Length;
                if (overlap > 0 && advance > (ulong)overlap)
                {
                    advance -= (ulong)overlap;
                }
                else if (advance == 0)
                {
                    advance = 1;
                }

                address += advance;
            }

            return 0;
        }

        public static ulong FindPatternInModules(
            Vmmsharp.VmmProcess process,
            IEnumerable<string> moduleNames,
            string patternString,
            out string foundModule,
            int chunkSize = 0x10000)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (moduleNames == null) throw new ArgumentNullException(nameof(moduleNames));

            foundModule = null;
            foreach (var moduleName in moduleNames)
            {
                if (string.IsNullOrWhiteSpace(moduleName)) continue;

                if (!TryGetModuleRange(process, moduleName, out ulong baseAddress, out ulong size))
                {
                    continue;
                }

                ulong address = FindPatternInRange(
                    (addr, sizeToRead) => SafeMemRead(process, addr, sizeToRead),
                    baseAddress,
                    baseAddress + size,
                    patternString,
                    chunkSize);

                if (address != 0)
                {
                    foundModule = moduleName;
                    return address;
                }
            }

            return 0;
        }

        private static byte[] SafeMemRead(Vmmsharp.VmmProcess process, ulong address, int size)
        {
            try
            {
                return process.MemRead(address, (uint)size);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static bool TryGetModuleRange(
            Vmmsharp.VmmProcess process,
            string moduleName,
            out ulong baseAddress,
            out ulong size)
        {
            baseAddress = 0;
            size = 0;

            // Use reflection to access MapModuleFromName return type (internal).
            var mapMethod = typeof(Vmmsharp.VmmProcess).GetMethod("MapModuleFromName", new[] { typeof(string) });
            if (mapMethod != null)
            {
                object module = mapMethod.Invoke(process, new object[] { moduleName });
                if (module != null)
                {
                    var moduleType = module.GetType();
                    bool isValid = ReadFieldValue<bool>(module, moduleType, "fValid");
                    if (isValid)
                    {
                        ulong vaBase = ReadFieldValue<ulong>(module, moduleType, "vaBase");
                        uint cbImageSize = ReadFieldValue<uint>(module, moduleType, "cbImageSize");
                        if (vaBase != 0 && cbImageSize != 0)
                        {
                            baseAddress = vaBase;
                            size = cbImageSize;
                            return true;
                        }
                    }
                }
            }

            // Fallback: use GetModuleBase and estimate size via sections.
            baseAddress = process.GetModuleBase(moduleName);
            if (baseAddress == 0)
            {
                return false;
            }

            ulong estimatedSize = 0;
            try
            {
                var sections = process.MapModuleSection(moduleName);
                if (sections != null)
                {
                    foreach (var section in sections)
                    {
                        uint va = GetSectionField<uint>(section, "VirtualAddress");
                        uint vsz = GetSectionField<uint>(section, "MiscPhysicalAddressOrVirtualSize");
                        ulong end = (ulong)va + vsz;
                        if (end > estimatedSize) estimatedSize = end;
                    }
                }
            }
            catch
            {
                estimatedSize = 0;
            }

            if (estimatedSize == 0)
            {
                return false;
            }

            size = estimatedSize;
            return true;
        }

        private static T ReadFieldValue<T>(object instance, Type type, string fieldName)
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (field == null) return default(T);
            return (T)field.GetValue(instance);
        }

        private static T GetSectionField<T>(object section, string fieldName)
        {
            var type = section.GetType();
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (field == null) return default(T);
            return (T)field.GetValue(section);
        }
    }
}
