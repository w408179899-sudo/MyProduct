local M = {}

M.user_agreement = {
    dialog = "user_agreement_dialog",
    html_view = "htmlview_agreement",
    checkbox = "agreement_check",
    agree = "agreement_yes",
    disagree = "agreement_no",
}

M.server_select = {
    dialog = "server_select_dialog",
    start = "start_button",
}

M.character_select = {
    dialog = "select_char_dialog_new",
    start = "start_button",
}

M.second_password = {
    dialog = "second_password_dialog",
    ok = "ok",
    cancel = "cancel",
    clear = "num_clear",
}

function M.get(group, key)
    local controls = M[group]
    if type(controls) ~= "table" then
        return nil
    end
    return controls[key]
end

function M.dialog(group)
    return M.get(group, "dialog")
end

function M.button(group, key)
    return M.get(group, key)
end

return M
