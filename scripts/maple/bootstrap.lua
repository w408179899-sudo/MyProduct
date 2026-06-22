local Config = require("maple.config")
local Blackboard = require("maple.blackboard")
local Clock = require("maple.core.clock")
local Logger = require("maple.systems.logger")
local MockEnvironment = require("maple.environment.mock_environment")
local MapleEnvironment = require("maple.environment.maple_environment")
local Perception = require("maple.systems.perception")
local Planner = require("maple.planner.planner")
local Executor = require("maple.systems.executor")
local RootTree = require("maple.behaviors.root_tree")
local Safety = require("maple.systems.safety")
local Metrics = require("maple.systems.metrics")
local Snapshot = require("maple.systems.snapshot")

local Bootstrap = {}

local function build_environment(opts, logger)
    if opts.environment then return opts.environment end
    if opts.environment_name == "maple" or opts.use_real_environment == true then
        return MapleEnvironment.new({
            world = opts.world,
            data_module = opts.data_module,
            module_name = opts.data_module_name,
            logger = logger,
            account_index = opts.account_index,
            target_name = opts.target_name,
            license_key = opts.license_key,
            skill_release = opts.skill_release or opts.account and opts.account.skill_release
        })
    end
    return MockEnvironment.new(opts.world)
end

function Bootstrap.new(opts)
    opts = opts or {}
    local logger = Logger.new("agent", Config.logging)
    local environment = build_environment(opts, logger)
    local bb = Blackboard.new(opts)
    local executor = Executor.new(environment, Config, logger)
    return {
        config = Config,
        blackboard = bb,
        logger = logger,
        environment = environment,
        clock = Clock.new(),
        perception = Perception.new(environment, logger, Config.perception),
        planner = Planner.new(Config, logger),
        executor = executor,
        root_tree = RootTree.new(executor, logger),
        safety = Safety.new(Config, logger),
        snapshot = Snapshot.new(Config, logger)
    }
end

function Bootstrap.tick(system)
    local bb = system.blackboard
    system.clock:begin_tick(bb)
    system.perception:update(bb)
    system.safety:check(bb)
    system.planner:update(bb)
    system.root_tree:tick(bb)
    system.executor:flush(bb)
    Metrics.tick(bb, system.executor)
    system.snapshot:maybe_save(bb)
    system.clock:end_tick(bb)
    return bb.runtime.running
end

return Bootstrap
