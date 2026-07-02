namespace Roadhog.Core.Model;

public enum AionClassId : uint
{
    Warrior = 0,
    Gladiator = 1,
    Templar = 2,
    Scout = 3,
    Assassin = 4,
    Ranger = 5,
    Mage = 6,
    Sorcerer = 7,
    Spiritmaster = 8,
    Priest = 9,
    Cleric = 10,
    Chanter = 11
}

public static class AionClassCatalog
{
    public const uint MaxKnownClassId = 11;

    public const uint SpiritmasterClassId = (uint)AionClassId.Spiritmaster;

    public const uint ClericClassId = (uint)AionClassId.Cleric;

    public static bool TryFromRaw(uint rawClassId, out AionClassId classId)
    {
        if (rawClassId <= MaxKnownClassId)
        {
            classId = (AionClassId)rawClassId;
            return true;
        }

        classId = default;
        return false;
    }

    public static string GetChineseName(AionClassId classId)
    {
        return classId switch
        {
            AionClassId.Warrior => "战士",
            AionClassId.Gladiator => "剑星",
            AionClassId.Templar => "守护星",
            AionClassId.Scout => "侦察者",
            AionClassId.Assassin => "杀星",
            AionClassId.Ranger => "弓星",
            AionClassId.Mage => "法师",
            AionClassId.Sorcerer => "魔道星",
            AionClassId.Spiritmaster => "精灵星",
            AionClassId.Priest => "祭司",
            AionClassId.Cleric => "治愈星",
            AionClassId.Chanter => "护法星",
            _ => string.Empty
        };
    }
}
