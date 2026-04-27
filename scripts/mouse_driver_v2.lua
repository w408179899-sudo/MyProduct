local MouseDriver = {}
MouseDriver.__index = MouseDriver

local MAX_PRNG = 2147483647

local PROFILE_PRESETS = {
    steady = {
        label = "steady",
        target_width = 10,
        report_rate_hz = 500,
        fitts_a_ms = 85,
        fitts_b_ms = 135,
        min_duration_ms = 140,
        max_duration_ms = 1100,
        speed_gain_min = 0.95,
        speed_gain_max = 1.10,
        deviation_min = 0.10,
        deviation_max = 0.18,
        deviation_px_min = 18,
        deviation_px_max = 96,
        noise_amplitude = 1.35,
        noise_frequency = 4.2,
        high_speed_noise_damping = 0.54,
        low_speed_noise_gain = 0.44,
        target_jitter_gain = 0.58,
        target_jitter_start = 0.74,
        overshoot_probability = 0.24,
        overshoot_min_ratio = 0.025,
        overshoot_max_ratio = 0.065,
        fatigue_noise_gain = 0.45,
        fatigue_speed_penalty = 0.22,
        fatigue_ramp_ms = 45 * 60 * 1000
    },
    swift = {
        label = "swift",
        target_width = 12,
        report_rate_hz = 500,
        fitts_a_ms = 70,
        fitts_b_ms = 115,
        min_duration_ms = 110,
        max_duration_ms = 760,
        speed_gain_min = 1.10,
        speed_gain_max = 1.32,
        deviation_min = 0.08,
        deviation_max = 0.15,
        deviation_px_min = 16,
        deviation_px_max = 82,
        noise_amplitude = 1.05,
        noise_frequency = 4.8,
        high_speed_noise_damping = 0.60,
        low_speed_noise_gain = 0.34,
        target_jitter_gain = 0.42,
        target_jitter_start = 0.78,
        overshoot_probability = 0.18,
        overshoot_min_ratio = 0.020,
        overshoot_max_ratio = 0.050,
        fatigue_noise_gain = 0.38,
        fatigue_speed_penalty = 0.18,
        fatigue_ramp_ms = 55 * 60 * 1000
    },
    careful = {
        label = "careful",
        target_width = 8,
        report_rate_hz = 500,
        fitts_a_ms = 105,
        fitts_b_ms = 155,
        min_duration_ms = 180,
        max_duration_ms = 1250,
        speed_gain_min = 0.82,
        speed_gain_max = 0.96,
        deviation_min = 0.12,
        deviation_max = 0.20,
        deviation_px_min = 20,
        deviation_px_max = 108,
        noise_amplitude = 1.55,
        noise_frequency = 3.9,
        high_speed_noise_damping = 0.48,
        low_speed_noise_gain = 0.58,
        target_jitter_gain = 0.72,
        target_jitter_start = 0.70,
        overshoot_probability = 0.30,
        overshoot_min_ratio = 0.025,
        overshoot_max_ratio = 0.072,
        fatigue_noise_gain = 0.55,
        fatigue_speed_penalty = 0.28,
        fatigue_ramp_ms = 40 * 60 * 1000
    },
    shaky = {
        label = "shaky",
        target_width = 10,
        report_rate_hz = 500,
        fitts_a_ms = 95,
        fitts_b_ms = 145,
        min_duration_ms = 150,
        max_duration_ms = 1180,
        speed_gain_min = 0.88,
        speed_gain_max = 1.04,
        deviation_min = 0.13,
        deviation_max = 0.24,
        deviation_px_min = 24,
        deviation_px_max = 122,
        noise_amplitude = 1.90,
        noise_frequency = 5.2,
        high_speed_noise_damping = 0.40,
        low_speed_noise_gain = 0.78,
        target_jitter_gain = 0.96,
        target_jitter_start = 0.66,
        overshoot_probability = 0.34,
        overshoot_min_ratio = 0.030,
        overshoot_max_ratio = 0.085,
        fatigue_noise_gain = 0.70,
        fatigue_speed_penalty = 0.26,
        fatigue_ramp_ms = 35 * 60 * 1000
    }
}

local PROFILE_ORDER = {
    "steady",
    "swift",
    "careful",
    "shaky"
}

local ProfileGenerator = {}
ProfileGenerator.__index = ProfileGenerator

local function now_ms()
    if type(sys) == "table" and type(sys.time) == "function" then
        local value = tonumber(sys.time())
        if value ~= nil then
            return value
        end
    end
    return math.floor((os.clock() or 0) * 1000)
end

local function clamp(value, min_value, max_value)
    if value < min_value then
        return min_value
    end
    if value > max_value then
        return max_value
    end
    return value
end

local function round(value)
    if value >= 0 then
        return math.floor(value + 0.5)
    end
    return math.ceil(value - 0.5)
end

local function copy_table(source)
    local result = {}
    for key, value in pairs(source or {}) do
        if type(value) == "table" then
            result[key] = copy_table(value)
        else
            result[key] = value
        end
    end
    return result
end

local function copy_point(point)
    return {
        x = tonumber(point.x) or 0,
        y = tonumber(point.y) or 0
    }
end

local function clamp_point(point, bounds)
    if type(bounds) ~= "table" then
        return copy_point(point)
    end

    return {
        x = clamp(tonumber(point.x) or 0, tonumber(bounds.left) or 0, tonumber(bounds.right) or 0),
        y = clamp(tonumber(point.y) or 0, tonumber(bounds.top) or 0, tonumber(bounds.bottom) or 0)
    }
end

local function normalize(dx, dy)
    local length = math.sqrt(dx * dx + dy * dy)
    if length <= 0.0001 then
        return 0, 0, 0
    end
    return dx / length, dy / length, length
end

local function cubic_point(p0, p1, p2, p3, t)
    local u = 1 - t
    local uu = u * u
    local tt = t * t
    local uuu = uu * u
    local ttt = tt * t
    return {
        x = uuu * p0.x + 3 * uu * t * p1.x + 3 * u * tt * p2.x + ttt * p3.x,
        y = uuu * p0.y + 3 * uu * t * p1.y + 3 * u * tt * p2.y + ttt * p3.y
    }
end

local function smoothstep5(t)
    return t * t * t * (t * (t * 6 - 15) + 10)
end

local function ease_out_cubic(t)
    local u = 1 - clamp(t, 0, 1)
    return 1 - u * u * u
end

local function ease_in_out_sine(t)
    return 0.5 - 0.5 * math.cos(math.pi * clamp(t, 0, 1))
end

local function ease_out_sine(t)
    return math.sin(clamp(t, 0, 1) * math.pi * 0.5)
end

local function lerp(a, b, t)
    return a + (b - a) * t
end

local function log2(value)
    return math.log(value) / math.log(2)
end

local function fnv1a32(text)
    local hash = 2166136261
    local value = tostring(text or "")
    for index = 1, #value do
        hash = (hash * 16777619 + string.byte(value, index)) % 4294967296
    end
    if hash <= 0 then
        hash = hash + 4294967295
    end
    return hash
end

local function normalize_motion_shape(burst_ratio, cruise_ratio, burst_distance, cruise_distance)
    local phase_one = clamp(tonumber(burst_ratio) or 0.16, 0.05, 0.42)
    local phase_two = clamp(tonumber(cruise_ratio) or 0.58, 0.12, 0.80)
    if phase_one + phase_two > 0.92 then
        phase_two = 0.92 - phase_one
    end
    local phase_three = math.max(0.08, 1 - phase_one - phase_two)

    local distance_one = clamp(tonumber(burst_distance) or 0.26, 0.08, 0.60)
    local distance_two = clamp(tonumber(cruise_distance) or 0.58, 0.10, 0.84)
    if distance_one + distance_two > 0.96 then
        distance_two = 0.96 - distance_one
    end
    local distance_three = math.max(0.04, 1 - distance_one - distance_two)

    return phase_one, phase_two, phase_three, distance_one, distance_two, distance_three
end

local function distribute_duration(total_ms, count)
    local delays = {}
    if count <= 0 then
        return delays
    end

    local base = math.floor(total_ms / count)
    local remainder = math.max(0, total_ms - base * count)
    for index = 1, count do
        delays[index] = base
    end
    for index = 1, remainder do
        local slot = ((index - 1) % count) + 1
        delays[slot] = delays[slot] + 1
    end
    return delays
end

local function scale_delays_to_duration(delays, target_ms)
    local scaled = {}
    local count = #delays
    if count <= 0 then
        return scaled
    end

    local target = math.max(0, round(target_ms))
    local source_total = 0
    for _, delay in ipairs(delays) do
        source_total = source_total + math.max(0, round(tonumber(delay) or 0))
    end

    if source_total <= 0 then
        return distribute_duration(target, count)
    end

    local assigned = 0
    for index, delay in ipairs(delays) do
        local scaled_delay = math.max(0, round(target * math.max(0, round(tonumber(delay) or 0)) / source_total))
        scaled[index] = scaled_delay
        assigned = assigned + scaled_delay
    end

    local delta = target - assigned
    local guard = 0
    while delta ~= 0 and count > 0 and guard < count * 4 do
        local index = ((guard % count) + 1)
        if delta > 0 then
            scaled[index] = (scaled[index] or 0) + 1
            delta = delta - 1
        elseif (scaled[index] or 0) > 0 then
            scaled[index] = scaled[index] - 1
            delta = delta + 1
        end
        guard = guard + 1
    end

    return scaled
end

local function append_plan_point(points, point)
    local x = round(tonumber(point.x) or 0)
    local y = round(tonumber(point.y) or 0)
    local time_value = math.max(0, tonumber(point.time) or 0)
    local pressure = clamp(tonumber(point.pressure) or 0, 0, 1)

    local last = points[#points]
    if last and last.x == x and last.y == y then
        last.time = math.max(last.time or 0, time_value)
        last.pressure = pressure
        return
    end

    points[#points + 1] = {
        x = x,
        y = y,
        time = time_value,
        pressure = pressure
    }
end

function ProfileGenerator.seed_from_key(key)
    return math.max(1, fnv1a32(key) % (MAX_PRNG - 1))
end

function ProfileGenerator.new(seed)
    local self = setmetatable({}, ProfileGenerator)
    self:set_seed(seed)
    return self
end

function ProfileGenerator:set_seed(seed)
    local value = math.floor(math.abs(tonumber(seed) or 1))
    value = value % (MAX_PRNG - 1)
    if value <= 0 then
        value = 1
    end
    self.seed = value
end

function ProfileGenerator:_hash01(index, salt)
    local raw = math.sin((self.seed * 0.013) + index * 12.9898 + salt * 78.233) * 43758.5453123
    return raw - math.floor(raw)
end

function ProfileGenerator:_profile_name_from_seed()
    local index = (math.floor(self.seed) % #PROFILE_ORDER) + 1
    return PROFILE_ORDER[index]
end

function ProfileGenerator:_traits()
    local traits = {
        reaction_speed = self:_hash01(1, 11),
        hand_stability = self:_hash01(2, 17),
        decisiveness = self:_hash01(3, 23),
        overshoot_tendency = self:_hash01(4, 29),
        endurance = self:_hash01(5, 31),
        tremor_bias = self:_hash01(6, 37),
        curve_bias = self:_hash01(7, 41)
    }
    return traits
end

function ProfileGenerator:generate(name, overrides)
    local chosen_name = tostring(name or "")
    if PROFILE_PRESETS[chosen_name] == nil then
        chosen_name = self:_profile_name_from_seed()
    end

    local profile = copy_table(PROFILE_PRESETS[chosen_name] or PROFILE_PRESETS.steady)
    local traits = self:_traits()
    local instability = 1 - traits.hand_stability

    profile.fitts_a_ms = round(clamp(
        (tonumber(profile.fitts_a_ms) or 80) * lerp(1.18, 0.86, traits.reaction_speed),
        45,
        200
    ))
    profile.fitts_b_ms = round(clamp(
        (tonumber(profile.fitts_b_ms) or 130) * lerp(1.16, 0.88, traits.reaction_speed),
        70,
        260
    ))
    profile.min_duration_ms = round(clamp(
        (tonumber(profile.min_duration_ms) or 120) * lerp(1.12, 0.84, traits.reaction_speed),
        60,
        420
    ))
    profile.max_duration_ms = round(clamp(
        (tonumber(profile.max_duration_ms) or 1200) * lerp(1.10, 0.90, traits.endurance),
        360,
        2200
    ))

    profile.speed_gain_min = clamp(
        (tonumber(profile.speed_gain_min) or 0.9) * lerp(0.94, 1.12, traits.reaction_speed),
        0.55,
        1.70
    )
    profile.speed_gain_max = clamp(
        (tonumber(profile.speed_gain_max) or 1.1) * lerp(0.96, 1.18, traits.reaction_speed),
        profile.speed_gain_min + 0.03,
        2.00
    )

    profile.deviation_min = clamp(
        (tonumber(profile.deviation_min) or 0.10) * lerp(0.78, 1.24, instability),
        0.03,
        0.34
    )
    profile.deviation_max = clamp(
        (tonumber(profile.deviation_max) or 0.18) * lerp(0.82, 1.32, instability),
        profile.deviation_min + 0.02,
        0.46
    )
    profile.deviation_px_min = round(clamp(
        (tonumber(profile.deviation_px_min) or 18) * lerp(0.80, 1.18, instability),
        8,
        54
    ))
    profile.deviation_px_max = round(clamp(
        (tonumber(profile.deviation_px_max) or 96) * lerp(0.84, 1.26, instability),
        profile.deviation_px_min + 18,
        196
    ))

    profile.noise_amplitude = clamp(
        (tonumber(profile.noise_amplitude) or 1.2) * lerp(0.68, 1.65, instability * 0.7 + traits.tremor_bias * 0.3),
        0.10,
        4.50
    )
    profile.noise_frequency = clamp(
        (tonumber(profile.noise_frequency) or 4.0) * lerp(0.80, 1.40, traits.tremor_bias),
        0.80,
        10.00
    )
    profile.tremor_amplitude = profile.noise_amplitude
    profile.tremor_frequency = profile.noise_frequency
    profile.tremor_octaves = clamp(round(lerp(2, 4, traits.tremor_bias)), 2, 5)
    profile.tremor_persistence = clamp(lerp(0.42, 0.68, traits.tremor_bias), 0.25, 0.80)
    profile.tremor_lacunarity = clamp(lerp(1.70, 2.45, traits.decisiveness), 1.20, 3.20)
    profile.tangent_noise_ratio = clamp(lerp(0.16, 0.36, traits.curve_bias), 0.05, 0.50)
    profile.high_speed_noise_damping = clamp(lerp(0.34, 0.68, traits.hand_stability), 0.15, 0.85)
    profile.low_speed_noise_gain = clamp(lerp(0.22, 0.98, instability), 0.10, 1.40)
    profile.target_jitter_gain = clamp(
        lerp(0.25, 1.10, instability * 0.72 + traits.tremor_bias * 0.28),
        0.10,
        1.60
    )
    profile.target_jitter_start = clamp(lerp(0.80, 0.64, instability), 0.55, 0.88)

    profile.overshoot_probability = clamp(
        (tonumber(profile.overshoot_probability) or 0.30) * lerp(0.72, 1.40, traits.overshoot_tendency),
        0.02,
        0.70
    )
    profile.overshoot_min_ratio = clamp(
        (tonumber(profile.overshoot_min_ratio) or 0.025) * lerp(0.85, 1.25, traits.overshoot_tendency),
        0.01,
        0.12
    )
    profile.overshoot_max_ratio = clamp(
        (tonumber(profile.overshoot_max_ratio) or 0.065) * lerp(0.88, 1.32, traits.overshoot_tendency),
        profile.overshoot_min_ratio + 0.01,
        0.20
    )
    profile.overshoot_lateral_jitter = round(clamp(lerp(3, 12, traits.curve_bias), 2, 18))

    profile.fatigue_noise_gain = clamp(
        (tonumber(profile.fatigue_noise_gain) or 0.5) * lerp(1.15, 0.82, traits.endurance),
        0.08,
        1.20
    )
    profile.fatigue_speed_penalty = clamp(
        (tonumber(profile.fatigue_speed_penalty) or 0.2) * lerp(1.20, 0.82, traits.endurance),
        0.05,
        0.65
    )
    profile.fatigue_ramp_ms = round(clamp(
        (tonumber(profile.fatigue_ramp_ms) or (45 * 60 * 1000)) * lerp(0.78, 1.35, traits.endurance),
        10 * 60 * 1000,
        90 * 60 * 1000
    ))

    profile.burst_ratio = clamp(lerp(0.12, 0.24, traits.reaction_speed), 0.08, 0.30)
    profile.cruise_ratio = clamp(lerp(0.40, 0.60, traits.decisiveness), 0.28, 0.66)
    profile.burst_distance_share = clamp(lerp(0.18, 0.34, traits.reaction_speed), 0.12, 0.40)
    profile.cruise_distance_share = clamp(lerp(0.40, 0.58, traits.decisiveness), 0.28, 0.66)
    profile.correction_burst_ratio = clamp(profile.burst_ratio * lerp(0.72, 0.88, traits.reaction_speed), 0.08, 0.24)
    profile.correction_cruise_ratio = clamp(profile.cruise_ratio * 0.55, 0.14, 0.40)
    profile.correction_burst_distance_share = clamp(profile.burst_distance_share * 0.82, 0.12, 0.36)
    profile.correction_cruise_distance_share = clamp(profile.cruise_distance_share * 0.55, 0.12, 0.42)
    profile.correction_duration_scale = clamp(lerp(0.54, 0.72, traits.decisiveness), 0.40, 0.86)
    profile.correction_noise_scale = clamp(lerp(0.36, 0.60, instability), 0.20, 0.80)

    profile.cp1_ratio_min = clamp(lerp(0.18, 0.30, traits.decisiveness), 0.12, 0.36)
    profile.cp1_ratio_max = clamp(profile.cp1_ratio_min + lerp(0.08, 0.14, traits.curve_bias), profile.cp1_ratio_min + 0.04, 0.46)
    profile.cp2_ratio_min = clamp(lerp(0.58, 0.72, traits.decisiveness), 0.48, 0.80)
    profile.cp2_ratio_max = clamp(profile.cp2_ratio_min + lerp(0.08, 0.14, traits.curve_bias), profile.cp2_ratio_min + 0.04, 0.90)
    profile.control_tangent_ratio = clamp(lerp(0.22, 0.52, traits.curve_bias), 0.10, 0.80)
    profile.control_bias_coupling = clamp(lerp(0.45, 0.82, traits.curve_bias), 0.20, 0.95)
    profile.traits = traits
    profile.seed = self.seed

    for key, value in pairs(overrides or {}) do
        profile[key] = value
    end

    return chosen_name, profile, traits
end

function MouseDriver.available_profiles()
    local names = {}
    for _, name in ipairs(PROFILE_ORDER) do
        names[#names + 1] = name
    end
    return names
end

function MouseDriver:_normalize_seed(seed)
    local value = math.floor(math.abs(tonumber(seed) or 0))
    value = value % (MAX_PRNG - 1)
    if value <= 0 then
        value = 1
    end
    return value
end

function MouseDriver:_next_random()
    self.random_state = (self.random_state * 48271) % MAX_PRNG
    return self.random_state / MAX_PRNG
end

function MouseDriver:randf(min_value, max_value)
    return min_value + (max_value - min_value) * self:_next_random()
end

function MouseDriver:randi(min_value, max_value)
    local low = math.floor(tonumber(min_value) or 0)
    local high = math.floor(tonumber(max_value) or low)
    if high < low then
        low, high = high, low
    end
    if high <= low then
        return low
    end
    return low + math.floor(self:_next_random() * (high - low + 1))
end

function MouseDriver:randn()
    local cached = tonumber(self.random_gaussian_cache)
    if cached ~= nil then
        self.random_gaussian_cache = nil
        return cached
    end

    local u1 = math.max(1e-7, self:_next_random())
    local u2 = self:_next_random()
    local magnitude = math.sqrt(-2 * math.log(u1))
    local theta = 2 * math.pi * u2

    self.random_gaussian_cache = magnitude * math.sin(theta)
    return magnitude * math.cos(theta)
end

function MouseDriver:gaussf(mean, deviation, min_value, max_value)
    local value = (tonumber(mean) or 0) + self:randn() * (tonumber(deviation) or 1)
    if min_value ~= nil and value < min_value then
        value = min_value
    end
    if max_value ~= nil and value > max_value then
        value = max_value
    end
    return value
end

function MouseDriver:_random_sign()
    if self:_next_random() < 0.5 then
        return -1
    end
    return 1
end

function MouseDriver:_hash01(index, salt)
    local raw = math.sin(index * 12.9898 + salt * 78.233 + self.seed * 0.0001) * 43758.5453123
    return raw - math.floor(raw)
end

function MouseDriver:_gradient(index, salt)
    return self:_hash01(index, salt) * 2 - 1
end

function MouseDriver:_noise1d(x, salt)
    local i0 = math.floor(x)
    local i1 = i0 + 1
    local local_t = x - i0
    local g0 = self:_gradient(i0, salt)
    local g1 = self:_gradient(i1, salt)
    local n0 = g0 * local_t
    local n1 = g1 * (local_t - 1)
    return lerp(n0, n1, smoothstep5(local_t)) * 2
end

function MouseDriver:_fractal_noise1d(x, salt, octaves, persistence, lacunarity)
    local total = 0
    local amplitude = 1
    local frequency = 1
    local normalization = 0
    local octave_count = clamp(round(tonumber(octaves) or 3), 1, 6)
    local persist = clamp(tonumber(persistence) or 0.55, 0.15, 0.95)
    local lac = clamp(tonumber(lacunarity) or 2.0, 1.1, 3.5)

    for octave = 1, octave_count do
        total = total + self:_noise1d(x * frequency + octave * 7.137, salt + octave * 19.91) * amplitude
        normalization = normalization + amplitude
        amplitude = amplitude * persist
        frequency = frequency * lac
    end

    if normalization <= 0 then
        return 0
    end
    return total / normalization
end

function MouseDriver:_profile_name_from_seed(seed)
    local index = (math.floor(seed) % #PROFILE_ORDER) + 1
    return PROFILE_ORDER[index]
end

function MouseDriver:set_seed(seed)
    self.seed = self:_normalize_seed(seed)
    self.random_state = self.seed
    self.random_gaussian_cache = nil
    self.profile_generator = self.profile_generator or ProfileGenerator.new(self.seed)
    self.profile_generator:set_seed(self.seed)
    self.profile_name = self.profile_name or self:_profile_name_from_seed(self.seed)
    self.created_at_ms = now_ms()
    self.noise_phase_x = self:randf(0, 32)
    self.noise_phase_y = self:randf(32, 64)
    self.noise_phase_t = self:randf(64, 96)
end

function MouseDriver:set_profile(name, overrides)
    local generator = self.profile_generator or ProfileGenerator.new(self.seed)
    generator:set_seed(self.seed)
    self.profile_generator = generator
    local chosen_name, profile, traits = generator:generate(name, overrides)
    self.profile_name = chosen_name
    self.profile = profile
    self.profile_traits = traits
    return self.profile_name, copy_table(self.profile), copy_table(self.profile_traits)
end

function MouseDriver:get_profile()
    return self.profile_name, copy_table(self.profile or {}), copy_table(self.profile_traits or {})
end

function MouseDriver:_fatigue_factor(reference_ms)
    local profile = self.profile or PROFILE_PRESETS.steady
    local elapsed = math.max(0, (tonumber(reference_ms) or now_ms()) - (tonumber(self.created_at_ms) or 0))
    local ramp_ms = math.max(60 * 1000, tonumber(profile.fatigue_ramp_ms) or (45 * 60 * 1000))
    return clamp(elapsed / ramp_ms, 0, 1)
end

function MouseDriver:_sample_motion_variation(distance, fatigue, correction)
    local z = clamp(self:gaussf(0, 1.0), -2.6, 2.6)
    local positive_tail = math.max(0, z - 1.10)
    local calm_tail = math.max(0, -z - 1.45)
    local fatigue_bias = clamp(0.30 + fatigue * 0.70 + self:gaussf(0.08, 0.10), 0.10, 1.0)
    local distance_scale = clamp((tonumber(distance) or 0) / 420, 0.25, 1.40)
    local archetype = "fatigued_drift"

    if correction then
        archetype = "micro_correction"
    elseif positive_tail >= 1.0 and distance_scale >= 0.45 then
        archetype = "rare_hook"
    elseif positive_tail >= 0.22 then
        archetype = "wide_sweep"
    elseif calm_tail >= 0.35 then
        archetype = "tight_sweep"
    end

    return {
        z = z,
        archetype = archetype,
        rare_motion = correction ~= true and positive_tail >= 1.0 and distance_scale >= 0.45,
        primary_bias_sign = self:_random_sign(),
        same_side_bias_probability = correction and 0.92 or clamp(0.88 - positive_tail * 0.36 + calm_tail * 0.12, 0.42, 0.97),
        deviation_scale = clamp(
            0.94 + fatigue_bias * 0.28 + z * 0.12 + positive_tail * 0.55 - calm_tail * 0.10,
            correction and 0.50 or 0.72,
            correction and 1.20 or 2.50
        ),
        noise_scale = clamp(
            1.04 + fatigue_bias * 0.40 + z * 0.10 + positive_tail * 0.62 - calm_tail * 0.05,
            correction and 0.45 or 0.80,
            correction and 1.15 or 2.90
        ),
        target_jitter_scale = clamp(0.96 + fatigue_bias * 0.30 + positive_tail * 0.28, 0.82, correction and 1.15 or 2.10),
        fatigue_sway_scale = clamp(0.34 + fatigue_bias * 0.52 + positive_tail * 0.18, 0.20, correction and 0.80 or 1.40),
        tangent_scale = clamp(0.88 + z * 0.08 + positive_tail * 0.16, 0.60, 1.55),
        duration_scale = clamp(1.00 + fatigue_bias * 0.10 + positive_tail * 0.04 + calm_tail * 0.05, 0.90, 1.22),
        overshoot_scale = correction and 0.65 or clamp(0.82 + positive_tail * 0.70 + fatigue_bias * 0.10 - calm_tail * 0.05, 0.45, 2.25),
        hook_strength = correction and 0.08 or clamp(positive_tail * 0.70 + distance_scale * 0.08, 0.04, 1.05),
        bias_coupling_scale = correction and 1.20 or clamp(1.12 - positive_tail * 0.42 + calm_tail * 0.18, 0.42, 1.28),
        burst_jitter_scale = correction and 0.15 or clamp(positive_tail * 0.55 + fatigue_bias * 0.18, 0.05, 1.15)
    }
end

function MouseDriver:_duration_window(opts)
    local profile = self.profile or PROFILE_PRESETS.steady
    local min_duration = tonumber(profile.min_duration_ms) or 120
    local max_duration = tonumber(profile.max_duration_ms) or 1200

    if type(opts) == "table" then
        if tonumber(opts.min_duration_ms) then
            min_duration = math.max(1, tonumber(opts.min_duration_ms) or min_duration)
        end
        if tonumber(opts.max_duration_ms) then
            max_duration = math.max(min_duration, tonumber(opts.max_duration_ms) or max_duration)
        end
    end

    local center_duration = tonumber(type(opts) == "table" and opts.duration_center_ms)
        or tonumber(profile.duration_center_ms)
        or lerp(min_duration, max_duration, 0.48)
    center_duration = clamp(center_duration, min_duration, max_duration)

    local duration_sigma = tonumber(type(opts) == "table" and opts.duration_sigma_ms)
        or tonumber(profile.duration_sigma_ms)
        or math.max(40, (max_duration - min_duration) / 4)
    duration_sigma = clamp(duration_sigma, 18, math.max(18, max_duration - min_duration))

    local gaussian_weight = tonumber(type(opts) == "table" and opts.duration_gaussian_weight)
        or tonumber(profile.duration_gaussian_weight)
        or 0.82
    gaussian_weight = clamp(gaussian_weight, 0, 1)

    local distribution = tostring(type(opts) == "table" and opts.duration_distribution or profile.duration_distribution or "default")

    return {
        min_duration_ms = min_duration,
        max_duration_ms = max_duration,
        center_duration_ms = center_duration,
        duration_sigma_ms = duration_sigma,
        gaussian_weight = gaussian_weight,
        distribution = distribution
    }
end

function MouseDriver:_predict_duration_ms(distance, target_width, fatigue, correction, opts, variation)
    local profile = self.profile or PROFILE_PRESETS.steady
    local width = math.max(2, tonumber(target_width) or tonumber(profile.target_width) or 10)
    local fitts_index = log2(distance / width + 1)
    local duration = (tonumber(profile.fitts_a_ms) or 80) + (tonumber(profile.fitts_b_ms) or 130) * fitts_index
    local speed_gain = self:randf(
        tonumber(profile.speed_gain_min) or 0.9,
        tonumber(profile.speed_gain_max) or 1.1
    )
    duration = duration / math.max(0.1, speed_gain)
    duration = duration * (1 + fatigue * (tonumber(profile.fatigue_speed_penalty) or 0.2))
    if correction then
        duration = duration * (tonumber(profile.correction_duration_scale) or 0.62)
    end
    if type(variation) == "table" and tonumber(variation.duration_scale) then
        duration = duration * math.max(0.65, tonumber(variation.duration_scale) or 1)
    end
    if type(opts) == "table" and tonumber(opts.duration_scale) then
        duration = duration * math.max(0.2, tonumber(opts.duration_scale) or 1)
    end

    local duration_window = self:_duration_window(opts)

    return clamp(
        round(duration),
        tonumber(duration_window.min_duration_ms) or 120,
        tonumber(duration_window.max_duration_ms) or 1200
    )
end

function MouseDriver:_sample_target_duration_ms(predicted_duration_ms, opts, variation)
    local duration_window = self:_duration_window(opts)
    local predicted = clamp(
        round(tonumber(predicted_duration_ms) or 0),
        tonumber(duration_window.min_duration_ms) or 120,
        tonumber(duration_window.max_duration_ms) or 1200
    )

    if string.lower(tostring(duration_window.distribution or "default")) ~= "gaussian" then
        return predicted
    end

    local center_duration = tonumber(duration_window.center_duration_ms) or predicted
    local duration_sigma = tonumber(duration_window.duration_sigma_ms) or 80
    local gaussian_weight = tonumber(duration_window.gaussian_weight) or 0.82
    local pull_ratio = clamp(0.22 + gaussian_weight * 0.20, 0.18, 0.45)
    local gaussian_mean = center_duration + (predicted - center_duration) * pull_ratio
    gaussian_mean = gaussian_mean + (tonumber(variation and variation.z) or 0) * 8
    gaussian_mean = clamp(
        gaussian_mean,
        tonumber(duration_window.min_duration_ms) or 120,
        tonumber(duration_window.max_duration_ms) or 1200
    )

    local gaussian_duration = self:gaussf(
        gaussian_mean,
        duration_sigma,
        tonumber(duration_window.min_duration_ms) or 120,
        tonumber(duration_window.max_duration_ms) or 1200
    )

    return clamp(
        round(lerp(predicted, gaussian_duration, gaussian_weight)),
        tonumber(duration_window.min_duration_ms) or 120,
        tonumber(duration_window.max_duration_ms) or 1200
    )
end

function MouseDriver:_retime_plan_points(points, target_duration_ms)
    if type(points) ~= "table" or #points <= 1 then
        return math.max(0, round(tonumber(target_duration_ms) or 0))
    end

    local delays = {}
    for index = 2, #points do
        local current_time = tonumber(points[index].time) or 0
        local previous_time = tonumber(points[index - 1].time) or 0
        delays[#delays + 1] = math.max(0, round(current_time - previous_time))
    end

    local scaled = scale_delays_to_duration(delays, target_duration_ms)
    local total_time = 0
    points[1].time = 0
    for index = 2, #points do
        total_time = total_time + math.max(0, tonumber(scaled[index - 1]) or 0)
        points[index].time = total_time
    end

    return total_time
end

function MouseDriver:_travel_progress(progress, correction)
    local p = clamp(progress, 0, 1)
    local profile = self.profile or PROFILE_PRESETS.steady
    local phase_one, phase_two, phase_three
    local distance_one, distance_two, distance_three

    if correction then
        phase_one, phase_two, phase_three, distance_one, distance_two, distance_three = normalize_motion_shape(
            tonumber(profile.correction_burst_ratio) or (tonumber(profile.burst_ratio) or 0.16) * 0.82,
            tonumber(profile.correction_cruise_ratio) or (tonumber(profile.cruise_ratio) or 0.58) * 0.55,
            tonumber(profile.correction_burst_distance_share) or (tonumber(profile.burst_distance_share) or 0.26) * 0.82,
            tonumber(profile.correction_cruise_distance_share) or (tonumber(profile.cruise_distance_share) or 0.58) * 0.55
        )
    else
        phase_one, phase_two, phase_three, distance_one, distance_two, distance_three = normalize_motion_shape(
            tonumber(profile.burst_ratio) or 0.16,
            tonumber(profile.cruise_ratio) or 0.58,
            tonumber(profile.burst_distance_share) or 0.26,
            tonumber(profile.cruise_distance_share) or 0.58
        )
    end

    if p < phase_one then
        return distance_one * ease_out_cubic(p / phase_one)
    end
    if p < phase_one + phase_two then
        return distance_one + distance_two * ((p - phase_one) / phase_two)
    end
    if correction then
        return distance_one + distance_two + distance_three * ease_out_sine((p - phase_one - phase_two) / phase_three)
    end
    return distance_one + distance_two + distance_three * ease_in_out_sine((p - phase_one - phase_two) / phase_three)
end

function MouseDriver:_pressure(progress, correction)
    local p = clamp(progress, 0, 1)
    local base = 0.20 + 0.72 * (math.sin(math.pi * p) ^ 0.82)
    if correction then
        return clamp(0.28 + base * 0.55, 0, 1)
    end
    return clamp(base, 0, 1)
end

function MouseDriver:_build_control_points(start_point, end_point, distance, fatigue, correction, variation)
    local profile = self.profile or PROFILE_PRESETS.steady
    local dir_x, dir_y = normalize(end_point.x - start_point.x, end_point.y - start_point.y)
    local normal_x = -dir_y
    local normal_y = dir_x

    local deviation = distance * self:randf(
        tonumber(profile.deviation_min) or 0.10,
        tonumber(profile.deviation_max) or 0.18
    )
    deviation = clamp(
        deviation,
        tonumber(profile.deviation_px_min) or 18,
        tonumber(profile.deviation_px_max) or 96
    )
    deviation = deviation * (1 + fatigue * 0.22)
    deviation = deviation * (tonumber(variation and variation.deviation_scale) or 1)
    if correction then
        deviation = deviation * (tonumber(profile.correction_noise_scale) or 0.52)
    end

    local tangent = deviation
        * (tonumber(profile.control_tangent_ratio) or 0.38)
        * (tonumber(variation and variation.tangent_scale) or 1)
    local primary_sign = tonumber(variation and variation.primary_bias_sign) or self:_random_sign()
    local secondary_sign = primary_sign
    if self:_next_random() > clamp(tonumber(variation and variation.same_side_bias_probability) or 0.78, 0.05, 0.98) then
        secondary_sign = -primary_sign
    end

    local bias_a = primary_sign * deviation * self:randf(0.48, 1.05)
    local bias_b = secondary_sign * deviation * self:randf(0.40, 1.00)
    local bias_coupling = clamp(
        (tonumber(profile.control_bias_coupling) or 0.55) * (tonumber(variation and variation.bias_coupling_scale) or 1),
        0.08,
        0.98
    )
    if self:_next_random() < bias_coupling then
        bias_b = bias_a * self:randf(0.55, 1.10)
    else
        bias_b = bias_b + secondary_sign * deviation * (tonumber(variation and variation.hook_strength) or 0) * self:randf(0.08, 0.42)
    end

    local cp1 = {
        x = start_point.x + (end_point.x - start_point.x) * self:randf(
            tonumber(profile.cp1_ratio_min) or 0.22,
            tonumber(profile.cp1_ratio_max) or 0.36
        )
            + normal_x * bias_a
            + dir_x * self:randf(-tangent, tangent),
        y = start_point.y + (end_point.y - start_point.y) * self:randf(
            tonumber(profile.cp1_ratio_min) or 0.22,
            tonumber(profile.cp1_ratio_max) or 0.36
        )
            + normal_y * bias_a
            + dir_y * self:randf(-tangent, tangent)
    }
    local cp2 = {
        x = start_point.x + (end_point.x - start_point.x) * self:randf(
            tonumber(profile.cp2_ratio_min) or 0.62,
            tonumber(profile.cp2_ratio_max) or 0.82
        )
            + normal_x * bias_b
            - dir_x * self:randf(-tangent, tangent),
        y = start_point.y + (end_point.y - start_point.y) * self:randf(
            tonumber(profile.cp2_ratio_min) or 0.62,
            tonumber(profile.cp2_ratio_max) or 0.82
        )
            + normal_y * bias_b
            - dir_y * self:randf(-tangent, tangent)
    }

    return cp1, cp2
end

function MouseDriver:_noise_offset(progress, dir_x, dir_y, correction, fatigue, stage_phase, speed_ratio, remaining_ratio, variation)
    local profile = self.profile or PROFILE_PRESETS.steady
    local p = clamp(progress, 0, 1)
    local speed = clamp(tonumber(speed_ratio) or 1, 0.10, 3.00)
    local target_remaining = clamp(tonumber(remaining_ratio) or (1 - p), 0, 1)
    local target_settle = ease_in_out_sine(1 - target_remaining)
    local target_start = clamp(tonumber(profile.target_jitter_start) or 0.74, 0.50, 0.92)
    local target_window = math.max(0.04, 1 - target_start)
    local near_target = ease_in_out_sine(clamp((p - target_start) / target_window, 0, 1))
    local high_speed_noise_damping = clamp(tonumber(profile.high_speed_noise_damping) or 0.5, 0, 0.9)
    local low_speed_noise_gain = clamp(tonumber(profile.low_speed_noise_gain) or 0.45, 0, 1.8)
    local target_jitter_gain = clamp(tonumber(profile.target_jitter_gain) or 0.6, 0, 2.4)
        * (tonumber(variation and variation.target_jitter_scale) or 1)
    local speed_damping = 1 - math.max(0, speed - 1) * high_speed_noise_damping * 0.55
    local low_speed_boost = 1 + math.max(0, 1 - speed) * low_speed_noise_gain
    local target_boost = 1 + near_target * target_jitter_gain
    local envelope = 0.16 + 0.32 * (math.sin(math.pi * p) ^ 0.72)
    envelope = envelope * clamp(speed_damping, 0.18, 1.0) * low_speed_boost * target_boost
    envelope = envelope * lerp(0.92, 1.10, target_settle)
    local amplitude = (tonumber(profile.tremor_amplitude) or tonumber(profile.noise_amplitude) or 1.2) * envelope
    amplitude = amplitude * (1 + fatigue * (tonumber(profile.fatigue_noise_gain) or 0.5))
    amplitude = amplitude * (tonumber(variation and variation.noise_scale) or 1)
    if correction then
        amplitude = amplitude * (tonumber(profile.correction_noise_scale) or 0.52)
    end

    local frequency = (tonumber(profile.tremor_frequency) or tonumber(profile.noise_frequency) or 4.0)
        * (correction and 1.15 or 1.0)
    local octaves = tonumber(profile.tremor_octaves) or 3
    local persistence = tonumber(profile.tremor_persistence) or 0.55
    local lacunarity = tonumber(profile.tremor_lacunarity) or 2.0
    local tangent_ratio = tonumber(profile.tangent_noise_ratio) or 0.28
    local t = progress * frequency
    local normal_x = -dir_y
    local normal_y = dir_x
    local perp_noise = self:_fractal_noise1d(t + self.noise_phase_x + stage_phase, 17, octaves, persistence, lacunarity)
    local tangent_noise = self:_fractal_noise1d(t * 1.37 + self.noise_phase_y + stage_phase, 53, octaves, persistence, lacunarity)
    local drift_envelope = 0.20 + 0.80 * (math.sin(math.pi * p) ^ 0.68)
    local fatigue_sway = self:_fractal_noise1d(t * 0.42 + self.noise_phase_t + stage_phase, 89, 2, 0.68, 1.85)
    local burst_in = ease_in_out_sine(clamp((p - 0.16) / 0.28, 0, 1))
    local burst_out = 1 - ease_in_out_sine(clamp((p - 0.78) / 0.18, 0, 1))
    local burst_window = burst_in * burst_out
    local burst_noise = self:_noise1d(t * 2.4 + stage_phase, 131)
    local perp_amount = perp_noise * amplitude
        + fatigue_sway * amplitude * (tonumber(variation and variation.fatigue_sway_scale) or 1) * drift_envelope
        + burst_noise * amplitude * (tonumber(variation and variation.burst_jitter_scale) or 0) * burst_window
    local tangent_amount = tangent_noise * amplitude * tangent_ratio

    return {
        x = normal_x * perp_amount + dir_x * tangent_amount,
        y = normal_y * perp_amount + dir_y * tangent_amount
    }
end

function MouseDriver:_sample_stage(start_point, end_point, opts)
    local dx = end_point.x - start_point.x
    local dy = end_point.y - start_point.y
    local dir_x, dir_y, distance = normalize(dx, dy)
    local correction = opts.correction == true
    local fatigue = tonumber(opts.fatigue) or 0
    local variation = type(opts) == "table" and opts.variation or nil

    if distance <= 0.75 then
        return {
            duration_ms = 0,
            points = {
                {
                    x = round(end_point.x),
                    y = round(end_point.y),
                    delay_ms = 0,
                    pressure = 0.25
                }
            }
        }
    end

    if type(variation) ~= "table" then
        variation = self:_sample_motion_variation(distance, fatigue, correction)
    end

    local cp1, cp2 = self:_build_control_points(start_point, end_point, distance, fatigue, correction, variation)
    local duration_ms = self:_predict_duration_ms(distance, opts.target_width, fatigue, correction, opts, variation)
    local report_rate_hz = clamp(
        tonumber(opts.report_rate_hz) or tonumber((self.profile or PROFILE_PRESETS.steady).report_rate_hz) or 500,
        24,
        1000
    )
    local min_steps = correction and 5 or 7
    if distance >= 160 then
        min_steps = min_steps + 2
    end
    if distance >= 520 then
        min_steps = min_steps + 2
    end
    local max_steps = correction and 36 or 72
    if distance >= 720 then
        max_steps = max_steps + 12
    end
    local steps = clamp(
        round(duration_ms * report_rate_hz / 1000),
        min_steps,
        max_steps
    )
    local delays = distribute_duration(duration_ms, steps)
    local points = {}
    local bounds = opts.bounds
    local stage_phase = self:randf(0, 24) + (correction and 12 or 0)
    local average_speed = distance / math.max(1, duration_ms)
    local previous_curve_point = copy_point(start_point)

    for index = 1, steps do
        local progress = index / steps
        local curve_t = self:_travel_progress(progress, correction)
        local base_point = cubic_point(start_point, cp1, cp2, end_point, curve_t)
        local base_dx = base_point.x - previous_curve_point.x
        local base_dy = base_point.y - previous_curve_point.y
        local base_step_distance = math.sqrt(base_dx * base_dx + base_dy * base_dy)
        local local_speed = base_step_distance / math.max(1, tonumber(delays[index]) or 0)
        local speed_ratio = local_speed / math.max(0.001, average_speed)
        local remaining_ratio = 1 - curve_t
        local noise = self:_noise_offset(
            progress,
            dir_x,
            dir_y,
            correction,
            fatigue,
            stage_phase,
            speed_ratio,
            remaining_ratio,
            variation
        )
        local point = {
            x = base_point.x + noise.x,
            y = base_point.y + noise.y
        }
        point = clamp_point(point, bounds)
        points[#points + 1] = {
            x = round(point.x),
            y = round(point.y),
            delay_ms = delays[index] or 0,
            pressure = self:_pressure(progress, correction)
        }
        previous_curve_point = base_point
    end

    points[#points] = {
        x = round(end_point.x),
        y = round(end_point.y),
        delay_ms = points[#points] and points[#points].delay_ms or 0,
        pressure = correction and 0.34 or 0.22
    }

    return {
        duration_ms = duration_ms,
        points = points,
        variation = copy_table(variation)
    }
end

function MouseDriver:plan_move(start_x, start_y, end_x, end_y, opts)
    local start_point = {
        x = tonumber(start_x) or 0,
        y = tonumber(start_y) or 0
    }
    local end_point = {
        x = tonumber(end_x) or 0,
        y = tonumber(end_y) or 0
    }
    local bounds = type(opts) == "table" and opts.bounds or nil
    start_point = clamp_point(start_point, bounds)
    end_point = clamp_point(end_point, bounds)

    local dir_x, dir_y, distance = normalize(end_point.x - start_point.x, end_point.y - start_point.y)
    local fatigue = self:_fatigue_factor(type(opts) == "table" and opts.now_ms or nil)
    local target_width = math.max(
        2,
        tonumber(type(opts) == "table" and opts.target_width) or tonumber((self.profile or PROFILE_PRESETS.steady).target_width) or 10
    )

    local points = {
        {
            x = round(start_point.x),
            y = round(start_point.y),
            time = 0,
            pressure = 0.12
        }
    }

    if distance <= 0.75 then
        append_plan_point(points, {
            x = end_point.x,
            y = end_point.y,
            time = 0,
            pressure = 0.15
        })
        return {
            points = points,
            duration_ms = 0,
            overshoot = false,
            profile_name = self.profile_name,
            profile = copy_table(self.profile or {}),
            profile_traits = copy_table(self.profile_traits or {}),
            fatigue = fatigue,
            distance = distance
        }
    end

    local allow_overshoot = type(opts) ~= "table" or opts.allow_overshoot ~= false
    local base_variation = self:_sample_motion_variation(distance, fatigue, false)
    local overshoot_probability = tonumber(type(opts) == "table" and opts.overshoot_probability)
        or tonumber((self.profile or PROFILE_PRESETS.steady).overshoot_probability)
        or 0.30
    overshoot_probability = clamp(
        overshoot_probability * (tonumber(base_variation.overshoot_scale) or 1),
        0.01,
        0.92
    )
    local overshoot = allow_overshoot and distance >= 48 and self:_next_random() < overshoot_probability
    local total_time = 0

    if overshoot then
        local profile = self.profile or PROFILE_PRESETS.steady
        local overshoot_lateral_jitter = (tonumber(profile.overshoot_lateral_jitter) or 8)
            * clamp(0.85 + (tonumber(base_variation.hook_strength) or 0) * 0.55, 0.80, 1.80)
        local overshoot_distance = clamp(
            distance * self:randf(
                tonumber(profile.overshoot_min_ratio) or 0.025,
                tonumber(profile.overshoot_max_ratio) or 0.065
            ) * clamp(0.92 + (tonumber(base_variation.hook_strength) or 0) * 0.38, 0.90, 1.70),
            8,
            36
        )
        local overshoot_point = clamp_point({
            x = end_point.x + dir_x * overshoot_distance + (-dir_y) * self:randf(-overshoot_lateral_jitter, overshoot_lateral_jitter),
            y = end_point.y + dir_y * overshoot_distance + dir_x * self:randf(-overshoot_lateral_jitter, overshoot_lateral_jitter)
        }, bounds)

        local stage_out = self:_sample_stage(start_point, overshoot_point, {
            bounds = bounds,
            fatigue = fatigue,
            variation = base_variation,
            target_width = target_width * 1.15,
            report_rate_hz = type(opts) == "table" and opts.report_rate_hz or nil
        })
        for _, point in ipairs(stage_out.points or {}) do
            total_time = total_time + math.max(0, tonumber(point.delay_ms) or 0)
            append_plan_point(points, {
                x = point.x,
                y = point.y,
                time = total_time,
                pressure = point.pressure
            })
        end

        local _, _, correction_distance = normalize(end_point.x - overshoot_point.x, end_point.y - overshoot_point.y)
        local stage_back = self:_sample_stage(overshoot_point, end_point, {
            bounds = bounds,
            fatigue = fatigue,
            variation = self:_sample_motion_variation(correction_distance, fatigue, true),
            target_width = math.max(2, target_width * 0.75),
            correction = true,
            report_rate_hz = type(opts) == "table" and opts.report_rate_hz or nil
        })
        for _, point in ipairs(stage_back.points or {}) do
            total_time = total_time + math.max(0, tonumber(point.delay_ms) or 0)
            append_plan_point(points, {
                x = point.x,
                y = point.y,
                time = total_time,
                pressure = point.pressure
            })
        end
    else
        local stage = self:_sample_stage(start_point, end_point, {
            bounds = bounds,
            fatigue = fatigue,
            variation = base_variation,
            target_width = target_width,
            report_rate_hz = type(opts) == "table" and opts.report_rate_hz or nil
        })
        for _, point in ipairs(stage.points or {}) do
            total_time = total_time + math.max(0, tonumber(point.delay_ms) or 0)
            append_plan_point(points, {
                x = point.x,
                y = point.y,
                time = total_time,
                pressure = point.pressure
            })
        end
    end

    append_plan_point(points, {
        x = end_point.x,
        y = end_point.y,
        time = total_time,
        pressure = 0.14
    })

    total_time = self:_retime_plan_points(
        points,
        self:_sample_target_duration_ms(total_time, opts, base_variation)
    )

    return {
        points = points,
        duration_ms = total_time,
        overshoot = overshoot,
        profile_name = self.profile_name,
        profile = copy_table(self.profile or {}),
        profile_traits = copy_table(self.profile_traits or {}),
        fatigue = fatigue,
        distance = distance,
        target_width = target_width,
        motion_archetype = base_variation.archetype,
        variation_z = base_variation.z,
        rare_motion = base_variation.rare_motion == true,
        motion_variation = copy_table(base_variation),
        overshoot_probability = overshoot_probability
    }
end

function MouseDriver.seed_from_key(key)
    return ProfileGenerator.seed_from_key(key)
end

function MouseDriver.new_profile_generator(opts)
    local seed = tonumber(opts and opts.seed)
    local profile_key = opts and opts.profile_key or nil
    if seed == nil and profile_key ~= nil then
        seed = ProfileGenerator.seed_from_key(profile_key)
    end
    if seed == nil then
        seed = (os.time() or 1) + now_ms()
    end
    return ProfileGenerator.new(seed)
end

function MouseDriver.new(opts)
    local self = setmetatable({}, MouseDriver)
    local seed = tonumber(opts and opts.seed)
    local profile_key = opts and opts.profile_key or nil
    if seed == nil and profile_key ~= nil then
        seed = ProfileGenerator.seed_from_key(profile_key)
    end
    if seed == nil then
        seed = (os.time() or 1) + now_ms()
    end
    self.profile_key = profile_key
    self:set_seed(seed)
    local explicit_profile = opts and opts.profile or nil
    local overrides = opts and opts.profile_overrides or nil
    self:set_profile(explicit_profile, overrides)
    return self
end

MouseDriver.ProfileGenerator = ProfileGenerator

return MouseDriver
