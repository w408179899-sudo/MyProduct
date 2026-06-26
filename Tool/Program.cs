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
        [STAThread]
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




            var earlyAionTestMode = Environment.GetEnvironmentVariable("AION_TEST_MODE");
            if (string.Equals(earlyAionTestMode, "path_follow_budget_test", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(earlyAionTestMode, "path_follow_budget_tests", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(earlyAionTestMode, "path_budget_test", StringComparison.OrdinalIgnoreCase))
            {
                bool passed = Tests.PathFollowDistanceBudgetTests.RunAll();
                passed = Tests.PathFollowMoveControlTests.RunAll() && passed;
                passed = Tests.CameraTurnVerificationTests.RunAll() && passed;
                if (!passed)
                {
                    Environment.ExitCode = 1;
                }

                return;
            }

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

                    var aionTestMode = Environment.GetEnvironmentVariable("AION_TEST_MODE") ?? "path_follow_test";
                    if (string.Equals(aionTestMode, "player", StringComparison.OrdinalIgnoreCase))
                    {
                        RunLocalPlayerInfoTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "player_offset", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "player_probe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "player_6c8", StringComparison.OrdinalIgnoreCase))
                    {
                        RunPlayerOffsetProbeTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "player_float_scan", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "player_scan_float", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "player_find_float", StringComparison.OrdinalIgnoreCase))
                    {
                        RunPlayerFloatScanTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "camera_watch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "camerawatch", StringComparison.OrdinalIgnoreCase))
                    {
                        RunCameraWatchTest(process, gameBase);
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

                    if (string.Equals(aionTestMode, "path_recorder", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "path_record", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "record_path", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "path_ui", StringComparison.OrdinalIgnoreCase))
                    {
                        RunPathRecorderWindow(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "path_follow_test", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "path_follow", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "follow_path", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "auto_path", StringComparison.OrdinalIgnoreCase))
                    {
                        RunPathFollowTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "face_target", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "facetarget", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "turn_target", StringComparison.OrdinalIgnoreCase))
                    {
                        RunFaceTargetCameraTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "face_target_combined", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "facetarget_combined", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "turn_target_combined", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "aim_target", StringComparison.OrdinalIgnoreCase))
                    {
                        RunFaceTargetCombinedCameraTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "fixed_yaw_pitch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "fixed_aim", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "camera_fixed_yaw_pitch", StringComparison.OrdinalIgnoreCase))
                    {
                        RunFixedCameraYawPitchTest(process, gameBase, ReadFaceTargetOptions());
                        return;
                    }

                    if (string.Equals(aionTestMode, "fixed_yaw", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "camera_fixed_yaw", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "turn_fixed_yaw", StringComparison.OrdinalIgnoreCase))
                    {
                        RunFixedCameraYawTest(process, gameBase, ReadFaceTargetOptions());
                        return;
                    }

                    if (string.Equals(aionTestMode, "fixed_pitch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "camera_fixed_pitch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "turn_fixed_pitch", StringComparison.OrdinalIgnoreCase))
                    {
                        RunFixedCameraPitchTest(process, gameBase, ReadFaceTargetOptions());
                        return;
                    }

                    if (string.Equals(aionTestMode, "target_yaw_probe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "yaw_probe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "target_angle", StringComparison.OrdinalIgnoreCase))
                    {
                        RunTargetYawProbeTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "camera_pixel_calibration", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "camera_pixel", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "pixel_yaw", StringComparison.OrdinalIgnoreCase))
                    {
                        RunCameraPixelCalibrationTest(process, gameBase);
                        return;
                    }

                    if (string.Equals(aionTestMode, "camera_pitch_pixel_calibration", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "pitch_pixel", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aionTestMode, "pixel_pitch", StringComparison.OrdinalIgnoreCase))
                    {
                        RunCameraPitchPixelCalibrationTest(process, gameBase);
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
        private const ulong SpecialCameraModeRva = 0xD218C8;
        private const ulong SpecialCameraPitchRva = 0xD218D8;
        private const ulong SpecialCameraRollRva = 0xD218DC;
        private const ulong SpecialCameraYawRva = 0xD218E0;
        private const ulong SpecialCameraDistanceRva = 0xD218E4;
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
            public float CameraDistance;
            public bool IsSpecialCamera;
            public ushort SpecialCameraMode;
            public ulong CameraPitchRva;
            public ulong CameraRollRva;
            public ulong CameraYawRva;
        }

        private struct PathFollowPoint
        {
            public double X;
            public double Y;
            public double Z;
        }

        private struct PathFollowPollSnapshot
        {
            public LocalPlayerInfo Local;
            public long AgeMs;
            public long ReadCount;
            public double Distance;
            public double TargetYaw;
            public double CameraYaw;
            public double CameraPitch;
            public double YawError;
            public double PitchError;
        }

        private sealed class PathFollowPollState
        {
            public readonly object SyncRoot = new object();
            public Thread Thread;
            public KmBoxInputWorker InputWorker;
            public bool StopRequested;
            public bool HasLocal;
            public LocalPlayerInfo Local;
            public string Error;
            public DateTime LastReadTime;
            public long ReadCount;
            public int TargetIndex = -1;
            public PathFollowPoint TargetPoint;
            public double ReachDistance;
            public bool HasMetrics;
            public double Distance;
            public double TargetYaw;
            public double CameraYaw;
            public double CameraPitch;
            public double YawError;
            public double PitchError;
            public bool IsMoving;
            public bool HasMoveStop;
            public bool MoveStopRequested;
            public int MoveStopTargetIndex;
            public LocalPlayerInfo MoveStopLocal;
            public double MoveStopDistance;
            public string MoveStopReason;
            public PathFollowDistanceBudget TravelBudget;
            public double TravelBudgetMovedDistance;
            public double TravelBudgetTotalDistance;
            public bool HasArrived;
            public int ArrivedTargetIndex;
            public LocalPlayerInfo ArrivedLocal;
            public double ArrivedDistance;
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

        private sealed class FaceTargetOptions
        {
            public string KmBoxPortName;
            public int DurationMs;
            public int SettleMs;
            public int MouseDownWarmupMs;
            public int MouseHoldAfterMoveMs;
            public int MaxAttempts;
            public int CalibrationPixels;
            public int CalibrationMs;
            public int MinCorrectionPixels;
            public double ToleranceDegrees;
            public double PixelsPerDegreeAbs;
            public double FixedTargetYawDegrees;
            public double FixedTargetPitchDegrees;
            public double PitchPixelsPerDegreeAbs;
            public double TargetYawOffsetDegrees;
            public string CameraPitchUnit;
            public string CameraYawUnit;
            public string BearingMode;
            public string YawFeedbackMode;
            public string DragMoveMode;
            public int DragStepPixels;
            public int DragFineStepPixels;
            public int DragStepDelayMs;
            public int DragPrimePixels;
            public int DragTailPixels;
            public int DragRampMaxPixels;
            public int DragLeadMs;
            public int DragMainMs;
            public int DragTailMs;
            public int AdaptiveReadSettleMs;
            public int AdaptiveReadTimeoutMs;
            public int AdaptiveStableMs;
            public int AdaptiveStableTimeoutMs;
            public int AdaptiveMaxBatches;
            public int TwoPassMaxPasses;
            public double AdaptiveFineThresholdDegrees;
            public double AdaptiveMidThresholdDegrees;
            public double AdaptiveMinYawDeltaDegrees;
            public double AdaptiveFinalThresholdDegrees;
            public double AdaptiveFinalPixelsPerDegreeAbs;
            public int AdaptiveCoarseBatchPixels;
            public int AdaptiveMidBatchPixels;
            public int AdaptiveFineStepPixels;
            public bool UseFixedYaw;
            public bool PitchInvertMouse;
            public bool AutoCalibrate;
            public bool ApplyMouse;
        }

        private interface IKmBoxInput
        {
            void KeyDown(int key);
            void KeyUp(int key);
            void MouseDown(KmMouseButton button);
            void MouseUp(KmMouseButton button);
            void MoveRelative(int deltaX, int deltaY);
        }

        private sealed class KmBoxClientInput : IKmBoxInput
        {
            private readonly KmBoxClient _client;

            public KmBoxClientInput(KmBoxClient client)
            {
                _client = client;
            }

            public void KeyDown(int key) { _client.KeyDown(key); }
            public void KeyUp(int key) { _client.KeyUp(key); }
            public void MouseDown(KmMouseButton button) { _client.MouseDown(button); }
            public void MouseUp(KmMouseButton button) { _client.MouseUp(button); }
            public void MoveRelative(int deltaX, int deltaY) { _client.MoveRelative(deltaX, deltaY); }
        }

        private sealed class KmBoxInputWorker : IKmBoxInput, IDisposable
        {
            private sealed class InputCommand
            {
                public Action<KmBoxClient> Action;
                public ManualResetEventSlim Done;
                public Exception Error;
            }

            private readonly object _syncRoot = new object();
            private readonly Queue<InputCommand> _urgentQueue = new Queue<InputCommand>();
            private readonly Queue<InputCommand> _normalQueue = new Queue<InputCommand>();
            private readonly KmBoxOptions _options;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
            private Exception _startupError;
            private bool _stopping;
            private bool _disposed;

            public KmBoxInputWorker(KmBoxOptions options)
            {
                _options = options;
                _thread = new Thread(Run);
                _thread.IsBackground = true;
                _thread.Name = "KMBox input worker";
                _thread.Start();
                _ready.Wait();
                if (_startupError != null)
                {
                    throw new InvalidOperationException("KMBox input worker failed to start.", _startupError);
                }
            }

            public void KeyDown(int key)
            {
                Invoke(km => km.KeyDown(key), false);
            }

            public void KeyUp(int key)
            {
                Invoke(km => km.KeyUp(key), false);
            }

            public void MouseDown(KmMouseButton button)
            {
                Invoke(km => km.MouseDown(button), false);
            }

            public void MouseUp(KmMouseButton button)
            {
                Invoke(km => km.MouseUp(button), false);
            }

            public void MoveRelative(int deltaX, int deltaY)
            {
                Invoke(km => km.MoveRelative(deltaX, deltaY), false);
            }

            public void RequestPathFollowArrivedStop()
            {
                RequestPathFollowStop("poll_arrived");
            }

            public void RequestPathFollowStop(string reason)
            {
                Post(km =>
                {
                    km.KeyUp(KmBoxKeyCodes.KEY_W);
                    Console.WriteLine("PathFollowInputUrgentKey W=up Reason=" + reason);
                    km.MouseUp(KmMouseButton.Right);
                    Console.WriteLine("PathFollowInputUrgentMouse Right=up Reason=" + reason);
                }, true);
            }

            public void Dispose()
            {
                lock (_syncRoot)
                {
                    _disposed = true;
                    _stopping = true;
                    Monitor.PulseAll(_syncRoot);
                }

                if (Thread.CurrentThread != _thread)
                {
                    _thread.Join(2000);
                }

                _ready.Dispose();
            }

            private void Invoke(Action<KmBoxClient> action, bool urgent)
            {
                var command = new InputCommand
                {
                    Action = action,
                    Done = new ManualResetEventSlim(false)
                };

                Enqueue(command, urgent);
                command.Done.Wait();
                command.Done.Dispose();
                if (command.Error != null)
                {
                    throw command.Error;
                }
            }

            private void Post(Action<KmBoxClient> action, bool urgent)
            {
                var command = new InputCommand { Action = action };
                Enqueue(command, urgent);
            }

            private void Enqueue(InputCommand command, bool urgent)
            {
                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (urgent)
                    {
                        _urgentQueue.Enqueue(command);
                    }
                    else
                    {
                        _normalQueue.Enqueue(command);
                    }

                    Monitor.PulseAll(_syncRoot);
                }
            }

            private InputCommand Dequeue()
            {
                lock (_syncRoot)
                {
                    while (!_stopping && _urgentQueue.Count == 0 && _normalQueue.Count == 0)
                    {
                        Monitor.Wait(_syncRoot);
                    }

                    if (_urgentQueue.Count > 0)
                    {
                        return _urgentQueue.Dequeue();
                    }

                    if (_normalQueue.Count > 0)
                    {
                        return _normalQueue.Dequeue();
                    }

                    return null;
                }
            }

            private void Run()
            {
                try
                {
                    using (var km = new KmBoxClient(_options))
                    {
                        km.Open();
                        _ready.Set();
                        while (true)
                        {
                            InputCommand command = Dequeue();
                            if (command == null)
                            {
                                break;
                            }

                            try
                            {
                                command.Action(km);
                            }
                            catch (Exception ex)
                            {
                                command.Error = ex;
                            }
                            finally
                            {
                                if (command.Done != null)
                                {
                                    command.Done.Set();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _startupError = ex;
                    _ready.Set();
                }
            }
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

        private static void RunPlayerOffsetProbeTest(VmmProcess process, ulong gameBase)
        {
            ulong offset = ReadRvaFromEnv("AION_PLAYER_OFFSET_PROBE_OFFSET", 0x6C8);
            int byteCount = ClampInt(ReadIntFromEnv("AION_PLAYER_OFFSET_PROBE_BYTES", 64), 8, 256);
            int beforeBytes = ClampInt(ReadIntFromEnv("AION_PLAYER_OFFSET_PROBE_BEFORE_BYTES", 32), 0, 128);

            Console.WriteLine("AION player offset probe.");
            Console.WriteLine("Offset=Player+0x" + offset.ToString("X") +
                              " Bytes=" + byteCount +
                              " BeforeBytes=" + beforeBytes +
                              " EnvOffset=AION_PLAYER_OFFSET_PROBE_OFFSET");

            LocalPlayerInfo info;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out info, out error))
            {
                Console.WriteLine("Read failed: " + error);
                return;
            }

            if (info.Entity == 0)
            {
                Console.WriteLine("Read failed: local player entity pointer is empty.");
                return;
            }

            ulong address = info.Entity + offset;
            Console.WriteLine("Local EntityId=" + info.EntityId +
                              " TargetId=" + info.TargetEntityId +
                              " PlayerEntity=" + FormatAddress(info.Entity) +
                              " ProbeAddress=" + FormatAddress(address) +
                              " Pos=" + FormatPosition(info));

            byte[] bytes;
            if (!TryReadBytes(process, address, byteCount, out bytes))
            {
                Console.WriteLine("Read failed: unable to read " + byteCount + " bytes at " + FormatAddress(address));
                return;
            }

            PrintScalarProbeValues(process, address, bytes);

            string inlineText;
            if (TryReadUtf16String(process, address, 64, out inlineText) && IsUsefulProbeText(inlineText))
            {
                Console.WriteLine("InlineUtf16=\"" + EscapeProbeText(inlineText) + "\"");
            }

            string msvcText;
            if (TryReadMsvcWString(process, address, out msvcText) && IsUsefulProbeText(msvcText))
            {
                Console.WriteLine("InlineMsvcWString=\"" + EscapeProbeText(msvcText) + "\"");
            }

            ulong pointer;
            if (TryReadPointer(process, address, out pointer))
            {
                Console.WriteLine("PointerCandidate=" + FormatAddress(pointer));

                byte[] pointedBytes;
                if (TryReadBytes(process, pointer, Math.Min(byteCount, 64), out pointedBytes))
                {
                    Console.WriteLine("PointerBytes:");
                    PrintHexDump(pointer, pointedBytes, 16);
                }

                string pointerUtf16;
                if (TryReadUtf16String(process, pointer, 64, out pointerUtf16) && IsUsefulProbeText(pointerUtf16))
                {
                    Console.WriteLine("PointerUtf16=\"" + EscapeProbeText(pointerUtf16) + "\"");
                }

                string pointerMsvcText;
                if (TryReadMsvcWString(process, pointer, out pointerMsvcText) && IsUsefulProbeText(pointerMsvcText))
                {
                    Console.WriteLine("PointerMsvcWString=\"" + EscapeProbeText(pointerMsvcText) + "\"");
                }
            }
            else
            {
                Console.WriteLine("PointerCandidate=n/a");
            }

            Console.WriteLine("BytesAtOffset:");
            PrintHexDump(address, bytes, 16);

            if (beforeBytes > 0 && address >= info.Entity + (ulong)beforeBytes)
            {
                ulong aroundAddress = address - (ulong)beforeBytes;
                int aroundBytes = ClampInt(beforeBytes + byteCount, byteCount, 384);
                byte[] around;
                if (TryReadBytes(process, aroundAddress, aroundBytes, out around))
                {
                    Console.WriteLine("AroundBytes Player+0x" + (offset - (ulong)beforeBytes).ToString("X") +
                                      "..Player+0x" + (offset + (ulong)byteCount - 1).ToString("X") + ":");
                    PrintHexDump(aroundAddress, around, 16);
                }
            }
        }

        private static void RunPlayerFloatScanTest(VmmProcess process, ulong gameBase)
        {
            ulong startOffset = ReadRvaFromEnv("AION_PLAYER_FLOAT_SCAN_START_OFFSET", 0x600);
            ulong endOffset = ReadRvaFromEnv("AION_PLAYER_FLOAT_SCAN_END_OFFSET", 0x780);
            if (endOffset < startOffset)
            {
                ulong tmp = startOffset;
                startOffset = endOffset;
                endOffset = tmp;
            }

            double target = ReadSignedDoubleFromEnv("AION_PLAYER_FLOAT_SCAN_TARGET", 6.0);
            double tolerance = ReadDoubleFromEnv("AION_PLAYER_FLOAT_SCAN_TOLERANCE", 0.01);
            int stride = ClampInt(ReadIntFromEnv("AION_PLAYER_FLOAT_SCAN_STRIDE", 4), 1, 16);
            int contextCount = ClampInt(ReadIntFromEnv("AION_PLAYER_FLOAT_SCAN_CONTEXT", 3), 0, 8);
            ulong byteLength = endOffset - startOffset + 4;
            if (byteLength > 0x4000)
            {
                byteLength = 0x4000;
                endOffset = startOffset + byteLength - 4;
            }

            Console.WriteLine("AION player float scan.");
            Console.WriteLine("Target=" + target.ToString("F6") +
                              " Tolerance=" + tolerance.ToString("F6") +
                              " Range=Player+0x" + startOffset.ToString("X") + "..Player+0x" + endOffset.ToString("X") +
                              " Stride=" + stride +
                              " Context=" + contextCount);

            LocalPlayerInfo info;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out info, out error))
            {
                Console.WriteLine("Read failed: " + error);
                return;
            }

            if (info.Entity == 0)
            {
                Console.WriteLine("Read failed: local player entity pointer is empty.");
                return;
            }

            ulong startAddress = info.Entity + startOffset;
            byte[] bytes;
            if (!TryReadBytes(process, startAddress, (int)byteLength, out bytes))
            {
                Console.WriteLine("Read failed: unable to read range at " + FormatAddress(startAddress) +
                                  " Length=" + byteLength);
                return;
            }

            Console.WriteLine("Local EntityId=" + info.EntityId +
                              " PlayerEntity=" + FormatAddress(info.Entity) +
                              " Pos=" + FormatPosition(info));

            int matches = 0;
            for (int i = 0; i + 4 <= bytes.Length; i += stride)
            {
                float value = BitConverter.ToSingle(bytes, i);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    continue;
                }

                double delta = Math.Abs(value - target);
                if (delta <= tolerance)
                {
                    matches++;
                    ulong offset = startOffset + (ulong)i;
                    ulong address = startAddress + (ulong)i;
                    Console.WriteLine("Match#" + matches +
                                      " Offset=Player+0x" + offset.ToString("X") +
                                      " Address=" + FormatAddress(address) +
                                      " F32=" + value.ToString("R") +
                                      " Delta=" + delta.ToString("F6") +
                                      " Context=" + FormatFloatScanContext(bytes, i, startOffset, contextCount));
                }
            }

            Console.WriteLine("FloatScanResult Matches=" + matches);
            if (matches == 0)
            {
                Console.WriteLine("No match in this range. Try widening AION_PLAYER_FLOAT_SCAN_START_OFFSET/END_OFFSET or set AION_PLAYER_FLOAT_SCAN_STRIDE=1.");
            }
        }

        private static void RunCameraWatchTest(VmmProcess process, ulong gameBase)
        {
            ulong startRva = ReadRvaFromEnv("AION_CAMERA_WATCH_START_RVA", 0xD1AD00);
            ulong endRva = ReadRvaFromEnv("AION_CAMERA_WATCH_END_RVA", 0xD1AD60);
            int intervalMs = ReadIntFromEnv("AION_CAMERA_WATCH_INTERVAL_MS", 250);
            double threshold = ReadDoubleFromEnv("AION_CAMERA_WATCH_THRESHOLD", 0.0001);

            if (endRva < startRva)
            {
                ulong swap = startRva;
                startRva = endRva;
                endRva = swap;
            }

            intervalMs = ClampInt(intervalMs, 50, 5000);
            Console.WriteLine("AION camera watch test.");
            Console.WriteLine("Watching float RVAs Game.dll+0x" + startRva.ToString("X") +
                              "..0x" + endRva.ToString("X") +
                              ", IntervalMs=" + intervalMs +
                              ", Threshold=" + threshold.ToString("F6") + ".");
            Console.WriteLine("Manually rotate/pitch the camera now. Press any key to stop.");
            Console.WriteLine("Current configured camera RVAs: pitch=0x" + GetCameraPitchRva().ToString("X") +
                              " roll=0x" + GetCameraRollRva().ToString("X") +
                              " yaw=0x" + GetCameraYawRva().ToString("X") + ".");
            Console.WriteLine("Special camera RVAs: mode=0x" + SpecialCameraModeRva.ToString("X") +
                              " pitch=0x" + SpecialCameraPitchRva.ToString("X") +
                              " roll=0x" + SpecialCameraRollRva.ToString("X") +
                              " yaw=0x" + SpecialCameraYawRva.ToString("X") +
                              " distance=0x" + SpecialCameraDistanceRva.ToString("X") + ".");

            var lastValues = new Dictionary<ulong, float>();
            for (ulong rva = startRva; rva <= endRva; rva += 4)
            {
                float value;
                if (TryReadSingle(process, gameBase + rva, out value) && IsReasonableFloat(value))
                {
                    lastValues[rva] = value;
                }

                if (rva > ulong.MaxValue - 4)
                {
                    break;
                }
            }

            PrintCameraWatchSnapshot(process, gameBase, "initial");
            PrintCameraWatchValues("initial floats", lastValues);

            while (!Console.KeyAvailable)
            {
                var changed = new Dictionary<ulong, float>();
                var nextValues = new Dictionary<ulong, float>();

                for (ulong rva = startRva; rva <= endRva; rva += 4)
                {
                    float value;
                    if (TryReadSingle(process, gameBase + rva, out value) && IsReasonableFloat(value))
                    {
                        nextValues[rva] = value;
                        float previous;
                        if (!lastValues.TryGetValue(rva, out previous) ||
                            Math.Abs(value - previous) > threshold)
                        {
                            changed[rva] = value;
                        }
                    }

                    if (rva > ulong.MaxValue - 4)
                    {
                        break;
                    }
                }

                if (changed.Count > 0)
                {
                    PrintCameraWatchSnapshot(process, gameBase, "changed");
                    PrintCameraWatchValues("changed floats", changed);
                }

                lastValues = nextValues;
                Thread.Sleep(intervalMs);
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

        private static void RunPathRecorderWindow(VmmProcess process, ulong gameBase)
        {
            Console.WriteLine("AION path recorder WPF test.");
            Console.WriteLine("Reading local player position and recording path points.");

            var app = System.Windows.Application.Current ?? new System.Windows.Application();
            app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            var window = new PathRecorderWindow(() => ReadPathRecorderSnapshot(process, gameBase));
            app.Run(window);
        }

        private static PathRecorderReadResult ReadPathRecorderSnapshot(VmmProcess process, ulong gameBase)
        {
            LocalPlayerInfo info;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out info, out error))
            {
                return PathRecorderReadResult.Fail(error);
            }

            if (!info.HasPosition)
            {
                return PathRecorderReadResult.Fail("local player position is not available");
            }

            var snapshot = new PathRecorderSnapshot
            {
                ReadTime = DateTime.Now,
                EntityId = info.EntityId,
                X = info.X,
                Y = info.Y,
                Z = info.Z,
                HasTransform = info.HasTransform,
                ActorYaw = info.HasTransform ? NormalizeSignedDegrees(info.Transform.WorldAngles.Z) : 0.0,
                CameraPitch = info.CameraPitch,
                CameraYaw = info.CameraYaw
            };

            return PathRecorderReadResult.Ok(snapshot);
        }

        private static PathFollowPollState StartPathFollowPoller(
            VmmProcess process,
            ulong gameBase,
            int intervalMs,
            LocalPlayerInfo initialLocal,
            FaceTargetOptions options,
            double targetPitch,
            KmBoxInputWorker inputWorker)
        {
            var state = new PathFollowPollState();
            lock (state.SyncRoot)
            {
                state.HasLocal = initialLocal.HasPosition;
                state.Local = initialLocal;
                state.LastReadTime = DateTime.Now;
                state.InputWorker = inputWorker;
            }

            state.Thread = new Thread(() =>
            {
                while (true)
                {
                    lock (state.SyncRoot)
                    {
                        if (state.StopRequested)
                        {
                            return;
                        }
                    }

                    LocalPlayerInfo polledLocal;
                    string error;
                    bool ok = TryReadLocalPlayerInfo(process, gameBase, out polledLocal, out error) && polledLocal.HasPosition;
                    bool requestArrivedStop = false;
                    bool requestMoveStop = false;
                    string moveStopReason = null;
                    lock (state.SyncRoot)
                    {
                        if (ok)
                        {
                            state.Local = polledLocal;
                            state.HasLocal = true;
                            state.Error = null;
                            state.LastReadTime = DateTime.Now;
                            state.ReadCount++;
                            UpdatePathFollowPollMetricsLocked(state, options, targetPitch);
                            if (state.HasMoveStop && !state.MoveStopRequested)
                            {
                                state.MoveStopRequested = true;
                                moveStopReason = string.IsNullOrWhiteSpace(state.MoveStopReason) ? "move_stop" : state.MoveStopReason;
                                requestMoveStop = true;
                            }
                            if (state.TargetIndex >= 0 && !state.HasArrived)
                            {
                                if (state.Distance <= state.ReachDistance)
                                {
                                    state.HasArrived = true;
                                    state.ArrivedTargetIndex = state.TargetIndex;
                                    state.ArrivedLocal = polledLocal;
                                    state.ArrivedDistance = state.Distance;
                                    requestArrivedStop = true;
                                }
                            }
                        }
                        else
                        {
                            state.Error = error ?? "local position unavailable";
                        }
                    }

                    if (requestArrivedStop && state.InputWorker != null)
                    {
                        state.InputWorker.RequestPathFollowArrivedStop();
                    }

                    if (requestMoveStop && state.InputWorker != null)
                    {
                        state.InputWorker.RequestPathFollowStop(moveStopReason ?? "move_stop");
                    }

                    Thread.Sleep(intervalMs);
                }
            });

            state.Thread.IsBackground = true;
            state.Thread.Name = "AION path follow poll";
            state.Thread.Start();
            return state;
        }

        private static bool TryGetPathFollowPolledLocal(
            PathFollowPollState state,
            out LocalPlayerInfo local,
            out string error,
            out long ageMs)
        {
            lock (state.SyncRoot)
            {
                local = state.Local;
                error = state.Error;
                if (!state.HasLocal)
                {
                    ageMs = 0;
                    return false;
                }

                ageMs = Math.Max(0, (long)(DateTime.Now - state.LastReadTime).TotalMilliseconds);
                return true;
            }
        }

        private static bool TryGetPathFollowPollSnapshot(
            PathFollowPollState state,
            out PathFollowPollSnapshot snapshot,
            out string error)
        {
            lock (state.SyncRoot)
            {
                error = state.Error;
                snapshot = new PathFollowPollSnapshot();
                if (!state.HasLocal || !state.HasMetrics)
                {
                    return false;
                }

                snapshot.Local = state.Local;
                snapshot.AgeMs = Math.Max(0, (long)(DateTime.Now - state.LastReadTime).TotalMilliseconds);
                snapshot.ReadCount = state.ReadCount;
                snapshot.Distance = state.Distance;
                snapshot.TargetYaw = state.TargetYaw;
                snapshot.CameraYaw = state.CameraYaw;
                snapshot.CameraPitch = state.CameraPitch;
                snapshot.YawError = state.YawError;
                snapshot.PitchError = state.PitchError;
                return true;
            }
        }

        private static bool TryWaitForPathFollowPollSnapshot(
            PathFollowPollState state,
            long previousReadCount,
            int timeoutMs,
            out PathFollowPollSnapshot snapshot,
            out string error)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
            do
            {
                if (TryGetPathFollowPollSnapshot(state, out snapshot, out error) &&
                    (snapshot.ReadCount != previousReadCount || timeoutMs <= 0))
                {
                    return true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                Thread.Sleep(1);
            }
            while (true);

            return TryGetPathFollowPollSnapshot(state, out snapshot, out error);
        }

        private static void SetPathFollowPollTarget(
            PathFollowPollState state,
            int targetIndex,
            PathFollowPoint target,
            double reachDistance,
            FaceTargetOptions options,
            double targetPitch)
        {
            lock (state.SyncRoot)
            {
                if (state.TargetIndex != targetIndex)
                {
                    state.HasArrived = false;
                    state.HasMetrics = false;
                    state.HasMoveStop = false;
                    state.MoveStopRequested = false;
                    state.MoveStopReason = null;
                    state.TravelBudget = null;
                    state.TravelBudgetMovedDistance = 0.0;
                    state.TravelBudgetTotalDistance = 0.0;
                }

                state.TargetIndex = targetIndex;
                state.TargetPoint = target;
                state.ReachDistance = reachDistance;
                UpdatePathFollowPollMetricsLocked(state, options, targetPitch);
            }
        }

        private static void UpdatePathFollowPollMetricsLocked(
            PathFollowPollState state,
            FaceTargetOptions options,
            double targetPitch)
        {
            if (!state.HasLocal || state.TargetIndex < 0 || !state.Local.HasPosition)
            {
                state.HasMetrics = false;
                return;
            }

            state.Distance = GetHorizontalDistance(state.Local, state.TargetPoint);
            state.TargetYaw = CalculatePathTargetYawDegrees(state.Local, state.TargetPoint, options);
            state.CameraYaw = GetCameraYawDegrees(state.Local.CameraYaw, options);
            state.CameraPitch = GetCameraPitchDegrees(state.Local.CameraPitch, options);
            state.YawError = NormalizeSignedDegrees(state.TargetYaw - state.CameraYaw);
            state.PitchError = targetPitch - state.CameraPitch;
            state.HasMetrics = true;
            UpdatePathFollowTravelBudgetLocked(state);
        }

        private static void UpdatePathFollowTravelBudgetLocked(PathFollowPollState state)
        {
            if (!state.IsMoving || state.HasArrived || state.HasMoveStop || state.TargetIndex < 0 || !state.HasMetrics || !state.Local.HasPosition)
            {
                return;
            }

            if (state.TravelBudget == null)
            {
                StartPathFollowTravelBudgetLocked(state);
            }

            if (state.TravelBudget == null)
            {
                return;
            }

            PathFollowDistanceBudgetResult result = state.TravelBudget.Update(ToPathFollowBudgetPoint(state.Local), state.ReachDistance);
            state.TravelBudgetMovedDistance = result.MovedDistance;
            state.TravelBudgetTotalDistance = result.TotalDistance;
            if (result.Decision == PathFollowDistanceBudgetDecision.TravelBudgetExceeded)
            {
                state.HasMoveStop = true;
                state.MoveStopReason = "travel_budget_exhausted";
                state.MoveStopTargetIndex = state.TargetIndex;
                state.MoveStopLocal = state.Local;
                state.MoveStopDistance = result.DistanceToTarget;
            }
        }

        private static void StartPathFollowTravelBudgetLocked(PathFollowPollState state)
        {
            if (state.TargetIndex < 0 || !state.Local.HasPosition)
            {
                state.TravelBudget = null;
                state.TravelBudgetMovedDistance = 0.0;
                state.TravelBudgetTotalDistance = 0.0;
                return;
            }

            state.TravelBudget = new PathFollowDistanceBudget(
                ToPathFollowBudgetPoint(state.Local),
                ToPathFollowBudgetPoint(state.TargetPoint));
            state.TravelBudgetMovedDistance = 0.0;
            state.TravelBudgetTotalDistance = state.TravelBudget.TotalDistance;
        }

        private static bool TryConsumePathFollowArrived(
            PathFollowPollState state,
            int targetIndex,
            out LocalPlayerInfo arrivedLocal,
            out double arrivedDistance)
        {
            lock (state.SyncRoot)
            {
                if (state.HasArrived && state.ArrivedTargetIndex == targetIndex)
                {
                    arrivedLocal = state.ArrivedLocal;
                    arrivedDistance = state.ArrivedDistance;
                    state.HasArrived = false;
                    return true;
                }
            }

            arrivedLocal = new LocalPlayerInfo();
            arrivedDistance = 0.0;
            return false;
        }

        private static bool TryConsumePathFollowMoveStop(
            PathFollowPollState state,
            int targetIndex,
            out LocalPlayerInfo stopLocal,
            out double stopDistance,
            out string stopReason)
        {
            lock (state.SyncRoot)
            {
                if (state.HasMoveStop && state.MoveStopTargetIndex == targetIndex)
                {
                    stopLocal = state.MoveStopLocal;
                    stopDistance = state.MoveStopDistance;
                    stopReason = string.IsNullOrWhiteSpace(state.MoveStopReason) ? "move_stop" : state.MoveStopReason;
                    state.HasMoveStop = false;
                    state.MoveStopRequested = false;
                    state.MoveStopReason = null;
                    return true;
                }
            }

            stopLocal = new LocalPlayerInfo();
            stopDistance = 0.0;
            stopReason = null;
            return false;
        }

        private static bool IsPathFollowStopPending(
            PathFollowPollState state,
            out string reason)
        {
            reason = null;
            if (state == null)
            {
                return false;
            }

            lock (state.SyncRoot)
            {
                if (state.StopRequested)
                {
                    reason = "poller_stop_requested";
                    return true;
                }

                if (state.HasMoveStop)
                {
                    reason = string.IsNullOrWhiteSpace(state.MoveStopReason) ? "move_stop" : state.MoveStopReason;
                    return true;
                }
            }

            return false;
        }

        private static void SetPathFollowMoving(PathFollowPollState state, bool moving)
        {
            if (state == null)
            {
                return;
            }

            lock (state.SyncRoot)
            {
                if (state.IsMoving == moving)
                {
                    return;
                }

                state.IsMoving = moving;
                if (moving)
                {
                    StartPathFollowTravelBudgetLocked(state);
                }
                else
                {
                    state.HasMoveStop = false;
                    state.MoveStopRequested = false;
                    state.MoveStopReason = null;
                    state.TravelBudget = null;
                    state.TravelBudgetMovedDistance = 0.0;
                    state.TravelBudgetTotalDistance = 0.0;
                }
            }
        }

        private static bool TryMarkPathFollowArrivedNow(
            PathFollowPollState state,
            out int arrivedTargetIndex,
            out double arrivedDistance)
        {
            lock (state.SyncRoot)
            {
                if (state.TargetIndex >= 0 && state.HasMetrics && state.Distance <= state.ReachDistance)
                {
                    state.HasArrived = true;
                    state.ArrivedTargetIndex = state.TargetIndex;
                    state.ArrivedLocal = state.Local;
                    state.ArrivedDistance = state.Distance;
                    arrivedTargetIndex = state.TargetIndex;
                    arrivedDistance = state.Distance;
                    return true;
                }
            }

            arrivedTargetIndex = -1;
            arrivedDistance = 0.0;
            return false;
        }

        private static void StopPathFollowPoller(PathFollowPollState state)
        {
            if (state == null)
            {
                return;
            }

            lock (state.SyncRoot)
            {
                state.StopRequested = true;
            }

            if (state.Thread != null && state.Thread.IsAlive)
            {
                state.Thread.Join(1000);
            }
        }

        private static void RunPathFollowTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            List<PathFollowPoint> path = LoadPathFollowPoints();
            if (path.Count == 0)
            {
                Console.WriteLine("AION path follow test failed: no path points.");
                return;
            }

            double reachDistance = Math.Max(0.2, ReadDoubleFromEnv("AION_PATH_FOLLOW_REACH_DISTANCE", 3.0));
            double yawTolerance = Math.Max(0.1, ReadDoubleFromEnv("AION_PATH_FOLLOW_YAW_TOLERANCE_DEG", 10.0));
            double microYawTolerance = Math.Max(0.1, ReadDoubleFromEnv("AION_PATH_FOLLOW_MICRO_YAW_TOLERANCE_DEG", 1.5));
            double restartYawThreshold = Math.Max(0.1, ReadDoubleFromEnv("AION_PATH_FOLLOW_RESTART_YAW_DEG", 15.0));
            double disableMoveAdjustDistance = Math.Max(0.0, ReadDoubleFromEnv("AION_PATH_FOLLOW_DISABLE_MOVE_ADJUST_DISTANCE", 15.0));
            double pitchTolerance = Math.Max(0.5, ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_TOLERANCE_DEG", 5.0));
            double targetPitch = ClampDouble(ReadSignedDoubleFromEnv("AION_PATH_FOLLOW_PITCH_DEG", options.FixedTargetPitchDegrees), -65.0, 85.0);
            options.ToleranceDegrees = Math.Min(options.ToleranceDegrees, yawTolerance);
            int tickMs = ClampInt(ReadIntFromEnv("AION_PATH_FOLLOW_TICK_MS", 10), 1, 2000);
            int logIntervalMs = ClampInt(ReadIntFromEnv("AION_PATH_FOLLOW_LOG_MS", 500), 0, 10000);
            int maxRunMs = ReadIntFromEnv("AION_PATH_FOLLOW_MAX_MS", 0);
            maxRunMs = maxRunMs <= 0 ? 0 : ClampInt(maxRunMs, 1000, 600000);
            bool loop = ReadBoolFromEnv("AION_PATH_FOLLOW_LOOP", true);
            bool reverse = ReadBoolFromEnv("AION_PATH_FOLLOW_REVERSE", false);
            if (reverse)
            {
                path.Reverse();
            }

            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error) || !local.HasPosition)
            {
                Console.WriteLine("AION path follow test failed: " + (error ?? "local position unavailable"));
                return;
            }

            string startMode = Environment.GetEnvironmentVariable("AION_PATH_FOLLOW_START_MODE");
            int targetIndex = ResolvePathFollowStartIndex(path, local, reachDistance, startMode);

            Console.WriteLine("AION path follow test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " Points=" + path.Count +
                              " StartTargetIndex=" + (targetIndex + 1) +
                              " StartMode=" + FormatPathFollowStartMode(startMode) +
                              " Reverse=" + (reverse ? "yes" : "no") +
                              " ReachDistance=" + reachDistance.ToString("F2") +
                              " TickMs=" + tickMs +
                              " LogMs=" + logIntervalMs +
                              " MaxRunMs=" + maxRunMs +
                              " Loop=" + (loop ? "yes" : "no") +
                              " BearingMode=" + options.BearingMode +
                              " TargetPitchDeg=" + targetPitch.ToString("F2") +
                              " YawPixelsPerDeg=" + options.PixelsPerDegreeAbs.ToString("F2") +
                              " PitchPixelsPerDeg=" + options.PitchPixelsPerDegreeAbs.ToString("F2") +
                              " YawToleranceDeg=" + yawTolerance.ToString("F2") +
                              " MicroYawToleranceDeg=" + microYawTolerance.ToString("F2") +
                              " RestartYawDeg=" + restartYawThreshold.ToString("F2") +
                              " DisableMoveAdjustDistance=" + disableMoveAdjustDistance.ToString("F2") +
                              " PitchToleranceDeg=" + pitchTolerance.ToString("F2"));
            Console.WriteLine("Camera yaw faces the next path point; pitch is fixed; W moves forward.");

            PathFollowPollState pollState = null;
            using (var input = new KmBoxInputWorker(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                pollState = StartPathFollowPoller(process, gameBase, tickMs, local, options, targetPitch, input);
                try
                {
                    bool wDown = false;
                    bool rightMouseDown = false;
                    var stopwatch = Stopwatch.StartNew();
                    long nextTickLogAt = 0;
                    try
                    {
                        input.KeyUp(KmBoxKeyCodes.KEY_W);
                        SetPathFollowMoving(pollState, false);
                        Console.WriteLine("PathFollowKey W=up Reason=start");

                        while (maxRunMs <= 0 || stopwatch.ElapsedMilliseconds <= maxRunMs)
                        {
                            PathFollowPoint target = path[targetIndex];
                            SetPathFollowPollTarget(pollState, targetIndex, target, reachDistance, options, targetPitch);

                            PathFollowPollSnapshot snapshot;
                            if (!TryGetPathFollowPollSnapshot(pollState, out snapshot, out error) || !snapshot.Local.HasPosition)
                            {
                                Console.WriteLine("PathFollowReadFailed Reason=" + (error ?? "local position unavailable"));
                                break;
                            }

                            local = snapshot.Local;
                            double distance = snapshot.Distance;
                            LocalPlayerInfo arrivedLocal;
                            double arrivedDistance;
                            bool latchedArrived = TryConsumePathFollowArrived(pollState, targetIndex, out arrivedLocal, out arrivedDistance);
                            if (latchedArrived)
                            {
                                local = arrivedLocal;
                                distance = arrivedDistance;
                            }

                            if (latchedArrived || distance <= reachDistance)
                            {
                                input.KeyUp(KmBoxKeyCodes.KEY_W);
                                wDown = false;
                                SetPathFollowMoving(pollState, false);
                                Console.WriteLine("PathFollowKey W=up Reason=arrived");
                                input.MouseUp(KmMouseButton.Right);
                                rightMouseDown = false;
                                Console.WriteLine("PathFollowMouse Right=up Reason=arrived");

                                Console.WriteLine("PathFollowArrived Index=" + (targetIndex + 1) +
                                                  " Distance=" + distance.ToString("F2") +
                                                  " Source=" + (latchedArrived ? "latched" : "current") +
                                                  " Pos=(" + local.X.ToString("F3") + "," + local.Y.ToString("F3") + "," + local.Z.ToString("F3") + ")");

                                if (targetIndex + 1 < path.Count)
                                {
                                    targetIndex++;
                                    continue;
                                }

                                if (loop)
                                {
                                    targetIndex = 0;
                                    continue;
                                }

                                Console.WriteLine("PathFollowResult=finished ElapsedMs=" + stopwatch.ElapsedMilliseconds);
                                break;
                            }

                            LocalPlayerInfo moveStopLocal;
                            double moveStopDistance;
                            string moveStopReason;
                            bool moveStopped = TryConsumePathFollowMoveStop(pollState, targetIndex, out moveStopLocal, out moveStopDistance, out moveStopReason);
                            if (moveStopped)
                            {
                                local = moveStopLocal;
                                distance = moveStopDistance;
                                input.KeyUp(KmBoxKeyCodes.KEY_W);
                                Console.WriteLine("PathFollowKey W=up Reason=" + moveStopReason);
                                wDown = false;
                                SetPathFollowMoving(pollState, false);
                                input.MouseUp(KmMouseButton.Right);
                                rightMouseDown = false;
                                Console.WriteLine("PathFollowMouse Right=up Reason=" + moveStopReason);

                                Console.WriteLine("PathFollowMoveStopped" +
                                                  " TargetIndex=" + (targetIndex + 1) +
                                                  " Reason=" + moveStopReason +
                                                  " Distance=" + distance.ToString("F2") +
                                                  " Pos=(" + local.X.ToString("F3") + "," + local.Y.ToString("F3") + "," + local.Z.ToString("F3") + ")");
                                Thread.Sleep(tickMs);
                                continue;
                            }

                            double targetYaw = snapshot.TargetYaw;
                            double currentYaw = snapshot.CameraYaw;
                            double currentPitch = snapshot.CameraPitch;
                            double yawError = snapshot.YawError;
                            double pitchError = snapshot.PitchError;
                            bool restartMoveForLargeYaw = PathFollowMoveControl.ShouldRestartMoveForYaw(wDown, yawError, restartYawThreshold);
                            if (restartMoveForLargeYaw)
                            {
                                input.KeyUp(KmBoxKeyCodes.KEY_W);
                                wDown = false;
                                SetPathFollowMoving(pollState, false);
                                Console.WriteLine("PathFollowKey W=up Reason=restart_yaw_error" +
                                                  " YawErrorDeg=" + yawError.ToString("F2") +
                                                  " RestartYawDeg=" + restartYawThreshold.ToString("F2"));
                                input.MouseUp(KmMouseButton.Right);
                                rightMouseDown = false;
                                Console.WriteLine("PathFollowMouse Right=up Reason=restart_yaw_error");
                            }

                            double activeYawTolerance = wDown ? microYawTolerance : yawTolerance;
                            bool moveAdjustDisabledByDistance = PathFollowMoveControl.ShouldDisableMoveAdjustByDistance(wDown, distance, disableMoveAdjustDistance);
                            bool needsTurn = PathFollowMoveControl.ShouldTurn(
                                restartMoveForLargeYaw,
                                moveAdjustDisabledByDistance,
                                yawError,
                                pitchError,
                                activeYawTolerance,
                                pitchTolerance);
                            long elapsedMs = stopwatch.ElapsedMilliseconds;
                            bool shouldLogTick = needsTurn || (logIntervalMs > 0 && elapsedMs >= nextTickLogAt);

                            if (shouldLogTick)
                            {
                                Console.WriteLine("PathFollowTick" +
                                                  " ElapsedMs=" + elapsedMs +
                                                  " TargetIndex=" + (targetIndex + 1) +
                                                  " Distance=" + distance.ToString("F2") +
                                                  " Pos=(" + local.X.ToString("F3") + "," + local.Y.ToString("F3") + "," + local.Z.ToString("F3") + ")" +
                                                  " CameraYaw=" + currentYaw.ToString("F2") +
                                                  " TargetYaw=" + targetYaw.ToString("F2") +
                                                  " YawError=" + yawError.ToString("F2") +
                                                  " ActiveYawTolerance=" + activeYawTolerance.ToString("F2") +
                                                  " CameraPitch=" + currentPitch.ToString("F2") +
                                                  " TargetPitch=" + targetPitch.ToString("F2") +
                                                  " PitchError=" + pitchError.ToString("F2") +
                                                  " PollAgeMs=" + snapshot.AgeMs +
                                                  " MoveAdjustDisabledByDistance=" + (moveAdjustDisabledByDistance ? "yes" : "no") +
                                                  " NeedsTurn=" + (needsTurn ? "yes" : "no"));
                                if (logIntervalMs > 0)
                                {
                                    nextTickLogAt = elapsedMs + logIntervalMs;
                                }
                            }

                            if (needsTurn)
                            {
                                double finalYawError;
                                double finalPitchError;
                                bool turnAligned;
                                if (wDown)
                                {
                                    if (!rightMouseDown)
                                    {
                                        input.MouseDown(KmMouseButton.Right);
                                        rightMouseDown = true;
                                        Console.WriteLine("PathFollowMouse Right=down Reason=move_angle_adjust");
                                        if (options.MouseDownWarmupMs > 0)
                                        {
                                            Thread.Sleep(options.MouseDownWarmupMs);
                                        }
                                    }

                                    turnAligned = DragPathFollowAngleAdjust(
                                        process,
                                        gameBase,
                                        input,
                                        pollState,
                                        target,
                                        targetPitch,
                                        options.PixelsPerDegreeAbs,
                                        options.PitchPixelsPerDegreeAbs,
                                        options,
                                        microYawTolerance,
                                        pitchTolerance,
                                        restartYawThreshold,
                                        out bool arrivedDuringAdjust,
                                        out bool restartDuringAdjust);
                                    Console.WriteLine("PathFollowMoveAngleAdjust W=down Aligned=" + (turnAligned ? "yes" : "no"));
                                    if (arrivedDuringAdjust)
                                    {
                                        wDown = false;
                                        SetPathFollowMoving(pollState, false);
                                        input.MouseUp(KmMouseButton.Right);
                                        rightMouseDown = false;
                                        Console.WriteLine("PathFollowMouse Right=up Reason=arrived_during_angle_adjust");

                                        continue;
                                    }

                                    if (restartDuringAdjust)
                                    {
                                        input.KeyUp(KmBoxKeyCodes.KEY_W);
                                        wDown = false;
                                        SetPathFollowMoving(pollState, false);
                                        Console.WriteLine("PathFollowKey W=up Reason=restart_yaw_error_during_adjust");
                                        input.MouseUp(KmMouseButton.Right);
                                        rightMouseDown = false;
                                        Console.WriteLine("PathFollowMouse Right=up Reason=restart_yaw_error_during_adjust");

                                        turnAligned = DragCameraCombinedTwoPassFixedYawPitch(
                                            process,
                                            gameBase,
                                            input,
                                            targetYaw,
                                            targetPitch,
                                            options.PixelsPerDegreeAbs,
                                            options.PitchPixelsPerDegreeAbs,
                                            options,
                                            false,
                                            true,
                                            true,
                                            out finalYawError,
                                            out finalPitchError);
                                        if (turnAligned)
                                        {
                                            rightMouseDown = true;
                                            Console.WriteLine("PathFollowMouse Right=held Reason=restart_turn_complete");
                                        }

                                        if (!turnAligned)
                                        {
                                            Console.WriteLine("PathFollowMoveHold Reason=restart_angle_not_aligned");
                                            Thread.Sleep(tickMs);
                                            continue;
                                        }
                                    }
                                }
                                else
                                {
                                    turnAligned = DragCameraCombinedTwoPassFixedYawPitch(
                                        process,
                                        gameBase,
                                        input,
                                        targetYaw,
                                        targetPitch,
                                        options.PixelsPerDegreeAbs,
                                        options.PitchPixelsPerDegreeAbs,
                                        options,
                                        false,
                                        true,
                                        true,
                                        out finalYawError,
                                        out finalPitchError);
                                    if (turnAligned)
                                    {
                                        rightMouseDown = true;
                                        Console.WriteLine("PathFollowMouse Right=held Reason=turn_complete");
                                    }

                                    if (!turnAligned)
                                    {
                                        Console.WriteLine("PathFollowMoveHold Reason=angle_not_aligned");
                                        Thread.Sleep(tickMs);
                                        continue;
                                    }
                                }
                            }

                            if (!wDown)
                            {
                                if (!rightMouseDown)
                                {
                                    input.MouseDown(KmMouseButton.Right);
                                    rightMouseDown = true;
                                    Console.WriteLine("PathFollowMouse Right=down Reason=move_start");
                                    if (options.MouseDownWarmupMs > 0)
                                    {
                                        Thread.Sleep(options.MouseDownWarmupMs);
                                    }
                                }

                                input.KeyUp(KmBoxKeyCodes.KEY_W);
                                input.KeyDown(KmBoxKeyCodes.KEY_W);
                                wDown = true;
                                SetPathFollowMoving(pollState, true);
                                Console.WriteLine("PathFollowKey W=down");
                            }

                            Thread.Sleep(tickMs);
                        }
                    }
                    finally
                    {
                        if (wDown)
                        {
                            try
                            {
                                input.KeyUp(KmBoxKeyCodes.KEY_W);
                                SetPathFollowMoving(pollState, false);
                                Console.WriteLine("PathFollowKey W=up");
                            }
                            catch
                            {
                            }
                        }

                        if (rightMouseDown)
                        {
                            try
                            {
                                input.MouseUp(KmMouseButton.Right);
                                Console.WriteLine("PathFollowMouse Right=up");
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                finally
                {
                    StopPathFollowPoller(pollState);
                }
            }
        }

        private static void RunFaceTargetCameraTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            if (options.UseFixedYaw)
            {
                RunFixedCameraYawTest(process, gameBase, options);
                return;
            }

            Console.WriteLine("AION face locked target camera test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " DurationMs=" + options.DurationMs +
                              " ToleranceDeg=" + options.ToleranceDegrees.ToString("F2") +
                              " BearingMode=" + options.BearingMode +
                              " CameraYawUnit=" + options.CameraYawUnit +
                              " YawFeedback=" + options.YawFeedbackMode +
                              " TargetYawOffsetDeg=" + options.TargetYawOffsetDegrees.ToString("F2") +
                              " ApplyMouse=" + (options.ApplyMouse ? "yes" : "no") +
                              " AutoCalibrate=" + (options.AutoCalibrate ? "yes" : "no") +
                              " MaxAttempts=" + options.MaxAttempts +
                              " MinCorrectionPixels=" + options.MinCorrectionPixels +
                              " DragMoveMode=" + options.DragMoveMode +
                              " TwoPassMaxPasses=" + options.TwoPassMaxPasses +
                              " DragChunkPixels=" + options.DragStepPixels +
                              " DragPrimePixels=" + options.DragPrimePixels +
                              " DragTailPixels=" + options.DragTailPixels +
                              " DragDistributionPeak=" + options.DragRampMaxPixels +
                              " DragStepPixels=" + options.DragStepPixels +
                              " DragFineStepPixels=" + options.DragFineStepPixels +
                              " DragPhasesMs=" + options.DragLeadMs + "/" + options.DragMainMs + "/" + options.DragTailMs +
                              " AdaptiveFine/MidDeg=" + options.AdaptiveFineThresholdDegrees.ToString("F2") + "/" + options.AdaptiveMidThresholdDegrees.ToString("F2") +
                              " AdaptiveMinYawDeltaDeg=" + options.AdaptiveMinYawDeltaDegrees.ToString("F2") +
                              " AdaptiveFinalDeg/Px=" + options.AdaptiveFinalThresholdDegrees.ToString("F2") + "/" + options.AdaptiveFinalPixelsPerDegreeAbs.ToString("F2") +
                              " AdaptiveBatch=" + options.AdaptiveFineStepPixels + "/" + options.AdaptiveMidBatchPixels + "/" + options.AdaptiveCoarseBatchPixels +
                              " AdaptiveReadSettleMs=" + options.AdaptiveReadSettleMs +
                              " AdaptiveReadTimeoutMs=" + options.AdaptiveReadTimeoutMs +
                              " AdaptiveStableMs=" + options.AdaptiveStableMs +
                              " AdaptiveStableTimeoutMs=" + options.AdaptiveStableTimeoutMs +
                              " AdaptiveMaxBatches=" + options.AdaptiveMaxBatches +
                              " DragStepDelayMs=" + options.DragStepDelayMs +
                              " MouseDownWarmupMs=" + options.MouseDownWarmupMs +
                              " MouseHoldAfterMoveMs=" + options.MouseHoldAfterMoveMs);
            Console.WriteLine("Lock a target first. Do not move the mouse while this test is running.");

            LocalPlayerInfo local;
            LockedTargetMonsterInfo target;
            string error;
            if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            PrintFaceTargetState("initial", local, target, options);
            if (!options.ApplyMouse)
            {
                Console.WriteLine("AION_FACE_TARGET_APPLY_MOUSE=0, snapshot only.");
                return;
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                double pixelsPerDegreeAbs = options.PixelsPerDegreeAbs;
                if (options.AutoCalibrate)
                {
                    double calibratedPixelsPerDegree;
                    if (TryCalibrateFaceTargetMouse(process, gameBase, km, options, out calibratedPixelsPerDegree, out error))
                    {
                        pixelsPerDegreeAbs = Math.Abs(calibratedPixelsPerDegree);
                    }
                    else
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Calibration failed: " + error);
                        Console.WriteLine("Continue with manual PixelsPerDegreeAbs=" + pixelsPerDegreeAbs.ToString("F4") + ".");
                    }
                }

                Console.WriteLine("PixelsPerDegreeAbs=" + pixelsPerDegreeAbs.ToString("F4") +
                                  " (set AION_FACE_TARGET_PIXELS_PER_DEG_ABS to tune; ErrorDeg>0 => drag left/dx<0).");

                bool success = false;
                int attemptsUsed = 0;
                double finalError = 0.0;
                for (int attempt = 1; attempt <= options.MaxAttempts; attempt++)
                {
                    attemptsUsed = attempt;
                    if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                        return;
                    }

                    string yawSource;
                    double currentYaw = GetFeedbackYawDegrees(local, options, out yawSource);
                    double targetYaw = CalculateTargetYawDegrees(local, target, options);
                    double errorDegrees = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalError = errorDegrees;
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "Attempt=" + attempt +
                        " ControlYawSource=" + yawSource +
                        " ControlYaw=" + currentYaw.ToString("F2") +
                        " CameraYaw=" + GetCameraYawDegrees(local.CameraYaw, options).ToString("F2") +
                        " ActorYaw=" + FormatActorYaw(local) +
                        " TargetYaw=" + targetYaw.ToString("F2") +
                        " ErrorDeg=" + errorDegrees.ToString("F2") +
                        " Distance=" + FormatDistance(target));

                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        success = true;
                        break;
                    }

                    if (IsAdaptiveDragMode(options))
                    {
                        finalError = DragCameraHorizontalAdaptiveFixedYaw(process, gameBase, km, targetYaw, pixelsPerDegreeAbs, options);
                        success = Math.Abs(finalError) <= options.ToleranceDegrees;
                        break;
                    }

                    if (IsTwoPassChunkDragMode(options))
                    {
                        finalError = DragCameraHorizontalTwoPassFaceTarget(process, gameBase, km, pixelsPerDegreeAbs, options);
                        success = Math.Abs(finalError) <= options.ToleranceDegrees;
                        break;
                    }

                    double rawDx;
                    bool minApplied;
                    int dx = CalculateCameraDragDx(errorDegrees, pixelsPerDegreeAbs, options, out rawDx, out minApplied);

                    Console.WriteLine(
                        "Attempt=" + attempt +
                        " YawDecision=" + (errorDegrees > 0 ? "increase" : "decrease") +
                        " MouseDrag=" + (dx < 0 ? "left" : "right") +
                        " RawDx=" + rawDx.ToString("F2") +
                        " Dx=" + dx +
                        " MoveCommands=" + EstimateDragMoveCommandCount(dx, options) +
                        " MinApplied=" + (minApplied ? "yes" : "no"));
                    DragCameraHorizontal(km, dx, options);
                    if (options.SettleMs > 0)
                    {
                        Thread.Sleep(options.SettleMs);
                    }
                }

                if (TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                {
                    PrintFaceTargetState("final", local, target, options);
                    string yawSource;
                    finalError = NormalizeSignedDegrees(CalculateTargetYawDegrees(local, target, options) - GetFeedbackYawDegrees(local, options, out yawSource));
                    success = success || Math.Abs(finalError) <= options.ToleranceDegrees;
                    Console.WriteLine("Result=" + (success ? "aligned" : "not_aligned") +
                                      " FinalErrorDeg=" + finalError.ToString("F2") +
                                      " AttemptsUsed=" + attemptsUsed);
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                }
            }
        }

        private static void RunFaceTargetCombinedCameraTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            double targetPitch = ClampDouble(options.FixedTargetPitchDegrees, -65.0, 85.0);
            Console.WriteLine("AION face locked target combined yaw/pitch camera test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " DurationMs=" + options.DurationMs +
                              " ToleranceDeg=" + options.ToleranceDegrees.ToString("F2") +
                              " BearingMode=" + options.BearingMode +
                              " CameraYawUnit=" + options.CameraYawUnit +
                              " CameraPitchUnit=" + options.CameraPitchUnit +
                              " FixedTargetPitchDeg=" + targetPitch.ToString("F2") +
                              " YawPixelsPerDeg=" + options.PixelsPerDegreeAbs.ToString("F4") +
                              " PitchPixelsPerDeg=" + options.PitchPixelsPerDegreeAbs.ToString("F4") +
                              " DragMoveMode=" + options.DragMoveMode +
                              " TwoPassMaxPasses=" + options.TwoPassMaxPasses +
                              " MaxChunkPixels=" + options.DragStepPixels +
                              " DragPrimePixels=" + options.DragPrimePixels +
                              " DragTailPixels=" + options.DragTailPixels +
                              " PitchInvertMouse=" + (options.PitchInvertMouse ? "yes" : "no") +
                              " ApplyMouse=" + (options.ApplyMouse ? "yes" : "no"));
            Console.WriteLine("Lock a target first. Yaw comes from target coordinates; pitch is fixed.");

            LocalPlayerInfo local;
            LockedTargetMonsterInfo target;
            string error;
            if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            PrintFaceTargetCombinedState("initial", local, target, targetPitch, options);
            if (!options.ApplyMouse)
            {
                Console.WriteLine("AION_FACE_TARGET_APPLY_MOUSE=0, snapshot only.");
                return;
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                double finalYawError = 0.0;
                double finalPitchError = 0.0;
                bool success = DragCameraCombinedTwoPassFaceTarget(
                    process,
                    gameBase,
                    km,
                    targetPitch,
                    options.PixelsPerDegreeAbs,
                    options.PitchPixelsPerDegreeAbs,
                    options,
                    out finalYawError,
                    out finalPitchError);

                if (TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                {
                    PrintFaceTargetCombinedState("final", local, target, targetPitch, options);
                    finalYawError = NormalizeSignedDegrees(CalculateTargetYawDegrees(local, target, options) - GetCameraYawDegrees(local.CameraYaw, options));
                    finalPitchError = targetPitch - GetCameraPitchDegrees(local.CameraPitch, options);
                    success = success ||
                              (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                               Math.Abs(finalPitchError) <= options.ToleranceDegrees);
                    Console.WriteLine("Result=" + (success ? "aligned" : "not_aligned") +
                                      " FinalYawErrorDeg=" + finalYawError.ToString("F2") +
                                      " FinalPitchErrorDeg=" + finalPitchError.ToString("F2"));
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                }
            }
        }

        private static void RunTargetYawProbeTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            Console.WriteLine("AION target yaw probe test.");
            Console.WriteLine("Lock a target and manually face the camera toward it. This test only reads coordinates and camera yaw.");

            LocalPlayerInfo local;
            LockedTargetMonsterInfo target;
            string error;
            if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            double cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            double actorYaw;
            bool hasActorYaw = TryGetActorYawDegrees(local, out actorYaw);
            double dx = target.X - local.X;
            double dy = target.Y - local.Y;
            double dz = target.Z - local.Z;
            double horizontalDistance = Math.Sqrt(dx * dx + dy * dy);
            double currentConfiguredTargetYaw = CalculateTargetYawDegrees(local, target, options);

            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                "Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " Target=(" + target.X.ToString("F2") + "," + target.Y.ToString("F2") + "," + target.Z.ToString("F2") + ")" +
                " Delta=(" + dx.ToString("F2") + "," + dy.ToString("F2") + "," + dz.ToString("F2") + ")" +
                " HorizontalDistance=" + horizontalDistance.ToString("F2") +
                " CameraYaw=" + cameraYaw.ToString("F2") +
                " ActorYaw=" + (hasActorYaw ? actorYaw.ToString("F2") : "n/a") +
                " ConfigBearingMode=" + options.BearingMode +
                " ConfigYawOffset=" + options.TargetYawOffsetDegrees.ToString("F2") +
                " ConfigTargetYaw=" + currentConfiguredTargetYaw.ToString("F2") +
                " ConfigErrorDeg=" + NormalizeSignedDegrees(currentConfiguredTargetYaw - cameraYaw).ToString("F2"));

            PrintTargetYawCandidate("yx", Math.Atan2(dx, dy), cameraYaw);
            PrintTargetYawCandidate("xy", Math.Atan2(dy, dx), cameraYaw);
            PrintTargetYawCandidate("-yx", Math.Atan2(-dx, dy), cameraYaw);
            PrintTargetYawCandidate("y-x", Math.Atan2(dx, -dy), cameraYaw);
            PrintTargetYawCandidate("-xy", Math.Atan2(-dy, dx), cameraYaw);
            PrintTargetYawCandidate("x-y", Math.Atan2(dy, -dx), cameraYaw);
        }

        private static void RunFixedCameraYawTest(VmmProcess process, ulong gameBase, FaceTargetOptions options)
        {
            double targetYaw = NormalizeSignedDegrees(options.FixedTargetYawDegrees);
            Console.WriteLine("AION fixed camera yaw test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " DurationMs=" + options.DurationMs +
                              " ToleranceDeg=" + options.ToleranceDegrees.ToString("F2") +
                              " CameraYawUnit=" + options.CameraYawUnit +
                              " FixedTargetYawDeg=" + targetYaw.ToString("F2") +
                              " ApplyMouse=" + (options.ApplyMouse ? "yes" : "no") +
                              " AutoCalibrate=" + (options.AutoCalibrate ? "yes" : "no") +
                              " MaxAttempts=" + options.MaxAttempts +
                              " MinCorrectionPixels=" + options.MinCorrectionPixels +
                              " DragMoveMode=" + options.DragMoveMode +
                              " TwoPassMaxPasses=" + options.TwoPassMaxPasses +
                              " DragChunkPixels=" + options.DragStepPixels +
                              " DragPrimePixels=" + options.DragPrimePixels +
                              " DragTailPixels=" + options.DragTailPixels +
                              " DragDistributionPeak=" + options.DragRampMaxPixels +
                              " DragStepPixels=" + options.DragStepPixels +
                              " DragFineStepPixels=" + options.DragFineStepPixels +
                              " DragPhasesMs=" + options.DragLeadMs + "/" + options.DragMainMs + "/" + options.DragTailMs +
                              " AdaptiveFine/MidDeg=" + options.AdaptiveFineThresholdDegrees.ToString("F2") + "/" + options.AdaptiveMidThresholdDegrees.ToString("F2") +
                              " AdaptiveMinYawDeltaDeg=" + options.AdaptiveMinYawDeltaDegrees.ToString("F2") +
                              " AdaptiveFinalDeg/Px=" + options.AdaptiveFinalThresholdDegrees.ToString("F2") + "/" + options.AdaptiveFinalPixelsPerDegreeAbs.ToString("F2") +
                              " AdaptiveBatch=" + options.AdaptiveFineStepPixels + "/" + options.AdaptiveMidBatchPixels + "/" + options.AdaptiveCoarseBatchPixels +
                              " AdaptiveReadSettleMs=" + options.AdaptiveReadSettleMs +
                              " AdaptiveReadTimeoutMs=" + options.AdaptiveReadTimeoutMs +
                              " AdaptiveStableMs=" + options.AdaptiveStableMs +
                              " AdaptiveStableTimeoutMs=" + options.AdaptiveStableTimeoutMs +
                              " AdaptiveMaxBatches=" + options.AdaptiveMaxBatches +
                              " DragStepDelayMs=" + options.DragStepDelayMs +
                              " MouseDownWarmupMs=" + options.MouseDownWarmupMs +
                              " MouseHoldAfterMoveMs=" + options.MouseHoldAfterMoveMs);
            Console.WriteLine("No locked target required. Do not move the mouse while this test is running.");

            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            PrintFixedCameraYawState("initial", local, targetYaw, options);
            if (!options.ApplyMouse)
            {
                Console.WriteLine("AION_FACE_TARGET_APPLY_MOUSE=0, snapshot only.");
                return;
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                if (options.AutoCalibrate)
                {
                    Console.WriteLine("AutoCalibrate is skipped in fixed yaw test; using manual PixelsPerDegreeAbs.");
                }

                double pixelsPerDegreeAbs = options.PixelsPerDegreeAbs;
                Console.WriteLine("PixelsPerDegreeAbs=" + pixelsPerDegreeAbs.ToString("F4") +
                                  " (set AION_FACE_TARGET_PIXELS_PER_DEG_ABS to tune; ErrorDeg>0 => drag left/dx<0).");

                bool success = false;
                int attemptsUsed = 0;
                double finalError = 0.0;
                for (int attempt = 1; attempt <= options.MaxAttempts; attempt++)
                {
                    attemptsUsed = attempt;
                    if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                        return;
                    }

                    double currentYaw = GetCameraYawDegrees(local.CameraYaw, options);
                    double errorDegrees = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalError = errorDegrees;
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "Attempt=" + attempt +
                        " CameraYaw=" + currentYaw.ToString("F2") +
                        " ActorYaw=" + FormatActorYaw(local) +
                        " TargetYaw=" + targetYaw.ToString("F2") +
                        " ErrorDeg=" + errorDegrees.ToString("F2"));

                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        success = true;
                        break;
                    }

                    if (IsAdaptiveDragMode(options))
                    {
                        finalError = DragCameraHorizontalAdaptiveFixedYaw(process, gameBase, km, targetYaw, pixelsPerDegreeAbs, options);
                        success = Math.Abs(finalError) <= options.ToleranceDegrees;
                        break;
                    }

                    if (IsTwoPassChunkDragMode(options))
                    {
                        finalError = DragCameraHorizontalTwoPassFixedYaw(process, gameBase, km, targetYaw, pixelsPerDegreeAbs, options);
                        success = Math.Abs(finalError) <= options.ToleranceDegrees;
                        break;
                    }

                    double rawDx;
                    bool minApplied;
                    int dx = CalculateCameraDragDx(errorDegrees, pixelsPerDegreeAbs, options, out rawDx, out minApplied);

                    Console.WriteLine(
                        "Attempt=" + attempt +
                        " YawDecision=" + (errorDegrees > 0 ? "increase" : "decrease") +
                        " MouseDrag=" + (dx < 0 ? "left" : "right") +
                        " RawDx=" + rawDx.ToString("F2") +
                        " Dx=" + dx +
                        " MoveCommands=" + EstimateDragMoveCommandCount(dx, options) +
                        " MinApplied=" + (minApplied ? "yes" : "no"));
                    DragCameraHorizontal(km, dx, options);
                    if (options.SettleMs > 0)
                    {
                        Thread.Sleep(options.SettleMs);
                    }
                }

                if (TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                {
                    PrintFixedCameraYawState("final", local, targetYaw, options);
                    finalError = NormalizeSignedDegrees(targetYaw - GetCameraYawDegrees(local.CameraYaw, options));
                    success = success || Math.Abs(finalError) <= options.ToleranceDegrees;
                    Console.WriteLine("Result=" + (success ? "aligned" : "not_aligned") +
                                      " FinalErrorDeg=" + finalError.ToString("F2") +
                                      " AttemptsUsed=" + attemptsUsed);
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                }
            }
        }

        private static void RunFixedCameraYawPitchTest(VmmProcess process, ulong gameBase, FaceTargetOptions options)
        {
            double targetYaw = NormalizeSignedDegrees(options.FixedTargetYawDegrees);
            double targetPitch = ClampDouble(options.FixedTargetPitchDegrees, -65.0, 85.0);
            Console.WriteLine("AION fixed camera yaw/pitch combined test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " DurationMs=" + options.DurationMs +
                              " ToleranceDeg=" + options.ToleranceDegrees.ToString("F2") +
                              " CameraYawUnit=" + options.CameraYawUnit +
                              " CameraPitchUnit=" + options.CameraPitchUnit +
                              " FixedTargetYawDeg=" + targetYaw.ToString("F2") +
                              " FixedTargetPitchDeg=" + targetPitch.ToString("F2") +
                              " YawPixelsPerDeg=" + options.PixelsPerDegreeAbs.ToString("F4") +
                              " PitchPixelsPerDeg=" + options.PitchPixelsPerDegreeAbs.ToString("F4") +
                              " DragMoveMode=" + options.DragMoveMode +
                              " TwoPassMaxPasses=" + options.TwoPassMaxPasses +
                              " MaxChunkPixels=" + options.DragStepPixels +
                              " DragPrimePixels=" + options.DragPrimePixels +
                              " DragTailPixels=" + options.DragTailPixels +
                              " PitchInvertMouse=" + (options.PitchInvertMouse ? "yes" : "no") +
                              " ApplyMouse=" + (options.ApplyMouse ? "yes" : "no"));
            Console.WriteLine("No locked target required. Do not move the mouse while this test is running.");

            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            PrintFixedCameraYawPitchState("initial", local, targetYaw, targetPitch, options);
            if (!options.ApplyMouse)
            {
                Console.WriteLine("AION_FACE_TARGET_APPLY_MOUSE=0, snapshot only.");
                return;
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                double finalYawError;
                double finalPitchError;
                bool success = DragCameraCombinedTwoPassFixedYawPitch(
                    process,
                    gameBase,
                    km,
                    targetYaw,
                    targetPitch,
                    options.PixelsPerDegreeAbs,
                    options.PitchPixelsPerDegreeAbs,
                    options,
                    false,
                    false,
                    false,
                    out finalYawError,
                    out finalPitchError);

                if (TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                {
                    PrintFixedCameraYawPitchState("final", local, targetYaw, targetPitch, options);
                    finalYawError = NormalizeSignedDegrees(targetYaw - GetCameraYawDegrees(local.CameraYaw, options));
                    finalPitchError = targetPitch - GetCameraPitchDegrees(local.CameraPitch, options);
                    success = success ||
                              (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                               Math.Abs(finalPitchError) <= options.ToleranceDegrees);
                    Console.WriteLine("Result=" + (success ? "aligned" : "not_aligned") +
                                      " FinalYawErrorDeg=" + finalYawError.ToString("F2") +
                                      " FinalPitchErrorDeg=" + finalPitchError.ToString("F2"));
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                }
            }
        }

        private static void RunFixedCameraPitchTest(VmmProcess process, ulong gameBase, FaceTargetOptions options)
        {
            double targetPitch = ClampDouble(options.FixedTargetPitchDegrees, -65.0, 85.0);
            Console.WriteLine("AION fixed camera pitch test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " DurationMs=" + options.DurationMs +
                              " ToleranceDeg=" + options.ToleranceDegrees.ToString("F2") +
                              " CameraPitchUnit=" + options.CameraPitchUnit +
                              " FixedTargetPitchDeg=" + targetPitch.ToString("F2") +
                              " ApplyMouse=" + (options.ApplyMouse ? "yes" : "no") +
                              " MaxAttempts=" + options.MaxAttempts +
                              " MinCorrectionPixels=" + options.MinCorrectionPixels +
                              " DragMoveMode=" + options.DragMoveMode +
                              " TwoPassMaxPasses=" + options.TwoPassMaxPasses +
                              " MaxChunkPixels=" + options.DragStepPixels +
                              " DragPrimePixels=" + options.DragPrimePixels +
                              " DragTailPixels=" + options.DragTailPixels +
                              " DragStepDelayMs=" + options.DragStepDelayMs +
                              " MouseDownWarmupMs=" + options.MouseDownWarmupMs +
                              " MouseHoldAfterMoveMs=" + options.MouseHoldAfterMoveMs +
                              " PitchInvertMouse=" + (options.PitchInvertMouse ? "yes" : "no"));
            Console.WriteLine("No locked target required. Do not move the mouse while this test is running.");

            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            PrintFixedCameraPitchState("initial", local, targetPitch, options);
            if (!options.ApplyMouse)
            {
                Console.WriteLine("AION_FACE_TARGET_APPLY_MOUSE=0, snapshot only.");
                return;
            }

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();

                double pixelsPerDegreeAbs = options.PitchPixelsPerDegreeAbs;
                Console.WriteLine("PitchPixelsPerDegreeAbs=" + pixelsPerDegreeAbs.ToString("F4") +
                                  " (set AION_CAMERA_PITCH_PIXELS_PER_DEG_ABS to tune; ErrorDeg>0 => drag " +
                                  (options.PitchInvertMouse ? "up/dy<0" : "down/dy>0") + ").");

                bool success = false;
                int attemptsUsed = 0;
                double finalError = 0.0;
                for (int attempt = 1; attempt <= options.MaxAttempts; attempt++)
                {
                    attemptsUsed = attempt;
                    if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                    {
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                        return;
                    }

                    double currentPitch = GetCameraPitchDegrees(local.CameraPitch, options);
                    double errorDegrees = targetPitch - currentPitch;
                    finalError = errorDegrees;
                    Console.WriteLine(
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                        "Attempt=" + attempt +
                        " CameraPitch=" + currentPitch.ToString("F2") +
                        " TargetPitch=" + targetPitch.ToString("F2") +
                        " ErrorDeg=" + errorDegrees.ToString("F2"));

                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        success = true;
                        break;
                    }

                    if (IsTwoPassChunkDragMode(options))
                    {
                        finalError = DragCameraVerticalTwoPassFixedPitch(process, gameBase, km, targetPitch, pixelsPerDegreeAbs, options);
                        success = Math.Abs(finalError) <= options.ToleranceDegrees;
                        break;
                    }

                    double rawDy;
                    bool minApplied;
                    int dy = CalculateCameraDragDy(errorDegrees, pixelsPerDegreeAbs, options, out rawDy, out minApplied);

                    Console.WriteLine(
                        "Attempt=" + attempt +
                        " PitchDecision=" + (errorDegrees > 0 ? "increase" : "decrease") +
                        " MouseDrag=" + (dy < 0 ? "up" : "down") +
                        " RawDy=" + rawDy.ToString("F2") +
                        " Dy=" + dy +
                        " MoveCommands=" + EstimateDragMoveCommandCount(dy, options) +
                        " MinApplied=" + (minApplied ? "yes" : "no"));
                    DragCameraVertical(km, dy, options);
                    if (options.SettleMs > 0)
                    {
                        Thread.Sleep(options.SettleMs);
                    }
                }

                if (TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                {
                    PrintFixedCameraPitchState("final", local, targetPitch, options);
                    finalError = targetPitch - GetCameraPitchDegrees(local.CameraPitch, options);
                    success = success || Math.Abs(finalError) <= options.ToleranceDegrees;
                    Console.WriteLine("Result=" + (success ? "aligned" : "not_aligned") +
                                      " FinalErrorDeg=" + finalError.ToString("F2") +
                                      " AttemptsUsed=" + attemptsUsed);
                }
                else
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                }
            }
        }

        private static void RunCameraPixelCalibrationTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            int totalPixels = ReadIntFromEnv("AION_CAMERA_PIXEL_CALIBRATION_TOTAL_PX", 1000);
            int stepPixels = ReadIntFromEnv("AION_CAMERA_PIXEL_CALIBRATION_STEP_PX", 1);
            int stepDelayMs = ReadIntFromEnv("AION_CAMERA_PIXEL_CALIBRATION_STEP_DELAY_MS", 0);
            totalPixels = ClampInt(totalPixels, -5000, 5000);
            stepPixels = ClampInt(stepPixels, -100, 100);
            stepDelayMs = ClampInt(stepDelayMs, 0, 50);

            if (totalPixels == 0)
            {
                totalPixels = 1000;
            }

            if (stepPixels == 0)
            {
                stepPixels = totalPixels > 0 ? 1 : -1;
            }

            if ((totalPixels > 0 && stepPixels < 0) ||
                (totalPixels < 0 && stepPixels > 0))
            {
                stepPixels = -stepPixels;
            }

            int steps = Math.Abs(totalPixels / stepPixels);
            int remainder = totalPixels - (steps * stepPixels);

            Console.WriteLine("AION camera pixel calibration test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " CameraYawUnit=" + options.CameraYawUnit +
                              " TotalPixels=" + totalPixels +
                              " StepPixels=" + stepPixels +
                              " Steps=" + steps +
                              " Remainder=" + remainder +
                              " StepDelayMs=" + stepDelayMs +
                              " MouseDownWarmupMs=" + options.MouseDownWarmupMs +
                              " MouseHoldAfterMoveMs=" + options.MouseHoldAfterMoveMs +
                              " SettleMs=" + options.SettleMs);
            Console.WriteLine("This test holds right mouse button and sends raw MoveRelative one step at a time.");

            LocalPlayerInfo before;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out before, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            double yawBefore = GetCameraYawDegrees(before.CameraYaw, options);
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] before" +
                " CameraYaw=" + yawBefore.ToString("F4") +
                " RawCameraYaw=" + before.CameraYaw.ToString("F4") +
                " ActorYaw=" + FormatActorYaw(before));

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                    Thread.Sleep(8);
                    km.MouseDown(KmMouseButton.Right);
                    if (options.MouseDownWarmupMs > 0)
                    {
                        Thread.Sleep(options.MouseDownWarmupMs);
                    }

                    for (int i = 0; i < steps; i++)
                    {
                        km.MoveRelative(stepPixels, 0);
                        if (stepDelayMs > 0)
                        {
                            Thread.Sleep(stepDelayMs);
                        }
                    }

                    if (remainder != 0)
                    {
                        km.MoveRelative(remainder, 0);
                    }

                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }
                }
                finally
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                    }
                    catch
                    {
                    }
                }
            }

            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            LocalPlayerInfo after;
            if (!TryReadLocalPlayerInfo(process, gameBase, out after, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                return;
            }

            double yawAfter = GetCameraYawDegrees(after.CameraYaw, options);
            double yawDelta = NormalizeSignedDegrees(yawAfter - yawBefore);
            double absYawDelta = Math.Abs(yawDelta);
            double pixelsPerDegree = absYawDelta > 0.0001 ? Math.Abs(totalPixels) / absYawDelta : 0.0;
            double degreesPerPixel = Math.Abs(totalPixels) > 0 ? yawDelta / Math.Abs(totalPixels) : 0.0;

            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] after" +
                " CameraYaw=" + yawAfter.ToString("F4") +
                " RawCameraYaw=" + after.CameraYaw.ToString("F4") +
                " ActorYaw=" + FormatActorYaw(after));
            Console.WriteLine("Result" +
                              " TotalPixels=" + totalPixels +
                              " YawBefore=" + yawBefore.ToString("F4") +
                              " YawAfter=" + yawAfter.ToString("F4") +
                              " DeltaDeg=" + yawDelta.ToString("F4") +
                              " AbsPixelsPerDeg=" + pixelsPerDegree.ToString("F4") +
                              " SignedDegPerPixel=" + degreesPerPixel.ToString("F6") +
                              " Direction=" + (yawDelta > 0 ? "yaw_increased" : yawDelta < 0 ? "yaw_decreased" : "unchanged"));
        }

        private static void RunCameraPitchPixelCalibrationTest(VmmProcess process, ulong gameBase)
        {
            FaceTargetOptions options = ReadFaceTargetOptions();
            int totalPixels = ReadIntFromEnv("AION_CAMERA_PITCH_PIXEL_CALIBRATION_TOTAL_PX", -500);
            int stepPixels = ReadIntFromEnv("AION_CAMERA_PITCH_PIXEL_CALIBRATION_STEP_PX", -1);
            int stepDelayMs = ReadIntFromEnv("AION_CAMERA_PITCH_PIXEL_CALIBRATION_STEP_DELAY_MS", 0);
            totalPixels = ClampInt(totalPixels, -5000, 5000);
            stepPixels = ClampInt(stepPixels, -100, 100);
            stepDelayMs = ClampInt(stepDelayMs, 0, 50);

            if (totalPixels == 0)
            {
                totalPixels = -500;
            }

            if (stepPixels == 0)
            {
                stepPixels = totalPixels > 0 ? 1 : -1;
            }

            if ((totalPixels > 0 && stepPixels < 0) ||
                (totalPixels < 0 && stepPixels > 0))
            {
                stepPixels = -stepPixels;
            }

            int steps = Math.Abs(totalPixels / stepPixels);
            int remainder = totalPixels - (steps * stepPixels);

            Console.WriteLine("AION camera pitch pixel calibration test.");
            Console.WriteLine("KmBoxPort=" + options.KmBoxPortName +
                              " CameraPitchUnit=" + options.CameraPitchUnit +
                              " TotalPixelsY=" + totalPixels +
                              " StepPixelsY=" + stepPixels +
                              " Steps=" + steps +
                              " RemainderY=" + remainder +
                              " StepDelayMs=" + stepDelayMs +
                              " MouseDownWarmupMs=" + options.MouseDownWarmupMs +
                              " MouseHoldAfterMoveMs=" + options.MouseHoldAfterMoveMs +
                              " SettleMs=" + options.SettleMs);
            Console.WriteLine("This test holds right mouse button and sends raw MoveRelative(0, dy) one step at a time.");

            LocalPlayerInfo before;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out before, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Read failed: " + error);
                return;
            }

            double pitchBefore = GetCameraPitchDegrees(before.CameraPitch, options);
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] before" +
                " CameraPitch=" + pitchBefore.ToString("F4") +
                " RawCameraPitch=" + before.CameraPitch.ToString("F4") +
                " CameraYaw=" + GetCameraYawDegrees(before.CameraYaw, options).ToString("F4"));

            using (var km = new KmBoxClient(new KmBoxOptions { PortName = options.KmBoxPortName }))
            {
                km.Open();
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                    Thread.Sleep(8);
                    km.MouseDown(KmMouseButton.Right);
                    if (options.MouseDownWarmupMs > 0)
                    {
                        Thread.Sleep(options.MouseDownWarmupMs);
                    }

                    for (int i = 0; i < steps; i++)
                    {
                        km.MoveRelative(0, stepPixels);
                        if (stepDelayMs > 0)
                        {
                            Thread.Sleep(stepDelayMs);
                        }
                    }

                    if (remainder != 0)
                    {
                        km.MoveRelative(0, remainder);
                    }

                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }
                }
                finally
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                    }
                    catch
                    {
                    }
                }
            }

            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            LocalPlayerInfo after;
            if (!TryReadLocalPlayerInfo(process, gameBase, out after, out error))
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Final read failed: " + error);
                return;
            }

            double pitchAfter = GetCameraPitchDegrees(after.CameraPitch, options);
            double pitchDelta = pitchAfter - pitchBefore;
            double absPitchDelta = Math.Abs(pitchDelta);
            double pixelsPerDegree = absPitchDelta > 0.0001 ? Math.Abs(totalPixels) / absPitchDelta : 0.0;
            double degreesPerPixel = Math.Abs(totalPixels) > 0 ? pitchDelta / Math.Abs(totalPixels) : 0.0;

            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] after" +
                " CameraPitch=" + pitchAfter.ToString("F4") +
                " RawCameraPitch=" + after.CameraPitch.ToString("F4") +
                " CameraYaw=" + GetCameraYawDegrees(after.CameraYaw, options).ToString("F4"));
            Console.WriteLine("Result" +
                              " TotalPixelsY=" + totalPixels +
                              " PitchBefore=" + pitchBefore.ToString("F4") +
                              " PitchAfter=" + pitchAfter.ToString("F4") +
                              " DeltaDeg=" + pitchDelta.ToString("F4") +
                              " AbsPixelsPerDeg=" + pixelsPerDegree.ToString("F4") +
                              " SignedDegPerPixel=" + degreesPerPixel.ToString("F6") +
                              " Direction=" + (pitchDelta > 0 ? "pitch_increased" : pitchDelta < 0 ? "pitch_decreased" : "unchanged"));
        }

        private static FaceTargetOptions ReadFaceTargetOptions()
        {
            string portName = Environment.GetEnvironmentVariable("KMBOX_PORT");
            if (string.IsNullOrWhiteSpace(portName))
            {
                portName = "COM11";
            }

            int durationMs = ReadIntFromEnv("AION_FACE_TARGET_DURATION_MS", 0);
            int settleMs = ReadIntFromEnv("AION_FACE_TARGET_SETTLE_MS", 20);
            int mouseDownWarmupMs = ReadIntFromEnv("AION_FACE_TARGET_MOUSE_DOWN_WARMUP_MS", 0);
            int mouseHoldAfterMoveMs = ReadIntFromEnv("AION_FACE_TARGET_MOUSE_HOLD_AFTER_MOVE_MS", 0);
            int maxAttempts = ReadIntFromEnv("AION_FACE_TARGET_MAX_ATTEMPTS", 1);
            int calibrationPixels = ReadIntFromEnv("AION_FACE_TARGET_CALIBRATION_PIXELS", 160);
            int calibrationMs = ReadIntFromEnv("AION_FACE_TARGET_CALIBRATION_MS", 120);
            int minCorrectionPixels = ReadIntFromEnv("AION_FACE_TARGET_MIN_CORRECTION_PIXELS", 70);
            int dragPrimePixels = ReadIntFromEnv("AION_FACE_TARGET_DRAG_PRIME_PIXELS", 5);
            int dragTailPixels = ReadIntFromEnv("AION_FACE_TARGET_DRAG_TAIL_PIXELS", 5);
            int dragRampMaxPixels = ReadIntFromEnv("AION_FACE_TARGET_DRAG_RAMP_MAX_PX", 6);
            int dragStepPixels = ReadIntFromEnv("AION_FACE_TARGET_DRAG_STEP_PX", 20);
            int dragFineStepPixels = ReadIntFromEnv("AION_FACE_TARGET_DRAG_FINE_STEP_PX", 10);
            int dragStepDelayMs = ReadIntFromEnv("AION_FACE_TARGET_DRAG_STEP_DELAY_MS", 0);
            int dragLeadMs = ReadIntFromEnv("AION_FACE_TARGET_DRAG_LEAD_MS", 200);
            int dragMainMs = ReadIntFromEnv("AION_FACE_TARGET_DRAG_MAIN_MS", 600);
            int dragTailMs = ReadIntFromEnv("AION_FACE_TARGET_DRAG_TAIL_MS", 200);
            int adaptiveReadSettleMs = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_READ_SETTLE_MS", 20);
            int adaptiveReadTimeoutMs = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_READ_TIMEOUT_MS", 900);
            int adaptiveStableMs = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_STABLE_MS", 160);
            int adaptiveStableTimeoutMs = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_STABLE_TIMEOUT_MS", 1500);
            int adaptiveMaxBatches = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_MAX_BATCHES", 40);
            int twoPassMaxPasses = ReadIntFromEnv("AION_FACE_TARGET_TWO_PASS_MAX_PASSES", 2);
            int adaptiveCoarseBatchPixels = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_COARSE_BATCH_PX", 100);
            int adaptiveMidBatchPixels = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_MID_BATCH_PX", 40);
            int adaptiveFineStepPixels = ReadIntFromEnv("AION_FACE_TARGET_ADAPTIVE_FINE_STEP_PX", 5);
            double toleranceDegrees = ReadDoubleFromEnv("AION_FACE_TARGET_TOLERANCE_DEG", 2.5);
            double adaptiveFineThresholdDegrees = ReadDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_FINE_THRESHOLD_DEG", 5.0);
            double adaptiveMidThresholdDegrees = ReadDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_MID_THRESHOLD_DEG", 20.0);
            double adaptiveMinYawDeltaDegrees = ReadDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_MIN_YAW_DELTA_DEG", 0.25);
            double adaptiveFinalThresholdDegrees = ReadDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_FINAL_THRESHOLD_DEG", 45.0);
            double adaptiveFinalPixelsPerDegreeAbs = Math.Abs(ReadSignedDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_FINAL_PIXELS_PER_DEG", 8.5));
            if (adaptiveFinalPixelsPerDegreeAbs < 0.0001)
            {
                adaptiveFinalPixelsPerDegreeAbs = 8.5;
            }

            double fixedTargetYawDegrees = ReadSignedDoubleFromEnv("AION_FACE_TARGET_FIXED_YAW_DEG", 90.0);
            double fixedTargetPitchDegrees = ReadSignedDoubleFromEnv("AION_CAMERA_FIXED_PITCH_DEG", 20.0);
            double pixelsPerDegreeAbs = Math.Abs(ReadSignedDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG_ABS", 0.0));
            if (pixelsPerDegreeAbs < 0.0001)
            {
                pixelsPerDegreeAbs = Math.Abs(ReadSignedDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG", 13.0));
            }

            if (pixelsPerDegreeAbs < 0.0001)
            {
                pixelsPerDegreeAbs = 13.0;
            }

            double pitchPixelsPerDegreeAbs = Math.Abs(ReadSignedDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG_ABS", 0.0));
            if (pitchPixelsPerDegreeAbs < 0.0001)
            {
                pitchPixelsPerDegreeAbs = Math.Abs(ReadSignedDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG", 13.0));
            }

            if (pitchPixelsPerDegreeAbs < 0.0001)
            {
                pitchPixelsPerDegreeAbs = 13.0;
            }

            double yawOffset = ReadSignedDoubleFromEnv("AION_FACE_TARGET_YAW_OFFSET_DEG", 0.0);
            string pitchUnit = Environment.GetEnvironmentVariable("AION_CAMERA_PITCH_UNIT");
            string yawUnit = Environment.GetEnvironmentVariable("AION_CAMERA_YAW_UNIT");
            string bearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
            string yawFeedbackMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_YAW_FEEDBACK");
            string dragMoveMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_DRAG_MOVE_MODE");

            return new FaceTargetOptions
            {
                KmBoxPortName = portName,
                DurationMs = ClampInt(durationMs, 0, 3000),
                SettleMs = ClampInt(settleMs, 0, 500),
                MouseDownWarmupMs = ClampInt(mouseDownWarmupMs, 0, 1000),
                MouseHoldAfterMoveMs = ClampInt(mouseHoldAfterMoveMs, 0, 1000),
                MaxAttempts = ClampInt(maxAttempts, 1, 8),
                CalibrationPixels = ClampInt(calibrationPixels, 20, 2000),
                CalibrationMs = ClampInt(calibrationMs, 20, 1000),
                MinCorrectionPixels = ClampInt(minCorrectionPixels, 0, 500),
                ToleranceDegrees = Math.Max(0.1, toleranceDegrees),
                PixelsPerDegreeAbs = pixelsPerDegreeAbs,
                FixedTargetYawDegrees = fixedTargetYawDegrees,
                FixedTargetPitchDegrees = ClampDouble(fixedTargetPitchDegrees, -65.0, 85.0),
                PitchPixelsPerDegreeAbs = pitchPixelsPerDegreeAbs,
                TargetYawOffsetDegrees = yawOffset,
                CameraPitchUnit = string.IsNullOrWhiteSpace(pitchUnit) ? "deg" : pitchUnit.Trim(),
                CameraYawUnit = string.IsNullOrWhiteSpace(yawUnit) ? "deg" : yawUnit.Trim(),
                BearingMode = string.IsNullOrWhiteSpace(bearingMode) ? "y-x" : bearingMode.Trim(),
                YawFeedbackMode = string.IsNullOrWhiteSpace(yawFeedbackMode) ? "camera" : yawFeedbackMode.Trim(),
                DragMoveMode = string.IsNullOrWhiteSpace(dragMoveMode) ? "two_pass_chunk" : dragMoveMode.Trim(),
                DragPrimePixels = ClampInt(dragPrimePixels, 0, 50),
                DragTailPixels = ClampInt(dragTailPixels, 0, 50),
                DragRampMaxPixels = ClampInt(dragRampMaxPixels, 1, 50),
                DragStepPixels = ClampInt(Math.Abs(dragStepPixels), 1, 500),
                DragFineStepPixels = ClampInt(Math.Abs(dragFineStepPixels), 1, 100),
                DragStepDelayMs = ClampInt(dragStepDelayMs, 0, 50),
                DragLeadMs = ClampInt(dragLeadMs, 0, 1000),
                DragMainMs = ClampInt(dragMainMs, 0, 2000),
                DragTailMs = ClampInt(dragTailMs, 0, 1000),
                AdaptiveReadSettleMs = ClampInt(adaptiveReadSettleMs, 0, 200),
                AdaptiveReadTimeoutMs = ClampInt(adaptiveReadTimeoutMs, 0, 2000),
                AdaptiveStableMs = ClampInt(adaptiveStableMs, 0, 1000),
                AdaptiveStableTimeoutMs = ClampInt(adaptiveStableTimeoutMs, 0, 5000),
                AdaptiveMaxBatches = ClampInt(adaptiveMaxBatches, 1, 200),
                TwoPassMaxPasses = ClampInt(twoPassMaxPasses, 1, 4),
                AdaptiveFineThresholdDegrees = Math.Max(0.1, adaptiveFineThresholdDegrees),
                AdaptiveMidThresholdDegrees = Math.Max(0.1, adaptiveMidThresholdDegrees),
                AdaptiveMinYawDeltaDegrees = Math.Max(0.0, adaptiveMinYawDeltaDegrees),
                AdaptiveFinalThresholdDegrees = Math.Max(0.1, adaptiveFinalThresholdDegrees),
                AdaptiveFinalPixelsPerDegreeAbs = Math.Max(0.1, adaptiveFinalPixelsPerDegreeAbs),
                AdaptiveCoarseBatchPixels = ClampInt(Math.Abs(adaptiveCoarseBatchPixels), 1, 1000),
                AdaptiveMidBatchPixels = ClampInt(Math.Abs(adaptiveMidBatchPixels), 1, 500),
                AdaptiveFineStepPixels = ClampInt(Math.Abs(adaptiveFineStepPixels), 1, 100),
                UseFixedYaw = ReadBoolFromEnv("AION_FACE_TARGET_USE_FIXED_YAW", false),
                PitchInvertMouse = ReadBoolFromEnv("AION_CAMERA_PITCH_INVERT_MOUSE", false),
                AutoCalibrate = ReadBoolFromEnv("AION_FACE_TARGET_AUTO_CALIBRATE", false),
                ApplyMouse = ReadBoolFromEnv("AION_FACE_TARGET_APPLY_MOUSE", true)
            };
        }

        private static void PrintCameraWatchSnapshot(VmmProcess process, ulong gameBase, string label)
        {
            LocalPlayerInfo info;
            string error;
            if (TryReadLocalPlayerInfo(process, gameBase, out info, out error))
            {
                Console.WriteLine(
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                    label +
                    " CameraMode=" + FormatCameraMode(info) +
                    " Camera(P/R/Y)=" +
                    info.CameraPitch.ToString("F4") + "/" +
                    info.CameraRoll.ToString("F4") + "/" +
                    info.CameraYaw.ToString("F4") +
                    " Pos=" + FormatPosition(info) +
                    " Transform=" + FormatTransform(info));
            }
            else
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + label + " local read failed: " + error);
            }
        }

        private static void PrintCameraWatchValues(string label, Dictionary<ulong, float> values)
        {
            if (values.Count == 0)
            {
                Console.WriteLine(label + ": none");
                return;
            }

            var ordered = values.OrderBy(item => item.Key).ToList();
            var builder = new StringBuilder();
            builder.Append(label);
            builder.Append(": ");

            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append("0x");
                builder.Append(ordered[i].Key.ToString("X"));
                builder.Append("=");
                builder.Append(ordered[i].Value.ToString("F4"));
            }

            Console.WriteLine(builder.ToString());
        }

        private static bool IsReasonableFloat(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   Math.Abs(value) < 1000000.0F;
        }

        private static bool TryReadFaceTargetSnapshot(
            VmmProcess process,
            ulong gameBase,
            out LocalPlayerInfo local,
            out LockedTargetMonsterInfo target,
            out string error)
        {
            target = new LockedTargetMonsterInfo();
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                return false;
            }

            if (!local.HasPosition)
            {
                error = "local player position is not available";
                return false;
            }

            if (!TryReadLockedTargetMonsterInfo(process, gameBase, out target, out error))
            {
                return false;
            }

            if (target.TargetEntityId == 0)
            {
                error = "no locked target";
                return false;
            }

            if (!target.HasPosition)
            {
                error = "locked target position is not available";
                return false;
            }

            return true;
        }

        private static bool TryCalibrateFaceTargetMouse(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            FaceTargetOptions options,
            out double pixelsPerDegree,
            out string error)
        {
            pixelsPerDegree = 0;
            LocalPlayerInfo before;
            LockedTargetMonsterInfo target;
            if (!TryReadFaceTargetSnapshot(process, gameBase, out before, out target, out error))
            {
                return false;
            }

            double yawBefore = GetCameraYawDegrees(before.CameraYaw, options);
            Console.WriteLine("Calibration: dragging +" + options.CalibrationPixels +
                              " px for " + options.CalibrationMs +
                              " ms from yaw " + yawBefore.ToString("F2") + ".");

            DragCameraHorizontal(km, options.CalibrationPixels, options);
            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            LocalPlayerInfo after;
            if (!TryReadFaceTargetSnapshot(process, gameBase, out after, out target, out error))
            {
                return false;
            }

            double yawAfter = GetCameraYawDegrees(after.CameraYaw, options);
            double yawDelta = NormalizeSignedDegrees(yawAfter - yawBefore);
            if (Math.Abs(yawDelta) < 0.05)
            {
                error = "camera yaw changed only " + yawDelta.ToString("F4") +
                        " deg; check right-button camera drag, KMBOX_PORT, or game focus";
                return false;
            }

            pixelsPerDegree = options.CalibrationPixels / yawDelta;
            Console.WriteLine("Calibration: yawAfter=" + yawAfter.ToString("F2") +
                              " DeltaDeg=" + yawDelta.ToString("F4") +
                              " PixelsPerDegree=" + pixelsPerDegree.ToString("F4") + ".");
            return true;
        }

        private static void DragCameraHorizontal(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            if (dx == 0)
            {
                return;
            }

            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                if (IsNormalDistributionDragMode(options))
                {
                    DragCameraHorizontalNormalDistribution(km, dx, options);
                }
                else if (IsRampDragMode(options))
                {
                    DragCameraHorizontalRamp(km, dx, options);
                }
                else if (IsChunkDragMode(options))
                {
                    DragCameraHorizontalChunks(km, dx, options);
                }
                else if (IsTwoPassChunkDragMode(options))
                {
                    DragCameraHorizontalChunks(km, dx, options);
                }
                else if (IsPhasedDragMode(options))
                {
                    DragCameraHorizontalPhased(km, dx, options);
                }
                else if (IsRawStepDragMode(options))
                {
                    DragCameraHorizontalRawSteps(km, dx, options);
                }
                else
                {
                    km.MoveRelativeHumanLike(dx, 0);
                }

                if (options.MouseHoldAfterMoveMs > 0)
                {
                    Thread.Sleep(options.MouseHoldAfterMoveMs);
                }
            }
            finally
            {
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                }
                catch
                {
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }
        }

        private static double DragCameraHorizontalTwoPassFixedYaw(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetYaw,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            double finalError = 0.0;
            bool mouseDown = false;
            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                mouseDown = true;
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                Console.WriteLine("TwoPassSession=begin HoldRight=yes MaxPasses=" + options.TwoPassMaxPasses);
                for (int pass = 1; pass <= options.TwoPassMaxPasses; pass++)
                {
                    double currentYaw;
                    if (!TryReadStableCameraYaw(process, gameBase, options, out currentYaw))
                    {
                        Console.WriteLine("TwoPassReadFailed Pass=" + pass + " Reason=camera_yaw");
                        return finalError;
                    }

                    double errorDegrees = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalError = errorDegrees;
                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        Console.WriteLine("TwoPassStop Pass=" + pass +
                                          " CameraYaw=" + currentYaw.ToString("F2") +
                                          " ErrorDeg=" + errorDegrees.ToString("F2") +
                                          " Reason=within_tolerance");
                        break;
                    }

                    double rawDx;
                    bool minApplied;
                    int dx = CalculateCameraDragDx(errorDegrees, pixelsPerDegreeAbs, options, false, out rawDx, out minApplied);
                    Console.WriteLine("TwoPass Pass=" + pass +
                                      " CameraYaw=" + currentYaw.ToString("F2") +
                                      " TargetYaw=" + targetYaw.ToString("F2") +
                                      " ErrorDeg=" + errorDegrees.ToString("F2") +
                                      " PixelsPerDeg=" + pixelsPerDegreeAbs.ToString("F2") +
                                      " RawDx=" + rawDx.ToString("F2") +
                                      " Dx=" + dx +
                                      " MoveCommands=" + EstimateChunkDragMoveCommandCount(dx, options) +
                                      " MaxChunkPx=" + options.DragStepPixels +
                                      " PrimeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
                                      " MinApplied=" + (minApplied ? "yes" : "no"));

                    DragCameraHorizontalChunks(km, dx, options);
                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }

                    double afterYaw;
                    if (TryReadStableCameraYaw(process, gameBase, options, out afterYaw))
                    {
                        finalError = NormalizeSignedDegrees(targetYaw - afterYaw);
                        Console.WriteLine("TwoPassResult Pass=" + pass +
                                          " CameraYaw=" + afterYaw.ToString("F2") +
                                          " ErrorDeg=" + finalError.ToString("F2") +
                                          " HoldRight=yes");
                    }
                }
            }
            finally
            {
                if (mouseDown)
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                        Console.WriteLine("TwoPassSession=end MouseUp=right");
                    }
                    catch
                    {
                    }
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }

            return finalError;
        }

        private static double DragCameraHorizontalTwoPassFaceTarget(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            double finalError = 0.0;
            bool mouseDown = false;
            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                mouseDown = true;
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                Console.WriteLine("TwoPassSession=begin HoldRight=yes MaxPasses=" + options.TwoPassMaxPasses);
                for (int pass = 1; pass <= options.TwoPassMaxPasses; pass++)
                {
                    LocalPlayerInfo local;
                    LockedTargetMonsterInfo target;
                    string error;
                    if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                    {
                        Console.WriteLine("TwoPassReadFailed Pass=" + pass + " Reason=" + error);
                        return finalError;
                    }

                    string yawSource;
                    double currentYaw = GetFeedbackYawDegrees(local, options, out yawSource);
                    double targetYaw = CalculateTargetYawDegrees(local, target, options);
                    double errorDegrees = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalError = errorDegrees;
                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        Console.WriteLine("TwoPassStop Pass=" + pass +
                                          " ControlYawSource=" + yawSource +
                                          " ControlYaw=" + currentYaw.ToString("F2") +
                                          " TargetYaw=" + targetYaw.ToString("F2") +
                                          " ErrorDeg=" + errorDegrees.ToString("F2") +
                                          " Reason=within_tolerance");
                        break;
                    }

                    double rawDx;
                    bool minApplied;
                    int dx = CalculateCameraDragDx(errorDegrees, pixelsPerDegreeAbs, options, false, out rawDx, out minApplied);
                    Console.WriteLine("TwoPass Pass=" + pass +
                                      " ControlYawSource=" + yawSource +
                                      " ControlYaw=" + currentYaw.ToString("F2") +
                                      " TargetYaw=" + targetYaw.ToString("F2") +
                                      " ErrorDeg=" + errorDegrees.ToString("F2") +
                                      " Distance=" + FormatDistance(target) +
                                      " PixelsPerDeg=" + pixelsPerDegreeAbs.ToString("F2") +
                                      " RawDx=" + rawDx.ToString("F2") +
                                      " Dx=" + dx +
                                      " MoveCommands=" + EstimateChunkDragMoveCommandCount(dx, options) +
                                      " MaxChunkPx=" + options.DragStepPixels +
                                      " PrimeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
                                      " MinApplied=" + (minApplied ? "yes" : "no"));

                    DragCameraHorizontalChunks(km, dx, options);
                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }

                    TryReadStableCameraYaw(process, gameBase, options, out currentYaw);

                    if (TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                    {
                        currentYaw = GetFeedbackYawDegrees(local, options, out yawSource);
                        targetYaw = CalculateTargetYawDegrees(local, target, options);
                        finalError = NormalizeSignedDegrees(targetYaw - currentYaw);
                        Console.WriteLine("TwoPassResult Pass=" + pass +
                                          " ControlYawSource=" + yawSource +
                                          " ControlYaw=" + currentYaw.ToString("F2") +
                                          " TargetYaw=" + targetYaw.ToString("F2") +
                                          " ErrorDeg=" + finalError.ToString("F2") +
                                          " Distance=" + FormatDistance(target) +
                                          " HoldRight=yes");
                    }
                }
            }
            finally
            {
                if (mouseDown)
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                        Console.WriteLine("TwoPassSession=end MouseUp=right");
                    }
                    catch
                    {
                    }
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }

            return finalError;
        }

        private static double DragCameraVerticalTwoPassFixedPitch(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetPitch,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            double finalError = 0.0;
            bool mouseDown = false;
            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                mouseDown = true;
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                Console.WriteLine("TwoPassPitchSession=begin HoldRight=yes MaxPasses=" + options.TwoPassMaxPasses);
                for (int pass = 1; pass <= options.TwoPassMaxPasses; pass++)
                {
                    double currentPitch;
                    if (!TryReadStableCameraPitch(process, gameBase, options, out currentPitch))
                    {
                        Console.WriteLine("TwoPassPitchReadFailed Pass=" + pass + " Reason=camera_pitch");
                        return finalError;
                    }

                    double errorDegrees = targetPitch - currentPitch;
                    finalError = errorDegrees;
                    if (Math.Abs(errorDegrees) <= options.ToleranceDegrees)
                    {
                        Console.WriteLine("TwoPassPitchStop Pass=" + pass +
                                          " CameraPitch=" + currentPitch.ToString("F2") +
                                          " ErrorDeg=" + errorDegrees.ToString("F2") +
                                          " Reason=within_tolerance");
                        break;
                    }

                    double rawDy;
                    bool minApplied;
                    int dy = CalculateCameraDragDy(errorDegrees, pixelsPerDegreeAbs, options, false, out rawDy, out minApplied);
                    Console.WriteLine("TwoPassPitch Pass=" + pass +
                                      " CameraPitch=" + currentPitch.ToString("F2") +
                                      " TargetPitch=" + targetPitch.ToString("F2") +
                                      " ErrorDeg=" + errorDegrees.ToString("F2") +
                                      " PixelsPerDeg=" + pixelsPerDegreeAbs.ToString("F2") +
                                      " RawDy=" + rawDy.ToString("F2") +
                                      " Dy=" + dy +
                                      " MoveCommands=" + EstimateChunkDragMoveCommandCount(dy, options) +
                                      " MaxChunkPx=" + options.DragStepPixels +
                                      " PrimeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
                                      " PitchInvertMouse=" + (options.PitchInvertMouse ? "yes" : "no") +
                                      " MinApplied=" + (minApplied ? "yes" : "no"));

                    DragCameraVerticalChunks(km, dy, options);
                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }

                    double afterPitch;
                    if (TryReadStableCameraPitch(process, gameBase, options, out afterPitch))
                    {
                        finalError = targetPitch - afterPitch;
                        Console.WriteLine("TwoPassPitchResult Pass=" + pass +
                                          " CameraPitch=" + afterPitch.ToString("F2") +
                                          " ErrorDeg=" + finalError.ToString("F2") +
                                          " HoldRight=yes");
                    }
                }
            }
            finally
            {
                if (mouseDown)
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                        Console.WriteLine("TwoPassPitchSession=end MouseUp=right");
                    }
                    catch
                    {
                    }
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }

            return finalError;
        }

        private static bool DragCameraCombinedTwoPassFixedYawPitch(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetYaw,
            double targetPitch,
            double yawPixelsPerDegreeAbs,
            double pitchPixelsPerDegreeAbs,
            FaceTargetOptions options,
            out double finalYawError,
            out double finalPitchError)
        {
            return DragCameraCombinedTwoPassFixedYawPitch(
                process,
                gameBase,
                new KmBoxClientInput(km),
                targetYaw,
                targetPitch,
                yawPixelsPerDegreeAbs,
                pitchPixelsPerDegreeAbs,
                options,
                false,
                false,
                false,
                out finalYawError,
                out finalPitchError);
        }

        private static bool DragCameraCombinedTwoPassFixedYawPitch(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetYaw,
            double targetPitch,
            double yawPixelsPerDegreeAbs,
            double pitchPixelsPerDegreeAbs,
            FaceTargetOptions options,
            bool keepRightDown,
            bool useFaceTargetMouseMove,
            bool leaveRightDown,
            out double finalYawError,
            out double finalPitchError)
        {
            return DragCameraCombinedTwoPassFixedYawPitch(
                process,
                gameBase,
                new KmBoxClientInput(km),
                targetYaw,
                targetPitch,
                yawPixelsPerDegreeAbs,
                pitchPixelsPerDegreeAbs,
                options,
                keepRightDown,
                useFaceTargetMouseMove,
                leaveRightDown,
                out finalYawError,
                out finalPitchError);
        }

        private static bool DragPathFollowAngleAdjust(
            VmmProcess process,
            ulong gameBase,
            IKmBoxInput km,
            PathFollowPollState pollState,
            PathFollowPoint target,
            double targetPitch,
            double yawPixelsPerDegreeAbs,
            double pitchPixelsPerDegreeAbs,
            FaceTargetOptions options,
            double yawTolerance,
            double pitchTolerance,
            double restartYawThreshold,
            out bool arrivedDuringAdjust,
            out bool restartDuringAdjust)
        {
            arrivedDuringAdjust = false;
            restartDuringAdjust = false;
            string error;
            PathFollowPollSnapshot snapshot;
            if (!TryGetPathFollowPollSnapshot(pollState, out snapshot, out error) || !snapshot.Local.HasPosition)
            {
                Console.WriteLine("PathFollowAngleAdjustSkipped Reason=" + (error ?? "local position unavailable"));
                return false;
            }

            double targetYaw = snapshot.TargetYaw;
            double currentYaw = snapshot.CameraYaw;
            double currentPitch = snapshot.CameraPitch;
            double yawError = snapshot.YawError;
            double pitchError = snapshot.PitchError;
            if (PathFollowMoveControl.ShouldRestartMoveForYaw(true, yawError, restartYawThreshold))
            {
                restartDuringAdjust = true;
                km.KeyUp(KmBoxKeyCodes.KEY_W);
                km.MouseUp(KmMouseButton.Right);
                SetPathFollowMoving(pollState, false);
                Console.WriteLine("PathFollowAngleAdjustStopped Reason=restart_yaw_error" +
                                  " YawErrorDeg=" + yawError.ToString("F2") +
                                  " RestartYawDeg=" + restartYawThreshold.ToString("F2") +
                                  " MoveCommands=0");
                return false;
            }

            if (Math.Abs(yawError) <= yawTolerance && Math.Abs(pitchError) <= pitchTolerance)
            {
                Console.WriteLine("PathFollowAngleAdjustSkipped Reason=within_tolerance" +
                                  " CameraYaw=" + currentYaw.ToString("F2") +
                                  " TargetYaw=" + targetYaw.ToString("F2") +
                                  " YawErrorDeg=" + yawError.ToString("F2") +
                                  " CameraPitch=" + currentPitch.ToString("F2") +
                                  " TargetPitch=" + targetPitch.ToString("F2") +
                                  " PitchErrorDeg=" + pitchError.ToString("F2") +
                                  " PollAgeMs=" + snapshot.AgeMs);
                return true;
            }

            double correctionYawError = Math.Abs(yawError) > yawTolerance ? yawError / 2.0 : 0.0;
            double correctionPitchError = Math.Abs(pitchError) > pitchTolerance ? pitchError / 2.0 : 0.0;
            double rawDx = 0.0;
            double rawDy = 0.0;
            bool minXApplied = false;
            bool minYApplied = false;
            int plannedDx = correctionYawError == 0.0
                ? 0
                : CalculateCameraDragDx(correctionYawError, yawPixelsPerDegreeAbs, options, false, out rawDx, out minXApplied);
            if (correctionYawError == 0.0)
            {
                rawDx = 0.0;
                minXApplied = false;
            }

            int plannedDy = correctionPitchError == 0.0
                ? 0
                : CalculateCameraDragDy(correctionPitchError, pitchPixelsPerDegreeAbs, options, false, out rawDy, out minYApplied);
            if (correctionPitchError == 0.0)
            {
                rawDy = 0.0;
                minYApplied = false;
            }

            int remainingX = Math.Abs(plannedDx);
            int remainingY = Math.Abs(plannedDy);
            int plannedMoveCommands = remainingX + remainingY;
            int pollWaitMs = ClampInt(ReadIntFromEnv("AION_PATH_FOLLOW_ANGLE_ADJUST_POLL_WAIT_MS", 20), 0, 200);
            Console.WriteLine("PathFollowAngleRecalc" +
                              " CameraYaw=" + currentYaw.ToString("F2") +
                              " TargetYaw=" + targetYaw.ToString("F2") +
                              " YawErrorDeg=" + yawError.ToString("F2") +
                              " CorrectionYawDeg=" + correctionYawError.ToString("F2") +
                              " CameraPitch=" + currentPitch.ToString("F2") +
                              " TargetPitch=" + targetPitch.ToString("F2") +
                              " PitchErrorDeg=" + pitchError.ToString("F2") +
                              " CorrectionPitchDeg=" + correctionPitchError.ToString("F2") +
                              " RawDx=" + rawDx.ToString("F2") +
                              " RawDy=" + rawDy.ToString("F2") +
                              " PlannedDx=" + plannedDx +
                              " PlannedDy=" + plannedDy +
                              " MoveCommands=" + plannedMoveCommands +
                              " StepPx=1" +
                              " CheckEachStep=yes" +
                              " PollWaitMs=" + pollWaitMs +
                              " MinApplied=" + (minXApplied || minYApplied ? "yes" : "no") +
                              " PollAgeMs=" + snapshot.AgeMs);

            int movedDx = 0;
            int movedDy = 0;
            int moveCommands = 0;
            bool aligned = false;
            while (remainingX > 0 || remainingY > 0)
            {
                string stopReason;
                if (IsPathFollowStopPending(pollState, out stopReason))
                {
                    Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + stopReason +
                                      " MoveCommands=" + moveCommands);
                    break;
                }

                int arrivedTargetIndex;
                double arrivedDistance;
                if (pollState != null && TryMarkPathFollowArrivedNow(pollState, out arrivedTargetIndex, out arrivedDistance))
                {
                    arrivedDuringAdjust = true;
                    Console.WriteLine("PathFollowArrivedLatch During=move_angle_adjust" +
                                      " Index=" + (arrivedTargetIndex + 1) +
                                      " Distance=" + arrivedDistance.ToString("F2"));
                    break;
                }

                if (!TryGetPathFollowPollSnapshot(pollState, out snapshot, out error) || !snapshot.Local.HasPosition)
                {
                    Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + (error ?? "poll snapshot unavailable"));
                    break;
                }

                yawError = snapshot.YawError;
                pitchError = snapshot.PitchError;
                if (PathFollowMoveControl.ShouldRestartMoveForYaw(true, yawError, restartYawThreshold))
                {
                    restartDuringAdjust = true;
                    km.KeyUp(KmBoxKeyCodes.KEY_W);
                    km.MouseUp(KmMouseButton.Right);
                    SetPathFollowMoving(pollState, false);
                    Console.WriteLine("PathFollowAngleAdjustStopped Reason=restart_yaw_error" +
                                      " YawErrorDeg=" + yawError.ToString("F2") +
                                      " RestartYawDeg=" + restartYawThreshold.ToString("F2") +
                                      " MoveCommands=" + moveCommands);
                    break;
                }

                if (Math.Abs(yawError) <= yawTolerance && Math.Abs(pitchError) <= pitchTolerance)
                {
                    aligned = true;
                    break;
                }

                bool movedOnePixel = false;
                if (remainingX > 0 && Math.Abs(yawError) > yawTolerance)
                {
                    if (IsPathFollowStopPending(pollState, out stopReason))
                    {
                        Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + stopReason +
                                          " MoveCommands=" + moveCommands);
                        break;
                    }

                    double stepRawDx;
                    bool stepMinApplied;
                    int currentDx = CalculateCameraDragDx(yawError, yawPixelsPerDegreeAbs, options, false, out stepRawDx, out stepMinApplied);
                    int stepX = currentDx < 0 ? -1 : 1;
                    long previousReadCount = snapshot.ReadCount;
                    SendCameraCombinedMoveStep(km, stepX, 0, options);
                    movedDx += stepX;
                    remainingX--;
                    moveCommands++;
                    movedOnePixel = true;

                    if (TryWaitForPathFollowPollSnapshot(pollState, previousReadCount, pollWaitMs, out snapshot, out error) &&
                        snapshot.Local.HasPosition)
                    {
                        if (IsPathFollowStopPending(pollState, out stopReason))
                        {
                            Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + stopReason +
                                              " MoveCommands=" + moveCommands);
                            break;
                        }

                        if (PathFollowMoveControl.ShouldRestartMoveForYaw(true, snapshot.YawError, restartYawThreshold))
                        {
                            restartDuringAdjust = true;
                            km.KeyUp(KmBoxKeyCodes.KEY_W);
                            km.MouseUp(KmMouseButton.Right);
                            SetPathFollowMoving(pollState, false);
                            Console.WriteLine("PathFollowAngleAdjustStopped Reason=restart_yaw_error" +
                                              " YawErrorDeg=" + snapshot.YawError.ToString("F2") +
                                              " RestartYawDeg=" + restartYawThreshold.ToString("F2") +
                                              " MoveCommands=" + moveCommands);
                            break;
                        }

                        if (TryMarkPathFollowArrivedNow(pollState, out arrivedTargetIndex, out arrivedDistance))
                        {
                            arrivedDuringAdjust = true;
                            Console.WriteLine("PathFollowArrivedLatch During=move_angle_adjust" +
                                              " Index=" + (arrivedTargetIndex + 1) +
                                              " Distance=" + arrivedDistance.ToString("F2"));
                            break;
                        }

                        if (Math.Abs(snapshot.YawError) <= yawTolerance &&
                            Math.Abs(snapshot.PitchError) <= pitchTolerance)
                        {
                            aligned = true;
                            break;
                        }
                    }
                }

                if (remainingY > 0 && Math.Abs(snapshot.PitchError) > pitchTolerance)
                {
                    if (IsPathFollowStopPending(pollState, out stopReason))
                    {
                        Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + stopReason +
                                          " MoveCommands=" + moveCommands);
                        break;
                    }

                    double stepRawDy;
                    bool stepMinApplied;
                    int currentDy = CalculateCameraDragDy(snapshot.PitchError, pitchPixelsPerDegreeAbs, options, false, out stepRawDy, out stepMinApplied);
                    int stepY = currentDy < 0 ? -1 : 1;
                    long previousReadCount = snapshot.ReadCount;
                    SendCameraCombinedMoveStep(km, 0, stepY, options);
                    movedDy += stepY;
                    remainingY--;
                    moveCommands++;
                    movedOnePixel = true;

                    if (TryWaitForPathFollowPollSnapshot(pollState, previousReadCount, pollWaitMs, out snapshot, out error) &&
                        snapshot.Local.HasPosition)
                    {
                        if (IsPathFollowStopPending(pollState, out stopReason))
                        {
                            Console.WriteLine("PathFollowAngleAdjustStopped Reason=" + stopReason +
                                              " MoveCommands=" + moveCommands);
                            break;
                        }

                        if (PathFollowMoveControl.ShouldRestartMoveForYaw(true, snapshot.YawError, restartYawThreshold))
                        {
                            restartDuringAdjust = true;
                            km.KeyUp(KmBoxKeyCodes.KEY_W);
                            km.MouseUp(KmMouseButton.Right);
                            SetPathFollowMoving(pollState, false);
                            Console.WriteLine("PathFollowAngleAdjustStopped Reason=restart_yaw_error" +
                                              " YawErrorDeg=" + snapshot.YawError.ToString("F2") +
                                              " RestartYawDeg=" + restartYawThreshold.ToString("F2") +
                                              " MoveCommands=" + moveCommands);
                            break;
                        }

                        if (TryMarkPathFollowArrivedNow(pollState, out arrivedTargetIndex, out arrivedDistance))
                        {
                            arrivedDuringAdjust = true;
                            Console.WriteLine("PathFollowArrivedLatch During=move_angle_adjust" +
                                              " Index=" + (arrivedTargetIndex + 1) +
                                              " Distance=" + arrivedDistance.ToString("F2"));
                            break;
                        }

                        if (Math.Abs(snapshot.YawError) <= yawTolerance &&
                            Math.Abs(snapshot.PitchError) <= pitchTolerance)
                        {
                            aligned = true;
                            break;
                        }
                    }
                }

                if (!movedOnePixel)
                {
                    aligned = Math.Abs(snapshot.YawError) <= yawTolerance &&
                              Math.Abs(snapshot.PitchError) <= pitchTolerance;
                    break;
                }
            }

            if (arrivedDuringAdjust)
            {
                km.KeyUp(KmBoxKeyCodes.KEY_W);
                Console.WriteLine("PathFollowKey W=up Reason=arrived_during_angle_adjust");
                SetPathFollowMoving(pollState, false);
                return true;
            }

            if (options.MouseHoldAfterMoveMs > 0)
            {
                Thread.Sleep(options.MouseHoldAfterMoveMs);
            }

            double finalYawError = yawError;
            double finalPitchError = pitchError;
            PathFollowPollSnapshot afterSnapshot;
            if (TryGetPathFollowPollSnapshot(pollState, out afterSnapshot, out error) && afterSnapshot.Local.HasPosition)
            {
                finalYawError = afterSnapshot.YawError;
                finalPitchError = afterSnapshot.PitchError;
                aligned = Math.Abs(finalYawError) <= yawTolerance && Math.Abs(finalPitchError) <= pitchTolerance;
            }

            Console.WriteLine("PathFollowAngleAdjustResult" +
                              " Aligned=" + (aligned ? "yes" : "no") +
                              " MoveCommands=" + moveCommands +
                              " MovedDx=" + movedDx +
                              " MovedDy=" + movedDy +
                              " FinalYawErrorDeg=" + finalYawError.ToString("F2") +
                              " FinalPitchErrorDeg=" + finalPitchError.ToString("F2"));

            return aligned ||
                   (Math.Abs(finalYawError) <= yawTolerance &&
                    Math.Abs(finalPitchError) <= pitchTolerance);
        }

        private static bool DragCameraCombinedTwoPassFixedYawPitch(
            VmmProcess process,
            ulong gameBase,
            IKmBoxInput km,
            double targetYaw,
            double targetPitch,
            double yawPixelsPerDegreeAbs,
            double pitchPixelsPerDegreeAbs,
            FaceTargetOptions options,
            bool keepRightDown,
            bool useFaceTargetMouseMove,
            bool leaveRightDown,
            out double finalYawError,
            out double finalPitchError)
        {
            finalYawError = 0.0;
            finalPitchError = 0.0;
            bool success = false;
            bool mouseDown = false;
            try
            {
                if (!keepRightDown)
                {
                    km.MouseUp(KmMouseButton.Right);
                    Thread.Sleep(8);
                    km.MouseDown(KmMouseButton.Right);
                    mouseDown = true;
                    if (options.MouseDownWarmupMs > 0)
                    {
                        Thread.Sleep(options.MouseDownWarmupMs);
                    }
                }

                Console.WriteLine("CombinedTwoPassSession=begin HoldRight=yes MaxPasses=" + options.TwoPassMaxPasses);
                for (int pass = 1; pass <= options.TwoPassMaxPasses; pass++)
                {
                    LocalPlayerInfo local;
                    string error;
                    if (!TryReadStableLocalPlayerInfo(process, gameBase, options, out local, out error))
                    {
                        Console.WriteLine("CombinedTwoPassReadFailed Pass=" + pass + " Reason=" + error);
                        break;
                    }

                    double currentYaw = GetCameraYawDegrees(local.CameraYaw, options);
                    double currentPitch = GetCameraPitchDegrees(local.CameraPitch, options);
                    finalYawError = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalPitchError = targetPitch - currentPitch;
                    double beforeYawError = finalYawError;
                    double beforePitchError = finalPitchError;
                    if (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                        Math.Abs(finalPitchError) <= options.ToleranceDegrees)
                    {
                        success = true;
                        Console.WriteLine("CombinedTwoPassStop Pass=" + pass +
                                          " CameraYaw=" + currentYaw.ToString("F2") +
                                          " CameraPitch=" + currentPitch.ToString("F2") +
                                          " YawErrorDeg=" + finalYawError.ToString("F2") +
                                          " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                          " Reason=within_tolerance");
                        break;
                    }

                    double rawDx;
                    double rawDy;
                    bool minXApplied;
                    bool minYApplied;
                    int dx;
                    int dy;
                    if (useFaceTargetMouseMove)
                    {
                        dx = CalculateCameraDragDx(finalYawError, yawPixelsPerDegreeAbs, options, false, out rawDx, out minXApplied);
                        dy = CalculateCameraDragDy(finalPitchError, pitchPixelsPerDegreeAbs, options, false, out rawDy, out minYApplied);
                    }
                    else
                    {
                        dx = 0;
                        dy = 0;
                        rawDx = 0.0;
                        rawDy = 0.0;
                        minXApplied = false;
                        minYApplied = false;
                        if (Math.Abs(finalYawError) > options.ToleranceDegrees)
                        {
                            dx = CalculateCameraDragDx(finalYawError, yawPixelsPerDegreeAbs, options, true, out rawDx, out minXApplied);
                        }

                        if (Math.Abs(finalPitchError) > options.ToleranceDegrees)
                        {
                            dy = CalculateCameraDragDy(finalPitchError, pitchPixelsPerDegreeAbs, options, true, out rawDy, out minYApplied);
                        }
                    }

                    Console.WriteLine("CombinedTwoPass Pass=" + pass +
                                      " CameraYaw=" + currentYaw.ToString("F2") +
                                      " TargetYaw=" + targetYaw.ToString("F2") +
                                      " YawErrorDeg=" + finalYawError.ToString("F2") +
                                      " CameraPitch=" + currentPitch.ToString("F2") +
                                      " TargetPitch=" + targetPitch.ToString("F2") +
                                      " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                      " RawDx=" + rawDx.ToString("F2") +
                                      " RawDy=" + rawDy.ToString("F2") +
                                      " Dx=" + dx +
                                      " Dy=" + dy +
                                      " MoveCommands=" + EstimateCombinedChunkDragMoveCommandCount(dx, dy, options) +
                                      " MaxChunkPx=" + options.DragStepPixels +
                                      " PrimeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
                                      " MoveLogic=" + (useFaceTargetMouseMove ? "face_target" : "fixed") +
                                      " MinApplied=" + (minXApplied || minYApplied ? "yes" : "no"));

                    DragCameraCombinedChunks(km, dx, dy, options);
                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }

                    LocalPlayerInfo afterLocal;
                    if (!WaitForCameraAnglesChange(process, gameBase, currentYaw, currentPitch, options, out afterLocal))
                    {
                        Console.WriteLine("CombinedTwoPassWait Pass=" + pass +
                                          " PreviousYaw=" + currentYaw.ToString("F2") +
                                          " PreviousPitch=" + currentPitch.ToString("F2") +
                                          " Result=no_angle_update_stop");
                        break;
                    }

                    double afterYaw = GetCameraYawDegrees(afterLocal.CameraYaw, options);
                    double afterPitch = GetCameraPitchDegrees(afterLocal.CameraPitch, options);
                    finalYawError = NormalizeSignedDegrees(targetYaw - afterYaw);
                    finalPitchError = targetPitch - afterPitch;
                    CameraTurnVerificationResult verification = CameraTurnVerification.Verify(
                        beforeYawError,
                        beforePitchError,
                        finalYawError,
                        finalPitchError);
                    Console.WriteLine("CombinedTwoPassResult Pass=" + pass +
                                      " CameraYaw=" + afterYaw.ToString("F2") +
                                      " CameraPitch=" + afterPitch.ToString("F2") +
                                      " YawErrorDeg=" + finalYawError.ToString("F2") +
                                      " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                      " HoldRight=yes");
                    Console.WriteLine("CombinedTwoPassVerify Pass=" + pass +
                                      " BeforeYawErrorDeg=" + beforeYawError.ToString("F2") +
                                      " AfterYawErrorDeg=" + finalYawError.ToString("F2") +
                                      " YawImproved=" + (verification.YawImproved ? "yes" : "no") +
                                      " YawOvershot=" + (verification.YawOvershot ? "yes" : "no") +
                                      " BeforePitchErrorDeg=" + beforePitchError.ToString("F2") +
                                      " AfterPitchErrorDeg=" + finalPitchError.ToString("F2") +
                                      " PitchImproved=" + (verification.PitchImproved ? "yes" : "no") +
                                      " PitchOvershot=" + (verification.PitchOvershot ? "yes" : "no") +
                                      " AnyImproved=" + (verification.AnyImproved ? "yes" : "no"));

                    if (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                        Math.Abs(finalPitchError) <= options.ToleranceDegrees)
                    {
                        success = true;
                        break;
                    }

                    if (!verification.AnyImproved)
                    {
                        Console.WriteLine("CombinedTwoPassStop Pass=" + pass +
                                          " Reason=no_improvement_after_move");
                        break;
                    }
                }
            }
            finally
            {
                if (mouseDown && !keepRightDown && !leaveRightDown)
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                        Console.WriteLine("CombinedTwoPassSession=end MouseUp=right");
                    }
                    catch
                    {
                    }
                }
                else if (mouseDown && leaveRightDown)
                {
                    Console.WriteLine("CombinedTwoPassSession=end MouseHeld=right");
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }

            return success;
        }

        private static bool DragCameraCombinedTwoPassFaceTarget(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetPitch,
            double yawPixelsPerDegreeAbs,
            double pitchPixelsPerDegreeAbs,
            FaceTargetOptions options,
            out double finalYawError,
            out double finalPitchError)
        {
            finalYawError = 0.0;
            finalPitchError = 0.0;
            bool success = false;
            bool mouseDown = false;
            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                mouseDown = true;
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                Console.WriteLine("CombinedTargetSession=begin HoldRight=yes MaxPasses=" + options.TwoPassMaxPasses);
                for (int pass = 1; pass <= options.TwoPassMaxPasses; pass++)
                {
                    LocalPlayerInfo local;
                    LockedTargetMonsterInfo target;
                    string error;
                    if (!TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                    {
                        Console.WriteLine("CombinedTargetReadFailed Pass=" + pass + " Reason=" + error);
                        break;
                    }

                    double currentYaw = GetCameraYawDegrees(local.CameraYaw, options);
                    double currentPitch = GetCameraPitchDegrees(local.CameraPitch, options);
                    double targetYaw = CalculateTargetYawDegrees(local, target, options);
                    finalYawError = NormalizeSignedDegrees(targetYaw - currentYaw);
                    finalPitchError = targetPitch - currentPitch;
                    if (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                        Math.Abs(finalPitchError) <= options.ToleranceDegrees)
                    {
                        success = true;
                        Console.WriteLine("CombinedTargetStop Pass=" + pass +
                                          " CameraYaw=" + currentYaw.ToString("F2") +
                                          " TargetYaw=" + targetYaw.ToString("F2") +
                                          " CameraPitch=" + currentPitch.ToString("F2") +
                                          " TargetPitch=" + targetPitch.ToString("F2") +
                                          " YawErrorDeg=" + finalYawError.ToString("F2") +
                                          " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                          " Distance=" + FormatDistance(target) +
                                          " Reason=within_tolerance");
                        break;
                    }

                    double rawDx;
                    double rawDy;
                    bool minXApplied;
                    bool minYApplied;
                    int dx = CalculateCameraDragDx(finalYawError, yawPixelsPerDegreeAbs, options, false, out rawDx, out minXApplied);
                    int dy = CalculateCameraDragDy(finalPitchError, pitchPixelsPerDegreeAbs, options, false, out rawDy, out minYApplied);

                    Console.WriteLine("CombinedTarget Pass=" + pass +
                                      " CameraYaw=" + currentYaw.ToString("F2") +
                                      " TargetYaw=" + targetYaw.ToString("F2") +
                                      " YawErrorDeg=" + finalYawError.ToString("F2") +
                                      " CameraPitch=" + currentPitch.ToString("F2") +
                                      " TargetPitch=" + targetPitch.ToString("F2") +
                                      " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                      " Distance=" + FormatDistance(target) +
                                      " RawDx=" + rawDx.ToString("F2") +
                                      " RawDy=" + rawDy.ToString("F2") +
                                      " Dx=" + dx +
                                      " Dy=" + dy +
                                      " MoveCommands=" + EstimateCombinedChunkDragMoveCommandCount(dx, dy, options) +
                                      " MaxChunkPx=" + options.DragStepPixels +
                                      " PrimeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
                                      " MinApplied=" + (minXApplied || minYApplied ? "yes" : "no"));

                    DragCameraCombinedChunks(km, dx, dy, options);
                    if (options.MouseHoldAfterMoveMs > 0)
                    {
                        Thread.Sleep(options.MouseHoldAfterMoveMs);
                    }

                    LocalPlayerInfo afterLocal;
                    if (!WaitForCameraAnglesChange(process, gameBase, currentYaw, currentPitch, options, out afterLocal))
                    {
                        Console.WriteLine("CombinedTargetWait Pass=" + pass +
                                          " PreviousYaw=" + currentYaw.ToString("F2") +
                                          " PreviousPitch=" + currentPitch.ToString("F2") +
                                          " Result=no_angle_update_stop");
                        break;
                    }

                    if (TryReadFaceTargetSnapshot(process, gameBase, out local, out target, out error))
                    {
                        currentYaw = GetCameraYawDegrees(local.CameraYaw, options);
                        currentPitch = GetCameraPitchDegrees(local.CameraPitch, options);
                        targetYaw = CalculateTargetYawDegrees(local, target, options);
                        finalYawError = NormalizeSignedDegrees(targetYaw - currentYaw);
                        finalPitchError = targetPitch - currentPitch;
                        Console.WriteLine("CombinedTargetResult Pass=" + pass +
                                          " CameraYaw=" + currentYaw.ToString("F2") +
                                          " TargetYaw=" + targetYaw.ToString("F2") +
                                          " CameraPitch=" + currentPitch.ToString("F2") +
                                          " TargetPitch=" + targetPitch.ToString("F2") +
                                          " YawErrorDeg=" + finalYawError.ToString("F2") +
                                          " PitchErrorDeg=" + finalPitchError.ToString("F2") +
                                          " Distance=" + FormatDistance(target) +
                                          " HoldRight=yes");

                        if (Math.Abs(finalYawError) <= options.ToleranceDegrees &&
                            Math.Abs(finalPitchError) <= options.ToleranceDegrees)
                        {
                            success = true;
                            break;
                        }
                    }
                    else
                    {
                        Console.WriteLine("CombinedTargetResultReadFailed Pass=" + pass + " Reason=" + error);
                        break;
                    }
                }
            }
            finally
            {
                if (mouseDown)
                {
                    try
                    {
                        km.MouseUp(KmMouseButton.Right);
                        Console.WriteLine("CombinedTargetSession=end MouseUp=right");
                    }
                    catch
                    {
                    }
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }

            return success;
        }

        private static double DragCameraHorizontalAdaptiveFixedYaw(
            VmmProcess process,
            ulong gameBase,
            KmBoxClient km,
            double targetYaw,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            double finalError = 0.0;
            int batches = 0;
            double lastObservedYaw = 0.0;
            double trackedPixelsPerDegreeAbs = pixelsPerDegreeAbs;
            bool hasObservedYaw = false;
            bool hasSentMovement = false;
            bool useFinalPixelsPerDegree = false;
            bool finalDragSessionStarted = false;

            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);

                int prime = Math.Max(0, options.DragPrimePixels);
                int primeDirection = 0;
                if (!TryReadCameraYaw(process, gameBase, options, out lastObservedYaw))
                {
                    Console.WriteLine("AdaptiveReadFailed batch=0 error=failed to read initial camera yaw");
                    return finalError;
                }
                hasObservedYaw = true;

                double trackedErrorDegrees = NormalizeSignedDegrees(targetYaw - lastObservedYaw);
                double rawDx;
                bool minApplied;
                int remainingDx = CalculateCameraDragDx(trackedErrorDegrees, trackedPixelsPerDegreeAbs, options, true, out rawDx, out minApplied);

                for (int batch = 1; batch <= options.AdaptiveMaxBatches; batch++)
                {
                    double observedYaw;
                    bool freshYaw = false;
                    if (TryReadCameraYaw(process, gameBase, options, out observedYaw))
                    {
                        double observedDelta = Math.Abs(NormalizeSignedDegrees(observedYaw - lastObservedYaw));
                        if (batch == 1 || observedDelta >= options.AdaptiveMinYawDeltaDegrees)
                        {
                            if (batch > 1)
                            {
                                double stableYaw;
                                if (WaitForCameraYawStable(process, gameBase, observedYaw, options, out stableYaw))
                                {
                                    observedYaw = stableYaw;
                                }
                            }

                            lastObservedYaw = observedYaw;
                            trackedErrorDegrees = NormalizeSignedDegrees(targetYaw - lastObservedYaw);
                            useFinalPixelsPerDegree = ShouldUseAdaptiveFinalPixelsPerDegree(trackedErrorDegrees, hasSentMovement, options);
                            trackedPixelsPerDegreeAbs = useFinalPixelsPerDegree ? options.AdaptiveFinalPixelsPerDegreeAbs : pixelsPerDegreeAbs;
                            remainingDx = CalculateCameraDragDx(trackedErrorDegrees, trackedPixelsPerDegreeAbs, options, !useFinalPixelsPerDegree, out rawDx, out minApplied);
                            if (useFinalPixelsPerDegree && !finalDragSessionStarted)
                            {
                                RestartCameraRightDrag(km, options);
                                primeDirection = 0;
                                finalDragSessionStarted = true;
                                Console.WriteLine("AdaptiveRestart=batch=" + batch +
                                                  " Reason=final_correction" +
                                                  " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                                  " ErrorDeg=" + trackedErrorDegrees.ToString("F2"));
                            }

                            freshYaw = true;
                        }
                    }

                    finalError = trackedErrorDegrees;
                    batches = batch;

                    double actualErrorDegrees = NormalizeSignedDegrees(targetYaw - lastObservedYaw);
                    if (freshYaw && Math.Abs(actualErrorDegrees) <= options.ToleranceDegrees)
                    {
                        Console.WriteLine("AdaptiveBatch=" + batch +
                                          " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                          " ErrorDeg=" + actualErrorDegrees.ToString("F2") +
                                          " RemainingDx=" + remainingDx +
                                          " Stop=actual_within_tolerance");
                        finalError = actualErrorDegrees;
                        break;
                    }

                    if (remainingDx == 0)
                    {
                        Console.WriteLine("AdaptiveBatch=" + batch +
                                          " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                          " ErrorDeg=" + actualErrorDegrees.ToString("F2") +
                                          " RemainingDx=0 Stop=pixels_exhausted_wait_final");
                        finalError = actualErrorDegrees;
                        break;
                    }

                    int direction = remainingDx < 0 ? -1 : 1;
                    if (primeDirection != direction)
                    {
                        primeDirection = direction;
                        for (int i = 0; i < prime; i++)
                        {
                            SendCameraMoveStep(km, direction, options);
                        }
                    }

                    int batchAbsPixels = CalculateAdaptiveBatchPixelsFromRemaining(remainingDx, trackedPixelsPerDegreeAbs, options);
                    int batchPixels = direction * batchAbsPixels;

                    Console.WriteLine("AdaptiveBatch=" + batch +
                                      " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                      " TargetYaw=" + targetYaw.ToString("F2") +
                                      " ErrorDeg=" + trackedErrorDegrees.ToString("F2") +
                                      " PixelsPerDeg=" + trackedPixelsPerDegreeAbs.ToString("F2") +
                                      " MinApplied=" + (minApplied ? "yes" : "no") +
                                      " RemainingDx=" + remainingDx +
                                      " Dx=" + batchPixels +
                                      " StepMode=" + FormatAdaptiveStepMode(trackedErrorDegrees, options) +
                                      " FreshYaw=" + (freshYaw ? "yes" : "no"));
                    SendCameraMoveStep(km, batchPixels, options);
                    hasSentMovement = true;

                    if (useFinalPixelsPerDegree)
                    {
                        double feedbackYaw;
                        if (WaitForCameraYawChange(process, gameBase, lastObservedYaw, options, out feedbackYaw))
                        {
                            double stableYaw;
                            if (WaitForCameraYawStable(process, gameBase, feedbackYaw, options, out stableYaw))
                            {
                                feedbackYaw = stableYaw;
                            }

                            lastObservedYaw = feedbackYaw;
                            trackedErrorDegrees = NormalizeSignedDegrees(targetYaw - lastObservedYaw);
                            useFinalPixelsPerDegree = ShouldUseAdaptiveFinalPixelsPerDegree(trackedErrorDegrees, hasSentMovement, options);
                            trackedPixelsPerDegreeAbs = useFinalPixelsPerDegree ? options.AdaptiveFinalPixelsPerDegreeAbs : pixelsPerDegreeAbs;
                            remainingDx = CalculateCameraDragDx(trackedErrorDegrees, trackedPixelsPerDegreeAbs, options, !useFinalPixelsPerDegree, out rawDx, out minApplied);
                            Console.WriteLine("AdaptiveFeedback=batch=" + batch +
                                              " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                              " ErrorDeg=" + trackedErrorDegrees.ToString("F2") +
                                              " RemainingDx=" + remainingDx);
                            continue;
                        }

                        RestartCameraRightDrag(km, options);
                        primeDirection = 0;
                        Console.WriteLine("AdaptiveFeedback=batch=" + batch +
                                          " CameraYaw=" + lastObservedYaw.ToString("F2") +
                                          " ErrorDeg=" + trackedErrorDegrees.ToString("F2") +
                                          " Result=no_yaw_update_restart");
                        continue;
                    }

                    remainingDx -= batchPixels;
                    trackedErrorDegrees = -remainingDx / trackedPixelsPerDegreeAbs;

                    if (options.AdaptiveReadSettleMs > 0)
                    {
                        Thread.Sleep(options.AdaptiveReadSettleMs);
                    }
                }
            }
            finally
            {
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                }
                catch
                {
                }
            }

            if (hasObservedYaw)
            {
                double observedYaw;
                WaitForCameraYawStable(process, gameBase, lastObservedYaw, options, out observedYaw);
            }

            double finalYaw;
            if (TryReadCameraYaw(process, gameBase, options, out finalYaw))
            {
                finalError = NormalizeSignedDegrees(targetYaw - finalYaw);
            }

            Console.WriteLine("AdaptiveResult FinalErrorDeg=" + finalError.ToString("F2") +
                              " Batches=" + batches);
            return finalError;
        }

        private static int CalculateAdaptiveBatchPixels(
            double errorDegrees,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            double absError = Math.Abs(errorDegrees);
            int requestedPixels = (int)Math.Round(absError * pixelsPerDegreeAbs, MidpointRounding.AwayFromZero);
            if (requestedPixels <= 0)
            {
                requestedPixels = 1;
            }

            int cap;
            if (absError <= options.AdaptiveFineThresholdDegrees)
            {
                cap = options.AdaptiveFineStepPixels;
            }
            else if (absError <= options.AdaptiveMidThresholdDegrees)
            {
                cap = options.AdaptiveMidBatchPixels;
            }
            else
            {
                cap = options.AdaptiveCoarseBatchPixels;
            }

            return Math.Max(1, Math.Min(requestedPixels, cap));
        }

        private static bool ShouldUseAdaptiveFinalPixelsPerDegree(
            double errorDegrees,
            bool hasSentMovement,
            FaceTargetOptions options)
        {
            return hasSentMovement &&
                   Math.Abs(errorDegrees) <= options.AdaptiveFinalThresholdDegrees &&
                   options.AdaptiveFinalPixelsPerDegreeAbs > 0.0001;
        }

        private static int CalculateAdaptiveBatchPixelsFromRemaining(
            int remainingDx,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options)
        {
            int requestedPixels = Math.Abs(remainingDx);
            double estimatedErrorDegrees = requestedPixels / pixelsPerDegreeAbs;

            int cap;
            if (estimatedErrorDegrees <= options.AdaptiveFineThresholdDegrees)
            {
                cap = options.AdaptiveFineStepPixels;
            }
            else if (estimatedErrorDegrees <= options.AdaptiveMidThresholdDegrees)
            {
                cap = options.AdaptiveMidBatchPixels;
            }
            else
            {
                cap = options.AdaptiveCoarseBatchPixels;
            }

            return Math.Max(1, Math.Min(requestedPixels, cap));
        }

        private static bool WaitForCameraYawChange(
            VmmProcess process,
            ulong gameBase,
            double previousYaw,
            FaceTargetOptions options,
            out double observedYaw)
        {
            observedYaw = previousYaw;

            if (options.AdaptiveReadSettleMs > 0)
            {
                Thread.Sleep(options.AdaptiveReadSettleMs);
            }

            if (options.AdaptiveReadTimeoutMs <= 0)
            {
                return TryReadCameraYaw(process, gameBase, options, out observedYaw) &&
                       Math.Abs(NormalizeSignedDegrees(observedYaw - previousYaw)) >= options.AdaptiveMinYawDeltaDegrees;
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds <= options.AdaptiveReadTimeoutMs)
            {
                double yaw;
                if (TryReadCameraYaw(process, gameBase, options, out yaw))
                {
                    observedYaw = yaw;
                    double delta = Math.Abs(NormalizeSignedDegrees(observedYaw - previousYaw));
                    if (delta >= options.AdaptiveMinYawDeltaDegrees)
                    {
                        return true;
                    }
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static bool WaitForCameraAnglesChange(
            VmmProcess process,
            ulong gameBase,
            double previousYaw,
            double previousPitch,
            FaceTargetOptions options,
            out LocalPlayerInfo observed)
        {
            observed = new LocalPlayerInfo();

            if (options.AdaptiveReadSettleMs > 0)
            {
                Thread.Sleep(options.AdaptiveReadSettleMs);
            }

            int timeoutMs = Math.Max(0, options.AdaptiveReadTimeoutMs);
            if (timeoutMs <= 0)
            {
                return TryReadChangedCameraAngles(process, gameBase, previousYaw, previousPitch, options, out observed);
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds <= timeoutMs)
            {
                if (TryReadChangedCameraAngles(process, gameBase, previousYaw, previousPitch, options, out observed))
                {
                    WaitForCameraAnglesStable(process, gameBase, options, ref observed);
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static bool TryReadChangedCameraAngles(
            VmmProcess process,
            ulong gameBase,
            double previousYaw,
            double previousPitch,
            FaceTargetOptions options,
            out LocalPlayerInfo observed)
        {
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out observed, out error))
            {
                return false;
            }

            double yaw = GetCameraYawDegrees(observed.CameraYaw, options);
            double pitch = GetCameraPitchDegrees(observed.CameraPitch, options);
            double yawDelta = Math.Abs(NormalizeSignedDegrees(yaw - previousYaw));
            double pitchDelta = Math.Abs(pitch - previousPitch);
            return yawDelta >= options.AdaptiveMinYawDeltaDegrees ||
                   pitchDelta >= options.AdaptiveMinYawDeltaDegrees;
        }

        private static bool WaitForCameraAnglesStable(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            ref LocalPlayerInfo stableLocal)
        {
            int stableMs = Math.Max(0, options.AdaptiveStableMs);
            int timeoutMs = Math.Max(stableMs, options.AdaptiveStableTimeoutMs);
            if (stableMs <= 0 || timeoutMs <= 0)
            {
                return true;
            }

            double stableYaw = GetCameraYawDegrees(stableLocal.CameraYaw, options);
            double stablePitch = GetCameraPitchDegrees(stableLocal.CameraPitch, options);
            var stopwatch = Stopwatch.StartNew();
            long stableSince = stopwatch.ElapsedMilliseconds;

            while (stopwatch.ElapsedMilliseconds <= timeoutMs)
            {
                LocalPlayerInfo local;
                string error;
                if (TryReadLocalPlayerInfo(process, gameBase, out local, out error))
                {
                    double yaw = GetCameraYawDegrees(local.CameraYaw, options);
                    double pitch = GetCameraPitchDegrees(local.CameraPitch, options);
                    double yawDelta = Math.Abs(NormalizeSignedDegrees(yaw - stableYaw));
                    double pitchDelta = Math.Abs(pitch - stablePitch);
                    if (yawDelta >= options.AdaptiveMinYawDeltaDegrees ||
                        pitchDelta >= options.AdaptiveMinYawDeltaDegrees)
                    {
                        stableLocal = local;
                        stableYaw = yaw;
                        stablePitch = pitch;
                        stableSince = stopwatch.ElapsedMilliseconds;
                    }
                    else if (stopwatch.ElapsedMilliseconds - stableSince >= stableMs)
                    {
                        return true;
                    }
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static bool WaitForCameraYawStable(
            VmmProcess process,
            ulong gameBase,
            double startYaw,
            FaceTargetOptions options,
            out double stableYaw)
        {
            stableYaw = startYaw;
            if (options.AdaptiveStableMs <= 0 || options.AdaptiveStableTimeoutMs <= 0)
            {
                return true;
            }

            var stopwatch = Stopwatch.StartNew();
            long stableSince = stopwatch.ElapsedMilliseconds;
            while (stopwatch.ElapsedMilliseconds <= options.AdaptiveStableTimeoutMs)
            {
                double yaw;
                if (TryReadCameraYaw(process, gameBase, options, out yaw))
                {
                    double delta = Math.Abs(NormalizeSignedDegrees(yaw - stableYaw));
                    if (delta >= options.AdaptiveMinYawDeltaDegrees)
                    {
                        stableYaw = yaw;
                        stableSince = stopwatch.ElapsedMilliseconds;
                    }
                    else if (stopwatch.ElapsedMilliseconds - stableSince >= options.AdaptiveStableMs)
                    {
                        return true;
                    }
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static bool TryReadCameraYaw(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            out double cameraYaw)
        {
            cameraYaw = 0.0;
            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                return false;
            }

            cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            return true;
        }

        private static bool TryReadStableCameraYaw(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            out double cameraYaw)
        {
            cameraYaw = 0.0;
            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            if (!TryReadCameraYaw(process, gameBase, options, out cameraYaw))
            {
                return false;
            }

            double stableYaw;
            if (WaitForCameraYawStable(process, gameBase, cameraYaw, options, out stableYaw))
            {
                cameraYaw = stableYaw;
            }

            return true;
        }

        private static bool TryReadCameraPitch(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            out double cameraPitch)
        {
            cameraPitch = 0.0;
            LocalPlayerInfo local;
            string error;
            if (!TryReadLocalPlayerInfo(process, gameBase, out local, out error))
            {
                return false;
            }

            cameraPitch = GetCameraPitchDegrees(local.CameraPitch, options);
            return true;
        }

        private static bool TryReadStableCameraPitch(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            out double cameraPitch)
        {
            cameraPitch = 0.0;
            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            if (!TryReadCameraPitch(process, gameBase, options, out cameraPitch))
            {
                return false;
            }

            double stablePitch;
            if (WaitForCameraPitchStable(process, gameBase, cameraPitch, options, out stablePitch))
            {
                cameraPitch = stablePitch;
            }

            return true;
        }

        private static bool TryReadStableLocalPlayerInfo(
            VmmProcess process,
            ulong gameBase,
            FaceTargetOptions options,
            out LocalPlayerInfo local,
            out string error)
        {
            if (options.SettleMs > 0)
            {
                Thread.Sleep(options.SettleMs);
            }

            return TryReadLocalPlayerInfo(process, gameBase, out local, out error);
        }

        private static bool WaitForCameraPitchStable(
            VmmProcess process,
            ulong gameBase,
            double startPitch,
            FaceTargetOptions options,
            out double stablePitch)
        {
            stablePitch = startPitch;
            int stableMs = Math.Max(0, options.AdaptiveStableMs);
            int timeoutMs = Math.Max(stableMs, options.AdaptiveStableTimeoutMs);
            var stopwatch = Stopwatch.StartNew();
            var stableWatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds <= timeoutMs)
            {
                double pitch;
                if (TryReadCameraPitch(process, gameBase, options, out pitch))
                {
                    double delta = Math.Abs(pitch - stablePitch);
                    if (delta >= options.AdaptiveMinYawDeltaDegrees)
                    {
                        stablePitch = pitch;
                        stableWatch.Restart();
                    }
                    else if (stableWatch.ElapsedMilliseconds >= stableMs)
                    {
                        return true;
                    }
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static string FormatAdaptiveStepMode(double errorDegrees, FaceTargetOptions options)
        {
            double absError = Math.Abs(errorDegrees);
            if (absError <= options.AdaptiveFineThresholdDegrees)
            {
                return "fine";
            }

            if (absError <= options.AdaptiveMidThresholdDegrees)
            {
                return "mid";
            }

            return "coarse";
        }

        private static bool IsRawStepDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "raw_step").Trim().ToLowerInvariant();
            return string.Equals(mode, "raw_step", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "raw", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "step", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "pixel", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "1px", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPhasedDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "phased").Trim().ToLowerInvariant();
            return string.Equals(mode, "phased", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "phase", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "fast_phase", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChunkDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "chunk").Trim().ToLowerInvariant();
            return string.Equals(mode, "chunk", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "chunk10", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "chunks", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTwoPassChunkDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "two_pass_chunk").Trim().ToLowerInvariant();
            return string.Equals(mode, "two_pass_chunk", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "twopass_chunk", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "two_pass", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "twopass", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "two_step_chunk", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "retry_chunk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRampDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "ramp").Trim().ToLowerInvariant();
            return string.Equals(mode, "ramp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "wave", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNormalDistributionDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "normal").Trim().ToLowerInvariant();
            return string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "gaussian", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "distribution", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "normal_distribution", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "bell", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "12345654321", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdaptiveDragMode(FaceTargetOptions options)
        {
            string mode = (options.DragMoveMode ?? "adaptive").Trim().ToLowerInvariant();
            return string.Equals(mode, "adaptive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "inner_loop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "single_hold_loop", StringComparison.OrdinalIgnoreCase);
        }

        private static int EstimateDragMoveCommandCount(int dx, FaceTargetOptions options)
        {
            if (dx == 0)
            {
                return 0;
            }

            if (!IsRawStepDragMode(options) &&
                !IsPhasedDragMode(options) &&
                !IsChunkDragMode(options) &&
                !IsTwoPassChunkDragMode(options) &&
                !IsRampDragMode(options) &&
                !IsNormalDistributionDragMode(options) &&
                !IsAdaptiveDragMode(options))
            {
                return 1;
            }

            if (IsAdaptiveDragMode(options))
            {
                return options.AdaptiveMaxBatches;
            }

            if (IsTwoPassChunkDragMode(options))
            {
                return options.TwoPassMaxPasses * EstimateChunkDragMoveCommandCount(dx, options);
            }

            if (IsNormalDistributionDragMode(options))
            {
                return EstimateNormalDistributionDragMoveCommandCount(dx, options);
            }

            if (IsRampDragMode(options))
            {
                return EstimateRampDragMoveCommandCount(dx, options);
            }

            if (IsChunkDragMode(options))
            {
                return EstimateChunkDragMoveCommandCount(dx, options);
            }

            if (IsPhasedDragMode(options))
            {
                return EstimatePhasedDragMoveCommandCount(dx, options);
            }

            int stepAbs = Math.Max(1, options.DragStepPixels);
            int absDx = Math.Abs(dx);
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), absDx);
            int remaining = absDx - prime;
            return prime + ((remaining + stepAbs - 1) / stepAbs);
        }

        private static int EstimateChunkDragMoveCommandCount(int dx, FaceTargetOptions options)
        {
            int absDx = Math.Abs(dx);
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), absDx);
            int remaining = absDx - prime;
            int tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
            int middle = remaining - tail;
            return prime + BuildGradientChunks(middle, Math.Max(1, options.DragStepPixels)).Length + tail;
        }

        private static int EstimateRampDragMoveCommandCount(int dx, FaceTargetOptions options)
        {
            int remaining = Math.Abs(dx);
            int count = 0;
            int[] pattern = BuildRampPattern(options.DragRampMaxPixels);

            while (remaining > 0)
            {
                for (int i = 0; i < pattern.Length && remaining > 0; i++)
                {
                    int step = Math.Min(pattern[i], remaining);
                    remaining -= step;
                    count++;
                }
            }

            return count;
        }

        private static int EstimateNormalDistributionDragMoveCommandCount(int dx, FaceTargetOptions options)
        {
            return BuildNormalDistributionChunks(Math.Abs(dx), options.DragRampMaxPixels).Length;
        }

        private static int EstimatePhasedDragMoveCommandCount(int dx, FaceTargetOptions options)
        {
            int absDx = Math.Abs(dx);
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), absDx);
            int remaining = absDx - prime;
            int mainStep = Math.Max(1, options.DragStepPixels);
            int fineStep = Math.Max(1, options.DragFineStepPixels);
            int mainCommands = remaining / mainStep;
            int tailPixels = remaining - (mainCommands * mainStep);
            int tailCommands = tailPixels == 0 ? 0 : (tailPixels + fineStep - 1) / fineStep;
            return prime + mainCommands + tailCommands;
        }

        private static void DragCameraHorizontalPhased(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            int sign = dx < 0 ? -1 : 1;
            int remaining = Math.Abs(dx);
            var stopwatch = Stopwatch.StartNew();

            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);
            for (int i = 0; i < prime; i++)
            {
                SendCameraMoveStep(km, sign, options);
                remaining -= 1;
            }

            WaitUntilElapsed(stopwatch, options.DragLeadMs);

            int mainStepAbs = Math.Max(1, options.DragStepPixels);
            while (remaining > mainStepAbs)
            {
                SendCameraMoveStep(km, sign * mainStepAbs, options);
                remaining -= mainStepAbs;
            }

            WaitUntilElapsed(stopwatch, options.DragLeadMs + options.DragMainMs);

            int fineStepAbs = Math.Max(1, options.DragFineStepPixels);
            while (remaining >= fineStepAbs)
            {
                SendCameraMoveStep(km, sign * fineStepAbs, options);
                remaining -= fineStepAbs;
            }

            if (remaining > 0)
            {
                SendCameraMoveStep(km, sign * remaining, options);
            }

            WaitUntilElapsed(stopwatch, options.DragLeadMs + options.DragMainMs + options.DragTailMs);
        }

        private static void SendCameraMoveStep(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            if (dx == 0)
            {
                return;
            }

            km.MoveRelative(dx, 0);
            if (options.DragStepDelayMs > 0)
            {
                Thread.Sleep(options.DragStepDelayMs);
            }
        }

        private static void SendCameraVerticalMoveStep(KmBoxClient km, int dy, FaceTargetOptions options)
        {
            if (dy == 0)
            {
                return;
            }

            km.MoveRelative(0, dy);
            if (options.DragStepDelayMs > 0)
            {
                Thread.Sleep(options.DragStepDelayMs);
            }
        }

        private static void SendCameraCombinedMoveStep(KmBoxClient km, int dx, int dy, FaceTargetOptions options)
        {
            SendCameraCombinedMoveStep(new KmBoxClientInput(km), dx, dy, options);
        }

        private static void SendCameraCombinedMoveStep(IKmBoxInput km, int dx, int dy, FaceTargetOptions options)
        {
            if (dx == 0 && dy == 0)
            {
                return;
            }

            km.MoveRelative(dx, dy);
            if (options.DragStepDelayMs > 0)
            {
                Thread.Sleep(options.DragStepDelayMs);
            }
        }

        private static void RestartCameraRightDrag(KmBoxClient km, FaceTargetOptions options)
        {
            km.MouseUp(KmMouseButton.Right);
            Thread.Sleep(20);
            km.MouseDown(KmMouseButton.Right);
            if (options.MouseDownWarmupMs > 0)
            {
                Thread.Sleep(options.MouseDownWarmupMs);
            }
        }

        private static void DragCameraHorizontalChunkSession(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            if (dx == 0)
            {
                return;
            }

            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                DragCameraHorizontalChunks(km, dx, options);

                if (options.MouseHoldAfterMoveMs > 0)
                {
                    Thread.Sleep(options.MouseHoldAfterMoveMs);
                }
            }
            finally
            {
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                }
                catch
                {
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }
        }

        private static int EstimateCombinedChunkDragMoveCommandCount(int dx, int dy, FaceTargetOptions options)
        {
            return Math.Max(EstimateChunkDragMoveCommandCount(dx, options), EstimateChunkDragMoveCommandCount(dy, options));
        }

        private static int EstimateCombinedOnePixelMoveCommandCount(int dx, int dy)
        {
            return Math.Max(Math.Abs(dx), Math.Abs(dy));
        }

        private static void DragCameraCombinedOnePixelSteps(KmBoxClient km, int dx, int dy, FaceTargetOptions options)
        {
            DragCameraCombinedOnePixelSteps(new KmBoxClientInput(km), dx, dy, options);
        }

        private static void DragCameraCombinedOnePixelSteps(IKmBoxInput km, int dx, int dy, FaceTargetOptions options)
        {
            bool arrivedDuringMove;
            DragCameraCombinedOnePixelSteps(km, dx, dy, options, null, out arrivedDuringMove);
        }

        private static void DragCameraCombinedOnePixelSteps(
            IKmBoxInput km,
            int dx,
            int dy,
            FaceTargetOptions options,
            PathFollowPollState pollState,
            out bool arrivedDuringMove)
        {
            arrivedDuringMove = false;
            int xSign = dx < 0 ? -1 : 1;
            int ySign = dy < 0 ? -1 : 1;
            int xCount = Math.Abs(dx);
            int yCount = Math.Abs(dy);
            int count = Math.Max(xCount, yCount);
            for (int i = 0; i < count; i++)
            {
                string stopReason;
                if (IsPathFollowStopPending(pollState, out stopReason))
                {
                    Console.WriteLine("PathFollowOnePixelMoveStopped Reason=" + stopReason +
                                      " MoveCommands=" + i);
                    break;
                }

                int stepX = i < xCount ? xSign : 0;
                int stepY = i < yCount ? ySign : 0;
                SendCameraCombinedMoveStep(km, stepX, stepY, options);
                int arrivedTargetIndex;
                double arrivedDistance;
                if (pollState != null && TryMarkPathFollowArrivedNow(pollState, out arrivedTargetIndex, out arrivedDistance))
                {
                    arrivedDuringMove = true;
                    Console.WriteLine("PathFollowArrivedLatch During=angle_adjust" +
                                      " Index=" + (arrivedTargetIndex + 1) +
                                      " Distance=" + arrivedDistance.ToString("F2"));
                    break;
                }

                if (IsPathFollowStopPending(pollState, out stopReason))
                {
                    Console.WriteLine("PathFollowOnePixelMoveStopped Reason=" + stopReason +
                                      " MoveCommands=" + (i + 1));
                    break;
                }
            }
        }

        private static void DragCameraCombinedChunks(KmBoxClient km, int dx, int dy, FaceTargetOptions options)
        {
            DragCameraCombinedChunks(new KmBoxClientInput(km), dx, dy, options);
        }

        private static void DragCameraCombinedChunks(IKmBoxInput km, int dx, int dy, FaceTargetOptions options)
        {
            int[] xChunks = BuildSignedCameraChunks(dx, options);
            int[] yChunks = BuildSignedCameraChunks(dy, options);
            int count = Math.Max(xChunks.Length, yChunks.Length);
            for (int i = 0; i < count; i++)
            {
                int stepX = i < xChunks.Length ? xChunks[i] : 0;
                int stepY = i < yChunks.Length ? yChunks[i] : 0;
                SendCameraCombinedMoveStep(km, stepX, stepY, options);
            }
        }

        private static int[] BuildSignedCameraChunks(int pixels, FaceTargetOptions options)
        {
            if (pixels == 0)
            {
                return new int[0];
            }

            int sign = pixels < 0 ? -1 : 1;
            int remaining = Math.Abs(pixels);
            var chunks = new List<int>();
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);

            for (int i = 0; i < prime; i++)
            {
                chunks.Add(sign);
                remaining -= 1;
            }

            int tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
            int chunkRemaining = remaining - tail;
            int[] middleChunks = BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels));
            for (int i = 0; i < middleChunks.Length; i++)
            {
                chunks.Add(sign * middleChunks[i]);
            }

            for (int i = 0; i < tail; i++)
            {
                chunks.Add(sign);
            }

            return chunks.ToArray();
        }

        private static void DragCameraVertical(KmBoxClient km, int dy, FaceTargetOptions options)
        {
            if (dy == 0)
            {
                return;
            }

            try
            {
                km.MouseUp(KmMouseButton.Right);
                Thread.Sleep(8);
                km.MouseDown(KmMouseButton.Right);
                if (options.MouseDownWarmupMs > 0)
                {
                    Thread.Sleep(options.MouseDownWarmupMs);
                }

                DragCameraVerticalChunks(km, dy, options);

                if (options.MouseHoldAfterMoveMs > 0)
                {
                    Thread.Sleep(options.MouseHoldAfterMoveMs);
                }
            }
            finally
            {
                try
                {
                    km.MouseUp(KmMouseButton.Right);
                }
                catch
                {
                }
            }

            if (options.DurationMs > 0)
            {
                Thread.Sleep(options.DurationMs);
            }
        }

        private static void DragCameraVerticalChunks(KmBoxClient km, int dy, FaceTargetOptions options)
        {
            int sign = dy < 0 ? -1 : 1;
            int remaining = Math.Abs(dy);
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);

            for (int i = 0; i < prime; i++)
            {
                SendCameraVerticalMoveStep(km, sign, options);
                remaining -= 1;
            }

            int tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
            int chunkRemaining = remaining - tail;
            int[] middleChunks = BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels));
            for (int i = 0; i < middleChunks.Length; i++)
            {
                SendCameraVerticalMoveStep(km, sign * middleChunks[i], options);
            }

            for (int i = 0; i < tail; i++)
            {
                SendCameraVerticalMoveStep(km, sign, options);
            }
        }

        private static void DragCameraHorizontalChunks(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            int sign = dx < 0 ? -1 : 1;
            int remaining = Math.Abs(dx);
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);

            for (int i = 0; i < prime; i++)
            {
                SendCameraMoveStep(km, sign, options);
                remaining -= 1;
            }

            int tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
            int chunkRemaining = remaining - tail;
            int[] middleChunks = BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels));
            for (int i = 0; i < middleChunks.Length; i++)
            {
                SendCameraMoveStep(km, sign * middleChunks[i], options);
            }

            for (int i = 0; i < tail; i++)
            {
                SendCameraMoveStep(km, sign, options);
            }
        }

        private static int[] BuildGradientChunks(int totalPixels, int maxStep)
        {
            if (totalPixels <= 0)
            {
                return new int[0];
            }

            maxStep = Math.Max(1, maxStep);
            int length = 1;
            while (GetMaxGradientSum(length, maxStep) < totalPixels)
            {
                length++;
            }

            int[] chunks = new int[length];
            for (int i = 0; i < chunks.Length; i++)
            {
                chunks[i] = 1;
            }

            int remaining = totalPixels - chunks.Length;
            int[] centerOutOrder = BuildCenterOutIndexOrder(chunks.Length);

            while (remaining > 0)
            {
                bool raised = false;
                for (int i = 0; i < centerOutOrder.Length && remaining > 0; i++)
                {
                    int index = centerOutOrder[i];
                    if (!CanRaiseGradientChunk(chunks, index, maxStep))
                    {
                        continue;
                    }

                    chunks[index]++;
                    remaining--;
                    raised = true;
                }

                if (!raised)
                {
                    break;
                }
            }

            return chunks;
        }

        private static int GetMaxGradientSum(int length, int maxStep)
        {
            int sum = 0;
            for (int i = 0; i < length; i++)
            {
                int distanceToEdge = Math.Min(i, length - 1 - i);
                sum += Math.Min(maxStep, distanceToEdge + 1);
            }

            return sum;
        }

        private static int[] BuildCenterOutIndexOrder(int length)
        {
            var order = new List<int>();
            int leftCenter = (length - 1) / 2;
            int rightCenter = length / 2;

            for (int offset = 0; order.Count < length; offset++)
            {
                int left = leftCenter - offset;
                if (left >= 0)
                {
                    order.Add(left);
                }

                int right = rightCenter + offset;
                if (right != left && right < length)
                {
                    order.Add(right);
                }
            }

            return order.ToArray();
        }

        private static bool CanRaiseGradientChunk(int[] chunks, int index, int maxStep)
        {
            if (chunks[index] >= maxStep)
            {
                return false;
            }

            if (chunks.Length > 1 && (index == 0 || index == chunks.Length - 1))
            {
                return false;
            }

            int nextValue = chunks[index] + 1;
            if (index > 0 && Math.Abs(nextValue - chunks[index - 1]) > 1)
            {
                return false;
            }

            if (index + 1 < chunks.Length && Math.Abs(nextValue - chunks[index + 1]) > 1)
            {
                return false;
            }

            return true;
        }

        private static void DragCameraHorizontalRamp(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            int sign = dx < 0 ? -1 : 1;
            int remaining = Math.Abs(dx);
            int[] pattern = BuildRampPattern(options.DragRampMaxPixels);

            while (remaining > 0)
            {
                for (int i = 0; i < pattern.Length && remaining > 0; i++)
                {
                    int stepAbs = Math.Min(pattern[i], remaining);
                    SendCameraMoveStep(km, sign * stepAbs, options);
                    remaining -= stepAbs;
                }
            }
        }

        private static void DragCameraHorizontalNormalDistribution(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            int sign = dx < 0 ? -1 : 1;
            int[] chunks = BuildNormalDistributionChunks(Math.Abs(dx), options.DragRampMaxPixels);

            for (int i = 0; i < chunks.Length; i++)
            {
                SendCameraMoveStep(km, sign * chunks[i], options);
            }
        }

        private static int[] BuildNormalDistributionChunks(int totalPixels, int peakWeight)
        {
            if (totalPixels <= 0)
            {
                return new int[0];
            }

            int[] weights = BuildRampPattern(peakWeight);
            int weightTotal = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                weightTotal += weights[i];
            }

            var chunks = new List<int>();
            int assigned = 0;
            double carry = 0.0;
            for (int i = 0; i < weights.Length; i++)
            {
                double exact = (totalPixels * (double)weights[i] / weightTotal) + carry;
                int chunk = (int)Math.Floor(exact);
                carry = exact - chunk;

                if (i == weights.Length - 1)
                {
                    chunk = totalPixels - assigned;
                }

                if (chunk > 0)
                {
                    chunks.Add(chunk);
                    assigned += chunk;
                }
            }

            int remainder = totalPixels - assigned;
            if (remainder > 0)
            {
                if (chunks.Count == 0)
                {
                    chunks.Add(remainder);
                }
                else
                {
                    chunks[chunks.Count - 1] += remainder;
                }
            }

            return chunks.ToArray();
        }

        private static int[] BuildRampPattern(int maxStep)
        {
            maxStep = Math.Max(1, maxStep);
            int length = (maxStep * 2) - 1;
            int[] pattern = new int[length];
            int index = 0;

            for (int step = 1; step <= maxStep; step++)
            {
                pattern[index++] = step;
            }

            for (int step = maxStep - 1; step >= 1; step--)
            {
                pattern[index++] = step;
            }

            return pattern;
        }

        private static void WaitUntilElapsed(Stopwatch stopwatch, int targetMs)
        {
            if (targetMs <= 0)
            {
                return;
            }

            int remainingMs = targetMs - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMs > 0)
            {
                Thread.Sleep(remainingMs);
            }
        }

        private static void DragCameraHorizontalRawSteps(KmBoxClient km, int dx, FaceTargetOptions options)
        {
            int stepAbs = Math.Max(1, options.DragStepPixels);
            int sign = dx < 0 ? -1 : 1;
            int remaining = Math.Abs(dx);
            int step = sign * stepAbs;
            int prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);

            for (int i = 0; i < prime; i++)
            {
                km.MoveRelative(sign, 0);
                remaining -= 1;
                if (options.DragStepDelayMs > 0)
                {
                    Thread.Sleep(options.DragStepDelayMs);
                }
            }

            while (remaining >= stepAbs)
            {
                km.MoveRelative(step, 0);
                remaining -= stepAbs;
                if (options.DragStepDelayMs > 0)
                {
                    Thread.Sleep(options.DragStepDelayMs);
                }
            }

            if (remaining > 0)
            {
                km.MoveRelative(sign * remaining, 0);
            }
        }

        private static void PrintFaceTargetState(
            string label,
            LocalPlayerInfo local,
            LockedTargetMonsterInfo target,
            FaceTargetOptions options)
        {
            double cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            double actorYaw = 0.0;
            bool hasActorYaw = TryGetActorYawDegrees(local, out actorYaw);
            double targetYaw = CalculateTargetYawDegrees(local, target, options);
            double cameraErrorDegrees = NormalizeSignedDegrees(targetYaw - cameraYaw);
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                label +
                " Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " Target=(" + target.X.ToString("F2") + "," + target.Y.ToString("F2") + "," + target.Z.ToString("F2") + ")" +
                " CameraMode=" + FormatCameraMode(local) +
                " RawCameraYaw=" + local.CameraYaw.ToString("F4") +
                " CameraYawDeg=" + cameraYaw.ToString("F2") +
                " ActorYawDeg=" + (hasActorYaw ? actorYaw.ToString("F2") : "n/a") +
                " TargetYawDeg=" + targetYaw.ToString("F2") +
                " CameraErrorDeg=" + cameraErrorDegrees.ToString("F2") +
                " ActorErrorDeg=" + (hasActorYaw ? NormalizeSignedDegrees(targetYaw - actorYaw).ToString("F2") : "n/a") +
                " Distance=" + FormatDistance(target));
        }

        private static void PrintFixedCameraYawState(
            string label,
            LocalPlayerInfo local,
            double targetYaw,
            FaceTargetOptions options)
        {
            double cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            double errorDegrees = NormalizeSignedDegrees(targetYaw - cameraYaw);
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                label +
                " Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " CameraMode=" + FormatCameraMode(local) +
                " RawCameraYaw=" + local.CameraYaw.ToString("F4") +
                " CameraYawDeg=" + cameraYaw.ToString("F2") +
                " ActorYawDeg=" + FormatActorYaw(local) +
                " TargetYawDeg=" + targetYaw.ToString("F2") +
                " ErrorDeg=" + errorDegrees.ToString("F2"));
        }

        private static void PrintFixedCameraPitchState(
            string label,
            LocalPlayerInfo local,
            double targetPitch,
            FaceTargetOptions options)
        {
            double cameraPitch = GetCameraPitchDegrees(local.CameraPitch, options);
            double errorDegrees = targetPitch - cameraPitch;
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                label +
                " Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " CameraMode=" + FormatCameraMode(local) +
                " RawCameraPitch=" + local.CameraPitch.ToString("F4") +
                " CameraPitchDeg=" + cameraPitch.ToString("F2") +
                " CameraYawDeg=" + GetCameraYawDegrees(local.CameraYaw, options).ToString("F2") +
                " TargetPitchDeg=" + targetPitch.ToString("F2") +
                " ErrorDeg=" + errorDegrees.ToString("F2"));
        }

        private static void PrintFixedCameraYawPitchState(
            string label,
            LocalPlayerInfo local,
            double targetYaw,
            double targetPitch,
            FaceTargetOptions options)
        {
            double cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            double cameraPitch = GetCameraPitchDegrees(local.CameraPitch, options);
            double yawErrorDegrees = NormalizeSignedDegrees(targetYaw - cameraYaw);
            double pitchErrorDegrees = targetPitch - cameraPitch;
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                label +
                " Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " CameraMode=" + FormatCameraMode(local) +
                " RawCameraYaw=" + local.CameraYaw.ToString("F4") +
                " RawCameraPitch=" + local.CameraPitch.ToString("F4") +
                " CameraYawDeg=" + cameraYaw.ToString("F2") +
                " CameraPitchDeg=" + cameraPitch.ToString("F2") +
                " TargetYawDeg=" + targetYaw.ToString("F2") +
                " TargetPitchDeg=" + targetPitch.ToString("F2") +
                " YawErrorDeg=" + yawErrorDegrees.ToString("F2") +
                " PitchErrorDeg=" + pitchErrorDegrees.ToString("F2"));
        }

        private static void PrintFaceTargetCombinedState(
            string label,
            LocalPlayerInfo local,
            LockedTargetMonsterInfo target,
            double targetPitch,
            FaceTargetOptions options)
        {
            double cameraYaw = GetCameraYawDegrees(local.CameraYaw, options);
            double cameraPitch = GetCameraPitchDegrees(local.CameraPitch, options);
            double targetYaw = CalculateTargetYawDegrees(local, target, options);
            double yawErrorDegrees = NormalizeSignedDegrees(targetYaw - cameraYaw);
            double pitchErrorDegrees = targetPitch - cameraPitch;
            Console.WriteLine(
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                label +
                " Local=(" + local.X.ToString("F2") + "," + local.Y.ToString("F2") + "," + local.Z.ToString("F2") + ")" +
                " Target=(" + target.X.ToString("F2") + "," + target.Y.ToString("F2") + "," + target.Z.ToString("F2") + ")" +
                " CameraMode=" + FormatCameraMode(local) +
                " RawCameraYaw=" + local.CameraYaw.ToString("F4") +
                " RawCameraPitch=" + local.CameraPitch.ToString("F4") +
                " CameraYawDeg=" + cameraYaw.ToString("F2") +
                " CameraPitchDeg=" + cameraPitch.ToString("F2") +
                " TargetYawDeg=" + targetYaw.ToString("F2") +
                " TargetPitchDeg=" + targetPitch.ToString("F2") +
                " YawErrorDeg=" + yawErrorDegrees.ToString("F2") +
                " PitchErrorDeg=" + pitchErrorDegrees.ToString("F2") +
                " Distance=" + FormatDistance(target));
        }

        private static double CalculateTargetYawDegrees(
            LocalPlayerInfo local,
            LockedTargetMonsterInfo target,
            FaceTargetOptions options)
        {
            double dx = target.X - local.X;
            double dy = target.Y - local.Y;
            return CalculateYawFromDeltaDegrees(dx, dy, options);
        }

        private static double CalculatePathTargetYawDegrees(
            LocalPlayerInfo local,
            PathFollowPoint target,
            FaceTargetOptions options)
        {
            double dx = target.X - local.X;
            double dy = target.Y - local.Y;
            return CalculateYawFromDeltaDegrees(dx, dy, options);
        }

        private static double CalculateYawFromDeltaDegrees(
            double dx,
            double dy,
            FaceTargetOptions options)
        {
            double angleRadians;
            string mode = (options.BearingMode ?? "yx").Trim().ToLowerInvariant();

            if (string.Equals(mode, "xy", StringComparison.OrdinalIgnoreCase))
            {
                angleRadians = Math.Atan2(dy, dx);
            }
            else if (string.Equals(mode, "negxy", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(mode, "-xy", StringComparison.OrdinalIgnoreCase))
            {
                angleRadians = Math.Atan2(-dy, dx);
            }
            else if (string.Equals(mode, "xnegy", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(mode, "x-y", StringComparison.OrdinalIgnoreCase))
            {
                angleRadians = Math.Atan2(dy, -dx);
            }
            else if (string.Equals(mode, "negyx", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(mode, "-yx", StringComparison.OrdinalIgnoreCase))
            {
                angleRadians = Math.Atan2(-dx, dy);
            }
            else if (string.Equals(mode, "ynegx", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(mode, "y-x", StringComparison.OrdinalIgnoreCase))
            {
                angleRadians = Math.Atan2(dx, -dy);
            }
            else
            {
                angleRadians = Math.Atan2(dx, dy);
            }

            return NormalizeSignedDegrees(RadiansToDegrees(angleRadians) + options.TargetYawOffsetDegrees);
        }

        private static void PrintTargetYawCandidate(string name, double radians, double cameraYaw)
        {
            double baseYaw = NormalizeSignedDegrees(RadiansToDegrees(radians));
            double plus90 = NormalizeSignedDegrees(baseYaw + 90.0);
            double minus90 = NormalizeSignedDegrees(baseYaw - 90.0);
            double plus180 = NormalizeSignedDegrees(baseYaw + 180.0);
            Console.WriteLine(
                "Candidate=" + name +
                " Base=" + baseYaw.ToString("F2") +
                " ErrBase=" + NormalizeSignedDegrees(baseYaw - cameraYaw).ToString("F2") +
                " Plus90=" + plus90.ToString("F2") +
                " ErrPlus90=" + NormalizeSignedDegrees(plus90 - cameraYaw).ToString("F2") +
                " Minus90=" + minus90.ToString("F2") +
                " ErrMinus90=" + NormalizeSignedDegrees(minus90 - cameraYaw).ToString("F2") +
                " Plus180=" + plus180.ToString("F2") +
                " ErrPlus180=" + NormalizeSignedDegrees(plus180 - cameraYaw).ToString("F2"));
        }

        private static List<PathFollowPoint> LoadPathFollowPoints()
        {
            string pathFile = Environment.GetEnvironmentVariable("AION_PATH_FILE");
            if (!string.IsNullOrWhiteSpace(pathFile) && System.IO.File.Exists(pathFile))
            {
                return ParsePathFollowPoints(System.IO.File.ReadAllText(pathFile));
            }

            string pathText = Environment.GetEnvironmentVariable("AION_PATH_POINTS");
            if (!string.IsNullOrWhiteSpace(pathText))
            {
                return ParsePathFollowPoints(pathText);
            }

            return ParsePathFollowPoints(GetDefaultPathFollowText());
        }

        private static List<PathFollowPoint> ParsePathFollowPoints(string text)
        {
            var points = new List<PathFollowPoint>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return points;
            }

            string normalized = text.Replace(";", Environment.NewLine);
            string[] lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                double x;
                double y;
                double z;
                if (TryParsePathDouble(parts[0], out x) &&
                    TryParsePathDouble(parts[1], out y) &&
                    TryParsePathDouble(parts[2], out z))
                {
                    points.Add(new PathFollowPoint { X = x, Y = y, Z = z });
                }
            }

            return points;
        }

        private static bool TryParsePathDouble(string text, out double value)
        {
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out value);
        }

        private static string GetDefaultPathFollowText()
        {
            return
                "1188.123, 1368.177, 209.254\n" +
                "1154.359, 1404.967, 209.196\n" +
                "1222.136, 1381.585, 208.125";
        }

        private static int FindNearestPathPointIndex(List<PathFollowPoint> path, LocalPlayerInfo local)
        {
            int bestIndex = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                double distance = GetHorizontalDistance(local, path[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int ResolvePathFollowStartIndex(
            List<PathFollowPoint> path,
            LocalPlayerInfo local,
            double reachDistance,
            string startMode)
        {
            int configuredStart = ReadIntFromEnv("AION_PATH_FOLLOW_START_INDEX", 0);
            if (configuredStart > 0)
            {
                return ClampInt(configuredStart - 1, 0, path.Count - 1);
            }

            string mode = FormatPathFollowStartMode(startMode);
            if (string.Equals(mode, "first", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(mode, "last", StringComparison.OrdinalIgnoreCase))
            {
                return path.Count - 1;
            }

            int targetIndex = FindNearestPathPointIndex(path, local);
            if (targetIndex + 1 < path.Count && GetHorizontalDistance(local, path[targetIndex]) <= reachDistance)
            {
                targetIndex++;
            }

            return targetIndex;
        }

        private static string FormatPathFollowStartMode(string startMode)
        {
            if (string.IsNullOrWhiteSpace(startMode))
            {
                return "nearest";
            }

            string mode = startMode.Trim().ToLowerInvariant();
            if (string.Equals(mode, "first", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "begin", StringComparison.OrdinalIgnoreCase))
            {
                return "first";
            }

            if (string.Equals(mode, "last", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "end", StringComparison.OrdinalIgnoreCase))
            {
                return "last";
            }

            return "nearest";
        }

        private static double GetHorizontalDistance(LocalPlayerInfo local, PathFollowPoint point)
        {
            double dx = point.X - local.X;
            double dy = point.Y - local.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static PathFollowBudgetPoint ToPathFollowBudgetPoint(LocalPlayerInfo local)
        {
            return new PathFollowBudgetPoint(local.X, local.Y);
        }

        private static PathFollowBudgetPoint ToPathFollowBudgetPoint(PathFollowPoint point)
        {
            return new PathFollowBudgetPoint(point.X, point.Y);
        }

        private static double GetCameraYawDegrees(float rawYaw, FaceTargetOptions options)
        {
            string unit = (options.CameraYawUnit ?? "auto").Trim().ToLowerInvariant();
            if (string.Equals(unit, "deg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "degree", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "degrees", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeSignedDegrees(rawYaw);
            }

            if (string.Equals(unit, "rad", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "radian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "radians", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeSignedDegrees(RadiansToDegrees(rawYaw));
            }

            if (Math.Abs(rawYaw) <= (Math.PI * 2.0 + 0.25))
            {
                return NormalizeSignedDegrees(RadiansToDegrees(rawYaw));
            }

            return NormalizeSignedDegrees(rawYaw);
        }

        private static double GetCameraPitchDegrees(float rawPitch, FaceTargetOptions options)
        {
            string unit = (options.CameraPitchUnit ?? "auto").Trim().ToLowerInvariant();
            double pitch;
            if (string.Equals(unit, "rad", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "radian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit, "radians", StringComparison.OrdinalIgnoreCase))
            {
                pitch = RadiansToDegrees(rawPitch);
            }
            else if (string.Equals(unit, "auto", StringComparison.OrdinalIgnoreCase) &&
                     Math.Abs(rawPitch) <= (Math.PI * 2.0 + 0.25))
            {
                pitch = RadiansToDegrees(rawPitch);
            }
            else
            {
                pitch = rawPitch;
            }

            return ClampDouble(pitch, -65.0, 85.0);
        }

        private static int CalculateCameraDragDx(
            double errorDegrees,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options,
            out double rawDx,
            out bool minApplied)
        {
            return CalculateCameraDragDx(errorDegrees, pixelsPerDegreeAbs, options, true, out rawDx, out minApplied);
        }

        private static int CalculateCameraDragDx(
            double errorDegrees,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options,
            bool applyMinCorrection,
            out double rawDx,
            out bool minApplied)
        {
            rawDx = -errorDegrees * pixelsPerDegreeAbs;
            minApplied = false;

            int dx = (int)Math.Round(rawDx, MidpointRounding.AwayFromZero);
            if (dx == 0)
            {
                dx = errorDegrees > 0 ? -1 : 1;
            }

            int sign = dx < 0 ? -1 : 1;
            int absDx = Math.Abs(dx);
            if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDx < options.MinCorrectionPixels)
            {
                dx = sign * options.MinCorrectionPixels;
                minApplied = true;
            }

            return dx;
        }

        private static int CalculateCameraDragDy(
            double errorDegrees,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options,
            out double rawDy,
            out bool minApplied)
        {
            return CalculateCameraDragDy(errorDegrees, pixelsPerDegreeAbs, options, true, out rawDy, out minApplied);
        }

        private static int CalculateCameraDragDy(
            double errorDegrees,
            double pixelsPerDegreeAbs,
            FaceTargetOptions options,
            bool applyMinCorrection,
            out double rawDy,
            out bool minApplied)
        {
            rawDy = errorDegrees * pixelsPerDegreeAbs;
            if (options.PitchInvertMouse)
            {
                rawDy = -rawDy;
            }

            minApplied = false;

            int dy = (int)Math.Round(rawDy, MidpointRounding.AwayFromZero);
            if (dy == 0)
            {
                dy = errorDegrees > 0 ? 1 : -1;
                if (options.PitchInvertMouse)
                {
                    dy = -dy;
                }
            }

            int sign = dy < 0 ? -1 : 1;
            int absDy = Math.Abs(dy);
            if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDy < options.MinCorrectionPixels)
            {
                dy = sign * options.MinCorrectionPixels;
                minApplied = true;
            }

            return dy;
        }

        private static double GetFeedbackYawDegrees(LocalPlayerInfo local, FaceTargetOptions options, out string source)
        {
            string mode = (options.YawFeedbackMode ?? "camera").Trim().ToLowerInvariant();
            double actorYaw;
            if ((string.Equals(mode, "actor", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode, "entity", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode, "character", StringComparison.OrdinalIgnoreCase)) &&
                TryGetActorYawDegrees(local, out actorYaw))
            {
                source = "actor";
                return actorYaw;
            }

            source = "camera";
            return GetCameraYawDegrees(local.CameraYaw, options);
        }

        private static bool TryGetActorYawDegrees(LocalPlayerInfo local, out double actorYaw)
        {
            actorYaw = 0.0;
            if (!local.HasTransform)
            {
                return false;
            }

            actorYaw = NormalizeSignedDegrees(local.Transform.WorldAngles.Z);
            return true;
        }

        private static string FormatActorYaw(LocalPlayerInfo local)
        {
            double actorYaw;
            return TryGetActorYawDegrees(local, out actorYaw)
                ? actorYaw.ToString("F2")
                : "n/a";
        }

        private static string FormatCameraMode(LocalPlayerInfo info)
        {
            string mode = info.IsSpecialCamera
                ? "special(" + info.SpecialCameraMode + ")"
                : "normal";

            return mode +
                   " RVAs=P:0x" + info.CameraPitchRva.ToString("X") +
                   "/R:0x" + info.CameraRollRva.ToString("X") +
                   "/Y:0x" + info.CameraYawRva.ToString("X");
        }

        private static double NormalizeAbsoluteDegrees(double angle)
        {
            angle %= 360.0;
            if (angle < 0)
            {
                angle += 360.0;
            }

            return angle;
        }

        private static double NormalizeSignedDegrees(double angle)
        {
            angle = NormalizeAbsoluteDegrees(angle);
            if (angle > 180.0)
            {
                angle -= 360.0;
            }

            return angle;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
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

            ushort specialCameraMode = 0;
            TryReadUInt16(process, gameBase + SpecialCameraModeRva, out specialCameraMode);

            bool useSpecialCamera = specialCameraMode != 0 && !HasCameraRvaOverride();
            ulong cameraPitchRva = useSpecialCamera ? SpecialCameraPitchRva : GetCameraPitchRva();
            ulong cameraRollRva = useSpecialCamera ? SpecialCameraRollRva : GetCameraRollRva();
            ulong cameraYawRva = useSpecialCamera ? SpecialCameraYawRva : GetCameraYawRva();
            info.IsSpecialCamera = useSpecialCamera;
            info.SpecialCameraMode = specialCameraMode;
            info.CameraPitchRva = cameraPitchRva;
            info.CameraRollRva = cameraRollRva;
            info.CameraYawRva = cameraYawRva;

            if (!TryReadSingle(process, gameBase + cameraPitchRva, out info.CameraPitch) ||
                !TryReadSingle(process, gameBase + cameraRollRva, out info.CameraRoll) ||
                !TryReadSingle(process, gameBase + cameraYawRva, out info.CameraYaw))
            {
                error = "failed to read camera angles at pitch=0x" + cameraPitchRva.ToString("X") +
                        " roll=0x" + cameraRollRva.ToString("X") +
                        " yaw=0x" + cameraYawRva.ToString("X");
                return false;
            }

            if (useSpecialCamera)
            {
                TryReadSingle(process, gameBase + SpecialCameraDistanceRva, out info.CameraDistance);
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

        private static void PrintScalarProbeValues(VmmProcess process, ulong address, byte[] bytes)
        {
            string u8 = bytes.Length >= 1 ? bytes[0].ToString() : "n/a";
            string u16 = bytes.Length >= 2 ? BitConverter.ToUInt16(bytes, 0).ToString() : "n/a";
            string i16 = bytes.Length >= 2 ? BitConverter.ToInt16(bytes, 0).ToString() : "n/a";
            string u32 = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0).ToString() : "n/a";
            string i32 = bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0).ToString() : "n/a";
            string f32 = bytes.Length >= 4 ? BitConverter.ToSingle(bytes, 0).ToString("R") : "n/a";
            string u64 = bytes.Length >= 8 ? BitConverter.ToUInt64(bytes, 0).ToString() : "n/a";
            string i64 = bytes.Length >= 8 ? BitConverter.ToInt64(bytes, 0).ToString() : "n/a";
            string f64 = bytes.Length >= 8 ? BitConverter.ToDouble(bytes, 0).ToString("R") : "n/a";

            ulong rawPointer = 0;
            bool hasPointer = TryReadPointer(process, address, out rawPointer);
            Console.WriteLine("Scalar" +
                              " U8=" + u8 +
                              " U16=" + u16 +
                              " I16=" + i16 +
                              " U32=" + u32 +
                              " I32=" + i32 +
                              " F32=" + f32 +
                              " U64=" + u64 +
                              " I64=" + i64 +
                              " F64=" + f64 +
                              " Ptr=" + (hasPointer ? FormatAddress(rawPointer) : "n/a"));
        }

        private static void PrintHexDump(ulong address, byte[] bytes, int columns)
        {
            if (bytes == null || bytes.Length == 0)
            {
                Console.WriteLine("  <empty>");
                return;
            }

            columns = Math.Max(4, columns);
            for (int offset = 0; offset < bytes.Length; offset += columns)
            {
                int count = Math.Min(columns, bytes.Length - offset);
                var hex = new StringBuilder();
                var ascii = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    byte b = bytes[offset + i];
                    if (i > 0)
                    {
                        hex.Append(' ');
                    }

                    hex.Append(b.ToString("X2"));
                    ascii.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }

                Console.WriteLine("  " + FormatAddress(address + (ulong)offset) +
                                  "  " + hex.ToString().PadRight(columns * 3 - 1) +
                                  "  " + ascii);
            }
        }

        private static bool IsUsefulProbeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            int printable = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsControl(trimmed[i]))
                {
                    printable++;
                }
            }

            return printable >= 2;
        }

        private static string EscapeProbeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n")
                       .Replace("\t", "\\t")
                       .Replace("\"", "\\\"");
        }

        private static string FormatFloatScanContext(byte[] bytes, int centerByteOffset, ulong startOffset, int contextCount)
        {
            if (contextCount <= 0)
            {
                return "[]";
            }

            var builder = new StringBuilder();
            builder.Append('[');
            bool hasAny = false;
            for (int contextIndex = -contextCount; contextIndex <= contextCount; contextIndex++)
            {
                int byteOffset = centerByteOffset + contextIndex * 4;
                if (byteOffset < 0 || byteOffset + 4 > bytes.Length)
                {
                    continue;
                }

                float value = BitConverter.ToSingle(bytes, byteOffset);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    continue;
                }

                if (hasAny)
                {
                    builder.Append(" | ");
                }

                builder.Append("0x");
                builder.Append((startOffset + (ulong)byteOffset).ToString("X"));
                builder.Append('=');
                builder.Append(value.ToString("R"));
                hasAny = true;
            }

            builder.Append(']');
            return builder.ToString();
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

        private static double ReadSignedDoubleFromEnv(string name, double defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(name);
            double value;
            if (!string.IsNullOrWhiteSpace(text) &&
                double.TryParse(text, out value))
            {
                return value;
            }

            return defaultValue;
        }

        private static ulong GetCameraPitchRva()
        {
            return ReadRvaFromEnv("AION_CAMERA_PITCH_RVA", CameraPitchRva);
        }

        private static ulong GetCameraRollRva()
        {
            return ReadRvaFromEnv("AION_CAMERA_ROLL_RVA", CameraRollRva);
        }

        private static ulong GetCameraYawRva()
        {
            return ReadRvaFromEnv("AION_CAMERA_YAW_RVA", CameraYawRva);
        }

        private static bool HasCameraRvaOverride()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_PITCH_RVA")) ||
                   !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_ROLL_RVA")) ||
                   !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AION_CAMERA_YAW_RVA"));
        }

        private static ulong ReadRvaFromEnv(string name, ulong defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }

            text = text.Trim();
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToUInt64(text.Substring(2), 16);
                }

                return Convert.ToUInt64(text, 10);
            }
            catch
            {
                return defaultValue;
            }
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
