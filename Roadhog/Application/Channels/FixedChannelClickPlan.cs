using Roadhog.Core.Accounts;

namespace Roadhog.Application.Channels;

public static class FixedChannelClickPlan
{
    public static readonly IReadOnlyList<FixedChannelClickStep> OrderedSteps = new[]
    {
        FixedChannelClickStep.Menu,
        FixedChannelClickStep.Service,
        FixedChannelClickStep.SwitchChannel,
        FixedChannelClickStep.ChannelMove,
        FixedChannelClickStep.SelectChannel,
        FixedChannelClickStep.Move
    };

    public static IReadOnlyList<FixedChannelClickPoint> FromSettings(
        FixedChannelMouseScriptSettings? settings)
    {
        settings ??= new FixedChannelMouseScriptSettings();
        return new[]
        {
            Create(FixedChannelClickStep.Menu, settings.Menu),
            Create(FixedChannelClickStep.Service, settings.Service),
            Create(FixedChannelClickStep.SwitchChannel, settings.SwitchChannel),
            Create(FixedChannelClickStep.ChannelMove, settings.ChannelMove),
            Create(FixedChannelClickStep.SelectChannel, settings.SelectChannel),
            Create(FixedChannelClickStep.Move, settings.Move)
        };
    }

    public static string Name(FixedChannelClickStep step)
    {
        return step switch
        {
            FixedChannelClickStep.Menu => "menu",
            FixedChannelClickStep.Service => "service",
            FixedChannelClickStep.SwitchChannel => "switch_channel",
            FixedChannelClickStep.ChannelMove => "channel_move",
            FixedChannelClickStep.SelectChannel => "select_channel",
            FixedChannelClickStep.Move => "move",
            _ => "unknown"
        };
    }

    private static FixedChannelClickPoint Create(
        FixedChannelClickStep step,
        ScreenPointScriptSettings? point)
    {
        return new FixedChannelClickPoint(step, point?.X ?? 0, point?.Y ?? 0);
    }
}
