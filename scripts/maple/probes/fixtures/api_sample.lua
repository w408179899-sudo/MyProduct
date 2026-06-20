return {
    source = "initial_colleague_api_sample",
    player_info = {
        Hp = "1500",
        Mp = "800",
        Level = "30",
        Exp = "12345",
        Job = "warrior",
        MaxHp = "2000",
        MaxMp = "1000",
        Nickname = "sample_hero",
        Gender = "0",
        CharId = "10001",
        AP = "0",
        X = "320.5",
        Y = "-180.0",
        WalkSpeed = "2.0",
        Gravity = "-1.0",
        Invincible = "false",
        MapId = "100020000",
        MapName = "sample_map",
        Entity = "sample_entity"
    },
    list_nearby = {
        mobCount = 1,
        dropCount = 1,
        portalCount = 1,
        npcCount = 1,
        mobs = {
            { Name = "sample_snail", MobId = 100101, Level = 5, x = 100.0, y = 200.0, Hp = "50", MaxHp = "50" }
        },
        drops = {
            { Name = "sample_potion", ItemId = 2000003, OwnerCID = "mine", DropperType = 1, Free = false, x = 105.0, y = 201.0 }
        },
        portals = {
            { Name = "sp", PortalType = 1, DestMap = "100020001", DestPortal = "in00", x = 0.0, y = 0.0 }
        },
        npcs = {
            { Name = "sample_guide", NpcCode = 9000000, x = 10.0, y = 20.0 }
        }
    },
    list_inventory = {
        meso = "1000",
        items = {
            { type = "consume", index = 1, Code = 2000003, Count = "10", CUID = "cuid-1", name = "sample_potion", itemType = 2, itemTypeName = "consume" }
        }
    },
    list_skills = {
        point = "2",
        used = "1",
        skills = {
            { tier = 1, index = 1, Code = 1001004, CurrentLevel = 3, name = "sample_slash" }
        }
    },
    list_quickslot = {
        { slot = 1, key = "Shift", cat = "Skill", id = "1001004" },
        { slot = 2, key = "Insert", cat = "Item", id = "2000003" }
    }
}
