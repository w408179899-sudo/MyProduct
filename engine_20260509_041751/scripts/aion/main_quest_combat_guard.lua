local M = {}

local interruptible_actions = {
    NavigateToNpc = true,
    FinalMoveToNpc = true,
    FollowRoute = true,
    InteractNpc = true,
    QuestTeleport = true,
    ClickUiControl = true,
    ClickUiControlWaitTeleport = true,
    ClickDialogX = true,
    ClickDialogXContinuous = true,
    ClickDialogXContinuousWaitTeleport = true,
    ClickDialogXWaitTeleport = true,
    ClickDialogXCompleteQuest = true,
    ClickDialogOkCompleteQuest = true,
    ClickObeliskConfirm = true,
    MapNodeTeleportByName = true,
    OpenQuestSubmit = true,
}

local combat_actions = {
    StartStationaryGrind = true,
    WaitLevelGrind = true,
    WaitQuestComplete = true,
    CompleteQuestGrind = true,
}

function M.actionInterruptible(action)
    if type(action) ~= "table" then
        return false
    end
    local name = tostring(action.name or "")
    if name == "" or combat_actions[name] then
        return false
    end
    return interruptible_actions[name] == true
end

function M.shouldBlock(args)
    args = args or {}
    if not M.actionInterruptible(args.action) then
        return false, "action-safe"
    end
    if args.live_target == true then
        return true, tostring(args.live_reason or "live-target")
    end
    if args.recent_damage == true then
        return true, "recent-damage"
    end
    if args.pending_loot == true then
        return true, "pending-loot"
    end
    return false, "combat-clear"
end

return M
