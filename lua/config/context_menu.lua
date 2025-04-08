print("context_menu")

if i == nil then
    i = 1
else
    i = i + 1
end

menu.onclick = function(...)
    print("menu.onclick", ...)
    local args = {...}
    if args[1] == "avatar" then
        print("avatar", args[2])
        -- local success, err = osc.send("/avatar/change", "avtr_00000000-0000-4000-0000-000000000000")
        -- if not success then
        --     print("send error", err)
        -- end
    end

    if args[1] == "reload menu" then
        menu.update()
    end
end

return {
    "reload menu",
    {
        name = "count: " .. i,
        disabled = true,
    },
    "--separator--",
    "You can customize this",
    "context menu by editing",
    "lua/config/context_menu.lua",
    "--separator--",
    "--separator--",
    {
        name = "avatar",
        items = {
            "1",
            "2",
            "3",
        }
    },
    {
        name = "sample disabled item",
        disabled = true,
    },
}
