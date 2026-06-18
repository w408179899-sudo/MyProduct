local Uuid = { next_id = 0 }

function Uuid.next(prefix)
    Uuid.next_id = Uuid.next_id + 1
    prefix = prefix or "id"
    return string.format("%s_%d", prefix, Uuid.next_id)
end

return Uuid
