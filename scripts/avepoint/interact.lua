function nearby_related_text(match, snapshot, step, max_distance)
    if type(match) ~= "table" or type(snapshot) ~= "table" or type(step) ~= "table" then
        return nil
    end

    local mx = tonumber(match.x)
    local my = tonumber(match.y)
    if mx == nil or my == nil then
        return nil
    end

    local exact_best = nil
    local pattern_best = nil
    local has_exact = type(step.related_exact_texts) == "table" and #step.related_exact_texts > 0
    local has_patterns = type(step.related_include_patterns) == "table" and #step.related_include_patterns > 0

    for _, item in ipairs(snapshot.texts or {}) do
        local text = trim(item.text or "")
        local name = tostring(item.name or "")
        local fullname = tostring(item.Fullname or item.fullname or "")
        local x = tonumber(item.x)
        local y = tonumber(item.y)
        if x ~= nil and y ~= nil then
            local dist = distance_2d(mx, my, x, y)
            if dist <= max_distance then
                local normalized = normalize_exact_text(text)
                if has_exact then
                    for _, wanted in ipairs(step.related_exact_texts) do
                        if normalized == normalize_exact_text(wanted) then
                            if not exact_best or dist < exact_best.distance then
                                exact_best = {
                                    text = text,
                                    name = name,
                                    fullname = fullname,
                                    distance = dist,
                                    x = x,
                                    y = y
                                }
                            end
                            break
                        end
                    end
                end

                if has_patterns and match_any_text(table.concat({
                    text,
                    name,
                    fullname
                }, " "), step.related_include_patterns) then
                    if not pattern_best or dist < pattern_best.distance then
                        pattern_best = {
                            text = text,
                            name = name,
                            fullname = fullname,
                            distance = dist,
                            x = x,
                            y = y
                        }
                    end
                end
            end
        end
    end

    if has_exact and not exact_best then
        return nil
    end

    if has_patterns and not pattern_best then
        return nil
    end

    local chosen = exact_best or pattern_best
    if chosen then
        local score = chosen.distance
        if exact_best and pattern_best then
            score = math.min(exact_best.distance, pattern_best.distance)
        end
        return {
            text = chosen.text,
            name = chosen.name,
            fullname = chosen.fullname,
            distance = chosen.distance,
            x = chosen.x,
            y = chosen.y,
            score = score,
            exact = exact_best,
            pattern = pattern_best
        }
    end

    return nil
end

function resolve_step_hint_point(step, opts)
    if type(step) ~= "table" then
        return nil
    end

    local hint_client_x = tonumber(step.hint_client_x)
    local hint_client_y = tonumber(step.hint_client_y)
    local hint_ratio_x = tonumber(step.hint_ratio_x)
    local hint_ratio_y = tonumber(step.hint_ratio_y)

    if hint_ratio_x ~= nil and hint_ratio_y ~= nil then
        local hwnd = avepoint_resolve_fetch_target_hwnd(opts)
        if hwnd ~= nil
            and type(wnd) == "table"
            and type(wnd.client_rect) == "function"
        then
            local _, _, client_w, client_h = wnd.client_rect(hwnd)
            if type(client_w) == "number"
                and type(client_h) == "number"
                and client_w > 0
                and client_h > 0
            then
                hint_client_x = hint_ratio_x * client_w
                hint_client_y = hint_ratio_y * client_h
            end
        end
    end

    if hint_client_x == nil or hint_client_y == nil then
        return nil
    end

    return {
        x = hint_client_x,
        y = hint_client_y,
        max_distance = math.max(0, tonumber(step.hint_max_distance) or math.huge)
    }
end

function pick_button_match(step, matches, snapshot, opts)
    if type(matches) ~= "table" or #matches == 0 then
        return nil, "Matched button not found."
    end

    local has_related_exact = type(step.related_exact_texts) == "table" and #step.related_exact_texts > 0
    local has_related_patterns = type(step.related_include_patterns) == "table" and #step.related_include_patterns > 0
    local hint = resolve_step_hint_point(step, opts)
    if not has_related_exact and not has_related_patterns then
        if hint then
            local best = nil
            local best_distance = nil

            for _, match in ipairs(matches) do
                local match_x = tonumber(match.x)
                local match_y = tonumber(match.y)
                if match_x ~= nil and match_y ~= nil then
                    local distance = distance_2d(match_x, match_y, hint.x, hint.y)
                    if distance <= hint.max_distance and (best_distance == nil or distance < best_distance) then
                        best = match
                        best_distance = distance
                    end
                end
            end

            if best then
                best.hint_distance = best_distance
                best.hint_x = hint.x
                best.hint_y = hint.y
                return best
            end

            return nil, "Matched button found but point hint condition failed."
        end

        return matches[1]
    end

    local best = nil
    local best_score = nil
    local max_distance = tonumber(step.related_max_distance) or 120

    for _, match in ipairs(matches) do
        local related = nearby_related_text(match, snapshot, step, max_distance)
        if related then
            local score = tonumber(related.score) or related.distance
            if best_score == nil or score < best_score then
                best = match
                best_score = score
                best.related_text = related.text
                best.related_name = related.name
                best.related_fullname = related.fullname
                best.related_text_distance = related.distance
                best.related_text_x = related.x
                best.related_text_y = related.y
            end
        end
    end

    if best then
        return best
    end

    return nil, "Matched button found but related text condition failed."
end

function avepoint_resolve_fetch_target_hwnd(opts)
    local preferred_hwnd = type(opts) == "table" and tonumber(opts.preferred_hwnd) or nil
    if preferred_hwnd ~= nil and preferred_hwnd ~= 0 then
        if type(wnd) ~= "table" or type(wnd.client_rect) ~= "function" then
            return preferred_hwnd
        end

        local _, _, w, h = wnd.client_rect(preferred_hwnd)
        if type(w) == "number" and type(h) == "number" and w > 0 and h > 0 then
            return preferred_hwnd
        end
    end

    local preferred_pid = type(opts) == "table" and tonumber(opts.preferred_pid) or nil
    if preferred_pid ~= nil and preferred_pid > 0 then
        local hwnd = nil
        if type(proc) == "table" and type(proc.window) == "function" then
            hwnd = proc.window(preferred_pid)
        end

        if (hwnd == nil or hwnd == 0)
            and type(wnd) == "table"
            and type(wnd.find_by_pid) == "function"
        then
            hwnd = wnd.find_by_pid(preferred_pid)
        end

        if hwnd ~= nil and hwnd ~= 0 then
            if type(opts) == "table" then
                opts.preferred_hwnd = hwnd
            end
            return hwnd
        end
    end

    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return nil, hwnd_err
    end

    if type(opts) == "table" then
        opts.preferred_hwnd = hwnd
    end

    return hwnd
end

function avepoint_capture_target_window_for_image_search(hwnd, image_preset)
    if image_preset.capture_set_foreground ~= false
        and type(wnd) == "table"
        and type(wnd.set_foreground) == "function"
    then
        wnd.set_foreground(hwnd)
        sys.sleep(tonumber(image_preset.capture_foreground_delay_ms) or 60)
    end

    local capture = nil
    local capture_method = nil
    local client_origin_x = 0
    local client_origin_y = 0

    if type(wnd) == "table" and type(wnd.client_rect) == "function" then
        local x, y, w, h = wnd.client_rect(hwnd)
        if type(x) == "number"
            and type(y) == "number"
            and type(w) == "number"
            and type(h) == "number"
            and w > 0
            and h > 0
        then
            client_origin_x = x
            client_origin_y = y
            capture = vision.capture(x, y, w, h)
            if valid_image(capture) then
                capture_method = "screen_region"
            else
                free_image(capture)
                capture = nil
            end
        end
    end

    if not capture then
        capture = vision.capture_window(hwnd, true)
        if valid_image(capture) then
            capture_method = "window_client"
        else
            free_image(capture)
            capture = nil
        end
    end

    if not capture then
        return nil, nil, nil, nil, "capture failed."
    end

    return capture, capture_method, client_origin_x, client_origin_y
end

function enrich_button_target_for_click(target, opts)
    if type(target) ~= "table" or target.kind == "image" then
        return target
    end

    local click_x = tonumber(target.x)
    local click_y = tonumber(target.y)
    if click_x == nil or click_y == nil then
        return target
    end

    local hwnd = avepoint_resolve_fetch_target_hwnd(opts)
    if not hwnd then
        return target
    end

    target.hwnd = hwnd
    target.click_button = target.click_button or "left"
    target.click_delay = tonumber(target.click_delay) or 50
    target.click_mode = tostring(target.click_mode or "api")
    target.hover_delay_ms = tonumber(target.hover_delay_ms) or 80

    if type(wnd) == "table" and type(wnd.client_rect) == "function" then
        local client_origin_x, client_origin_y = wnd.client_rect(hwnd)
        if type(client_origin_x) == "number" and type(client_origin_y) == "number" then
            target.click_screen_x = math.floor(client_origin_x + click_x + 0.5)
            target.click_screen_y = math.floor(client_origin_y + click_y + 0.5)
        end
    end

    return target
end

function fetch_button_by_text_distance(step, opts)
    if type(step) ~= "table" then
        return nil, nil
    end

    local anchor_text = trim(step.distance_anchor_exact_text or "")
    local button_name = tostring(step.distance_button_name or "")
    if anchor_text == "" or button_name == "" then
        return nil, nil
    end

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local distance_min = tonumber(step.distance_min) or 0
    local distance_max = tonumber(step.distance_max) or distance_min
    if distance_max < distance_min then
        distance_min, distance_max = distance_max, distance_min
    end

    local target_distance = (distance_min + distance_max) * 0.5
    local normalized_anchor_text = normalize_exact_text(anchor_text)
    local texts = snapshot.texts or {}
    local buttons = snapshot.buttons or {}
    local best = nil
    local nearest = nil

    for _, text_item in ipairs(texts) do
        if normalize_exact_text(text_item.text or "") == normalized_anchor_text then
            local text_x = tonumber(text_item.x)
            local text_y = tonumber(text_item.y)
            if text_x ~= nil and text_y ~= nil then
                for _, button_item in ipairs(buttons) do
                    local item_name = tostring(button_item.name or "")
                    local item_fullname = tostring(button_item.Fullname or button_item.fullname or "")
                    if item_name == button_name or item_fullname == button_name then
                        local button_x = tonumber(button_item.x)
                        local button_y = tonumber(button_item.y)
                        if button_x ~= nil and button_y ~= nil then
                            local distance = distance_2d(button_x, button_y, text_x, text_y)
                            local delta = math.abs(distance - target_distance)
                            local candidate = {
                                kind = "button",
                                addr = button_item.addr,
                                name = button_item.name,
                                text = button_item.text,
                                fullname = button_item.Fullname or button_item.fullname,
                                x = button_item.x,
                                y = button_item.y,
                                item = button_item,
                                distance = distance,
                                related_text = tostring(text_item.text or ""),
                                related_name = tostring(text_item.name or ""),
                                related_fullname = tostring(text_item.Fullname or text_item.fullname or ""),
                                related_text_distance = distance,
                                related_text_x = text_item.x,
                                related_text_y = text_item.y
                            }

                            if not nearest or delta < nearest.delta then
                                nearest = {
                                    candidate = candidate,
                                    delta = delta
                                }
                            end

                            if distance >= distance_min and distance <= distance_max then
                                if not best or delta < best.delta then
                                    best = {
                                        candidate = candidate,
                                        delta = delta
                                    }
                                end
                            end
                        end
                    end
                end
            end
        end
    end

    if not best then
        if nearest and nearest.candidate then
            return nil, string.format(
                "Distance target not found [%s]. nearest=%s related_text=%s distance=%.6f delta=%.6f expected=(%.3f, %.3f)",
                tostring(step.label or ""),
                control_summary(nearest.candidate),
                tostring(nearest.candidate.related_text or ""),
                tonumber(nearest.candidate.related_text_distance) or 0,
                tonumber(nearest.delta) or 0,
                distance_min,
                distance_max
            )
        end

        return nil, string.format(
            "Distance target not found [%s]. anchor_text=%s button_name=%s",
            tostring(step.label or ""),
            anchor_text,
            button_name
        )
    end

    local target = best.candidate
    enrich_button_target_for_click(target, opts)

    log.info(string.format(
        "Fetched button %s | anchor_mode=text_distance %s related_text=%s related_name=%s related_distance=%s expected=(%.3f, %.3f)",
        tostring(step.label or ""),
        control_summary(target),
        tostring(target.related_text or ""),
        tostring(target.related_name or ""),
        tostring(target.related_text_distance or ""),
        distance_min,
        distance_max
    ))

    return target
end

function step_has_non_distance_button_lookup(step, image_preset)
    if type(step) ~= "table" then
        return false
    end

    if image_preset ~= nil then
        return true
    end

    if type(step.anchor_exact_texts) == "table" and #step.anchor_exact_texts > 0 then
        return true
    end

    if type(step.anchor_include_patterns) == "table" and #step.anchor_include_patterns > 0 then
        return true
    end

    if type(step.include_patterns) == "table" and #step.include_patterns > 0 then
        return true
    end

    return false
end

local function step_supports_text_distance_lookup(step)
    return type(step) == "table"
        and trim(step.distance_anchor_exact_text or "") ~= ""
        and tostring(step.distance_button_name or "") ~= ""
end

local function step_supports_anchor_lookup(step)
    return type(step) == "table"
        and (
            (type(step.anchor_exact_texts) == "table" and #step.anchor_exact_texts > 0)
            or (type(step.anchor_include_patterns) == "table" and #step.anchor_include_patterns > 0)
        )
end

local function step_supports_generic_match_lookup(step)
    return type(step) == "table"
        and type(step.include_patterns) == "table"
        and #step.include_patterns > 0
end

local function resolve_step_target_by_image(step, opts, image_preset)
    if type(image_preset) ~= "table" then
        return nil, nil
    end

    if type(vision) ~= "table"
        or type(vision.capture) ~= "function"
        or type(vision.capture_window) ~= "function"
        or type(vision.load) ~= "function"
        or type(vision.find) ~= "function"
    then
        return nil, "vision API is not available."
    end

    local hwnd, hwnd_err = avepoint_resolve_fetch_target_hwnd(opts)
    if not hwnd then
        return nil, hwnd_err
    end

    local template_path = resolve_project_path(image_preset.template_path)
    local template = vision.load(template_path)
    if not template then
        return nil, "template load failed: " .. tostring(template_path)
    end

    local capture, capture_method, client_origin_x, client_origin_y, capture_err =
        avepoint_capture_target_window_for_image_search(hwnd, image_preset)
    if not capture then
        vision.free(template)
        return nil, capture_err or "capture failed."
    end

    local threshold = tonumber(image_preset.template_threshold) or 0.99
    local x, y, score = vision.find(capture, template, threshold)
    local match_mode = "color"
    local match_threshold = threshold

    if (not x or not y)
        and image_preset.allow_gray_fallback ~= false
        and type(vision.to_gray) == "function"
    then
        local gray_capture = vision.to_gray(capture)
        local gray_template = vision.to_gray(template)
        if gray_capture and gray_template then
            local gray_threshold = math.max(0.72, threshold - 0.06)
            x, y, score = vision.find(gray_capture, gray_template, gray_threshold)
            if x and y then
                match_mode = "gray"
                match_threshold = gray_threshold
            end
        end
        if gray_capture then
            vision.free(gray_capture)
        end
        if gray_template then
            vision.free(gray_template)
        end
    end

    if not x or not y then
        vision.free(template)
        vision.free(capture)
        return nil, string.format(
            "template not found: path=%s threshold=%.2f capture_method=%s",
            tostring(template_path),
            threshold,
            tostring(capture_method or "")
        )
    end

    local template_w = tonumber(template:width()) or 0
    local template_h = tonumber(template:height()) or 0
    local click_x = tonumber(x) or 0
    local click_y = tonumber(y) or 0
    local center_x = image_preset.click_center_x
    if center_x == nil then
        center_x = image_preset.click_center ~= false
    end
    local center_y = image_preset.click_center_y
    if center_y == nil then
        center_y = image_preset.click_center ~= false
    end
    if center_x then
        click_x = click_x + template_w * 0.5
    end
    if center_y then
        click_y = click_y + template_h * 0.5
    end
    click_x = math.floor(click_x + (tonumber(image_preset.click_offset_x) or 0) + 0.5)
    click_y = math.floor(click_y + (tonumber(image_preset.click_offset_y) or 0) + 0.5)

    local match_x = math.floor((tonumber(x) or 0) + 0.5)
    local match_y = math.floor((tonumber(y) or 0) + 0.5)
    local match_screen_x = math.floor(client_origin_x + match_x + 0.5)
    local match_screen_y = math.floor(client_origin_y + match_y + 0.5)
    local click_screen_x = math.floor(client_origin_x + click_x + 0.5)
    local click_screen_y = math.floor(client_origin_y + click_y + 0.5)

    vision.free(template)
    vision.free(capture)

    local target = {
        kind = "image",
        addr = "image",
        text = tostring(step.label or ""),
        name = tostring(image_preset.template_path or ""),
        fullname = tostring(template_path),
        x = click_x,
        y = click_y,
        hwnd = hwnd,
        click_screen_x = click_screen_x,
        click_screen_y = click_screen_y,
        click_button = image_preset.click_button or "left",
        click_delay = tonumber(image_preset.click_delay) or 50,
        click_repeat_count = math.max(1, math.floor(tonumber(image_preset.click_repeat_count) or 1)),
        click_repeat_interval_ms = math.max(0, tonumber(image_preset.click_repeat_interval_ms) or 120),
        click_mode = tostring(image_preset.click_mode or "api"),
        hover_delay_ms = tonumber(image_preset.hover_delay_ms) or 80,
        match_x = match_x,
        match_y = match_y,
        match_screen_x = match_screen_x,
        match_screen_y = match_screen_y,
        capture_method = capture_method,
        match_mode = match_mode,
        threshold = match_threshold,
        score = tonumber(score) or 0,
        template_w = template_w,
        template_h = template_h
    }

    log.info(string.format(
        "Fetched image target %s | path=%s mode=%s threshold=%.2f score=%.4f capture_method=%s match=(%d,%d) click_client=(%d,%d) click_screen=(%d,%d) repeat=%d",
        tostring(step.label or ""),
        tostring(template_path),
        tostring(match_mode),
        tonumber(match_threshold) or 0,
        tonumber(score) or 0,
        tostring(capture_method or ""),
        match_x,
        match_y,
        click_x,
        click_y,
        click_screen_x,
        click_screen_y,
        tonumber(target.click_repeat_count) or 1
    ))

    return target
end

local function resolve_step_target_by_anchor(step, opts)
    if not step_supports_anchor_lookup(step) then
        return nil, nil
    end

    local anchors, anchor_err = nav.find_controls_by_match({
        include_buttons = false,
        include_images = false,
        include_texts = true,
        exact_texts = step.anchor_exact_texts,
        include_patterns = step.anchor_include_patterns,
        exclude_patterns = step.anchor_exclude_patterns
    })
    if not anchors then
        return nil, anchor_err
    end

    local button_candidates, button_err = nav.find_controls_by_match({
        include_buttons = true,
        include_images = false,
        include_texts = false,
        include_patterns = step.neighbor_include_patterns or step.include_patterns,
        exclude_patterns = step.neighbor_exclude_patterns or step.exclude_patterns
    })
    if not button_candidates then
        return nil, button_err
    end

    local best = nil
    local best_score = nil

    for _, anchor in ipairs(anchors) do
        local anchor_x = tonumber(anchor.x)
        local anchor_y = tonumber(anchor.y)
        if anchor_x ~= nil and anchor_y ~= nil then
            if step.anchor_pick_mode == "point" then
                local point_matches = nil
                point_matches = select(1, nav.find_controls_at_point(anchor_x, anchor_y, {
                    include_buttons = true,
                    include_images = false,
                    include_texts = false,
                    include_patterns = step.neighbor_include_patterns or step.include_patterns,
                    exclude_patterns = step.neighbor_exclude_patterns or step.exclude_patterns,
                    max_distance = step.anchor_point_max_distance or step.neighbor_max_distance or 140,
                    limit = 6
                }))
                if point_matches and #point_matches > 0 then
                    local target = point_matches[1]
                    local score = tonumber(target.distance) or 0
                    if best_score == nil or score < best_score then
                        best = {
                            kind = target.kind,
                            addr = target.addr,
                            name = target.name,
                            text = target.text,
                            fullname = target.fullname,
                            x = target.x,
                            y = target.y,
                            item = target.item,
                            distance = target.distance
                        }
                        best_score = score
                        best.related_text = tostring(anchor.text or "")
                        best.related_name = tostring(anchor.name or "")
                        best.related_fullname = tostring(anchor.fullname or anchor.Fullname or "")
                        best.related_text_distance = score
                        best.related_text_x = anchor.x
                        best.related_text_y = anchor.y
                    end
                end
            else
                for _, target in ipairs(button_candidates) do
                    local target_x = tonumber(target.x)
                    local target_y = tonumber(target.y)
                    if target_x ~= nil and target_y ~= nil then
                        local score = distance_2d(anchor_x, anchor_y, target_x, target_y)
                        local max_distance = step.neighbor_max_distance or step.related_max_distance or 120
                        if score <= max_distance then
                            if step.anchor_prefer_above == true and target_y < anchor_y then
                                score = score + 10000
                            end
                            if best_score == nil or score < best_score then
                                best = {
                                    kind = target.kind,
                                    addr = target.addr,
                                    name = target.name,
                                    text = target.text,
                                    fullname = target.fullname,
                                    x = target.x,
                                    y = target.y,
                                    item = target.item
                                }
                                best_score = score
                                best.related_text = tostring(anchor.text or "")
                                best.related_name = tostring(anchor.name or "")
                                best.related_fullname = tostring(anchor.fullname or anchor.Fullname or "")
                                best.related_text_distance = distance_2d(anchor_x, anchor_y, target_x, target_y)
                                best.related_text_x = anchor.x
                                best.related_text_y = anchor.y
                            end
                        end
                    end
                end
            end
        end
    end

    if not best then
        return nil, "Matched anchor found but nearby button not found."
    end

    log.info(string.format(
        "Fetched button %s | anchor_mode=text_anchor %s related_text=%s related_name=%s related_distance=%s",
        tostring(step.label or ""),
        control_summary(best),
        tostring(best.related_text or ""),
        tostring(best.related_name or ""),
        tostring(best.related_text_distance or "")
    ))

    return best
end

local function resolve_step_target_by_generic_match(step, opts)
    if not step_supports_generic_match_lookup(step) then
        return nil, nil
    end

    local matches, err, snapshot = nav.find_controls_by_match({
        include_buttons = true,
        include_images = false,
        include_texts = false,
        include_patterns = step.include_patterns,
        exclude_patterns = step.exclude_patterns
    })
    if not matches then
        return nil, err
    end

    local target, pick_err = pick_button_match(step, matches, snapshot, opts)
    if not target then
        return nil, pick_err
    end

    log.info(string.format(
        "Fetched button %s | candidates=%d %s related_text=%s related_name=%s related_distance=%s hint_distance=%s",
        tostring(step.label or ""),
        #matches,
        control_summary(target),
        tostring(target.related_text or ""),
        tostring(target.related_name or ""),
        tostring(target.related_text_distance or ""),
        tostring(target.hint_distance or "")
    ))

    return target
end

STEP_TARGET_RESOLVERS = {
    {
        name = "text_distance",
        can_resolve = function(step, ctx)
            return step_supports_text_distance_lookup(step)
        end,
        resolve = function(step, opts, ctx)
            return fetch_button_by_text_distance(step, opts)
        end
    },
    {
        name = "image",
        can_resolve = function(step, ctx)
            return type(ctx.image_preset) == "table"
        end,
        resolve = function(step, opts, ctx)
            return resolve_step_target_by_image(step, opts, ctx.image_preset)
        end
    },
    {
        name = "text_anchor",
        can_resolve = function(step, ctx)
            return step_supports_anchor_lookup(step)
        end,
        resolve = function(step, opts, ctx)
            return resolve_step_target_by_anchor(step, opts)
        end
    },
    {
        name = "generic_match",
        can_resolve = function(step, ctx)
            return step_supports_generic_match_lookup(step)
        end,
        resolve = function(step, opts, ctx)
            return resolve_step_target_by_generic_match(step, opts)
        end
    }
}

local function step_has_fallback_target_resolver(step, ctx)
    for _, resolver in ipairs(STEP_TARGET_RESOLVERS) do
        if resolver.name ~= "text_distance" and resolver.can_resolve(step, ctx) then
            return true
        end
    end
    return false
end

function fetch_button_for_step(step, opts)
    local ctx = {
        image_preset = resolve_image_click_preset(step)
    }

    local last_err = nil
    for _, resolver in ipairs(STEP_TARGET_RESOLVERS) do
        if resolver.can_resolve(step, ctx) then
            local target, err = resolver.resolve(step, opts, ctx)
            if target then
                return target
            end

            if err ~= nil and err ~= "" then
                last_err = err
                if resolver.name == "text_distance" and step_has_fallback_target_resolver(step, ctx) then
                    log.warn(string.format(
                        "Distance-first target miss [%s]; fallback to generic match: %s",
                        tostring(type(step) == "table" and step.label or ""),
                        tostring(err)
                    ))
                else
                    return nil, err
                end
            end
        end
    end

    return nil, last_err or "No target resolver matched the step."
end

function prepare_key_input(opts)
    local hwnd, hwnd_err = avepoint_resolve_fetch_target_hwnd(opts)
    if not hwnd then
        return nil, hwnd_err
    end

    if type(wnd) == "table" and type(wnd.set_foreground) == "function" then
        wnd.set_foreground(hwnd)
        sys.sleep(avepoint_delay_ms("prepare_focus", 100))
    end

    if type(keybd) ~= "table" or type(keybd.click) ~= "function" then
        return nil, "keybd.click is not available."
    end

    if type(keybd.set_mode) == "function" then
        local preferred_mode = MODE == "api" and "api" or "driver"
        local fallback_mode = preferred_mode == "api" and "driver" or "api"
        local ok = keybd.set_mode(preferred_mode)
        if not ok then
            keybd.set_mode(fallback_mode)
        end
    end

    if type(keybd.set_window) == "function" then
        pcall(keybd.set_window, hwnd)
    end

    return hwnd
end

function prepare_mouse_input()
    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return nil, hwnd_err
    end

    if type(wnd) == "table" and type(wnd.set_foreground) == "function" then
        wnd.set_foreground(hwnd)
        sys.sleep(avepoint_delay_ms("prepare_focus", 80))
    end

    if type(mouse) ~= "table" or type(mouse.click) ~= "function" then
        return nil, "mouse.click is not available."
    end

    if type(mouse.set_mode) == "function" then
        local preferred_mode = MODE == "api" and "api" or "driver"
        local fallback_mode = preferred_mode == "api" and "driver" or "api"
        local ok = mouse.set_mode(preferred_mode)
        if not ok then
            mouse.set_mode(fallback_mode)
        end
    end

    if type(mouse.set_window) == "function" then
        pcall(mouse.set_window, hwnd)
    end

    return hwnd
end

press_key = function(vk, label, opts)
    local _, err = prepare_key_input(opts)
    if err then
        return false, err
    end

    local ok = keybd.click(vk)
    if not ok then
        return false, string.format("keybd.click(0x%02X) failed.", tonumber(vk) or 0)
    end

    log.info(string.format(
        "Pressed key 0x%02X%s",
        tonumber(vk) or 0,
        label and (" | " .. label) or ""
    ))
    return true
end

click_current_mouse_button = function(button, label, click_delay_ms)
    if type(human_mouse) == "table" and type(human_mouse.cancel_async_move) == "function" then
        human_mouse.cancel_async_move()
    end

    local _, err = prepare_mouse_input()
    if err then
        return false, err
    end

    local click_button = tostring(button or "left")
    local actual_click_delay_ms = avepoint_delay_ms("click", math.max(1, tonumber(click_delay_ms) or 50))
    local ok = mouse.click(click_button, actual_click_delay_ms)
    if not ok then
        return false, string.format("mouse.click(%s) failed.", click_button)
    end

    local x, y = nil, nil
    if type(mouse.position) == "function" then
        x, y = mouse.position()
    end

    log.info(string.format(
        "Clicked mouse %s%s%s",
        click_button,
        label and (" | " .. label) or "",
        type(x) == "number" and type(y) == "number" and string.format(" pos=(%d,%d)", x, y) or ""
    ))
    return true
end

function ground_item_label(item)
    if type(item) ~= "table" then
        return ""
    end

    for _, key in ipairs({
        "name", "Name", "text", "Text",
        "fullname", "Fullname", "displayName", "DisplayName"
    }) do
        local value = trim(item[key])
        if value ~= "" then
            return value
        end
    end

    return ""
end

function summarize_ground_items(items, max_items)
    local names = {}
    local limit = math.max(1, tonumber(max_items) or 3)

    for _, item in ipairs(items or {}) do
        local label = ground_item_label(item)
        if label ~= "" then
            names[#names + 1] = label
            if #names >= limit then
                break
            end
        end
    end

    if #names == 0 then
        return ""
    end

    return table.concat(names, ", ")
end

function enum_ground_items_snapshot()
    if type(nav.enum_ground_items) ~= "function" then
        return nil, "nav.enum_ground_items is not available."
    end

    return nav.enum_ground_items()
end

function maybe_pickup_loot(now)
    if state.pickup_active ~= true then
        return true
    end

    if state.pickup_skip_until_exit == true then
        return true
    end

    if now < (state.pickup_next_at or 0) then
        return true
    end

    local items, enum_err = enum_ground_items_snapshot()

    if items == nil then
        state.pickup_next_at = now + PICKUP_SCAN_INTERVAL_MS
        if now - (state.pickup_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
            state.pickup_last_warn_at = now
            log.warn("Auto pickup scan failed: " .. tostring(enum_err))
        end
        return true
    end

    local item_count = #items
    state.pickup_last_seen_count = item_count

    if item_count <= 0 then
        state.pickup_last_logged_count = 0
        state.pickup_stuck_reference_count = 0
        state.pickup_stuck_attempts = 0
        state.pickup_next_at = now + PICKUP_SCAN_INTERVAL_MS
        return true
    end

    local summary = summarize_ground_items(items, 3)
    if item_count ~= (state.pickup_last_logged_count or 0)
        or now - (state.pickup_last_info_at or 0) >= PICKUP_INFO_INTERVAL_MS
    then
        state.pickup_last_info_at = now
        state.pickup_last_logged_count = item_count
        log.info(string.format(
            "Ground items detected | count=%d%s",
            item_count,
            summary ~= "" and (" items=" .. summary) or ""
        ))
    end

    local ok, key_err = press_key(VK_A, string.format("pickup loot count=%d", item_count))
    if ok then
        if tonumber(state.pickup_stuck_reference_count) == item_count then
            state.pickup_stuck_attempts = (tonumber(state.pickup_stuck_attempts) or 0) + 1
        else
            state.pickup_stuck_reference_count = item_count
            state.pickup_stuck_attempts = 1
        end

        if (tonumber(state.pickup_stuck_attempts) or 0) >= PICKUP_STUCK_MAX_ATTEMPTS then
            local force_cleanup = item_count >= PICKUP_BAG_FULL_MIN_ITEMS
            state.pickup_skip_until_exit = true
            if force_cleanup then
                state.force_cleanup_after_exit = true
            end
            state.pickup_next_at = now + PICKUP_SCAN_INTERVAL_MS
            log.warn(string.format(
                "Auto pickup appears stuck | count=%d attempts=%d action=skip_until_exit cleanup_after_exit=%s",
                item_count,
                tonumber(state.pickup_stuck_attempts) or 0,
                force_cleanup and "true" or "false"
            ))
            return true
        end

        state.pickup_next_at = now + PICKUP_PRESS_INTERVAL_MS
        return true
    end

    state.pickup_next_at = now + PICKUP_SCAN_INTERVAL_MS
    if now - (state.pickup_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
        state.pickup_last_warn_at = now
        local message = key_err or string.format("keybd.click(0x%02X) failed.", VK_A)
        log.warn("Auto pickup failed: " .. tostring(message))
    end

    return true
end

function maybe_wait_for_loot_before_exit(stage_name, now, context_label)
    if state.pickup_skip_until_exit == true then
        if now - (state.pickup_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
            state.pickup_last_warn_at = now
            log.warn(string.format(
                "Skipping remaining ground items before %s | reason=pickup_stuck_or_bag_full",
                tostring(context_label or stage_name)
            ))
        end
        return false
    end

    local items, enum_err = enum_ground_items_snapshot()
    if items == nil then
        if now - (state.pickup_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
            state.pickup_last_warn_at = now
            log.warn("Exit loot scan failed: " .. tostring(enum_err))
        end
        set_stage(stage_name, PICKUP_SCAN_INTERVAL_MS)
        return true
    end

    local item_count = #items
    if item_count <= 0 then
        state.pickup_last_logged_count = 0
        return false
    end

    if state.pickup_active ~= true then
        enable_map_pickup(0)
    else
        state.pickup_next_at = 0
    end

    local summary = summarize_ground_items(items, 3)
    if item_count ~= (state.pickup_last_logged_count or 0)
        or now - (state.pickup_last_info_at or 0) >= PICKUP_INFO_INTERVAL_MS
    then
        state.pickup_last_info_at = now
        state.pickup_last_logged_count = item_count
        log.info(string.format(
            "Waiting ground items before %s | count=%d%s",
            tostring(context_label or stage_name),
            item_count,
            summary ~= "" and (" items=" .. summary) or ""
        ))
    end

    maybe_pickup_loot(now)
    set_stage(stage_name, PICKUP_SCAN_INTERVAL_MS)
    return true
end

function press_d_key(label, opts)
    local ok, err = press_key(VK_D, label, opts)
    if not ok then
        return false, err
    end
    return true
end

function press_escape_key(label, opts)
    local ok, err = press_key(VK_ESCAPE, label, opts)
    if not ok then
        return false, err
    end
    return true
end

function click_screen_point(step_label, screen_x, screen_y, options)
    local ok, result_or_err = human_mouse.move_and_click(screen_x, screen_y, {
        hwnd = options and options.hwnd or nil,
        set_foreground = options and options.hwnd ~= nil,
        mouse_mode = tostring((options and options.click_mode) or "api"),
        click_button = options and options.click_button or "left",
        click_delay_ms = avepoint_delay_ms("click", tonumber(options and options.click_delay) or 50),
        before_click_extra_delay_ms = avepoint_delay_ms("hover", tonumber(options and options.hover_delay_ms) or 0),
        min_duration_ms = HUMAN_MOUSE_MOVE_DURATION.min_ms,
        max_duration_ms = HUMAN_MOUSE_MOVE_DURATION.max_ms,
        duration_center_ms = HUMAN_MOUSE_MOVE_DURATION.center_ms,
        duration_sigma_ms = HUMAN_MOUSE_MOVE_DURATION.sigma_ms,
        duration_gaussian_weight = HUMAN_MOUSE_MOVE_DURATION.gaussian_weight,
        duration_distribution = "gaussian",
        report_rate_hz = HUMAN_MOUSE_MOVE_DURATION.report_rate_hz
    })
    if not ok then
        return false, string.format(
            "human mouse click failed [%s]: %s",
            tostring(step_label or ""),
            tostring(result_or_err)
        )
    end

    return true
end

function image_target_screen_distance(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return math.huge
    end

    local ax = tonumber(a.match_screen_x) or tonumber(a.click_screen_x)
    local ay = tonumber(a.match_screen_y) or tonumber(a.click_screen_y)
    local bx = tonumber(b.match_screen_x) or tonumber(b.click_screen_x)
    local by = tonumber(b.match_screen_y) or tonumber(b.click_screen_y)
    if ax == nil or ay == nil or bx == nil or by == nil then
        return math.huge
    end

    return distance_2d(ax, ay, bx, by)
end

function build_image_retry_probe_region(target)
    if type(target) ~= "table" then
        return nil
    end

    local match_x = tonumber(target.match_screen_x) or tonumber(target.click_screen_x)
    local match_y = tonumber(target.match_screen_y) or tonumber(target.click_screen_y)
    local click_x = tonumber(target.click_screen_x) or match_x
    local click_y = tonumber(target.click_screen_y) or match_y
    local width = math.max(1, tonumber(target.template_w) or 1)
    local height = math.max(1, tonumber(target.template_h) or 1)
    if match_x == nil or match_y == nil or click_x == nil or click_y == nil then
        return nil
    end

    local margin = 20
    local left = math.floor(math.min(match_x, click_x) - margin)
    local top = math.floor(math.min(match_y, click_y) - margin)
    local right = math.ceil(math.max(match_x + width, click_x) + margin)
    local bottom = math.ceil(math.max(match_y + height, click_y) + margin)
    if left < 0 then
        left = 0
    end
    if top < 0 then
        top = 0
    end

    return {
        x = left,
        y = top,
        w = math.max(1, right - left),
        h = math.max(1, bottom - top)
    }
end

function capture_probe_region(region)
    if type(region) ~= "table"
        or type(vision) ~= "table"
        or type(vision.capture) ~= "function"
    then
        return nil
    end

    local img = vision.capture(region.x, region.y, region.w, region.h)
    if not valid_image(img) then
        free_image(img)
        return nil
    end

    return img
end

function verify_retryable_image_click(step, target, success_probe_step)
    if type(step) ~= "table" or type(target) ~= "table" or target.kind ~= "image" then
        return true
    end

    local preset = resolve_image_click_preset(step)
    if type(preset) ~= "table" or preset.retry_until_target_disappears ~= true then
        return true
    end

    local verify_delay_ms = avepoint_delay_ms(
        "verify",
        math.max(0, tonumber(preset.retry_verify_delay_ms) or IMAGE_CLICK_RETRY_VERIFY_DELAY_MS)
    )
    local verify_timeout_ms = math.max(verify_delay_ms, tonumber(preset.retry_verify_timeout_ms) or IMAGE_CLICK_RETRY_VERIFY_TIMEOUT_MS)
    local verify_poll_ms = math.max(100, tonumber(preset.retry_verify_poll_ms) or IMAGE_CLICK_RETRY_POLL_MS)
    local same_target_distance = math.max(0, tonumber(preset.retry_same_target_distance) or IMAGE_CLICK_RETRY_POSITION_TOLERANCE)
    local compare_threshold = tonumber(preset.retry_compare_threshold) or IMAGE_CLICK_RETRY_COMPARE_THRESHOLD

    if verify_delay_ms > 0 then
        sys.sleep(verify_delay_ms)
    end

    local deadline = sys.time() + math.max(0, verify_timeout_ms - verify_delay_ms)
    local probe_region = build_image_retry_probe_region(target)
    local baseline_probe = capture_probe_region(probe_region)
    local last_similarity = nil
    local last_distance = nil

    while true do
        if type(success_probe_step) == "table" then
            local next_target = nil
            next_target = fetch_button_for_step(success_probe_step)
            if next_target then
                free_image(baseline_probe)
                return true
            end
        end

        local current_target = nil
        local fetch_err = nil
        current_target, fetch_err = fetch_button_for_step(step)
        if not current_target then
            free_image(baseline_probe)
            return true
        end

        last_distance = image_target_screen_distance(target, current_target)
        if current_target.kind ~= "image" or last_distance > same_target_distance then
            free_image(baseline_probe)
            return true
        end

        if baseline_probe and type(vision) == "table" and type(vision.compare) == "function" then
            local current_probe = capture_probe_region(probe_region)
            if current_probe then
                last_similarity = vision.compare(baseline_probe, current_probe)
            end
            free_image(current_probe)
        end

        if last_similarity ~= nil and last_similarity < compare_threshold then
            free_image(baseline_probe)
            return true
        end

        if sys.time() >= deadline then
            free_image(baseline_probe)
            return false, string.format(
                "Image click had no effect [%s]: target still visible distance=%.2f similarity=%s timeout=%dms",
                tostring(step.label or ""),
                tonumber(last_distance) or -1,
                last_similarity and string.format("%.4f", last_similarity) or "nil",
                verify_timeout_ms
            ), true
        end

        sys.sleep(verify_poll_ms)
    end
end

function click_fetched_target(step, target, opts)
    local step_label = type(step) == "table" and tostring(step.label or "") or tostring(step or "")
    local ok = false
    local click_err = nil
    local prefer_screen_click = type(step) == "table" and step.prefer_screen_click == true

    if target.kind == "image"
        or (
            prefer_screen_click
            and tonumber(target.click_screen_x) ~= nil
            and tonumber(target.click_screen_y) ~= nil
        )
    then
        local repeat_count = target.kind == "image"
            and math.max(1, math.floor(tonumber(target.click_repeat_count) or 1))
            or 1
        local repeat_interval_ms = math.max(0, tonumber(target.click_repeat_interval_ms) or 120)
        for click_index = 1, repeat_count do
            ok, click_err = click_screen_point(step_label, target.click_screen_x, target.click_screen_y, {
                hwnd = target.hwnd,
                click_mode = target.click_mode,
                hover_delay_ms = click_index == 1 and target.hover_delay_ms or 0,
                click_button = target.click_button,
                click_delay = target.click_delay
            })
            if not ok then
                break
            end
            if click_index < repeat_count then
                sys.sleep(avepoint_delay_ms("click_repeat", repeat_interval_ms))
            end
        end
    else
        ok, click_err = nav.control_click(target.addr)
    end

    if not ok then
        return false, string.format(
            "Click target failed [%s]: %s",
            tostring(step_label),
            tostring(click_err)
        )
    end

    local verified, verify_err, retryable = verify_retryable_image_click(
        step,
        target,
        opts and opts.success_probe_step or nil
    )
    if not verified then
        return false, verify_err, retryable
    end

    log.info(string.format(
        "Clicked target %s | %s",
        tostring(step_label),
        control_summary(target)
    ))

    local cleanup_click_x = tonumber(target.click_screen_x)
    local cleanup_click_y = tonumber(target.click_screen_y)
    if cleanup_click_x ~= nil and cleanup_click_y ~= nil then
        state.bag_cleanup_last_click_screen_x = cleanup_click_x
        state.bag_cleanup_last_click_screen_y = cleanup_click_y
        state.bag_cleanup_last_click_hwnd = target.hwnd
    end

    return true
end

function avepoint_begin_map_revive(hp, hp_source)
    local route = state.route
    if type(route) ~= "table" or route.name ~= "Map route" then
        return false
    end

    local route_index = tonumber(route.index) or 0
    local route_total = type(route.points) == "table" and #route.points or 0

    state.route = nil
    reset_route_escape()
    reset_route_start_retry()
    disable_map_pickup("Map pickup disabled during map revive")
    reset_human_idle_move()
    reset_revive_state()
    state.revive_started_at = sys.time()
    set_stage("map_revive", 0)

    log.warn(string.format(
        "Map death detected | hp=%s source=%s route_index=%d/%d action=revive_at_checkpoint",
        tostring(hp),
        tostring(hp_source or ""),
        route_index,
        route_total
    ))
    return true
end

function avepoint_maybe_handle_map_death()
    if not initialized or state.running ~= true then
        return false
    end

    if state.stage == "map_revive" then
        return false
    end

    local route = state.route
    if type(route) ~= "table" or route.name ~= "Map route" then
        return false
    end

    local info, info_err = nav.player_info()
    if info == nil then
        return false, info_err
    end

    local hp, hp_source = avepoint_extract_player_hp(info)
    if hp == nil or hp > 0 then
        return false
    end

    return avepoint_begin_map_revive(hp, hp_source)
end

function avepoint_update_map_revive()
    local map_points = state.map_points
    if type(map_points) ~= "table" or #map_points <= 0 then
        return false, "Map revive requires active map route points."
    end

    local now = sys.time()
    if (state.revive_started_at or 0) == 0 then
        state.revive_started_at = now
        state.revive_last_warn_at = 0
    end

    local info, info_err = nav.player_info()
    local hp, hp_source = avepoint_extract_player_hp(info)
    local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
    local last_err = nil

    if hp ~= nil and hp > 0 and cur_x ~= nil and cur_y ~= nil then
        if (state.revive_resume_ready_at or 0) == 0 then
            state.revive_resume_ready_at = now
        end

        local stable_elapsed = now - (state.revive_resume_ready_at or now)
        if stable_elapsed >= MAP_ROUTE_READY_STABLE_MS then
            local start_index, nearest_distance = nearest_route_point_index(map_points, cur_x, cur_y)
            if start_index == nil then
                return false, "Unable to find nearest route point after revive."
            end

            log.info(string.format(
                "Map revive resume | hp=%.2f source=%s pos=%.2f, %.2f, %.2f nearest=%d/%d distance=%.2f stable=%dms",
                tonumber(hp) or 0,
                tostring(hp_source or ""),
                tonumber(cur_x) or 0,
                tonumber(cur_y) or 0,
                tonumber(cur_z) or 0,
                start_index,
                #map_points,
                tonumber(nearest_distance) or 0,
                stable_elapsed
            ))

            reset_revive_state()
            return start_route(map_points, "Map route", "begin_exit_route", start_index)
        end

        last_err = string.format(
            "revived player state ready stable=%d/%dms pos=%.2f, %.2f, %.2f hp=%.2f",
            stable_elapsed,
            MAP_ROUTE_READY_STABLE_MS,
            tonumber(cur_x) or 0,
            tonumber(cur_y) or 0,
            tonumber(cur_z) or 0,
            tonumber(hp) or 0
        )
    else
        state.revive_resume_ready_at = 0
        local since_click = now - (tonumber(state.revive_clicked_at) or 0)
        if (state.revive_clicked_at or 0) == 0 or since_click >= 3000 then
            local revive_steps = {
                make_revive_at_checkpoint_step(),
                make_revive_at_town_step()
            }
            local clicked_any = false
            local last_fetch_err = nil

            for _, revive_step in ipairs(revive_steps) do
                local target, fetch_err = fetch_button_for_step(revive_step)
                if target then
                    local clicked, click_err, retryable = click_fetched_target(revive_step, target)
                    if clicked then
                        state.revive_clicked_at = now
                        state.revive_click_count = (tonumber(state.revive_click_count) or 0) + 1
                        last_err = string.format(
                            "%s clicked count=%d waiting_for_player_state",
                            tostring(revive_step.distance_anchor_exact_text or revive_step.label or "revive"),
                            tonumber(state.revive_click_count) or 0
                        )
                        clicked_any = true
                        break
                    end

                    last_err = click_err
                    if not retryable then
                        return false, click_err
                    end
                    last_fetch_err = click_err
                else
                    last_fetch_err = fetch_err
                end
            end

            if not clicked_any then
                last_err = tostring(last_fetch_err or info_err or pos_err or "Revive target not found.")
            end
        else
            last_err = string.format(
                "waiting revive result after click count=%d retry_in=%dms hp=%s pos_err=%s info_err=%s",
                tonumber(state.revive_click_count) or 0,
                math.max(0, 3000 - since_click),
                hp ~= nil and string.format("%.2f", tonumber(hp) or 0) or "nil",
                tostring(pos_err or ""),
                tostring(info_err or "")
            )
        end
    end

    local elapsed = now - (state.revive_started_at or now)
    if elapsed >= 120000 then
        return false, string.format(
            "Map revive timeout after %dms: %s",
            elapsed,
            tostring(last_err)
        )
    end

    if now - (state.revive_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
        state.revive_last_warn_at = now
        log.warn(string.format(
            "Waiting map revive | elapsed=%dms err=%s",
            elapsed,
            tostring(last_err)
        ))
    end

    return true
end

function update_bag_cleanup()
    local action = BAG_CLEANUP_ACTIONS[state.bag_cleanup_index]
    if not action then
        local next_stage = state.bag_cleanup_next_stage or "press_entry_d"
        log.info(string.format(
            "Bag cleanup completed | next_stage=%s",
            tostring(next_stage)
        ))
        reset_bag_cleanup_state()
        set_stage(next_stage, 0)
        return true
    end

    if action.kind == "key" then
        local ok, err = press_key(tonumber(action.vk) or VK_B, tostring(action.label or "bag cleanup key"))
        if not ok then
            return false, err
        end

        state.bag_cleanup_index = state.bag_cleanup_index + 1
        state.bag_cleanup_retry_index = nil
        state.bag_cleanup_retry_started_at = 0
        state.bag_cleanup_retry_last_warn_at = 0
        set_stage("bag_cleanup", random_bag_flow_delay_ms())
        return true
    end

    if action.kind == "relative_click" then
        local anchor_x = tonumber(state.bag_cleanup_last_click_screen_x)
        local anchor_y = tonumber(state.bag_cleanup_last_click_screen_y)
        if anchor_x == nil or anchor_y == nil then
            return false, string.format(
                "Bag cleanup relative click missing anchor [%s].",
                tostring(action.label or "")
            )
        end

        seed_random_once()

        local offset_x_min = tonumber(action.offset_x_min) or 0
        local offset_x_max = tonumber(action.offset_x_max) or offset_x_min
        local offset_y_min = tonumber(action.offset_y_min) or 0
        local offset_y_max = tonumber(action.offset_y_max) or offset_y_min
        if offset_x_max < offset_x_min then
            offset_x_min, offset_x_max = offset_x_max, offset_x_min
        end
        if offset_y_max < offset_y_min then
            offset_y_min, offset_y_max = offset_y_max, offset_y_min
        end

        local target_x = anchor_x + math.random(offset_x_min, offset_x_max)
        local target_y = anchor_y + math.random(offset_y_min, offset_y_max)
        local ok, err = click_screen_point(action.label, target_x, target_y, {
            hwnd = state.bag_cleanup_last_click_hwnd,
            click_mode = action.click_mode,
            hover_delay_ms = action.hover_delay_ms,
            click_button = action.click_button,
            click_delay = action.click_delay
        })
        if not ok then
            return false, string.format(
                "Bag cleanup relative click failed [%s]: %s",
                tostring(action.label or ""),
                tostring(err)
            )
        end

        log.info(string.format(
            "Bag cleanup relative click %s | anchor=(%d,%d) target=(%d,%d) range_x=%d..%d range_y=%d..%d",
            tostring(action.label or ""),
            anchor_x,
            anchor_y,
            target_x,
            target_y,
            offset_x_min,
            offset_x_max,
            offset_y_min,
            offset_y_max
        ))

        state.bag_cleanup_last_click_screen_x = target_x
        state.bag_cleanup_last_click_screen_y = target_y
        state.bag_cleanup_index = state.bag_cleanup_index + 1
        state.bag_cleanup_retry_index = nil
        state.bag_cleanup_retry_started_at = 0
        state.bag_cleanup_retry_last_warn_at = 0
        set_stage("bag_cleanup", random_bag_flow_delay_ms())
        return true
    end

    local step = action.step or {}
    local now = sys.time()
    if state.bag_cleanup_retry_index ~= state.bag_cleanup_index then
        state.bag_cleanup_retry_index = state.bag_cleanup_index
        state.bag_cleanup_retry_started_at = now
        state.bag_cleanup_retry_last_warn_at = 0
    end

    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.bag_cleanup_retry_started_at or now)
        if elapsed >= BAG_CLEANUP_TIMEOUT_MS then
            return false, string.format(
                "Bag cleanup timeout [%s]: %s",
                tostring(step.label or ""),
                tostring(err)
            )
        end

        if now - (state.bag_cleanup_retry_last_warn_at or 0) >= BAG_CLEANUP_WARN_INTERVAL_MS then
            state.bag_cleanup_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting bag cleanup step %s | elapsed=%dms err=%s",
                tostring(step.label or ""),
                elapsed,
                tostring(err)
            ))
        end

        set_stage("bag_cleanup", BAG_CLEANUP_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("bag_cleanup", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    state.bag_cleanup_index = state.bag_cleanup_index + 1
    state.bag_cleanup_retry_index = nil
    state.bag_cleanup_retry_started_at = 0
    state.bag_cleanup_retry_last_warn_at = 0
    set_stage("bag_cleanup", random_bag_flow_delay_ms())
    return true
end

function update_stash_store()
    local now = sys.time()
    if (state.stash_retry_started_at or 0) == 0 then
        state.stash_retry_started_at = now
        state.stash_retry_last_warn_at = 0
    end

    local step = make_stash_oneclick_store_step()
    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.stash_retry_started_at or now)
        if elapsed >= STEP_FETCH_TIMEOUT_MS then
            return false, string.format(
                "Stash store timeout [%s]: %s",
                tostring(step.label),
                tostring(err)
            )
        end

        if now - (state.stash_retry_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
            state.stash_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting stash step %s | elapsed=%dms err=%s",
                tostring(step.label),
                elapsed,
                tostring(err)
            ))
        end

        set_stage("stash_store_click", STEP_RETRY_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("stash_store_click", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    state.stash_retry_started_at = 0
    state.stash_retry_last_warn_at = 0
    set_stage("stash_store_escape", random_bag_flow_delay_ms())
    return true
end

function update_stash_escape()
    local now = sys.time()
    if (state.stash_retry_started_at or 0) == 0 then
        state.stash_retry_started_at = now
        state.stash_retry_last_warn_at = 0
    end

    local step = make_stash_back_step()
    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.stash_retry_started_at or now)
        if elapsed >= STEP_FETCH_TIMEOUT_MS then
            return false, string.format(
                "Stash back timeout [%s]: %s",
                tostring(step.label),
                tostring(err)
            )
        end

        if now - (state.stash_retry_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
            state.stash_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting stash step %s | elapsed=%dms err=%s",
                tostring(step.label),
                elapsed,
                tostring(err)
            ))
        end

        set_stage("stash_store_escape", STEP_RETRY_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("stash_store_escape", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    state.stash_retry_started_at = 0
    state.stash_retry_last_warn_at = 0
    set_stage("begin_stash_return", random_bag_flow_delay_ms())
    return true
end

function update_exit_portal_click()
    local now = sys.time()
    if (state.exit_image_retry_started_at or 0) == 0 then
        state.exit_image_retry_started_at = now
        state.exit_image_retry_last_warn_at = 0
    end

    local step = make_exit_portal_step()
    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.exit_image_retry_started_at or now)
        if elapsed >= STEP_FETCH_TIMEOUT_MS then
            log.warn(string.format(
                "Exit portal fetch timeout | elapsed=%dms err=%s action=escape_then_image_retry",
                elapsed,
                tostring(err)
            ))
            reset_exit_image_retry()
            set_stage("exit_interference_escape", EXIT_VERIFY_RETRY_MS)
            return true
        end

        if now - (state.exit_image_retry_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
            state.exit_image_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting exit portal button | elapsed=%dms err=%s",
                elapsed,
                tostring(err)
            ))
        end

        set_stage("press_exit_d", STEP_RETRY_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("press_exit_d", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    reset_exit_image_retry()
    reset_exit_verify_state()
    state.exit_verify_source = "portal"
    set_stage(
        "verify_exit_result",
        avepoint_delay_ms("key_stage", tonumber(current_map and current_map.exit_key_delay_ms) or KEY_STAGE_DELAY_MS)
    )
    return true
end

function update_exit_chumen_click()
    local now = sys.time()
    if (state.exit_image_retry_started_at or 0) == 0 then
        state.exit_image_retry_started_at = now
        state.exit_image_retry_last_warn_at = 0
    end

    local step = {
        label = EXIT_STEP_CHUMEN
    }
    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.exit_image_retry_started_at or now)
        if elapsed >= STEP_FETCH_TIMEOUT_MS then
            log.warn(string.format(
                "Exit chumen fetch timeout | elapsed=%dms err=%s action=reroute_then_image_retry",
                elapsed,
                tostring(err)
            ))
            reset_exit_image_retry()
            set_stage("begin_exit_route_for_chumen", EXIT_VERIFY_RETRY_MS)
            return true
        end

        if now - (state.exit_image_retry_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
            state.exit_image_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting exit chumen button | elapsed=%dms err=%s",
                elapsed,
                tostring(err)
            ))
        end

        set_stage("exit_chumen_click", STEP_RETRY_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("exit_chumen_click", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    reset_exit_image_retry()
    reset_exit_verify_state()
    state.exit_verify_source = "chumen"
    set_stage(
        "verify_exit_result",
        avepoint_delay_ms("key_stage", tonumber(current_map and current_map.exit_key_delay_ms) or KEY_STAGE_DELAY_MS)
    )
    return true
end

