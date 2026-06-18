return {
    BindClient = { timeout = "login", max_retries = 1, required_params = { "account_index" } },
    Login = { timeout = "login", max_retries = 1, required_params = { "account" } },
    NavigateTo = { timeout = "navigation", max_retries = 2, required_params = { "destination" } },
    InteractNpc = { timeout = "interaction", max_retries = 1, required_params = { "npc_id" } },
    ProcessInventoryRules = { timeout = "inventory", max_retries = 1, required_params = {} },
    EvaluateEquipmentCandidates = { timeout = "equipment", max_retries = 1, required_params = {} },
    LearnSkill = { timeout = "skill", max_retries = 1, required_params = { "skill_id" } },
    Wait = { timeout = "action", max_retries = 0, required_params = { "seconds" } },
    Idle = { timeout = "action", max_retries = 0, required_params = {} },
    Stop = { timeout = "action", max_retries = 0, required_params = { "reason" } }
}
