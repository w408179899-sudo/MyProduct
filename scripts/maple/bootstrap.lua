local Config = require("maple.config")
local Blackboard = require("maple.blackboard")
local Clock = require("maple.core.clock")
local Logger = require("maple.systems.logger")
local MockEnvironment = require("maple.environment.mock_environment")
local Perception = require("maple.systems.perception")
local Planner = require("maple.planner.planner")
local Executor = require("maple.systems.executor")
local RootTree = require("maple.behaviors.root_tree")
local Safety = require("maple.systems.safety")
local Metrics = require("maple.systems.metrics")
local Snapshot = require("maple.systems.snapshot")

local Bootstrap = {}

function Bootstrap.new(opts)
    opts = opts or {}
    local logger = Logger.new("agent", Config.logging)
    local environment = opts.environment or MockEnvironment.new(opts.world)
    local bb = Blackboard.new(opts)
    local executor = Executor.new(environment, Config, logger)
    return {
        config = Config,
        blackboard = bb,
        logger = logger,
        environment = environment,
        clock = Clock.new(),
        perception = Perception.new(environment, logger),
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
