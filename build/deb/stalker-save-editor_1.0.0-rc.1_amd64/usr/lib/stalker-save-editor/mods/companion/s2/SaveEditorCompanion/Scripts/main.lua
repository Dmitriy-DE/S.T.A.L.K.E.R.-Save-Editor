--[[
  S.T.A.L.K.E.R. 2: Heart of Chornobyl — Save Editor Companion (UE4SS Lua Mod).
  Implements protocol v1 (MOD_COMPANION_PROTOCOL.md) using UE4SS Lua scripting
  and Unreal Engine reflection / GSC debug console commands.

  Target folder: Stalker2/Binaries/Win64/ue4ss/Mods/SaveEditorCompanion/Scripts/main.lua
--]]

local PROTOCOL = "v1"
local POLL_INTERVAL_MS = 2000

local cmd_file_path = nil
local tmp_file_path = nil
local out_file_path = nil

local function get_storage_paths()
	if cmd_file_path ~= nil then
		return cmd_file_path, tmp_file_path, out_file_path
	end

	-- Look for %LOCALAPPDATA%\Stalker2\Saved or fallback to current directory
	local local_app_data = os.getenv("LOCALAPPDATA")
	local base_dir = "."
	if local_app_data ~= nil and local_app_data ~= "" then
		base_dir = local_app_data .. "\\Stalker2\\Saved"
	end

	cmd_file_path = base_dir .. "\\save_editor_cmd.txt"
	tmp_file_path = base_dir .. "\\save_editor_out.tmp"
	out_file_path = base_dir .. "\\save_editor_out.txt"
	return cmd_file_path, tmp_file_path, out_file_path
end

local function clean_text(text)
	return (string.gsub(tostring(text or ""), "[\r\n]+", " "))
end

local function write_reply(id, status, text)
	local _, tmp_path, out_path = get_storage_paths()
	local f = io.open(tmp_path, "w")
	if f == nil then
		print("[SaveEditorCompanion] Error: unable to open output file " .. tostring(tmp_path))
		return
	end
	f:write(PROTOCOL, " ", id, " ", status, " ", clean_text(text), "\n")
	f:close()
	os.remove(out_path)
	os.rename(tmp_path, out_path)
end

-- Unreal Engine helper functions via UE4SS
local function get_world()
	if FindFirstOf ~= nil then
		return FindFirstOf("World")
	end
	return nil
end

local function get_player_controller()
	if FindFirstOf ~= nil then
		return FindFirstOf("PlayerController")
	end
	return nil
end

local function execute_console_command(cmd)
	local world = get_world()
	local pc = get_player_controller()
	if StaticFindObject ~= nil then
		local kismet = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
		if kismet ~= nil and kismet.ExecuteConsoleCommand ~= nil and world ~= nil then
			kismet:ExecuteConsoleCommand(world, cmd, pc)
			return true
		end
	end
	if ExecuteConsoleCommand ~= nil and world ~= nil then
		ExecuteConsoleCommand(world, cmd)
		return true
	end
	print("[SaveEditorCompanion] Notice: console execution requested: " .. tostring(cmd))
	return false
end

local handlers = {}

function handlers.ping(args)
	return "ok", "pong"
end

function handlers.info(args)
	return "ok", "stalker2 1.x-ue4ss"
end

function handlers.pos(args)
	local pc = get_player_controller()
	if pc ~= nil and pc:IsValid() and pc.Pawn ~= nil and pc.Pawn:IsValid() then
		local pos = pc.Pawn:K2_GetActorLocation()
		if pos ~= nil then
			return "ok", string.format("%.2f,%.2f,%.2f", pos.X, pos.Y, pos.Z)
		end
	end
	return "error", "player actor not available"
end

function handlers.money(args)
	local delta = tonumber(args[1])
	if delta == nil then
		return "error", "usage: money <amount>"
	end
	execute_console_command("XAddMoneyToPlayer " .. tostring(delta))
	return "ok", "money modified by " .. tostring(delta)
end

function handlers.give(args)
	local proto_id = args[1]
	local count = tonumber(args[2]) or 1
	if proto_id == nil or proto_id == "" then
		return "error", "usage: give <prototype_id> [count]"
	end
	-- Native S2 command: XCreateItemInInventoryByID <PrototypeID> <ObjUID> <Count> <Durability>
	execute_console_command(string.format("XCreateItemInInventoryByID %s 0 %d 1.0", proto_id, count))
	return "ok", string.format("spawned %d of %s", count, proto_id)
end

function handlers.teleport(args)
	local x = tonumber(args[1])
	local y = tonumber(args[2])
	local z = tonumber(args[3])
	if x == nil or y == nil or z == nil then
		return "error", "usage: teleport <x> <y> <z>"
	end
	-- Use native GSC command XTeleportTo or actor K2_SetActorLocation
	local pc = get_player_controller()
	if pc ~= nil and pc:IsValid() and pc.Pawn ~= nil and pc.Pawn:IsValid() then
		pc.Pawn:K2_SetActorLocation({ X = x, Y = y, Z = z }, false, {}, true)
		return "ok", string.format("teleported to %.2f, %.2f, %.2f", x, y, z)
	end
	execute_console_command(string.format("XTeleportTo %.2f %.2f %.2f", x, y, z))
	return "ok", string.format("teleported via command to %.2f, %.2f, %.2f", x, y, z)
end

local function process_command_line(line)
	local tokens = {}
	for token in string.gmatch(line, "%S+") do
		table.insert(tokens, token)
	end

	if #tokens < 2 then
		return
	end

	local proto = tokens[1]
	local id = tokens[2]
	local cmd = tokens[3]

	if proto ~= PROTOCOL then
		write_reply(id or "0", "error", "unsupported protocol version: " .. tostring(proto))
		return
	end

	if cmd == nil then
		write_reply(id, "error", "missing command")
		return
	end

	local args = {}
	for i = 4, #tokens do
		table.insert(args, tokens[i])
	end

	local handler = handlers[cmd]
	if handler == nil then
		write_reply(id, "unsupported", "command not supported in S2: " .. cmd)
		return
	end

	local status, text = handler(args)
	write_reply(id, status, text or "")
end

local function poll_commands()
	local cmd_path, _, _ = get_storage_paths()
	local f = io.open(cmd_path, "r")
	if f == nil then
		return
	end

	local content = f:read("*all")
	f:close()
	os.remove(cmd_path)

	if content ~= nil and content ~= "" then
		for line in string.gmatch(content, "[^\r\n]+") do
			process_command_line(line)
		end
	end
end

-- Initialize periodic polling in UE4SS
if LoopAsync ~= nil then
	LoopAsync(POLL_INTERVAL_MS, function()
		poll_commands()
		return false -- continue looping
	end)
	print("[SaveEditorCompanion] S.T.A.L.K.E.R. 2 companion initialized with LoopAsync (" .. POLL_INTERVAL_MS .. "ms)")
else
	print("[SaveEditorCompanion] S.T.A.L.K.E.R. 2 companion loaded (LoopAsync not available)")
end
