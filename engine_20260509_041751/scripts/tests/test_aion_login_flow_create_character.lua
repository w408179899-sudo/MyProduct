local T = require("tests.test_framework")

local function clear_login_flow_modules()
    package.loaded["aion.login_flow"] = nil
    package.loaded["aion.core"] = nil
    package.loaded["aion.account"] = nil
    package.loaded["aion.security"] = nil
    package.loaded["aion.ui"] = nil
    package.loaded["aion_ui_buttons"] = nil
end

local function clone_chars(chars)
    local out = {}
    for i, char in ipairs(chars or {}) do
        local copy = {}
        for key, value in pairs(char) do
            copy[key] = value
        end
        out[i] = copy
    end
    return out
end

local function make_account(character_name, race, job, gender, server_key)
    return {
        account = "mock@example.com",
        second_password = "",
        server = {
            key = server_key or 2,
            server_id = 0,
            character_name = character_name or "",
        },
        character = {
            race = race or 1,
            job = job or 0x2,
            gender = gender,
        },
    }
end

local function make_ctx(account, state)
    local now = 0
    return {
        index = 1,
        account = account,
        accounts_cfg = {
            login_flow = {
                init_timeout_seconds = 30,
                server_timeout_seconds = 5,
                character_timeout_seconds = 5,
                enter_game_timeout_seconds = 10,
                poll_interval_ms = 250,
                agreement_timeout_seconds = 0,
                create_character_recheck_timeout_seconds = 3,
                create_character_recheck_interval_ms = 250,
                create_character_max_attempts = 4,
            },
        },
        candidate = { pid = 4242 },
        now_ms = function()
            now = now + 50
            return now
        end,
        sleep = function(ms)
            now = now + math.min(tonumber(ms) or 0, 50)
            if state.advance_to_server_scene_on_sleep then
                state.advance_to_server_scene_on_sleep = false
                state.external_agreement_advanced = true
                state.scene = 0xA
            end
        end,
        random = function(min_value, _)
            return min_value
        end,
        set_status = function(_, status, message)
            state.last_status = status
            state.last_message = message
        end,
        set_character = function(_, char)
            state.shared_character = char
        end,
        set_progress = function(value)
            state.progress = value
        end,
    }
end

local function install_mocks(opts)
    opts = opts or {}
    clear_login_flow_modules()

    local state = {
        scene = opts.initial_scene or 0xA,
        chars = clone_chars(opts.chars),
        create_calls = {},
        select_calls = {},
        click_calls = {},
        fail_names = opts.fail_names or {},
        selected_server_key = nil,
        selected = nil,
        entered = false,
        next_id = 1000,
    }

    local function scene_name(index)
        if index == 0x8 then
            return "agreement"
        elseif index == 0xA then
            return "server"
        elseif index == 0xC then
            return "character"
        end
        return "unknown"
    end

    package.loaded["aion.core"] = {
        ensureInit = function(pid)
            state.bound_pid = pid
            return true, nil
        end,
        getScene = function()
            return true, { index = state.scene, name = scene_name(state.scene) }, nil
        end,
        getCharacter = function()
            if state.entered and state.selected then
                return true, state.selected, nil
            end
            return true, nil, nil
        end,
    }

    package.loaded["aion.account"] = {
        serverList = function()
            state.server_list_calls = (state.server_list_calls or 0) + 1
            if opts.late_agreement_after_empty_server_list and state.server_list_calls == 1 then
                state.scene = 0x8
                return true, {}, nil
            end
            if opts.server_list_requires_server_scene and state.scene ~= 0xA then
                return true, {}, nil
            end
            return true, {
                { key = opts.server_key or 2, server_id = 9001, addr = 11 },
            }, nil
        end,
        selectServer = function(key)
            state.selected_server_key = key
            state.scene = 0xC
            return true, true, nil
        end,
        characterList = function()
            return true, state.chars, nil
        end,
        createCharacter = function(name, gender, race, job)
            state.create_calls[#state.create_calls + 1] = {
                name = name,
                gender = gender,
                race = race,
                job = job,
            }
            if state.fail_names[name] then
                return true, false, "duplicate"
            end
            state.next_id = state.next_id + 1
            local char = {
                id = state.next_id,
                name = name,
                level = 1,
                race = race,
                job = job,
                addr = state.next_id * 10,
            }
            state.chars[#state.chars + 1] = char
            return true, true, nil
        end,
        selectCharacter = function(index)
            state.select_calls[#state.select_calls + 1] = index
            if type(index) ~= "number" then
                return true, false, "index expected"
            end
            state.selected = state.chars[index]
            if not state.selected then
                return true, false, "character index missing"
            end
            state.entered = true
            return true, true, nil
        end,
    }

    package.loaded["aion.security"] = {
        secondPwdDialog = function()
            return true, nil, nil
        end,
    }

    package.loaded["aion.ui"] = {
        find = function(name)
            if name == "user_agreement_dialog" then
                if state.scene == 0x8 then
                    return true, { addr = 76, visible = true, name = name }, nil
                end
                return false, nil, "absent"
            end
            if opts.agreement_controls_missing_once and state.scene == 0x8 then
                if name == "button_ok" then
                    state.advance_to_server_scene_on_sleep = true
                end
                return false, nil, "not found"
            end
            return true, { addr = 77, visible = true, name = name }, nil
        end,
        click = function(target)
            state.click_calls[#state.click_calls + 1] = target
            return true, true, nil
        end,
        children = function()
            return true, {}, nil
        end,
        list = function()
            return true, {}, nil
        end,
    }

    package.loaded["aion_ui_buttons"] = {
        dialog = function(group)
            if group == "user_agreement" then
                return "user_agreement_dialog"
            elseif group == "server_select" then
                return "server_select_dialog"
            elseif group == "character_select" then
                return "select_char_dialog_new"
            elseif group == "second_password" then
                return "second_password_dialog"
            end
            return group .. "_dialog"
        end,
        button = function(_, key)
            if key == "start" then
                return "start_button"
            elseif key == "ok" then
                return "ok"
            end
            return tostring(key)
        end,
    }

    return require("aion.login_flow"), state
end

local function run_flow(opts)
    local flow, state = install_mocks(opts)
    local account = opts.account
    local ok, message = flow.run(make_ctx(account, state))
    return flow, state, account, ok, message
end

local function run()
    T.reset()
    T.log("\n=== aion login flow create character tests ===")

    T.test("selects configured existing character without creating", function()
        local _, state, _, ok, message = run_flow({
            chars = {
                { id = 1, name = "Guardhero", level = 9, race = 1, job = 0x2, addr = 101 },
            },
            account = make_account("Guardhero", 1, 0x2, nil, 2),
        })

        T.assert_true(ok, message)
        T.assert_eq(state.selected_server_key, 2)
        T.assert_eq(#state.create_calls, 0)
        T.assert_eq(state.select_calls[1], 1)
        T.assert_eq(state.selected.name, "Guardhero")
        T.assert_contains(message, "Guardhero")
    end)

    T.test("continues server select when late agreement is handled externally", function()
        local _, state, _, ok, message = run_flow({
            chars = {
                { id = 1, name = "Guardhero", level = 9, race = 1, job = 0x2, addr = 101 },
            },
            account = make_account("Guardhero", 1, 0x2, nil, 2),
            late_agreement_after_empty_server_list = true,
            agreement_controls_missing_once = true,
            server_list_requires_server_scene = true,
        })

        T.assert_true(ok, message)
        T.assert_true(state.external_agreement_advanced, "external agreement transition should be observed")
        T.assert_gte(state.server_list_calls or 0, 2)
        T.assert_eq(state.selected_server_key, 2)
        T.assert_eq(state.select_calls[1], 1)
        T.assert_eq(state.selected.name, "Guardhero")
    end)

    T.test("creates configured character when other characters already exist", function()
        local _, state, account, ok, message = run_flow({
            chars = {
                { id = 1, name = "Otherhero", level = 3, race = 1, job = 0x8, addr = 101 },
            },
            account = make_account("Wantedhero", 1, 0x2, nil, 2),
        })

        T.assert_true(ok, message)
        T.assert_eq(#state.create_calls, 1)
        T.assert_eq(state.create_calls[1].name, "Wantedhero")
        T.assert_eq(state.create_calls[1].gender, 0)
        T.assert_eq(state.create_calls[1].race, 1)
        T.assert_eq(state.create_calls[1].job, 0x2)
        T.assert_eq(state.select_calls[1], 2)
        T.assert_eq(state.selected.name, "Wantedhero")
        T.assert_eq(account.server.character_name, "Wantedhero")
    end)

    T.test("falls back to generated ten-letter name when configured name fails", function()
        local _, state, account, ok, message = run_flow({
            chars = {},
            fail_names = { Takenhero = true },
            account = make_account("Takenhero", 1, 0x2, nil, 2),
        })

        T.assert_true(ok, message)
        T.assert_eq(#state.create_calls, 2)
        T.assert_eq(state.create_calls[1].name, "Takenhero")
        T.assert_eq(state.create_calls[2].name, "Silverleaf")
        T.assert_eq(#state.create_calls[2].name, 10)
        T.assert_true(string.match(state.create_calls[2].name, "^%a+$") ~= nil)
        T.assert_eq(state.select_calls[1], 1)
        T.assert_eq(state.selected.name, "Silverleaf")
        T.assert_eq(account.server.character_name, "Silverleaf")
    end)

    T.test("generated helper returns readable ten-letter names", function()
        local flow = install_mocks({ account = make_account("") })
        local name = flow._test.generatedCharacterName({ random = function() return 1 end }, {})
        T.assert_eq(name, "Silverleaf")
        T.assert_eq(#name, 10)
        T.assert_true(string.match(name, "^%a+$") ~= nil)
    end)

    clear_login_flow_modules()
    return T.report("aion_login_flow_create_character")
end

return { run = run }
