local vrchat_is_ready = false
function ready()
    vrchat_is_ready = true
end

function main()
    while not vrchat_is_ready do
        sleep(0.5)
    end
    print("ready")

    -- local success, err = osc.send("/avatar/change", "avtr_00000000-0000-4000-0000-000000000000")
    -- if not success then
    --     print("send error", err)
    -- end
end

function on_avatar_change(avatar)
    print("avatar changed", avatar)
end

function receive(address, values)
    print("received", address, values)
    if address == "/avatar/change" then
        on_avatar_change(values[1])
    end
end
