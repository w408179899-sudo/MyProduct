local Debugger = {}

function Debugger.summary(bb)
    return string.format("tick=%s goal=%s failures=%s", tostring(bb.runtime.tick), tostring(bb.task.active_goal), tostring(bb.task.failure_count))
end

return Debugger
