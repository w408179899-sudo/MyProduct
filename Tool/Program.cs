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
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Keep the default console encoding if this host does not allow changing it.
            }

            //var runKmBoxDigitOneDemo = true;
            //if (runKmBoxDigitOneDemo)
            //{
            //    RunKmBoxDigitOneLoop();
            //    return;
            //}




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
                    var processName = Environment.GetEnvironmentVariable("VMM_PROCESS") ?? "Aion.bin";//Aion.bin
                    var process = vmm.Process(processName);

                    if (!process.IsValid)
                    {
                        Console.Error.WriteLine("Target process not found: " + processName);
                        return;
                    }





                    Console.WriteLine("Connected to process: " + process.Name + " (PID " + process.PID + ")");

                    var moduleName = Environment.GetEnvironmentVariable("VMM_MODULE") ?? "Game.dll";
                    ulong gameBase = process.GetModuleBase(moduleName);
                    if (gameBase == 0)
                    {
                        Console.Error.WriteLine("Module not found: " + moduleName);
                        return;
                    }

                    Console.WriteLine("Module base: " + moduleName + " = 0x" + gameBase.ToString("X"));

                    var aionTestMode = Environment.GetEnvironmentVariable("AION_TEST_MODE") ?? "skills";
                    if (string.Equals(aionTestMode, "player", StringComparison.OrdinalIgnoreCase))
                    {
                        RunLocalPlayerInfoTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "gather", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "gathers", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "gatherlist", StringComparison.OrdinalIgnoreCase))
                    {
                        RunGatherListTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "abnormal", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "abnormals", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "debuff", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "status", StringComparison.OrdinalIgnoreCase))
                    {
                        RunAbnormalStatusTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "inventory", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "items", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "bag", StringComparison.OrdinalIgnoreCase))
                    {
                        RunInventoryListTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "skills", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "skilllist", StringComparison.OrdinalIgnoreCase))
                    {
                        RunSkillListTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "target", StringComparison.OrdinalIgnoreCase))
                    {
                        RunLockedTargetMonsterInfoTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "monsters", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "monsterlist", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "list", StringComparison.OrdinalIgnoreCase))
                    {
                        RunMonsterListTest(process, gameBase);
                        return;
                    }

                    if (!string.Equals(aionTestMode, "0", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(aionTestMode, "legacy", StringComparison.OrdinalIgnoreCase))
                    {
                        RunMonsterListTest(process, gameBase);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Vmm connection failed: " + ex.Message);
            }
        }

        private static async Task RunKmBoxDigitOneLoop()
        {
            string portName = Environment.GetEnvironmentVariable("KMBOX_PORT");
            if (string.IsNullOrWhiteSpace(portName))
            {
                portName = "COM8";
            }

            int intervalMs = 1;
            string intervalText = Environment.GetEnvironmentVariable("KMBOX_KEY_INTERVAL_MS");
            if (!string.IsNullOrWhiteSpace(intervalText))
            {
                int parsed;
                if (int.TryParse(intervalText, out parsed) && parsed > 0)
                {
                    intervalMs = parsed;
                }
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = portName }))
            {
                km.Open();
                Console.WriteLine("KmBox key demo started on " + portName + ".");
                Console.WriteLine("Sending key '1' repeatedly. Press any key to stop.");


                await km.MouseUpAsync(KmMouseButton.Right);
                await km.MouseDownAsync(KmMouseButton.Right);


                km.MoveRelativeHumanLike(500, 0);



                //while (!Console.KeyAvailable)
                //{
                //    km.MoveRelative(20, 0);
                //    //km.KeyPress(0x31);
                //    Thread.Sleep(intervalMs);
                //    await km.MouseWheelAsync(-1);
                //}

                Console.ReadKey(true);
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

        private const ulong EntitySystemPointerRva = 0x904690;
        private const ulong ServerObjectTreeRva = 0xD21740;
        private const ulong LocalEntityIdRva = 0xD21798;
        private const ulong CurrentMaxHpRva = 0xD267DC;
        private const ulong CurrentHpRva = 0xD267E0;
        private const ulong CurrentMaxMpRva = 0xD267E4;
        private const ulong CurrentMpRva = 0xD267E8;
        private const ulong CurrentDpRva = 0xD267EE;
        private const ulong CameraPitchRva = 0xD1AD14;
        private const ulong CameraRollRva = 0xD1AD18;
        private const ulong CameraYawRva = 0xD1AD1C;
        private const ulong PrimaryPartyListRva = 0xD1BAE8;
        private const ulong SecondaryPartyListRva = 0xD1BB50;
        private const ulong SkillManagerGlobalRva = 0xD004A0;
        private const ulong InventoryManagerGlobalRva = 0xD004A0;

        private const ulong EntityTreeOffset = 0x58;
        private const ulong NodeLeftOffset = 0x00;
        private const ulong NodeParentOffset = 0x08;
        private const ulong NodeRightOffset = 0x10;
        private const ulong NodeIsNilOffset = 0x19;
        private const ulong NodeIdOffset = 0x20;
        private const ulong NodeEntityOffset = 0x28;
        private const ulong EntityTypeOffset = 0xF2;
        private const ulong EntityPositionFlagsOffset = 0xC0;
        private const uint EntityUseAlternatePositionFlag = 0x400;
        private const ulong EntityWorldPositionOffset = 0x4B4;
        private const ulong EntityWorldAnglesOffset = 0x4E8;
        private const ulong EntityLocalPositionOffset = 0x4F4;
        private const ulong EntityLocalAnglesOffset = 0x500;
        private const ulong ServerNodeServerObjectIdOffset = 0x1C;
        private const ulong ServerNodeEntityIdOffset = 0x20;
        private const ushort EntityTypeNpc = 3;
        private const ulong EntityProxyManagerVfuncOffset = 0xB8;

        private const ulong ActorEntityOffset = 0x08;
        private const ulong ActorObjectTypeOffset = 0x20;
        private const ulong ActorServerObjectIdOffset = 0x2C;
        private const ulong ActorNpcTemplateIdOffset = 0x30;
        private const ulong ActorLevelOffset = 0x3E;
        private const ulong ActorHpPercentOffset = 0x40;
        private const ulong ActorNameOffset = 0x42;
        private const ulong ActorTargetServerObjectIdOffset = 0x358;
        private const ulong ActorAbnormalBeginOffset = 0xF18;
        private const ulong ActorAbnormalEndOffset = 0xF20;
        private const ulong ActorAbnormalCapacityOffset = 0xF28;
        private const ulong ActorAbnormalCategory0CountOffset = 0xF30;
        private const ulong ActorBuffCountOffset = 0xF34;
        private const ulong ActorPhysicalAbnormalCountOffset = 0xF38;
        private const ulong ActorMentalAbnormalCountOffset = 0xF3C;
        private const ulong ActorMaxHpOffset = 0x11A0;
        private const ulong ActorCurrentHpOffset = 0x11A4;

        private const uint GatherObjectType = 7;
        private const ulong GatherSourceIdOffset = 0x30;
        private const ulong GatherDisplayLevelOffset = 0x3E;
        private const ulong GatherStateOrRemainingOffset = 0x40;
        private const ulong GatherNameOffset = 0x42;
        private const ulong GatherInteractionRadiusOffset = 0x168;
        private const ulong GatherSpawnPositionOffset = 0x19C;

        private const uint AbnormalCategoryPhysical = 2;
        private const ulong AbnormalEntrySize = 0x12;
        private const ulong AbnormalEntryField00Offset = 0x00;
        private const ulong AbnormalEntryIdOffset = 0x04;
        private const ulong AbnormalEntryDispelCategoryOffset = 0x08;
        private const ulong AbnormalEntryTimeOrSourceOffset = 0x0C;
        private const ulong AbnormalEntryLevelOrStackOffset = 0x10;

        private const ulong PartyListNodeDataOffset = 0x10;
        private const ulong PartyMemberServerObjectIdOffset = 0x04;
        private const ulong PartyMemberDataFlagsOffset = 0x37;
        private const byte PartyMemberHasAbnormalBlockFlag = 0x08;
        private const ulong PartyMemberAbnormalCountOffset = 0x77;
        private const ulong PartyMemberAbnormalEntriesOffset = 0x79;
        private const ulong PartyMemberUpdateTimeOffset = 0x859;
        private const int PartyMemberMaxAbnormalCount = 112;

        private const ulong LearnedSkillTreeOffset = 0x828;
        private const ulong LearnedSkillOuterSkillIdOffset = 0x20;
        private const ulong LearnedSkillOuterLevelTreeHeaderOffset = 0x28;
        private const ulong LearnedSkillOuterLevelTreeSizeOffset = 0x30;
        private const ulong LearnedSkillInnerLevelOffset = 0x20;
        private const ulong LearnedSkillInnerItemListHeaderOffset = 0x28;
        private const ulong LearnedSkillInnerItemListSizeOffset = 0x30;
        private const ulong ListNodeNextOffset = 0x00;
        private const ulong ListNodePrevOffset = 0x08;
        private const ulong ListNodeValueOffset = 0x10;

        private const ulong SkillItemSkillIdOffset = 0x08;
        private const ulong SkillItemField0COffset = 0x0C;
        private const ulong SkillItemRankValueOffset = 0x10;
        private const ulong SkillItemNameOffset = 0x18;
        private const ulong SkillItemCooldownDurationOffset = 0x50;
        private const ulong SkillItemCooldownEndTimeOffset = 0x54;
        private const ulong SkillItemItemTypeOffset = 0x58;
        private const ulong SkillItemField5COffset = 0x5C;
        private const ulong SkillItemToggleStateOffset = 0x60;
        private const ulong SkillItemSkillLevelOffset = 0x64;
        private const ulong SkillItemStaticFieldD8Offset = 0x68;
        private const ulong SkillItemRuntimeStateOffset = 0x6C;
        private const ulong SkillItemTimeOrExpiryOffset = 0x70;
        private const ulong SkillItemSourceFlagsOffset = 0x74;
        private const ulong SkillItemField78Offset = 0x78;
        private const ulong SkillItemPseudoTypeOffset = 0x7C;
        private const ulong SkillItemSpecialMetadataOffset = 0x80;

        private const ulong InventoryCapacityOffset = 0x774;
        private const ulong InventoryItemTreeHeaderOffset = 0x778;
        private const ulong InventoryItemTreeCountOffset = 0x780;
        private const ulong InventoryEquipmentIdsOffset = 0x788;
        private const int InventoryEquipmentIdCount = 32;
        private const int InventorySlotsPerPage = 27;

        private const ulong InventoryNodeInstanceIdOffset = 0x20;
        private const ulong InventoryNodeItemOffset = 0x28;
        private const ulong InventoryItemInstanceIdOffset = 0x08;
        private const ulong InventoryItemTemplateIdOffset = 0x0C;
        private const ulong InventoryItemCountOffset = 0x10;
        private const ulong InventoryItemNameOffset = 0x18;
        private const ulong InventoryItemTypeOffset = 0x60;
        private const ulong InventoryItemEquipmentMaskOffset = 0x74;
        private const ulong InventoryItemFlagsOffset = 0x78;
        private const ulong InventoryItemValueOffset = 0x80;
        private const ulong InventoryItemSlotOffset = 0x4EE;
        private const ulong InventoryItemCustomNameOffset = 0x4F4;
        private const ulong InventoryItemExpiryOffset = 0x530;
        private const ulong InventoryItemDurationOffset = 0x548;
        private const ulong InventoryItemExtraStateOffset = 0x550;
        private const uint InventoryCashItemFlag = 0x1000;

        private struct Vec3
        {
            public float X;
            public float Y;
            public float Z;
        }

        private struct EntityTransformSnapshot
        {
            public Vec3 WorldPosition;
            public Vec3 WorldAngles;
            public Vec3 LocalPosition;
            public Vec3 LocalAngles;
        }

        private struct ActorInfo
        {
            public ulong Actor;
            public ulong Entity;
            public uint ObjectType;
            public uint ServerObjectId;
            public uint NpcTemplateId;
            public ushort Level;
            public byte HpPercent;
            public uint TargetServerObjectId;
            public uint MaxHp;
            public uint CurrentHp;
            public string Name;
            public string ResolveSource;
        }

        private struct LocalPlayerInfo
        {
            public ushort EntityId;
            public ushort TargetEntityId;
            public uint CurrentHp;
            public uint MaxHp;
            public uint CurrentMp;
            public uint MaxMp;
            public ushort CurrentDp;
            public ulong EntitySystem;
            public ulong EntityTreeHeader;
            public ulong Entity;
            public bool HasPosition;
            public ulong PositionOffset;
            public float X;
            public float Y;
            public float Z;
            public bool HasTransform;
            public EntityTransformSnapshot Transform;
            public float CameraPitch;
            public float CameraRoll;
            public float CameraYaw;
        }

        private struct LockedTargetMonsterInfo
        {
            public ushort TargetEntityId;
            public bool HasServerObjectId;
            public uint ServerObjectId;
            public ulong ServerObjectTreeHeader;
            public ulong EntitySystem;
            public ulong EntityTreeHeader;
            public ulong Entity;
            public bool HasEntityType;
            public ushort EntityType;
            public bool HasPosition;
            public ulong PositionOffset;
            public float X;
            public float Y;
            public float Z;
            public bool HasTransform;
            public EntityTransformSnapshot Transform;
            public bool HasActor;
            public ActorInfo Actor;
            public bool HasDistance;
            public double DistanceToLocalPlayer;
        }

        private struct MonsterListEntry
        {
            public ushort EntityId;
            public uint ServerObjectId;
            public ulong Entity;
            public ushort EntityType;
            public ulong PositionOffset;
            public float X;
            public float Y;
            public float Z;
            public double DistanceToLocalPlayer;
        }

        private struct GatherListEntry
        {
            public ushort EntityId;
            public uint ServerObjectId;
            public ulong Entity;
            public ulong GatherObject;
            public uint GatherSourceId;
            public ushort DisplayLevel;
            public byte StateOrRemaining;
            public string Name;
            public float InteractionRadius;
            public bool HasPosition;
            public ulong PositionOffset;
            public float X;
            public float Y;
            public float Z;
            public bool HasSpawnPosition;
            public float SpawnX;
            public float SpawnY;
            public float SpawnZ;
            public bool HasDistance;
            public double DistanceToLocalPlayer;
            public bool IsLockedTarget;
            public string ResolveSource;
        }

        private struct AbnormalStatusEntry
        {
            public ulong Address;
            public uint Field00;
            public uint AbnormalId;
            public uint DispelCategory;
            public uint TimeOrSource;
            public ushort LevelOrStack;
        }

        private struct ActorAbnormalStatusSnapshot
        {
            public ulong Actor;
            public ulong Entity;
            public ushort EntityId;
            public uint ObjectType;
            public uint ServerObjectId;
            public string Name;
            public string ResolveSource;
            public bool HasDistance;
            public double DistanceToLocalPlayer;
            public ulong Begin;
            public ulong End;
            public ulong Capacity;
            public uint Category0Count;
            public uint BuffCount;
            public uint PhysicalCount;
            public uint MentalCount;
            public List<AbnormalStatusEntry> Entries;
        }

        private struct PartyMemberAbnormalSnapshot
        {
            public string ListName;
            public ulong Member;
            public uint ServerObjectId;
            public byte DataFlags;
            public bool HasAbnormalBlock;
            public short RawCount;
            public uint UpdateTime;
            public List<AbnormalStatusEntry> Entries;
            public int PhysicalCount;
        }

        private struct LearnedSkillInfo
        {
            public uint SkillId;
            public ushort HighestLevel;
            public ulong SkillItem;
            public string Name;
            public string DisplayBaseName;
            public int DisplayTier;
            public uint Field0C;
            public ulong RankValue;
            public uint CooldownDuration;
            public uint CooldownEndTime;
            public uint ItemType;
            public uint Field5C;
            public uint ToggleState;
            public uint SkillLevel;
            public uint StaticFieldD8;
            public uint RuntimeState;
            public uint TimeOrExpiry;
            public uint SourceFlags;
            public uint Field78;
            public uint PseudoType;
            public uint SpecialMetadata;
            public ulong LevelTreeSize;
            public ulong ItemListSize;
        }

        private struct InventoryItemInfo
        {
            public ulong Address;
            public uint InstanceId;
            public uint TemplateId;
            public long Count;
            public string Name;
            public string CustomName;
            public uint ItemType;
            public uint EquipmentMask;
            public uint Flags;
            public ulong Value;
            public short Slot;
            public int Page;
            public int Cell;
            public int Row;
            public int Column;
            public ulong ExpiryTimeRaw;
            public uint DurationSeconds;
            public uint ExtraState;
            public bool IsInEquipmentArray;
        }

        private struct InventorySnapshot
        {
            public ulong ManagerAddress;
            public uint Capacity;
            public ulong TreeItemCount;
            public uint[] EquipmentInstanceIds;
            public List<InventoryItemInfo> Items;
        }

        private static void RunLocalPlayerInfoTest(VmmProcess process, ulong gameBase)
        {
            Console.WriteLine("AION local player info test from TXT/AION.txt offsets.");
            Console.WriteLine("Press any key to stop.");

            while (!Console.KeyAvailable)
            {
                LocalPlayerInfo info;
                string error;
                if (TryReadLocalPlayerInfo(process, gameBase, out info, out error))
                {
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "EntityId=" + info.EntityId +
                        " TargetId=" + info.TargetEntityId +
                        " Entity=" + FormatAddress(info.Entity) +
                        " HP=" + info.CurrentHp + "/" + info.MaxHp +
                        " (" + FormatPercent(info.CurrentHp, info.MaxHp) + ")" +
                        " MP=" + info.CurrentMp + "/" + info.MaxMp +
                        " (" + FormatPercent(info.CurrentMp, info.MaxMp) + ")" +
                        " DP=" + info.CurrentDp +
                        " Pos=" + FormatPosition(info) +
                        " Transform=" + FormatTransform(info) +
                        " Camera(P/R/Y)=" +
                        info.CameraPitch.ToString("F2") + "/" +
                        info.CameraRoll.ToString("F2") + "/" +
                        info.CameraYaw.ToString("F2"));
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                }

                Thread.Sleep(500);
            }

            Console.ReadKey(true);
        }

        private static void RunGatherListTest(VmmProcess process, ulong gameBase)
        {
            double radius = ReadDoubleFromEnv("AION_GATHER_LIST_RADIUS", 120.0);
            int limit = ReadIntFromEnv("AION_GATHER_LIST_LIMIT", 40);

            Console.WriteLine("AION gather object list test from TXT/AION.txt offsets.");
            Console.WriteLine("Traversing ServerObject tree -> EntitySystem tree -> GameObject, filtering objectType == 7.");
            Console.WriteLine("Radius=" + radius.ToString("F1") + ", Limit=" + limit + ". Press any key to stop.");
            Console.WriteLine("Set AION_TEST_MODE=abnormal for physical abnormal status test.");

            while (!Console.KeyAvailable)
            {
                List<GatherListEntry> entries;
                int scannedServerObjects;
                int resolvedEntities;
                int resolvedGameObjects;
                int gatherObjects;
                string error;

                if (TryReadGatherList(
                    process,
                    gameBase,
                    radius,
                    limit,
                    out entries,
                    out scannedServerObjects,
                    out resolvedEntities,
                    out resolvedGameObjects,
                    out gatherObjects,
                    out error))
                {
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "Rows=" + entries.Count +
                        " ScannedServerObjects=" + scannedServerObjects +
                        " ResolvedEntities=" + resolvedEntities +
                        " ResolvedGameObjects=" + resolvedGameObjects +
                        " GatherObjects=" + gatherObjects);

                    for (int i = 0; i < entries.Count; i++)
                    {
                        Console.WriteLine(FormatGatherListEntry(i + 1, entries[i]));
                    }
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                }

                Thread.Sleep(1500);
            }

            Console.ReadKey(true);
        }

        private static void RunAbnormalStatusTest(VmmProcess process, ulong gameBase)
        {
            bool printAllEntries = ReadBoolFromEnv("AION_ABNORMAL_PRINT_ALL", false);
            bool includeVisible = ReadBoolFromEnv("AION_ABNORMAL_INCLUDE_VISIBLE", true);
            bool includeParty = ReadBoolFromEnv("AION_ABNORMAL_INCLUDE_PARTY", false);
            double radius = ReadDoubleFromEnv("AION_ABNORMAL_VISIBLE_RADIUS", 120.0);
            int limit = ReadIntFromEnv("AION_ABNORMAL_VISIBLE_LIMIT", 40);

            Console.WriteLine("AION physical abnormal status test from TXT/AION.txt offsets.");
            Console.WriteLine("Reading local Actor+0xF38 and Actor+0xF18..0xF20 abnormal entries; physical category is 2.");
            Console.WriteLine("Actor abnormal status only covers visible/loaded actors. VisibleScan=" + (includeVisible ? "yes" : "no") +
                              ", VisibleRadius=" + radius.ToString("F1") +
                              ", VisibleLimit=" + limit +
                              ", IncludePartySnapshot=" + (includeParty ? "yes" : "no") +
                              ", PrintAllEntries=" + (printAllEntries ? "yes" : "no") + ". Press any key to stop.");

            while (!Console.KeyAvailable)
            {
                ActorAbnormalStatusSnapshot local;
                string error;
                if (TryReadLocalActorAbnormalStatus(process, gameBase, out local, out error))
                {
                    Console.WriteLine(FormatActorAbnormalSnapshot("Local", local));
                    PrintAbnormalEntries(local.Entries, printAllEntries);
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Local abnormal read failed: " + error);
                }

                if (includeVisible)
                {
                    List<ActorAbnormalStatusSnapshot> visibleActors;
                    int scannedServerObjects;
                    int resolvedActors;
                    int physicalActors;
                    if (TryReadVisibleActorAbnormalSnapshots(
                        process,
                        gameBase,
                        radius,
                        limit,
                        out visibleActors,
                        out scannedServerObjects,
                        out resolvedActors,
                        out physicalActors,
                        out error))
                    {
                        PrintVisibleActorAbnormalSnapshots(visibleActors, scannedServerObjects, resolvedActors, physicalActors, printAllEntries);
                    }
                    else
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Visible abnormal scan failed: " + error);
                    }
                }

                if (includeParty)
                {
                    List<PartyMemberAbnormalSnapshot> partyMembers;
                    if (TryReadPartyMemberAbnormalSnapshots(process, gameBase, out partyMembers, out error))
                    {
                        PrintPartyAbnormalSnapshots(partyMembers, printAllEntries);
                    }
                    else
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Party abnormal read failed: " + error);
                    }
                }

                Thread.Sleep(1000);
            }

            Console.ReadKey(true);
        }

        private static void RunInventoryListTest(VmmProcess process, ulong gameBase)
        {
            bool includeEquipped = ReadBoolFromEnv("AION_INVENTORY_INCLUDE_EQUIPPED", false);
            int limit = ReadIntFromEnv("AION_INVENTORY_LIMIT", 200);
            int columns = ReadIntFromEnv("AION_BAG_COLUMNS", 9);
            if (columns <= 0 || InventorySlotsPerPage % columns != 0)
            {
                columns = 9;
            }

            Console.WriteLine("AION inventory item list test from TXT/AION.txt offsets.");
            Console.WriteLine("Traversing Game.dll+0x" + InventoryManagerGlobalRva.ToString("X") + " -> InventoryManager+0x" + InventoryItemTreeHeaderOffset.ToString("X") + ".");
            Console.WriteLine("Normal bag only=" + (!includeEquipped ? "yes" : "no") + ". Set AION_INVENTORY_INCLUDE_EQUIPPED=1 to include equipped/non-bag items.");
            Console.WriteLine("Rows/columns assume " + columns + " columns per page; page/cell come directly from slot/27. Limit=" + limit + ".");

            InventorySnapshot snapshot;
            string error;
            if (!TryReadInventorySnapshot(process, gameBase, columns, out snapshot, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey(true);
                return;
            }

            List<InventoryItemInfo> items = snapshot.Items ?? new List<InventoryItemInfo>();
            int totalItems = items.Count;
            int equippedItems = items.Count(IsEquippedInventoryItem);
            int normalBagItems = items.Count(IsNormalBagInventoryItem);

            if (!includeEquipped)
            {
                items = items.Where(IsNormalBagInventoryItem).ToList();
            }

            items.Sort(CompareInventoryItems);

            int usedSlots;
            int freeSlots;
            CountInventorySlots(snapshot.Capacity, snapshot.Items, out usedSlots, out freeSlots);

            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                "Manager=" + FormatAddress(snapshot.ManagerAddress) +
                " Capacity=" + snapshot.Capacity +
                " TreeCount=" + snapshot.TreeItemCount +
                " Items=" + totalItems +
                " BagItems=" + normalBagItems +
                " EquippedLike=" + equippedItems +
                " UsedSlots=" + usedSlots +
                " FreeSlots=" + freeSlots +
                " Showing=" + (limit > 0 && items.Count > limit ? limit : items.Count));

            PrintEquipmentInstanceIds(snapshot.EquipmentInstanceIds);

            int rowCount = items.Count;
            if (limit > 0 && rowCount > limit)
            {
                rowCount = limit;
            }

            for (int i = 0; i < rowCount; i++)
            {
                Console.WriteLine(FormatInventoryItem(i + 1, items[i]));
            }

            Console.WriteLine("Press any key to exit. Set AION_TEST_MODE=skills/target/player/monsters for other tests.");
            Console.ReadKey(true);
        }

        private static void RunSkillListTest(VmmProcess process, ulong gameBase)
        {
            bool groupByName = ReadBoolFromEnv("AION_SKILL_GROUP_BY_NAME", true);
            bool filterUseful = ReadBoolFromEnv("AION_SKILL_FILTER_USEFUL", true);

            Console.WriteLine("AION learned skill list test from TXT/AION.txt offsets.");
            Console.WriteLine("Traversing Game.dll+0x" + SkillManagerGlobalRva.ToString("X") + " -> SkillManager+0x" + LearnedSkillTreeOffset.ToString("X") + ".");
            Console.WriteLine("Each skillId only prints the highest learned level and the last SkillItem in that level list.");
            Console.WriteLine("Display-name grouping=" + (groupByName ? "on" : "off") + ". Set AION_SKILL_GROUP_BY_NAME=0 to print the raw skillId list.");
            Console.WriteLine("Useful-skill filter=" + (filterUseful ? "on" : "off") + ". Set AION_SKILL_FILTER_USEFUL=0 to print passive/system skills too.");

            List<LearnedSkillInfo> skills;
            int outerNodeCount;
            string error;
            if (TryReadHighestLearnedSkills(process, gameBase, out skills, out outerNodeCount, out error))
            {
                int rawSkillCount = skills.Count;
                if (groupByName)
                {
                    skills = SelectHighestDisplaySkillPerName(skills);
                }

                int groupedSkillCount = skills.Count;
                if (filterUseful)
                {
                    skills = FilterUsefulLearnedSkills(skills);
                }

                Console.WriteLine(
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                    "Rows=" + skills.Count +
                    " RawRows=" + rawSkillCount +
                    " GroupedRows=" + groupedSkillCount +
                    " FilteredOut=" + (groupedSkillCount - skills.Count) +
                    " OuterNodes=" + outerNodeCount);

                for (int i = 0; i < skills.Count; i++)
                {
                    Console.WriteLine(FormatLearnedSkill(i + 1, skills[i]));
                }
            }
            else
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
            }

            Console.WriteLine("Press any key to exit. Set AION_TEST_MODE=target/player/monsters for other tests.");
            Console.ReadKey(true);
        }

        private static void RunLockedTargetMonsterInfoTest(VmmProcess process, ulong gameBase)
        {
            Console.WriteLine("AION locked target monster test from TXT/AION.txt offsets.");
            Console.WriteLine("Testing: Game.dll+0xD2179A target EntityId, EntitySystem tree, ServerObject tree, CEntity position.");
            Console.WriteLine("Press any key to stop. Set AION_TEST_MODE=player for local player test, AION_TEST_MODE=monsters for list test, AION_TEST_MODE=0 for old logic.");

            while (!Console.KeyAvailable)
            {
                LockedTargetMonsterInfo info;
                string error;
                if (TryReadLockedTargetMonsterInfo(process, gameBase, out info, out error))
                {
                    if (info.TargetEntityId == 0)
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] No locked target.");
                    }
                    else
                    {
                        Console.WriteLine(
                            "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                            "TargetEntityId=" + info.TargetEntityId +
                            " ServerId=" + FormatServerObjectId(info) +
                            " Entity=" + FormatAddress(info.Entity) +
                            " EntityType=" + FormatEntityType(info) +
                            " NpcLike=" + FormatNpcLike(info) +
                            " Actor=" + FormatActor(info) +
                            " Pos=" + FormatPosition(info) +
                            " Transform=" + FormatTransform(info) +
                            " Distance=" + FormatDistance(info));
                    }
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                }

                //Thread.Sleep(500);
            }

            Console.ReadKey(true);
        }

        private static void RunMonsterListTest(VmmProcess process, ulong gameBase)
        {
            double radius = ReadDoubleFromEnv("AION_MONSTER_LIST_RADIUS", 80.0);
            int limit = ReadIntFromEnv("AION_MONSTER_LIST_LIMIT", 30);

            Console.WriteLine("AION monster/NPC list test from TXT/AION.txt offsets.");
            Console.WriteLine("Traversing ServerObject tree -> EntitySystem tree -> CEntity. CEntity+0xF2 == 3 is treated as NPC/monster-like.");
            Console.WriteLine("Radius=" + radius.ToString("F1") + ", Limit=" + limit + ". Press any key to stop.");
            Console.WriteLine("Set AION_TEST_MODE=target for locked target test, AION_TEST_MODE=player for local player test.");

            while (!Console.KeyAvailable)
            {
                List<MonsterListEntry> entries;
                int scannedServerObjects;
                int resolvedEntities;
                int npcLikeEntities;
                string error;

                if (TryReadMonsterList(
                    process,
                    gameBase,
                    radius,
                    limit,
                    out entries,
                    out scannedServerObjects,
                    out resolvedEntities,
                    out npcLikeEntities,
                    out error))
                {
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "Rows=" + entries.Count +
                        " ScannedServerObjects=" + scannedServerObjects +
                        " ResolvedEntities=" + resolvedEntities +
                        " NpcLike=" + npcLikeEntities);

                    for (int i = 0; i < entries.Count; i++)
                    {
                        MonsterListEntry entry = entries[i];
                        Console.WriteLine(
                            "#" + (i + 1).ToString("00") +
                            " Dist=" + entry.DistanceToLocalPlayer.ToString("F2") +
                            " EntityId=" + entry.EntityId +
                            " ServerId=" + entry.ServerObjectId +
                            " CEntityType=" + entry.EntityType +
                            " Entity=" + FormatAddress(entry.Entity) +
                            " Pos=X=" + entry.X.ToString("F2") +
                            " Y=" + entry.Y.ToString("F2") +
                            " Z=" + entry.Z.ToString("F2") +
                            " Offset=0x" + entry.PositionOffset.ToString("X"));
                    }
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                }

                Thread.Sleep(1500);
            }

            Console.ReadKey(true);
        }

        private static bool TryReadLocalPlayerInfo(
            VmmProcess process,
            ulong gameBase,
            out LocalPlayerInfo info,
            out string error)
        {
            info = new LocalPlayerInfo();
            error = null;

            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out info.EntityId))
            {
                error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
                return false;
            }

            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out info.TargetEntityId))
            {
                error = "failed to read target entity id at Game.dll+0x" + (LocalEntityIdRva + 2).ToString("X");
                return false;
            }

            if (!TryReadUInt32(process, gameBase + CurrentHpRva, out info.CurrentHp) ||
                !TryReadUInt32(process, gameBase + CurrentMaxHpRva, out info.MaxHp) ||
                !TryReadUInt32(process, gameBase + CurrentMpRva, out info.CurrentMp) ||
                !TryReadUInt32(process, gameBase + CurrentMaxMpRva, out info.MaxMp) ||
                !TryReadUInt16(process, gameBase + CurrentDpRva, out info.CurrentDp))
            {
                error = "failed to read HP/MP/DP globals";
                return false;
            }

            if (!TryReadSingle(process, gameBase + CameraPitchRva, out info.CameraPitch) ||
                !TryReadSingle(process, gameBase + CameraRollRva, out info.CameraRoll) ||
                !TryReadSingle(process, gameBase + CameraYawRva, out info.CameraYaw))
            {
                error = "failed to read camera angles";
                return false;
            }

            if (TryReadPointer(process, gameBase + EntitySystemPointerRva, out info.EntitySystem) &&
                TryReadPointer(process, info.EntitySystem + EntityTreeOffset, out info.EntityTreeHeader) &&
                TryFindEntityById(process, info.EntityTreeHeader, info.EntityId, out info.Entity) &&
                TryReadEntityPosition(process, info.Entity, out info.X, out info.Y, out info.Z, out info.PositionOffset))
            {
                info.HasPosition = true;

                EntityTransformSnapshot transform;
                if (TryReadEntityTransform(process, info.Entity, out transform))
                {
                    info.HasTransform = true;
                    info.Transform = transform;
                }
            }

            return true;
        }

        private static bool TryReadLockedTargetMonsterInfo(
            VmmProcess process,
            ulong gameBase,
            out LockedTargetMonsterInfo info,
            out string error)
        {
            info = new LockedTargetMonsterInfo();
            error = null;

            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out info.TargetEntityId))
            {
                error = "failed to read current target entity id at Game.dll+0x" + (LocalEntityIdRva + 2).ToString("X");
                return false;
            }

            if (info.TargetEntityId == 0)
            {
                return true;
            }

            uint serverObjectId;
            ulong serverTreeHeader;
            if (TryFindServerObjectByEntityId(process, gameBase, info.TargetEntityId, out serverObjectId, out serverTreeHeader))
            {
                info.HasServerObjectId = true;
                info.ServerObjectId = serverObjectId;
                info.ServerObjectTreeHeader = serverTreeHeader;
            }
            else
            {
                info.ServerObjectTreeHeader = serverTreeHeader;
            }

            if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out info.EntitySystem))
            {
                error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
                return false;
            }

            if (!TryReadPointer(process, info.EntitySystem + EntityTreeOffset, out info.EntityTreeHeader))
            {
                error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
                return false;
            }

            if (!TryFindEntityById(process, info.EntityTreeHeader, info.TargetEntityId, out info.Entity))
            {
                error = "target entity id " + info.TargetEntityId + " was not found in EntitySystem tree";
                return false;
            }

            ushort entityType;
            if (TryReadUInt16(process, info.Entity + EntityTypeOffset, out entityType))
            {
                info.HasEntityType = true;
                info.EntityType = entityType;
            }

            if (!TryReadEntityPosition(process, info.Entity, out info.X, out info.Y, out info.Z, out info.PositionOffset))
            {
                error = "failed to read target position from dynamic CEntity position offset";
                return false;
            }

            info.HasPosition = true;

            EntityTransformSnapshot transform;
            if (TryReadEntityTransform(process, info.Entity, out transform))
            {
                info.HasTransform = true;
                info.Transform = transform;
            }

            ActorInfo actor;
            if (TryResolveActorFromEntityExperimental(
                process,
                info.Entity,
                info.HasServerObjectId ? info.ServerObjectId : 0,
                out actor))
            {
                info.HasActor = true;
                info.Actor = actor;
            }

            ushort localEntityId;
            ulong localEntity;
            float localX;
            float localY;
            float localZ;
            ulong localPositionOffset;
            if (TryReadUInt16(process, gameBase + LocalEntityIdRva, out localEntityId) &&
                TryFindEntityById(process, info.EntityTreeHeader, localEntityId, out localEntity) &&
                TryReadEntityPosition(process, localEntity, out localX, out localY, out localZ, out localPositionOffset))
            {
                double dx = info.X - localX;
                double dy = info.Y - localY;
                double dz = info.Z - localZ;
                info.HasDistance = true;
                info.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            return true;
        }

        private static bool TryReadMonsterList(
            VmmProcess process,
            ulong gameBase,
            double radius,
            int limit,
            out List<MonsterListEntry> entries,
            out int scannedServerObjects,
            out int resolvedEntities,
            out int npcLikeEntities,
            out string error)
        {
            entries = new List<MonsterListEntry>();
            scannedServerObjects = 0;
            resolvedEntities = 0;
            npcLikeEntities = 0;
            error = null;

            ulong entitySystem;
            if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out entitySystem))
            {
                error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
                return false;
            }

            ulong entityTreeHeader;
            if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out entityTreeHeader))
            {
                error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
                return false;
            }

            ushort localEntityId;
            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out localEntityId))
            {
                error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
                return false;
            }

            ulong localEntity;
            if (!TryFindEntityById(process, entityTreeHeader, localEntityId, out localEntity))
            {
                error = "failed to resolve local entity " + localEntityId + " in EntitySystem tree";
                return false;
            }

            float localX;
            float localY;
            float localZ;
            ulong localPositionOffset;
            if (!TryReadEntityPosition(process, localEntity, out localX, out localY, out localZ, out localPositionOffset))
            {
                error = "failed to read local entity position";
                return false;
            }

            ulong serverTreeHeader;
            if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader) || serverTreeHeader == 0)
            {
                error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X");
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out node))
            {
                error = "failed to read ServerObject tree begin node";
                return false;
            }

            for (int guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
            {
                if (IsNilNode(process, node, serverTreeHeader))
                {
                    break;
                }

                scannedServerObjects++;

                uint serverObjectId;
                ushort entityId;
                if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId) &&
                    TryReadUInt16(process, node + ServerNodeEntityIdOffset, out entityId) &&
                    entityId != 0 &&
                    entityId != localEntityId)
                {
                    ulong entity;
                    if (TryFindEntityById(process, entityTreeHeader, entityId, out entity) && entity != 0)
                    {
                        resolvedEntities++;

                        ushort entityType;
                        if (TryReadUInt16(process, entity + EntityTypeOffset, out entityType) &&
                            entityType == EntityTypeNpc)
                        {
                            npcLikeEntities++;

                            float x;
                            float y;
                            float z;
                            ulong positionOffset;
                            if (TryReadEntityPosition(process, entity, out x, out y, out z, out positionOffset) &&
                                IsReasonablePosition(x, y, z))
                            {
                                double dx = x - localX;
                                double dy = y - localY;
                                double dz = z - localZ;
                                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                                if (radius <= 0 || distance <= radius)
                                {
                                    entries.Add(new MonsterListEntry
                                    {
                                        EntityId = entityId,
                                        ServerObjectId = serverObjectId,
                                        Entity = entity,
                                        EntityType = entityType,
                                        PositionOffset = positionOffset,
                                        X = x,
                                        Y = y,
                                        Z = z,
                                        DistanceToLocalPlayer = distance
                                    });
                                }
                            }
                        }
                    }
                }

                ulong next;
                if (!TryGetNextTreeNode(process, serverTreeHeader, node, out next) || next == node)
                {
                    break;
                }

                node = next;
            }

            entries.Sort(delegate (MonsterListEntry left, MonsterListEntry right)
            {
                return left.DistanceToLocalPlayer.CompareTo(right.DistanceToLocalPlayer);
            });

            if (limit > 0 && entries.Count > limit)
            {
                entries.RemoveRange(limit, entries.Count - limit);
            }

            return true;
        }

        private static bool TryReadGatherList(
            VmmProcess process,
            ulong gameBase,
            double radius,
            int limit,
            out List<GatherListEntry> entries,
            out int scannedServerObjects,
            out int resolvedEntities,
            out int resolvedGameObjects,
            out int gatherObjects,
            out string error)
        {
            entries = new List<GatherListEntry>();
            scannedServerObjects = 0;
            resolvedEntities = 0;
            resolvedGameObjects = 0;
            gatherObjects = 0;
            error = null;

            ulong entitySystem;
            if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out entitySystem))
            {
                error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
                return false;
            }

            ulong entityTreeHeader;
            if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out entityTreeHeader))
            {
                error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
                return false;
            }

            ushort localEntityId;
            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out localEntityId))
            {
                error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
                return false;
            }

            ushort targetEntityId = 0;
            TryReadUInt16(process, gameBase + LocalEntityIdRva + 2, out targetEntityId);

            ulong localEntity;
            float localX = 0;
            float localY = 0;
            float localZ = 0;
            ulong localPositionOffset;
            bool hasLocalPosition =
                TryFindEntityById(process, entityTreeHeader, localEntityId, out localEntity) &&
                TryReadEntityPosition(process, localEntity, out localX, out localY, out localZ, out localPositionOffset);

            ulong serverTreeHeader;
            if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader) || serverTreeHeader == 0)
            {
                error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X");
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out node))
            {
                error = "failed to read ServerObject tree begin node";
                return false;
            }

            for (int guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
            {
                if (IsNilNode(process, node, serverTreeHeader))
                {
                    break;
                }

                scannedServerObjects++;

                uint serverObjectId;
                ushort entityId;
                if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId) &&
                    TryReadUInt16(process, node + ServerNodeEntityIdOffset, out entityId) &&
                    entityId != 0)
                {
                    ulong entity;
                    if (TryFindEntityById(process, entityTreeHeader, entityId, out entity) && entity != 0)
                    {
                        resolvedEntities++;

                        ActorInfo gameObject;
                        if (TryResolveActorFromEntityExperimental(process, entity, serverObjectId, out gameObject))
                        {
                            resolvedGameObjects++;

                            if (gameObject.ObjectType == GatherObjectType)
                            {
                                gatherObjects++;

                                GatherListEntry entry;
                                if (TryReadGatherObjectInfo(
                                    process,
                                    gameObject.Actor,
                                    entity,
                                    entityId,
                                    serverObjectId,
                                    targetEntityId,
                                    hasLocalPosition,
                                    localX,
                                    localY,
                                    localZ,
                                    gameObject.ResolveSource,
                                    out entry))
                                {
                                    if (radius <= 0 ||
                                        !entry.HasDistance ||
                                        entry.DistanceToLocalPlayer <= radius ||
                                        entry.IsLockedTarget)
                                    {
                                        entries.Add(entry);
                                    }
                                }
                            }
                        }
                    }
                }

                ulong next;
                if (!TryGetNextTreeNode(process, serverTreeHeader, node, out next) || next == node)
                {
                    break;
                }

                node = next;
            }

            entries.Sort(delegate (GatherListEntry left, GatherListEntry right)
            {
                if (left.IsLockedTarget != right.IsLockedTarget)
                {
                    return left.IsLockedTarget ? -1 : 1;
                }

                if (left.HasDistance != right.HasDistance)
                {
                    return left.HasDistance ? -1 : 1;
                }

                if (left.HasDistance && right.HasDistance)
                {
                    return left.DistanceToLocalPlayer.CompareTo(right.DistanceToLocalPlayer);
                }

                return left.ServerObjectId.CompareTo(right.ServerObjectId);
            });

            if (limit > 0 && entries.Count > limit)
            {
                entries.RemoveRange(limit, entries.Count - limit);
            }

            return true;
        }

        private static bool TryReadGatherObjectInfo(
            VmmProcess process,
            ulong gatherObject,
            ulong entity,
            ushort entityId,
            uint fallbackServerObjectId,
            ushort targetEntityId,
            bool hasLocalPosition,
            float localX,
            float localY,
            float localZ,
            string resolveSource,
            out GatherListEntry entry)
        {
            entry = new GatherListEntry
            {
                EntityId = entityId,
                Entity = entity,
                GatherObject = gatherObject,
                Name = string.Empty,
                ResolveSource = resolveSource,
                ServerObjectId = fallbackServerObjectId,
                IsLockedTarget = entityId != 0 && entityId == targetEntityId
            };

            uint objectType;
            if (!TryReadUInt32(process, gatherObject + ActorObjectTypeOffset, out objectType) ||
                objectType != GatherObjectType)
            {
                return false;
            }

            uint serverObjectId;
            if (TryReadUInt32(process, gatherObject + ActorServerObjectIdOffset, out serverObjectId) &&
                serverObjectId != 0)
            {
                entry.ServerObjectId = serverObjectId;
            }

            TryReadUInt32(process, gatherObject + GatherSourceIdOffset, out entry.GatherSourceId);
            TryReadUInt16(process, gatherObject + GatherDisplayLevelOffset, out entry.DisplayLevel);
            TryReadByte(process, gatherObject + GatherStateOrRemainingOffset, out entry.StateOrRemaining);
            TryReadSingle(process, gatherObject + GatherInteractionRadiusOffset, out entry.InteractionRadius);

            string name;
            if (TryReadUtf16String(process, gatherObject + GatherNameOffset, 64, out name))
            {
                entry.Name = name;
            }

            if (TryReadEntityPosition(process, entity, out entry.X, out entry.Y, out entry.Z, out entry.PositionOffset) &&
                IsReasonablePosition(entry.X, entry.Y, entry.Z))
            {
                entry.HasPosition = true;
            }

            Vec3 spawn;
            if (TryReadVec3(process, gatherObject + GatherSpawnPositionOffset, out spawn) &&
                IsReasonablePosition(spawn.X, spawn.Y, spawn.Z))
            {
                entry.HasSpawnPosition = true;
                entry.SpawnX = spawn.X;
                entry.SpawnY = spawn.Y;
                entry.SpawnZ = spawn.Z;
            }

            if (hasLocalPosition && (entry.HasPosition || entry.HasSpawnPosition))
            {
                float x = entry.HasPosition ? entry.X : entry.SpawnX;
                float y = entry.HasPosition ? entry.Y : entry.SpawnY;
                float z = entry.HasPosition ? entry.Z : entry.SpawnZ;
                double dx = x - localX;
                double dy = y - localY;
                double dz = z - localZ;
                entry.HasDistance = true;
                entry.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            return true;
        }

        private static bool TryReadLocalActorAbnormalStatus(
            VmmProcess process,
            ulong gameBase,
            out ActorAbnormalStatusSnapshot snapshot,
            out string error)
        {
            snapshot = new ActorAbnormalStatusSnapshot();
            error = null;

            ushort localEntityId;
            if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out localEntityId) || localEntityId == 0)
            {
                error = "failed to read local entity id at Game.dll+0x" + LocalEntityIdRva.ToString("X");
                return false;
            }

            ulong entitySystem;
            ulong entityTreeHeader;
            ulong localEntity;
            if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out entitySystem) ||
                !TryReadPointer(process, entitySystem + EntityTreeOffset, out entityTreeHeader) ||
                !TryFindEntityById(process, entityTreeHeader, localEntityId, out localEntity))
            {
                error = "failed to resolve local CEntity from EntitySystem tree";
                return false;
            }

            uint serverObjectId = 0;
            ulong serverTreeHeader;
            TryFindServerObjectByEntityId(process, gameBase, localEntityId, out serverObjectId, out serverTreeHeader);

            ActorInfo actor;
            if (!TryResolveActorFromEntityExperimental(process, localEntity, serverObjectId, out actor))
            {
                error = "failed to resolve local Actor from local CEntity";
                return false;
            }

            if (!TryReadActorAbnormalStatus(process, actor.Actor, actor.Entity, actor.ServerObjectId, actor.Name, out snapshot))
            {
                error = "failed to read local Actor abnormal status fields";
                return false;
            }

            snapshot.EntityId = localEntityId;
            snapshot.ResolveSource = actor.ResolveSource;
            return true;
        }

        private static bool TryReadVisibleActorAbnormalSnapshots(
            VmmProcess process,
            ulong gameBase,
            double radius,
            int limit,
            out List<ActorAbnormalStatusSnapshot> snapshots,
            out int scannedServerObjects,
            out int resolvedActors,
            out int physicalActors,
            out string error)
        {
            snapshots = new List<ActorAbnormalStatusSnapshot>();
            scannedServerObjects = 0;
            resolvedActors = 0;
            physicalActors = 0;
            error = null;

            ulong entitySystem;
            if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out entitySystem))
            {
                error = "failed to read EntitySystem pointer at Game.dll+0x" + EntitySystemPointerRva.ToString("X");
                return false;
            }

            ulong entityTreeHeader;
            if (!TryReadPointer(process, entitySystem + EntityTreeOffset, out entityTreeHeader))
            {
                error = "failed to read EntitySystem tree header at EntitySystem+0x" + EntityTreeOffset.ToString("X");
                return false;
            }

            ushort localEntityId = 0;
            ulong localEntity;
            float localX = 0;
            float localY = 0;
            float localZ = 0;
            ulong localPositionOffset;
            bool hasLocalPosition =
                TryReadUInt16(process, gameBase + LocalEntityIdRva, out localEntityId) &&
                TryFindEntityById(process, entityTreeHeader, localEntityId, out localEntity) &&
                TryReadEntityPosition(process, localEntity, out localX, out localY, out localZ, out localPositionOffset);

            ulong serverTreeHeader;
            if (!TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader) || serverTreeHeader == 0)
            {
                error = "failed to read ServerObject tree header at Game.dll+0x" + ServerObjectTreeRva.ToString("X");
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out node))
            {
                error = "failed to read ServerObject tree begin node";
                return false;
            }

            for (int guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
            {
                if (IsNilNode(process, node, serverTreeHeader))
                {
                    break;
                }

                scannedServerObjects++;

                uint serverObjectId;
                ushort entityId;
                if (TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId) &&
                    TryReadUInt16(process, node + ServerNodeEntityIdOffset, out entityId) &&
                    entityId != 0 &&
                    entityId != localEntityId)
                {
                    ulong entity;
                    if (TryFindEntityById(process, entityTreeHeader, entityId, out entity) && entity != 0)
                    {
                        ActorInfo actor;
                        if (TryResolveActorFromEntityExperimental(process, entity, serverObjectId, out actor))
                        {
                            resolvedActors++;

                            ActorAbnormalStatusSnapshot snapshot;
                            if (TryReadActorAbnormalStatus(process, actor.Actor, actor.Entity, actor.ServerObjectId, actor.Name, out snapshot))
                            {
                                snapshot.EntityId = entityId;
                                snapshot.ResolveSource = actor.ResolveSource;

                                float x;
                                float y;
                                float z;
                                ulong positionOffset;
                                if (hasLocalPosition &&
                                    TryReadEntityPosition(process, entity, out x, out y, out z, out positionOffset) &&
                                    IsReasonablePosition(x, y, z))
                                {
                                    double dx = x - localX;
                                    double dy = y - localY;
                                    double dz = z - localZ;
                                    snapshot.HasDistance = true;
                                    snapshot.DistanceToLocalPlayer = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                }

                                int physicalEntryCount = CountAbnormalEntriesByCategory(snapshot.Entries, AbnormalCategoryPhysical);
                                bool hasPhysical = snapshot.PhysicalCount != 0 || physicalEntryCount != 0;
                                if (hasPhysical)
                                {
                                    physicalActors++;
                                }

                                if ((radius <= 0 || !snapshot.HasDistance || snapshot.DistanceToLocalPlayer <= radius) &&
                                    hasPhysical)
                                {
                                    snapshots.Add(snapshot);
                                }
                            }
                        }
                    }
                }

                ulong next;
                if (!TryGetNextTreeNode(process, serverTreeHeader, node, out next) || next == node)
                {
                    break;
                }

                node = next;
            }

            snapshots.Sort(delegate (ActorAbnormalStatusSnapshot left, ActorAbnormalStatusSnapshot right)
            {
                if (left.HasDistance != right.HasDistance)
                {
                    return left.HasDistance ? -1 : 1;
                }

                if (left.HasDistance && right.HasDistance)
                {
                    int distanceCompare = left.DistanceToLocalPlayer.CompareTo(right.DistanceToLocalPlayer);
                    if (distanceCompare != 0)
                    {
                        return distanceCompare;
                    }
                }

                return left.ServerObjectId.CompareTo(right.ServerObjectId);
            });

            if (limit > 0 && snapshots.Count > limit)
            {
                snapshots.RemoveRange(limit, snapshots.Count - limit);
            }

            return true;
        }

        private static bool TryReadActorAbnormalStatus(
            VmmProcess process,
            ulong actor,
            ulong entity,
            uint serverObjectId,
            string name,
            out ActorAbnormalStatusSnapshot snapshot)
        {
            snapshot = new ActorAbnormalStatusSnapshot
            {
                Actor = actor,
                Entity = entity,
                ServerObjectId = serverObjectId,
                Name = name ?? string.Empty,
                Entries = new List<AbnormalStatusEntry>()
            };

            if (!IsLikelyUserPointer(actor))
            {
                return false;
            }

            TryReadPointer(process, actor + ActorAbnormalBeginOffset, out snapshot.Begin);
            TryReadPointer(process, actor + ActorAbnormalEndOffset, out snapshot.End);
            TryReadPointer(process, actor + ActorAbnormalCapacityOffset, out snapshot.Capacity);
            TryReadUInt32(process, actor + ActorObjectTypeOffset, out snapshot.ObjectType);
            TryReadUInt32(process, actor + ActorAbnormalCategory0CountOffset, out snapshot.Category0Count);
            TryReadUInt32(process, actor + ActorBuffCountOffset, out snapshot.BuffCount);
            TryReadUInt32(process, actor + ActorPhysicalAbnormalCountOffset, out snapshot.PhysicalCount);
            TryReadUInt32(process, actor + ActorMentalAbnormalCountOffset, out snapshot.MentalCount);

            snapshot.Entries = ReadAbnormalStatusEntries(process, snapshot.Begin, snapshot.End, 256);
            return true;
        }

        private static List<AbnormalStatusEntry> ReadAbnormalStatusEntries(
            VmmProcess process,
            ulong begin,
            ulong end,
            int maxCount)
        {
            var entries = new List<AbnormalStatusEntry>();
            if (!IsLikelyUserPointer(begin) ||
                !IsLikelyUserPointer(end) ||
                end < begin ||
                maxCount <= 0)
            {
                return entries;
            }

            ulong byteLength = end - begin;
            ulong rawCount = byteLength / AbnormalEntrySize;
            if (rawCount > (ulong)maxCount)
            {
                rawCount = (ulong)maxCount;
            }

            for (ulong i = 0; i < rawCount; i++)
            {
                ulong address = begin + i * AbnormalEntrySize;
                AbnormalStatusEntry entry;
                if (TryReadAbnormalStatusEntry(process, address, out entry))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static bool TryReadAbnormalStatusEntry(
            VmmProcess process,
            ulong address,
            out AbnormalStatusEntry entry)
        {
            entry = new AbnormalStatusEntry { Address = address };

            return TryReadUInt32(process, address + AbnormalEntryField00Offset, out entry.Field00) &&
                   TryReadUInt32(process, address + AbnormalEntryIdOffset, out entry.AbnormalId) &&
                   TryReadUInt32(process, address + AbnormalEntryDispelCategoryOffset, out entry.DispelCategory) &&
                   TryReadUInt32(process, address + AbnormalEntryTimeOrSourceOffset, out entry.TimeOrSource) &&
                   TryReadUInt16(process, address + AbnormalEntryLevelOrStackOffset, out entry.LevelOrStack);
        }

        private static bool TryReadPartyMemberAbnormalSnapshots(
            VmmProcess process,
            ulong gameBase,
            out List<PartyMemberAbnormalSnapshot> snapshots,
            out string error)
        {
            snapshots = new List<PartyMemberAbnormalSnapshot>();
            error = null;

            string primaryError;
            ReadPartyMemberAbnormalList(process, gameBase + PrimaryPartyListRva, "primary", snapshots, out primaryError);

            string secondaryError;
            ReadPartyMemberAbnormalList(process, gameBase + SecondaryPartyListRva, "secondary", snapshots, out secondaryError);

            if (snapshots.Count == 0 && primaryError != null && secondaryError != null)
            {
                error = primaryError + "; " + secondaryError;
                return false;
            }

            return true;
        }

        private static bool ReadPartyMemberAbnormalList(
            VmmProcess process,
            ulong listGlobalAddress,
            string listName,
            List<PartyMemberAbnormalSnapshot> snapshots,
            out string error)
        {
            error = null;

            ulong head;
            if (!TryReadPointer(process, listGlobalAddress, out head) || head == 0)
            {
                error = "failed to read " + listName + " party list head at " + FormatAddress(listGlobalAddress);
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, head + ListNodeNextOffset, out node))
            {
                error = "failed to read " + listName + " party list first node";
                return false;
            }

            var visited = new HashSet<ulong>();
            for (int guard = 0; node != 0 && node != head && guard < 256; guard++)
            {
                if (!visited.Add(node))
                {
                    break;
                }

                ulong member;
                if (TryReadPointer(process, node + PartyListNodeDataOffset, out member) && member != 0)
                {
                    PartyMemberAbnormalSnapshot snapshot;
                    if (TryReadPartyMemberAbnormalSnapshot(process, member, listName, out snapshot))
                    {
                        snapshots.Add(snapshot);
                    }
                }

                ulong next;
                if (!TryReadPointer(process, node + ListNodeNextOffset, out next) || next == node)
                {
                    break;
                }

                node = next;
            }

            return true;
        }

        private static bool TryReadPartyMemberAbnormalSnapshot(
            VmmProcess process,
            ulong member,
            string listName,
            out PartyMemberAbnormalSnapshot snapshot)
        {
            snapshot = new PartyMemberAbnormalSnapshot
            {
                ListName = listName,
                Member = member,
                Entries = new List<AbnormalStatusEntry>()
            };

            if (!IsLikelyUserPointer(member))
            {
                return false;
            }

            TryReadUInt32(process, member + PartyMemberServerObjectIdOffset, out snapshot.ServerObjectId);
            TryReadByte(process, member + PartyMemberDataFlagsOffset, out snapshot.DataFlags);
            snapshot.HasAbnormalBlock = (snapshot.DataFlags & PartyMemberHasAbnormalBlockFlag) != 0;
            TryReadInt16(process, member + PartyMemberAbnormalCountOffset, out snapshot.RawCount);
            TryReadUInt32(process, member + PartyMemberUpdateTimeOffset, out snapshot.UpdateTime);

            int count = snapshot.RawCount;
            if (count < 0)
            {
                count = 0;
            }
            else if (count > PartyMemberMaxAbnormalCount)
            {
                count = PartyMemberMaxAbnormalCount;
            }

            ulong entriesAddress = member + PartyMemberAbnormalEntriesOffset;
            for (int i = 0; i < count; i++)
            {
                AbnormalStatusEntry entry;
                if (TryReadAbnormalStatusEntry(process, entriesAddress + (ulong)i * AbnormalEntrySize, out entry))
                {
                    snapshot.Entries.Add(entry);
                    if (entry.DispelCategory == AbnormalCategoryPhysical)
                    {
                        snapshot.PhysicalCount++;
                    }
                }
            }

            return snapshot.ServerObjectId != 0 ||
                   snapshot.RawCount != 0 ||
                   snapshot.HasAbnormalBlock;
        }

        private static bool TryReadInventorySnapshot(
            VmmProcess process,
            ulong gameBase,
            int columns,
            out InventorySnapshot snapshot,
            out string error)
        {
            snapshot = new InventorySnapshot
            {
                EquipmentInstanceIds = new uint[InventoryEquipmentIdCount],
                Items = new List<InventoryItemInfo>()
            };
            error = null;

            ulong manager;
            if (!TryReadPointer(process, gameBase + InventoryManagerGlobalRva, out manager) || manager == 0)
            {
                error = "failed to read InventoryManager pointer at Game.dll+0x" + InventoryManagerGlobalRva.ToString("X");
                return false;
            }

            snapshot.ManagerAddress = manager;

            uint capacity;
            if (TryReadUInt32(process, manager + InventoryCapacityOffset, out capacity))
            {
                snapshot.Capacity = capacity;
            }

            ulong treeCount;
            if (TryReadUInt64(process, manager + InventoryItemTreeCountOffset, out treeCount))
            {
                snapshot.TreeItemCount = treeCount;
            }

            snapshot.EquipmentInstanceIds = ReadInventoryEquipmentInstanceIds(process, manager);

            ulong header;
            if (!TryReadPointer(process, manager + InventoryItemTreeHeaderOffset, out header) || header == 0)
            {
                error = "failed to read inventory item tree header at InventoryManager+0x" + InventoryItemTreeHeaderOffset.ToString("X");
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, header + NodeLeftOffset, out node))
            {
                error = "failed to read inventory item tree begin node";
                return false;
            }

            var visited = new HashSet<ulong>();
            int guardLimit = snapshot.TreeItemCount > 0 && snapshot.TreeItemCount < 100000
                ? checked((int)snapshot.TreeItemCount + 16)
                : 100000;

            for (int guard = 0; node != 0 && node != header && guard < guardLimit; guard++)
            {
                if (!visited.Add(node) || IsNilNode(process, node, header))
                {
                    break;
                }

                InventoryItemInfo item;
                if (TryReadInventoryItemFromNode(process, node, columns, snapshot.EquipmentInstanceIds, out item))
                {
                    snapshot.Items.Add(item);
                }

                ulong next;
                if (!TryGetNextTreeNode(process, header, node, out next) || next == node)
                {
                    break;
                }

                node = next;
            }

            return true;
        }

        private static bool TryReadInventoryItemFromNode(
            VmmProcess process,
            ulong node,
            int columns,
            uint[] equipmentInstanceIds,
            out InventoryItemInfo info)
        {
            info = new InventoryItemInfo
            {
                Name = string.Empty,
                CustomName = string.Empty,
                Page = -1,
                Cell = -1,
                Row = -1,
                Column = -1
            };

            uint nodeInstanceId;
            ulong item;
            if (!TryReadUInt32(process, node + InventoryNodeInstanceIdOffset, out nodeInstanceId) ||
                !TryReadPointer(process, node + InventoryNodeItemOffset, out item) ||
                item == 0)
            {
                return false;
            }

            uint instanceId;
            if (!TryReadUInt32(process, item + InventoryItemInstanceIdOffset, out instanceId) ||
                instanceId == 0 ||
                instanceId != nodeInstanceId)
            {
                return false;
            }

            info.Address = item;
            info.InstanceId = instanceId;
            info.IsInEquipmentArray = ContainsUInt32(equipmentInstanceIds, instanceId);

            TryReadUInt32(process, item + InventoryItemTemplateIdOffset, out info.TemplateId);

            ulong count;
            if (TryReadUInt64(process, item + InventoryItemCountOffset, out count))
            {
                info.Count = unchecked((long)count);
            }

            string name;
            if (TryReadMsvcWString(process, item + InventoryItemNameOffset, out name))
            {
                info.Name = name;
            }

            string customName;
            if (TryReadUtf16String(process, item + InventoryItemCustomNameOffset, 26, out customName))
            {
                info.CustomName = customName;
            }

            TryReadUInt32(process, item + InventoryItemTypeOffset, out info.ItemType);
            TryReadUInt32(process, item + InventoryItemEquipmentMaskOffset, out info.EquipmentMask);
            TryReadUInt32(process, item + InventoryItemFlagsOffset, out info.Flags);
            TryReadUInt64(process, item + InventoryItemValueOffset, out info.Value);
            TryReadUInt64(process, item + InventoryItemExpiryOffset, out info.ExpiryTimeRaw);
            TryReadUInt32(process, item + InventoryItemDurationOffset, out info.DurationSeconds);
            TryReadUInt32(process, item + InventoryItemExtraStateOffset, out info.ExtraState);

            short slot;
            if (TryReadInt16(process, item + InventoryItemSlotOffset, out slot))
            {
                info.Slot = slot;
                if (slot >= 0)
                {
                    info.Page = slot / InventorySlotsPerPage;
                    info.Cell = slot % InventorySlotsPerPage;
                    info.Row = info.Cell / columns;
                    info.Column = info.Cell % columns;
                }
                else
                {
                    info.Slot = slot;
                }
            }

            return true;
        }

        private static uint[] ReadInventoryEquipmentInstanceIds(VmmProcess process, ulong manager)
        {
            var result = new uint[InventoryEquipmentIdCount];
            for (int i = 0; i < result.Length; i++)
            {
                uint value;
                if (TryReadUInt32(process, manager + InventoryEquipmentIdsOffset + (ulong)(i * 4), out value))
                {
                    result[i] = value;
                }
            }

            return result;
        }

        private static bool TryReadHighestLearnedSkills(
            VmmProcess process,
            ulong gameBase,
            out List<LearnedSkillInfo> skills,
            out int outerNodeCount,
            out string error)
        {
            skills = new List<LearnedSkillInfo>();
            outerNodeCount = 0;
            error = null;

            ulong skillManager;
            if (!TryReadPointer(process, gameBase + SkillManagerGlobalRva, out skillManager) || skillManager == 0)
            {
                error = "failed to read SkillManager pointer at Game.dll+0x" + SkillManagerGlobalRva.ToString("X");
                return false;
            }

            ulong outerHeader;
            if (!TryReadPointer(process, skillManager + LearnedSkillTreeOffset, out outerHeader) || outerHeader == 0)
            {
                error = "failed to read learned skill tree header at SkillManager+0x" + LearnedSkillTreeOffset.ToString("X");
                return false;
            }

            ulong outerNode;
            if (!TryReadPointer(process, outerHeader + NodeLeftOffset, out outerNode))
            {
                error = "failed to read learned skill tree begin node";
                return false;
            }

            var visited = new HashSet<ulong>();
            for (int guard = 0; outerNode != 0 && outerNode != outerHeader && guard < 65536; guard++)
            {
                if (!visited.Add(outerNode) || IsNilNode(process, outerNode, outerHeader))
                {
                    break;
                }

                outerNodeCount++;

                LearnedSkillInfo skill;
                if (TryReadHighestLearnedSkillFromOuterNode(process, outerNode, out skill))
                {
                    skills.Add(skill);
                }

                ulong next;
                if (!TryGetNextTreeNode(process, outerHeader, outerNode, out next) || next == outerNode)
                {
                    break;
                }

                outerNode = next;
            }

            skills.Sort(delegate (LearnedSkillInfo left, LearnedSkillInfo right)
            {
                return left.SkillId.CompareTo(right.SkillId);
            });

            return true;
        }

        private static bool TryReadHighestLearnedSkillFromOuterNode(
            VmmProcess process,
            ulong outerNode,
            out LearnedSkillInfo skill)
        {
            skill = new LearnedSkillInfo();
            skill.Name = string.Empty;
            skill.DisplayBaseName = string.Empty;

            uint skillId;
            if (!TryReadUInt32(process, outerNode + LearnedSkillOuterSkillIdOffset, out skillId) || skillId == 0)
            {
                return false;
            }

            ulong innerHeader;
            if (!TryReadPointer(process, outerNode + LearnedSkillOuterLevelTreeHeaderOffset, out innerHeader) || innerHeader == 0)
            {
                return false;
            }

            ulong levelTreeSize;
            if (TryReadUInt64(process, outerNode + LearnedSkillOuterLevelTreeSizeOffset, out levelTreeSize))
            {
                skill.LevelTreeSize = levelTreeSize;
            }

            ulong highestLevelNode;
            if (!TryReadPointer(process, innerHeader + NodeRightOffset, out highestLevelNode) ||
                highestLevelNode == 0 ||
                highestLevelNode == innerHeader ||
                IsNilNode(process, highestLevelNode, innerHeader))
            {
                return false;
            }

            ushort level;
            if (!TryReadUInt16(process, highestLevelNode + LearnedSkillInnerLevelOffset, out level))
            {
                return false;
            }

            ulong itemListHeader;
            if (!TryReadPointer(process, highestLevelNode + LearnedSkillInnerItemListHeaderOffset, out itemListHeader) ||
                itemListHeader == 0)
            {
                return false;
            }

            ulong itemListSize;
            if (TryReadUInt64(process, highestLevelNode + LearnedSkillInnerItemListSizeOffset, out itemListSize))
            {
                skill.ItemListSize = itemListSize;
            }

            ulong lastNode;
            if (!TryReadPointer(process, itemListHeader + ListNodePrevOffset, out lastNode) ||
                lastNode == 0 ||
                lastNode == itemListHeader)
            {
                return false;
            }

            ulong item;
            if (!TryReadPointer(process, lastNode + ListNodeValueOffset, out item) || item == 0)
            {
                return false;
            }

            uint itemSkillId;
            if (!TryReadUInt32(process, item + SkillItemSkillIdOffset, out itemSkillId) ||
                itemSkillId != skillId)
            {
                return false;
            }

            skill.SkillId = skillId;
            skill.HighestLevel = level;
            skill.SkillItem = item;

            string name;
            if (TryReadMsvcWString(process, item + SkillItemNameOffset, out name))
            {
                skill.Name = name;
            }

            string displayBaseName;
            int displayTier;
            GetSkillDisplayNameParts(skill.Name, out displayBaseName, out displayTier);
            skill.DisplayBaseName = displayBaseName;
            skill.DisplayTier = displayTier;

            TryReadUInt32(process, item + SkillItemField0COffset, out skill.Field0C);
            TryReadUInt64(process, item + SkillItemRankValueOffset, out skill.RankValue);
            TryReadUInt32(process, item + SkillItemCooldownDurationOffset, out skill.CooldownDuration);
            TryReadUInt32(process, item + SkillItemCooldownEndTimeOffset, out skill.CooldownEndTime);
            TryReadUInt32(process, item + SkillItemItemTypeOffset, out skill.ItemType);
            TryReadUInt32(process, item + SkillItemField5COffset, out skill.Field5C);
            TryReadUInt32(process, item + SkillItemToggleStateOffset, out skill.ToggleState);
            TryReadUInt32(process, item + SkillItemSkillLevelOffset, out skill.SkillLevel);
            TryReadUInt32(process, item + SkillItemStaticFieldD8Offset, out skill.StaticFieldD8);
            TryReadUInt32(process, item + SkillItemRuntimeStateOffset, out skill.RuntimeState);
            TryReadUInt32(process, item + SkillItemTimeOrExpiryOffset, out skill.TimeOrExpiry);
            TryReadUInt32(process, item + SkillItemSourceFlagsOffset, out skill.SourceFlags);
            TryReadUInt32(process, item + SkillItemField78Offset, out skill.Field78);
            TryReadUInt32(process, item + SkillItemPseudoTypeOffset, out skill.PseudoType);
            TryReadUInt32(process, item + SkillItemSpecialMetadataOffset, out skill.SpecialMetadata);

            return true;
        }

        private static bool TryFindEntityById(VmmProcess process, ulong header, ushort entityId, out ulong entity)
        {
            entity = 0;
            if (header == 0 || entityId == 0)
            {
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, header + NodeParentOffset, out node))
            {
                return false;
            }

            for (int guard = 0; node != 0 && node != header && guard < 65536; guard++)
            {
                byte isNil;
                if (!TryReadByte(process, node + NodeIsNilOffset, out isNil) || isNil != 0)
                {
                    return false;
                }

                ushort nodeId;
                if (!TryReadUInt16(process, node + NodeIdOffset, out nodeId))
                {
                    return false;
                }

                if (entityId < nodeId)
                {
                    if (!TryReadPointer(process, node + NodeLeftOffset, out node))
                    {
                        return false;
                    }
                }
                else if (entityId > nodeId)
                {
                    if (!TryReadPointer(process, node + NodeRightOffset, out node))
                    {
                        return false;
                    }
                }
                else
                {
                    return TryReadPointer(process, node + NodeEntityOffset, out entity);
                }
            }

            return false;
        }

        private static bool TryReadEntityPosition(
            VmmProcess process,
            ulong entity,
            out float x,
            out float y,
            out float z,
            out ulong positionOffset)
        {
            x = 0;
            y = 0;
            z = 0;
            positionOffset = EntityWorldPositionOffset;

            uint flags;
            if (!TryReadUInt32(process, entity + EntityPositionFlagsOffset, out flags))
            {
                return false;
            }

            positionOffset = (flags & EntityUseAlternatePositionFlag) != 0
                ? EntityLocalPositionOffset
                : EntityWorldPositionOffset;

            return TryReadSingle(process, entity + positionOffset, out x) &&
                   TryReadSingle(process, entity + positionOffset + 4, out y) &&
                   TryReadSingle(process, entity + positionOffset + 8, out z);
        }

        private static bool TryReadEntityTransform(
            VmmProcess process,
            ulong entity,
            out EntityTransformSnapshot transform)
        {
            transform = new EntityTransformSnapshot();

            return TryReadVec3(process, entity + EntityWorldPositionOffset, out transform.WorldPosition) &&
                   TryReadVec3(process, entity + EntityWorldAnglesOffset, out transform.WorldAngles) &&
                   TryReadVec3(process, entity + EntityLocalPositionOffset, out transform.LocalPosition) &&
                   TryReadVec3(process, entity + EntityLocalAnglesOffset, out transform.LocalAngles);
        }

        private static bool TryReadVec3(VmmProcess process, ulong address, out Vec3 value)
        {
            value = new Vec3();

            return TryReadSingle(process, address, out value.X) &&
                   TryReadSingle(process, address + 4, out value.Y) &&
                   TryReadSingle(process, address + 8, out value.Z);
        }

        private static bool TryResolveActorFromEntityExperimental(
            VmmProcess process,
            ulong entity,
            uint expectedServerObjectId,
            out ActorInfo actor)
        {
            actor = new ActorInfo();

            ulong proxyManager;
            ulong proxyOffset;
            if (TryResolveProxyManagerFromEntityVfunc(process, entity, out proxyManager, out proxyOffset) &&
                TryFindActorCandidateInPointerRegion(
                    process,
                    proxyManager,
                    0x400,
                    entity,
                    expectedServerObjectId,
                    "proxyManager(vfunc_0xB8, entity+0x" + proxyOffset.ToString("X") + ")",
                    out actor))
            {
                return true;
            }

            if (TryFindActorCandidateInPointerRegion(
                process,
                entity,
                0x800,
                entity,
                expectedServerObjectId,
                "CEntity direct scan",
                out actor))
            {
                return true;
            }

            for (ulong offset = 0; offset < 0x800; offset += 8)
            {
                ulong pointer;
                if (!TryReadPointer(process, entity + offset, out pointer))
                {
                    continue;
                }

                if (TryFindActorCandidateInPointerRegion(
                    process,
                    pointer,
                    0x300,
                    entity,
                    expectedServerObjectId,
                    "CEntity+0x" + offset.ToString("X") + " nested scan",
                    out actor))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveProxyManagerFromEntityVfunc(
            VmmProcess process,
            ulong entity,
            out ulong proxyManager,
            out ulong proxyOffset)
        {
            proxyManager = 0;
            proxyOffset = 0;

            ulong vtable;
            ulong function;
            if (!TryReadPointer(process, entity, out vtable) ||
                !TryReadPointer(process, vtable + EntityProxyManagerVfuncOffset, out function))
            {
                return false;
            }

            byte[] code;
            if (!TryReadBytes(process, function, 16, out code))
            {
                return false;
            }

            // mov rax, [rcx+imm32]
            if (code.Length >= 7 &&
                code[0] == 0x48 &&
                code[1] == 0x8B &&
                code[2] == 0x81)
            {
                proxyOffset = BitConverter.ToUInt32(code, 3);
            }
            // mov rax, [rcx+imm8]
            else if (code.Length >= 4 &&
                     code[0] == 0x48 &&
                     code[1] == 0x8B &&
                     code[2] == 0x41)
            {
                proxyOffset = code[3];
            }
            else
            {
                return false;
            }

            return TryReadPointer(process, entity + proxyOffset, out proxyManager);
        }

        private static bool TryFindActorCandidateInPointerRegion(
            VmmProcess process,
            ulong region,
            ulong regionSize,
            ulong expectedEntity,
            uint expectedServerObjectId,
            string source,
            out ActorInfo actor)
        {
            actor = new ActorInfo();
            int bestScore = -1;

            if (!IsLikelyUserPointer(region))
            {
                return false;
            }

            for (ulong offset = 0; offset < regionSize; offset += 8)
            {
                ulong candidate;
                ActorInfo candidateInfo;
                int score;

                if (TryReadPointer(process, region + offset, out candidate) &&
                    TryReadActorInfo(
                        process,
                        candidate,
                        expectedEntity,
                        expectedServerObjectId,
                        source + "+0x" + offset.ToString("X"),
                        out candidateInfo,
                        out score) &&
                    score > bestScore)
                {
                    bestScore = score;
                    actor = candidateInfo;
                }
            }

            return bestScore >= 60;
        }

        private static bool TryReadActorInfo(
            VmmProcess process,
            ulong actorAddress,
            ulong expectedEntity,
            uint expectedServerObjectId,
            string source,
            out ActorInfo actor,
            out int score)
        {
            actor = new ActorInfo();
            score = 0;

            if (!IsLikelyUserPointer(actorAddress))
            {
                return false;
            }

            ulong actorEntity;
            uint objectType;
            uint serverObjectId;
            if (!TryReadPointer(process, actorAddress + ActorEntityOffset, out actorEntity) ||
                !TryReadUInt32(process, actorAddress + ActorObjectTypeOffset, out objectType) ||
                !TryReadUInt32(process, actorAddress + ActorServerObjectIdOffset, out serverObjectId))
            {
                return false;
            }

            if (actorEntity == expectedEntity)
            {
                score += 50;
            }
            else
            {
                return false;
            }

            if (objectType > 0 && objectType <= 32)
            {
                score += 10;
            }
            else
            {
                return false;
            }

            if (expectedServerObjectId != 0 && serverObjectId == expectedServerObjectId)
            {
                score += 40;
            }
            else if (serverObjectId != 0)
            {
                score += 10;
            }

            actor.Actor = actorAddress;
            actor.Entity = actorEntity;
            actor.ObjectType = objectType;
            actor.ServerObjectId = serverObjectId;
            actor.ResolveSource = source;

            TryReadUInt32(process, actorAddress + ActorNpcTemplateIdOffset, out actor.NpcTemplateId);
            TryReadUInt16(process, actorAddress + ActorLevelOffset, out actor.Level);
            TryReadByte(process, actorAddress + ActorHpPercentOffset, out actor.HpPercent);
            TryReadUInt32(process, actorAddress + ActorTargetServerObjectIdOffset, out actor.TargetServerObjectId);
            TryReadUInt32(process, actorAddress + ActorMaxHpOffset, out actor.MaxHp);
            TryReadUInt32(process, actorAddress + ActorCurrentHpOffset, out actor.CurrentHp);

            string name;
            if (TryReadUtf16String(process, actorAddress + ActorNameOffset, 64, out name))
            {
                actor.Name = name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    score += 10;
                }
            }
            else
            {
                actor.Name = string.Empty;
            }

            return true;
        }

        private static bool IsReasonablePosition(float x, float y, float z)
        {
            return !float.IsNaN(x) &&
                   !float.IsNaN(y) &&
                   !float.IsNaN(z) &&
                   !float.IsInfinity(x) &&
                   !float.IsInfinity(y) &&
                   !float.IsInfinity(z) &&
                   Math.Abs(x) < 10000000.0f &&
                   Math.Abs(y) < 10000000.0f &&
                   Math.Abs(z) < 10000000.0f;
        }

        private static bool TryFindServerObjectByEntityId(
            VmmProcess process,
            ulong gameBase,
            ushort entityId,
            out uint serverObjectId,
            out ulong serverTreeHeader)
        {
            serverObjectId = 0;
            serverTreeHeader = 0;

            if (entityId == 0 ||
                !TryReadPointer(process, gameBase + ServerObjectTreeRva, out serverTreeHeader) ||
                serverTreeHeader == 0)
            {
                return false;
            }

            ulong node;
            if (!TryReadPointer(process, serverTreeHeader + NodeLeftOffset, out node))
            {
                return false;
            }

            for (int guard = 0; node != 0 && node != serverTreeHeader && guard < 100000; guard++)
            {
                if (IsNilNode(process, node, serverTreeHeader))
                {
                    return false;
                }

                ushort nodeEntityId;
                if (!TryReadUInt16(process, node + ServerNodeEntityIdOffset, out nodeEntityId))
                {
                    return false;
                }

                if (nodeEntityId == entityId)
                {
                    return TryReadUInt32(process, node + ServerNodeServerObjectIdOffset, out serverObjectId);
                }

                ulong next;
                if (!TryGetNextTreeNode(process, serverTreeHeader, node, out next) || next == node)
                {
                    return false;
                }

                node = next;
            }

            return false;
        }

        private static bool TryGetNextTreeNode(VmmProcess process, ulong header, ulong node, out ulong next)
        {
            next = 0;

            ulong right;
            if (!TryReadPointer(process, node + NodeRightOffset, out right))
            {
                return false;
            }

            if (!IsNilNode(process, right, header))
            {
                ulong current = right;
                for (int guard = 0; guard < 1024; guard++)
                {
                    ulong left;
                    if (!TryReadPointer(process, current + NodeLeftOffset, out left))
                    {
                        return false;
                    }

                    if (IsNilNode(process, left, header))
                    {
                        next = current;
                        return true;
                    }

                    current = left;
                }

                return false;
            }

            ulong parent;
            if (!TryReadPointer(process, node + NodeParentOffset, out parent))
            {
                return false;
            }

            for (int guard = 0; !IsNilNode(process, parent, header) && guard < 1024; guard++)
            {
                ulong parentRight;
                if (!TryReadPointer(process, parent + NodeRightOffset, out parentRight))
                {
                    return false;
                }

                if (node != parentRight)
                {
                    break;
                }

                node = parent;
                if (!TryReadPointer(process, parent + NodeParentOffset, out parent))
                {
                    return false;
                }
            }

            next = parent;
            return true;
        }

        private static bool IsNilNode(VmmProcess process, ulong node, ulong header)
        {
            if (node == 0 || node == header)
            {
                return true;
            }

            byte isNil;
            return !TryReadByte(process, node + NodeIsNilOffset, out isNil) || isNil != 0;
        }

        private static bool IsNormalBagInventoryItem(InventoryItemInfo item)
        {
            return item.Slot >= 0 && item.EquipmentMask == 0;
        }

        private static bool IsEquippedInventoryItem(InventoryItemInfo item)
        {
            return item.EquipmentMask != 0 || item.IsInEquipmentArray;
        }

        private static int CompareInventoryItems(InventoryItemInfo left, InventoryItemInfo right)
        {
            bool leftBag = IsNormalBagInventoryItem(left);
            bool rightBag = IsNormalBagInventoryItem(right);
            if (leftBag != rightBag)
            {
                return leftBag ? -1 : 1;
            }

            if (left.Slot != right.Slot)
            {
                return left.Slot.CompareTo(right.Slot);
            }

            if (left.TemplateId != right.TemplateId)
            {
                return left.TemplateId.CompareTo(right.TemplateId);
            }

            return left.InstanceId.CompareTo(right.InstanceId);
        }

        private static void CountInventorySlots(
            uint capacity,
            List<InventoryItemInfo> items,
            out int usedSlots,
            out int freeSlots)
        {
            usedSlots = 0;
            freeSlots = 0;

            if (items == null || capacity == 0 || capacity > 100000)
            {
                return;
            }

            var occupied = new HashSet<int>();
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItemInfo item = items[i];
                if (IsNormalBagInventoryItem(item) &&
                    item.Slot >= 0 &&
                    item.Slot < capacity)
                {
                    occupied.Add(item.Slot);
                }
            }

            usedSlots = occupied.Count;
            freeSlots = checked((int)capacity) - usedSlots;
            if (freeSlots < 0)
            {
                freeSlots = 0;
            }
        }

        private static bool ContainsUInt32(uint[] values, uint value)
        {
            if (values == null || value == 0)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PrintEquipmentInstanceIds(uint[] equipmentInstanceIds)
        {
            if (equipmentInstanceIds == null)
            {
                return;
            }

            var ids = new List<uint>();
            for (int i = 0; i < equipmentInstanceIds.Length; i++)
            {
                if (equipmentInstanceIds[i] != 0)
                {
                    ids.Add(equipmentInstanceIds[i]);
                }
            }

            if (ids.Count == 0)
            {
                Console.WriteLine("EquipmentInstanceIds=[]");
                return;
            }

            Console.WriteLine("EquipmentInstanceIds=[" + string.Join(",", ids) + "]");
        }

        private static string FormatInventoryItem(int index, InventoryItemInfo item)
        {
            return "#" + index.ToString("000") +
                   " " + FormatInventoryLocation(item) +
                   " Addr=" + FormatAddress(item.Address) +
                   " InstanceId=" + item.InstanceId +
                   " TemplateId=" + item.TemplateId +
                   " Count=" + item.Count +
                   " Name=\"" + item.Name + "\"" +
                   " CustomName=\"" + item.CustomName + "\"" +
                   " Type=" + item.ItemType +
                   " EquipMask=0x" + item.EquipmentMask.ToString("X") +
                   " Equipped=" + (IsEquippedInventoryItem(item) ? "yes" : "no") +
                   " EquipArray=" + (item.IsInEquipmentArray ? "yes" : "no") +
                   " Cash=" + (((item.Flags & InventoryCashItemFlag) != 0) ? "yes" : "no") +
                   " Flags=0x" + item.Flags.ToString("X8") +
                   " Value=" + item.Value +
                   " ExpiryRaw=" + item.ExpiryTimeRaw +
                   " DurationSec=" + item.DurationSeconds +
                   " ExtraState=0x" + item.ExtraState.ToString("X");
        }

        private static string FormatInventoryLocation(InventoryItemInfo item)
        {
            if (item.Slot < 0)
            {
                return "Slot=n/a Page=n/a Cell=n/a Row=n/a Col=n/a";
            }

            return "Slot=" + item.Slot +
                   " Page=" + (item.Page + 1) +
                   " Cell=" + (item.Cell + 1) +
                   " Row=" + (item.Row + 1) +
                   " Col=" + (item.Column + 1);
        }

        private static string FormatGatherListEntry(int index, GatherListEntry entry)
        {
            return "#" + index.ToString("00") +
                   (entry.IsLockedTarget ? " [TARGET]" : string.Empty) +
                   " Dist=" + (entry.HasDistance ? entry.DistanceToLocalPlayer.ToString("F2") : "n/a") +
                   " EntityId=" + entry.EntityId +
                   " ServerId=" + entry.ServerObjectId +
                   " SourceId=" + entry.GatherSourceId +
                   " Gather=" + FormatAddress(entry.GatherObject) +
                   " Entity=" + FormatAddress(entry.Entity) +
                   " Name=\"" + entry.Name + "\"" +
                   " DisplayLevel=" + entry.DisplayLevel +
                   " StateOrRemain=" + entry.StateOrRemaining +
                   " Radius=" + entry.InteractionRadius.ToString("F2") +
                   " Pos=" + FormatGatherPosition(entry) +
                   " Spawn=" + FormatGatherSpawnPosition(entry) +
                   " Source=" + entry.ResolveSource;
        }

        private static string FormatGatherPosition(GatherListEntry entry)
        {
            if (!entry.HasPosition)
            {
                return "n/a";
            }

            return "X=" + entry.X.ToString("F2") +
                   " Y=" + entry.Y.ToString("F2") +
                   " Z=" + entry.Z.ToString("F2") +
                   " Offset=0x" + entry.PositionOffset.ToString("X");
        }

        private static string FormatGatherSpawnPosition(GatherListEntry entry)
        {
            if (!entry.HasSpawnPosition)
            {
                return "n/a";
            }

            return "X=" + entry.SpawnX.ToString("F2") +
                   " Y=" + entry.SpawnY.ToString("F2") +
                   " Z=" + entry.SpawnZ.ToString("F2");
        }

        private static string FormatActorAbnormalSnapshot(string label, ActorAbnormalStatusSnapshot snapshot)
        {
            int physicalEntryCount = CountAbnormalEntriesByCategory(snapshot.Entries, AbnormalCategoryPhysical);
            return "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                   label + "Actor=" + FormatAddress(snapshot.Actor) +
                   " Entity=" + FormatAddress(snapshot.Entity) +
                   " EntityId=" + snapshot.EntityId +
                   " ObjType=" + snapshot.ObjectType +
                   " ServerId=" + snapshot.ServerObjectId +
                   " Name=\"" + snapshot.Name + "\"" +
                   " Dist=" + (snapshot.HasDistance ? snapshot.DistanceToLocalPlayer.ToString("F2") : "n/a") +
                   " HasPhysical=" + (snapshot.PhysicalCount != 0 || physicalEntryCount != 0 ? "yes" : "no") +
                   " Counts(Category0/Buff/Physical/Mental)=" +
                   snapshot.Category0Count + "/" +
                   snapshot.BuffCount + "/" +
                   snapshot.PhysicalCount + "/" +
                   snapshot.MentalCount +
                   " EntryCount=" + (snapshot.Entries == null ? 0 : snapshot.Entries.Count) +
                   " PhysicalEntries=" + physicalEntryCount +
                   " Array=" + FormatAddress(snapshot.Begin) + "-" + FormatAddress(snapshot.End) +
                   " Capacity=" + FormatAddress(snapshot.Capacity) +
                   " Source=" + snapshot.ResolveSource;
        }

        private static void PrintVisibleActorAbnormalSnapshots(
            List<ActorAbnormalStatusSnapshot> snapshots,
            int scannedServerObjects,
            int resolvedActors,
            int physicalActors,
            bool printAllEntries)
        {
            Console.WriteLine(
                "VisibleActors ScannedServerObjects=" + scannedServerObjects +
                " ResolvedActors=" + resolvedActors +
                " PhysicalActors=" + physicalActors +
                " Rows=" + (snapshots == null ? 0 : snapshots.Count));

            if (snapshots == null || snapshots.Count == 0)
            {
                Console.WriteLine("VisiblePhysicalAbnormalActors=[]");
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                Console.WriteLine(FormatActorAbnormalSnapshot("Visible#" + (i + 1).ToString("00") + " ", snapshots[i]));
                PrintAbnormalEntries(snapshots[i].Entries, printAllEntries);
            }
        }

        private static void PrintAbnormalEntries(List<AbnormalStatusEntry> entries, bool printAllEntries)
        {
            if (entries == null || entries.Count == 0)
            {
                Console.WriteLine("  AbnormalEntries=[]");
                return;
            }

            int printed = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                AbnormalStatusEntry entry = entries[i];
                if (!printAllEntries && entry.DispelCategory != AbnormalCategoryPhysical)
                {
                    continue;
                }

                printed++;
                Console.WriteLine("  " + FormatAbnormalEntry(printed, entry));
            }

            if (printed == 0)
            {
                Console.WriteLine("  PhysicalAbnormalEntries=[]");
            }
        }

        private static string FormatAbnormalEntry(int index, AbnormalStatusEntry entry)
        {
            return "#" + index.ToString("00") +
                   " Addr=" + FormatAddress(entry.Address) +
                   " Id=" + entry.AbnormalId +
                   " Category=" + entry.DispelCategory +
                   " CategoryName=" + FormatAbnormalCategory(entry.DispelCategory) +
                   " LevelOrStack=" + entry.LevelOrStack +
                   " TimeOrSource=0x" + entry.TimeOrSource.ToString("X") +
                   " Field00=0x" + entry.Field00.ToString("X");
        }

        private static string FormatAbnormalCategory(uint category)
        {
            switch (category)
            {
                case 0:
                    return "never/none";
                case 1:
                    return "buff";
                case 2:
                    return "debuffphy";
                case 3:
                    return "debuffmen";
                case 8:
                    return "extra";
                default:
                    return "unknown";
            }
        }

        private static int CountAbnormalEntriesByCategory(List<AbnormalStatusEntry> entries, uint category)
        {
            if (entries == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].DispelCategory == category)
                {
                    count++;
                }
            }

            return count;
        }

        private static void PrintPartyAbnormalSnapshots(List<PartyMemberAbnormalSnapshot> snapshots, bool printAllEntries)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                Console.WriteLine("PartyMembers=[]");
                return;
            }

            int printedMembers = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                PartyMemberAbnormalSnapshot snapshot = snapshots[i];
                bool hasPhysical = snapshot.PhysicalCount > 0;
                if (!printAllEntries && !hasPhysical)
                {
                    continue;
                }

                printedMembers++;
                Console.WriteLine(
                    "Party#" + printedMembers.ToString("00") +
                    " List=" + snapshot.ListName +
                    " Member=" + FormatAddress(snapshot.Member) +
                    " ServerId=" + snapshot.ServerObjectId +
                    " HasBlock=" + (snapshot.HasAbnormalBlock ? "yes" : "no") +
                    " Flags=0x" + snapshot.DataFlags.ToString("X2") +
                    " RawCount=" + snapshot.RawCount +
                    " EntryCount=" + (snapshot.Entries == null ? 0 : snapshot.Entries.Count) +
                    " PhysicalCount=" + snapshot.PhysicalCount +
                    " UpdateTime=0x" + snapshot.UpdateTime.ToString("X"));

                if (printAllEntries && snapshot.Entries != null)
                {
                    PrintAbnormalEntries(snapshot.Entries, true);
                }
            }

            if (printedMembers == 0)
            {
                Console.WriteLine("PartyPhysicalAbnormalMembers=[]");
            }
        }

        private static List<LearnedSkillInfo> SelectHighestDisplaySkillPerName(List<LearnedSkillInfo> skills)
        {
            var selected = new Dictionary<string, LearnedSkillInfo>(StringComparer.Ordinal);

            for (int i = 0; i < skills.Count; i++)
            {
                LearnedSkillInfo skill = skills[i];
                string key = GetLearnedSkillDisplayGroupKey(skill);

                LearnedSkillInfo current;
                if (!selected.TryGetValue(key, out current) ||
                    CompareLearnedSkillDisplayLevel(skill, current) > 0)
                {
                    selected[key] = skill;
                }
            }

            var result = selected.Values.ToList();
            result.Sort(delegate (LearnedSkillInfo left, LearnedSkillInfo right)
            {
                return left.SkillId.CompareTo(right.SkillId);
            });

            return result;
        }

        private static List<LearnedSkillInfo> FilterUsefulLearnedSkills(List<LearnedSkillInfo> skills)
        {
            var result = new List<LearnedSkillInfo>();
            for (int i = 0; i < skills.Count; i++)
            {
                if (IsUsefulLearnedSkill(skills[i]))
                {
                    result.Add(skills[i]);
                }
            }

            return result;
        }

        private static bool IsUsefulLearnedSkill(LearnedSkillInfo skill)
        {
            string name = skill.Name ?? string.Empty;
            string baseName = string.IsNullOrWhiteSpace(skill.DisplayBaseName)
                ? name
                : skill.DisplayBaseName;

            if (skill.SkillId >= 50000)
            {
                return false;
            }

            if (IsIgnoredUtilitySkillName(name) || IsIgnoredUtilitySkillName(baseName))
            {
                return false;
            }

            if (ContainsAny(name, IgnoredSkillNameParts) || ContainsAny(baseName, IgnoredSkillNameParts))
            {
                return false;
            }

            if (skill.HighestLevel == 0 && skill.SkillLevel == 0)
            {
                return false;
            }

            if (skill.ToggleState != 0)
            {
                return true;
            }

            if (skill.CooldownDuration > 0)
            {
                return true;
            }

            return skill.StaticFieldD8 != 0 ||
                   skill.RuntimeState != 0 ||
                   skill.SourceFlags != 0;
        }

        private static bool IsIgnoredUtilitySkillName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.Trim();
            for (int i = 0; i < IgnoredUtilitySkillNames.Length; i++)
            {
                if (string.Equals(name, IgnoredUtilitySkillNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string text, string[] values)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (text.IndexOf(values[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly string[] IgnoredUtilitySkillNames =
        {
            "紧急返回",
            "精气提取",
            "奥德提取",
            "炼金术",
            "物质变幻",
            "宠物管理",
            "宠物礼物",
            "自动使用物品",
            "自动拾取物品",
            "战斗/一般转换",
            "休息/一般转换",
            "捡取道具",
            "选择对象的对象",
            "切换武器",
            "走/跑 转换",
            "攻击/对话",
            "飞行/着陆切换",
            "封魂石 使用/解除",
            "自动打猎申报"
        };

        private static readonly string[] IgnoredSkillNameParts =
        {
            "基础",
            "基本",
            "穿着",
            "修炼",
            "防御力增加",
            "抵抗强化",
            "返回",
            "提取",
            "炼金术",
            "物质变幻",
            "宠物",
            "一般转换",
            "捡取道具",
            "选择对象",
            "切换武器",
            "走/跑",
            "攻击/对话",
            "飞行/着陆",
            "封魂石",
            "自动打猎",
            "自动使用物品",
            "自动拾取物品",
            "显示标志",
            "选择证物"
        };

        private static string GetLearnedSkillDisplayGroupKey(LearnedSkillInfo skill)
        {
            if (skill.DisplayTier > 0 && !string.IsNullOrWhiteSpace(skill.DisplayBaseName))
            {
                return "name:" + skill.DisplayBaseName;
            }

            return "id:" + skill.SkillId.ToString();
        }

        private static int CompareLearnedSkillDisplayLevel(LearnedSkillInfo left, LearnedSkillInfo right)
        {
            if (left.DisplayTier != right.DisplayTier)
            {
                return left.DisplayTier.CompareTo(right.DisplayTier);
            }

            if (left.SkillLevel != right.SkillLevel)
            {
                return left.SkillLevel.CompareTo(right.SkillLevel);
            }

            if (left.HighestLevel != right.HighestLevel)
            {
                return left.HighestLevel.CompareTo(right.HighestLevel);
            }

            return left.SkillId.CompareTo(right.SkillId);
        }

        private static void GetSkillDisplayNameParts(string name, out string baseName, out int tier)
        {
            name = (name ?? string.Empty).Trim();
            baseName = name;
            tier = 0;

            if (name.Length == 0)
            {
                return;
            }

            int end = name.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(name[end]))
            {
                end--;
            }

            int romanStart = end;
            while (romanStart >= 0 && IsRomanNumeralChar(name[romanStart]))
            {
                romanStart--;
            }

            int suffixStart = romanStart + 1;
            if (suffixStart > end)
            {
                return;
            }

            string roman = name.Substring(suffixStart, end - suffixStart + 1).ToUpperInvariant();
            int parsedTier;
            if (!TryParseRomanNumeral(roman, out parsedTier) || parsedTier <= 0 || parsedTier > 50)
            {
                return;
            }

            char before = romanStart >= 0 ? name[romanStart] : '\0';
            if (roman.Length == 1 && IsAsciiLetterOrDigit(before))
            {
                return;
            }

            string parsedBaseName = name.Substring(0, suffixStart).TrimEnd(' ', '\t', '　', '-', '－');
            if (string.IsNullOrWhiteSpace(parsedBaseName))
            {
                return;
            }

            baseName = parsedBaseName;
            tier = parsedTier;
        }

        private static bool IsRomanNumeralChar(char value)
        {
            value = char.ToUpperInvariant(value);
            return value == 'I' ||
                   value == 'V' ||
                   value == 'X' ||
                   value == 'L' ||
                   value == 'C' ||
                   value == 'D' ||
                   value == 'M';
        }

        private static bool TryParseRomanNumeral(string value, out int result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim().ToUpperInvariant();
            int previous = 0;
            for (int i = value.Length - 1; i >= 0; i--)
            {
                int current = GetRomanNumeralValue(value[i]);
                if (current == 0)
                {
                    result = 0;
                    return false;
                }

                if (current < previous)
                {
                    result -= current;
                }
                else
                {
                    result += current;
                    previous = current;
                }
            }

            return result > 0 && string.Equals(ToRomanNumeral(result), value, StringComparison.Ordinal);
        }

        private static int GetRomanNumeralValue(char value)
        {
            switch (char.ToUpperInvariant(value))
            {
                case 'I':
                    return 1;
                case 'V':
                    return 5;
                case 'X':
                    return 10;
                case 'L':
                    return 50;
                case 'C':
                    return 100;
                case 'D':
                    return 500;
                case 'M':
                    return 1000;
                default:
                    return 0;
            }
        }

        private static string ToRomanNumeral(int value)
        {
            if (value <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendRomanNumeral(builder, ref value, 1000, "M");
            AppendRomanNumeral(builder, ref value, 900, "CM");
            AppendRomanNumeral(builder, ref value, 500, "D");
            AppendRomanNumeral(builder, ref value, 400, "CD");
            AppendRomanNumeral(builder, ref value, 100, "C");
            AppendRomanNumeral(builder, ref value, 90, "XC");
            AppendRomanNumeral(builder, ref value, 50, "L");
            AppendRomanNumeral(builder, ref value, 40, "XL");
            AppendRomanNumeral(builder, ref value, 10, "X");
            AppendRomanNumeral(builder, ref value, 9, "IX");
            AppendRomanNumeral(builder, ref value, 5, "V");
            AppendRomanNumeral(builder, ref value, 4, "IV");
            AppendRomanNumeral(builder, ref value, 1, "I");
            return builder.ToString();
        }

        private static void AppendRomanNumeral(StringBuilder builder, ref int value, int number, string text)
        {
            while (value >= number)
            {
                builder.Append(text);
                value -= number;
            }
        }

        private static bool IsAsciiLetterOrDigit(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                   (value >= 'a' && value <= 'z') ||
                   (value >= '0' && value <= '9');
        }

        private static string FormatLearnedSkill(int index, LearnedSkillInfo skill)
        {
            return "#" + index.ToString("000") +
                   " Id=" + skill.SkillId +
                   " HighestLevel=" + skill.HighestLevel +
                   " ItemLevel=" + skill.SkillLevel +
                   " Item=" + FormatAddress(skill.SkillItem) +
                   " Name=\"" + skill.Name + "\"" +
                   FormatDisplaySkillGroup(skill) +
                   " Toggle=" + skill.ToggleState +
                   " Cooldown=" + skill.CooldownDuration + "/" + skill.CooldownEndTime +
                   " ItemType=" + skill.ItemType +
                   " RuntimeState=" + skill.RuntimeState +
                   " Rank=" + skill.RankValue +
                   " SourceFlags=0x" + skill.SourceFlags.ToString("X") +
                   " PseudoType=" + skill.PseudoType +
                   " SpecialMetadata=" + skill.SpecialMetadata +
                   " StaticFieldD8=0x" + skill.StaticFieldD8.ToString("X") +
                   " TimeOrExpiry=" + skill.TimeOrExpiry +
                   " Field0C=0x" + skill.Field0C.ToString("X") +
                   " Field5C=0x" + skill.Field5C.ToString("X") +
                   " Field78=0x" + skill.Field78.ToString("X") +
                   " LevelTreeSize=" + skill.LevelTreeSize +
                   " ItemListSize=" + skill.ItemListSize;
        }

        private static string FormatDisplaySkillGroup(LearnedSkillInfo skill)
        {
            if (skill.DisplayTier <= 0 || string.IsNullOrWhiteSpace(skill.DisplayBaseName))
            {
                return string.Empty;
            }

            return " Base=\"" + skill.DisplayBaseName + "\"" +
                   " Tier=" + skill.DisplayTier;
        }

        private static string FormatAddress(ulong address)
        {
            return address == 0 ? "n/a" : "0x" + address.ToString("X");
        }

        private static string FormatPosition(LocalPlayerInfo info)
        {
            if (!info.HasPosition)
            {
                return "n/a";
            }

            return "X=" + info.X.ToString("F2") +
                   " Y=" + info.Y.ToString("F2") +
                   " Z=" + info.Z.ToString("F2") +
                   " Offset=0x" + info.PositionOffset.ToString("X");
        }

        private static string FormatTransform(LocalPlayerInfo info)
        {
            if (!info.HasTransform)
            {
                return "n/a";
            }

            return FormatTransform(info.Transform);
        }

        private static string FormatPosition(LockedTargetMonsterInfo info)
        {
            if (!info.HasPosition)
            {
                return "n/a";
            }

            return "X=" + info.X.ToString("F2") +
                   " Y=" + info.Y.ToString("F2") +
                   " Z=" + info.Z.ToString("F2") +
                   " Offset=0x" + info.PositionOffset.ToString("X");
        }

        private static string FormatTransform(LockedTargetMonsterInfo info)
        {
            if (!info.HasTransform)
            {
                return "n/a";
            }

            return FormatTransform(info.Transform);
        }

        private static string FormatActor(LockedTargetMonsterInfo info)
        {
            if (!info.HasActor)
            {
                return "n/a";
            }

            ActorInfo actor = info.Actor;
            return FormatAddress(actor.Actor) +
                   " Source=" + actor.ResolveSource +
                   " ObjType=" + actor.ObjectType +
                   " ServerId=" + actor.ServerObjectId +
                   " TemplateId=" + actor.NpcTemplateId +
                   " Level=" + actor.Level +
                   " HpPercent=" + actor.HpPercent +
                   " HP=" + actor.CurrentHp + "/" + actor.MaxHp +
                   " TargetServerId=" + actor.TargetServerObjectId +
                   " Name=\"" + actor.Name + "\"";
        }

        private static string FormatTransform(EntityTransformSnapshot transform)
        {
            return "WorldPos=" + FormatVec3(transform.WorldPosition) +
                   " WorldAng=" + FormatVec3(transform.WorldAngles) +
                   " LocalPos=" + FormatVec3(transform.LocalPosition) +
                   " LocalAng=" + FormatVec3(transform.LocalAngles);
        }

        private static string FormatVec3(Vec3 value)
        {
            return "(" + value.X.ToString("F2") +
                   "," + value.Y.ToString("F2") +
                   "," + value.Z.ToString("F2") + ")";
        }

        private static string FormatServerObjectId(LockedTargetMonsterInfo info)
        {
            return info.HasServerObjectId ? info.ServerObjectId.ToString() : "n/a";
        }

        private static string FormatEntityType(LockedTargetMonsterInfo info)
        {
            return info.HasEntityType ? info.EntityType.ToString() : "n/a";
        }

        private static string FormatNpcLike(LockedTargetMonsterInfo info)
        {
            return info.HasEntityType && info.EntityType == EntityTypeNpc ? "yes" : "no";
        }

        private static string FormatDistance(LockedTargetMonsterInfo info)
        {
            return info.HasDistance ? info.DistanceToLocalPlayer.ToString("F2") : "n/a";
        }

        private static double ReadDoubleFromEnv(string name, double defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(name);
            double value;
            if (!string.IsNullOrWhiteSpace(text) &&
                double.TryParse(text, out value) &&
                value >= 0)
            {
                return value;
            }

            return defaultValue;
        }

        private static bool ReadBoolFromEnv(string name, bool defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }

            text = text.Trim();
            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }

        private static int ReadIntFromEnv(string name, int defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(name);
            int value;
            if (!string.IsNullOrWhiteSpace(text) &&
                int.TryParse(text, out value) &&
                value >= 0)
            {
                return value;
            }

            return defaultValue;
        }

        private static string FormatPercent(uint current, uint max)
        {
            if (max == 0)
            {
                return "0.0%";
            }

            return (current * 100.0 / max).ToString("F1") + "%";
        }

        private static bool TryReadByte(VmmProcess process, ulong address, out byte value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 1);
                if (buffer == null || buffer.Length < 1)
                {
                    return false;
                }

                value = buffer[0];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadBytes(VmmProcess process, ulong address, int count, out byte[] value)
        {
            value = null;
            try
            {
                var buffer = process.MemRead(address, (uint)count);
                if (buffer == null || buffer.Length < count)
                {
                    return false;
                }

                value = buffer;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadUtf16String(VmmProcess process, ulong address, int maxChars, out string value)
        {
            value = string.Empty;

            byte[] buffer;
            if (!TryReadBytes(process, address, maxChars * 2, out buffer))
            {
                return false;
            }

            int byteCount = 0;
            while (byteCount + 1 < buffer.Length)
            {
                if (buffer[byteCount] == 0 && buffer[byteCount + 1] == 0)
                {
                    break;
                }

                byteCount += 2;
            }

            if (byteCount == 0)
            {
                return true;
            }

            value = Encoding.Unicode.GetString(buffer, 0, byteCount);
            return true;
        }

        private static bool TryReadMsvcWString(VmmProcess process, ulong stringObject, out string value)
        {
            value = string.Empty;

            ulong length;
            ulong capacity;
            if (!TryReadUInt64(process, stringObject + 0x10, out length) ||
                !TryReadUInt64(process, stringObject + 0x18, out capacity))
            {
                return false;
            }

            if (length == 0)
            {
                return true;
            }

            if (length > 256 || capacity > 0x100000)
            {
                return false;
            }

            ulong characters = stringObject;
            if (capacity >= 8 && !TryReadPointer(process, stringObject, out characters))
            {
                return false;
            }

            if (characters == 0)
            {
                return false;
            }

            return TryReadUtf16StringByLength(process, characters, (int)length, out value);
        }

        private static bool TryReadUtf16StringByLength(VmmProcess process, ulong address, int charCount, out string value)
        {
            value = string.Empty;

            if (charCount <= 0)
            {
                return true;
            }

            byte[] buffer;
            if (!TryReadBytes(process, address, charCount * 2, out buffer))
            {
                return false;
            }

            int byteCount = buffer.Length;
            for (int i = 0; i + 1 < buffer.Length; i += 2)
            {
                if (buffer[i] == 0 && buffer[i + 1] == 0)
                {
                    byteCount = i;
                    break;
                }
            }

            value = byteCount == 0
                ? string.Empty
                : Encoding.Unicode.GetString(buffer, 0, byteCount);
            return true;
        }

        private static bool TryReadUInt16(VmmProcess process, ulong address, out ushort value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 2);
                if (buffer == null || buffer.Length < 2)
                {
                    return false;
                }

                value = BitConverter.ToUInt16(buffer, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadInt16(VmmProcess process, ulong address, out short value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 2);
                if (buffer == null || buffer.Length < 2)
                {
                    return false;
                }

                value = BitConverter.ToInt16(buffer, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadSingle(VmmProcess process, ulong address, out float value)
        {
            value = 0;
            try
            {
                var buffer = process.MemRead(address, 4);
                if (buffer == null || buffer.Length < 4)
                {
                    return false;
                }

                value = BitConverter.ToSingle(buffer, 0);
                return true;
            }
            catch
            {
                return false;
            }
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
