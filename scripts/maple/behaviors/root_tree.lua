local PrioritySelector = require("maple.bt.priority_selector")
local Branch = require("maple.behaviors.branch_factory")
local CombatBranch = require("maple.behaviors.combat_branch")

local RootTree = {}

function RootTree.new(executor, logger)
    local children = {
        Branch.new("recovery", 800, "Wait", function() return { seconds = 1 } end, executor, logger),
        Branch.new("safety", 700, "Stop", function() return { reason = "safety" } end, executor, logger),
        Branch.new("stuck", 600, "Wait", function() return { seconds = 1 } end, executor, logger),
        Branch.new("inventory", 500, "ProcessInventoryRules", nil, executor, logger),
        Branch.new("equipment", 400, "EvaluateEquipmentCandidates", nil, executor, logger),
        Branch.new("skill", 300, "LearnSkill", function(bb)
            local first = bb.skill.available and bb.skill.available[1]
            return { skill_id = first and first.id or "mock_skill" }
        end, executor, logger),
        CombatBranch.new(executor, logger),
        Branch.new("quest", 200, "Idle", nil, executor, logger),
        Branch.new("idle", 0, "Idle", nil, executor, logger)
    }
    return PrioritySelector.new("Root", children)
end

return RootTree
