-- Differential run: the retail and the patched smart_terrain.script must behave identically while a camp is never
-- overloaded. usage: lua5.1 smart_jobs_diff.lua RETAIL.script PATCHED.script [ROUNDS]
local function load_script(path)
  local env = setmetatable({}, { __index = _G })
  env.printf = function() end
  env.print_table = function() end
  env.abort = function(fmt, ...) error("ABORT: " .. string.format(fmt, ...), 0) end
  env.cse_alife_smart_zone = {}
  env.class = function(name)
    local c = {}; c.__index = c; env[name] = c
    return function(base) setmetatable(c, { __index = base }) end
  end
  env.super = function() end
  env.now = 0
  env.game = { get_game_time = function() return { t = env.now, diffSec = function(self, other) return self.t - other.t end } end }
  env.modules = { stype_mobile = 1, stype_stalker = 2 }
  env.log = {}
  env.xr_logic = { switch_to_section = function(obj) env.log[#env.log + 1] = "reset" .. obj.id end }
  env.db = { storage = {} }
  local chunk = assert(loadfile(path)); setfenv(chunk, env); chunk()
  return env
end
local function new_smart(env, job_count)
  local s = setmetatable({}, env.se_smart_terrain)
  s.npc_info, s.dead_time, s.npc_by_job_section, s.job_data, s.jobs = {}, {}, {}, {}, {}
  s.b_registred, s.npc_to_register, s.id, s.ltx = true, {}, 1000, "ltx"
  s.board = { squads = {}, smarts = {} }
  function s:name() return "test_smart" end
  function s:distance_to_job_location() return 1000000 end
  function s:setup_logic() end
  function s:fill_npc_info(obj)
    return { se_obj = obj, is_monster = false, need_job = "nil", job_prior = -1, job_id = -1, begin_job = false, stype = 2 }
  end
  local clusters = { { _prior = 20, jobs = {} }, { _prior = 10, jobs = {} } }
  for i = 1, job_count do
    s.job_data[i] = { section = "logic@job" .. i }
    local c = clusters[i % 2 + 1]
    c.jobs[#c.jobs + 1] = { job_id = i, _prior = c._prior }
  end
  s.jobs = clusters
  return s
end
local function state(env, s, job_count)
  local parts = {}
  for id, info in pairs(s.npc_info) do
    if type(info) == "table" and info.se_obj then parts[#parts + 1] = string.format("%d:%s", id, tostring(info.job_id)) end
  end
  table.sort(parts)
  local owners = {}
  local function walk(jobs) for _, j in ipairs(jobs) do if j.jobs then walk(j.jobs) else owners[j.job_id] = tostring(j.npc_id) .. "/" .. tostring(s.npc_by_job_section[s.job_data[j.job_id].section]) end end end
  walk(s.jobs)
  local dead = {}
  for k in pairs(s.dead_time) do dead[#dead + 1] = k end
  table.sort(dead)
  local out = table.concat(parts, " ") .. " |"
  for i = 1, job_count do out = out .. " " .. owners[i] end
  out = out .. " | dead " .. table.concat(dead, ",") .. " | " .. table.concat(env.log, ",")
  env.log = {}
  return out
end
local A, B = load_script(arg[1]), load_script(arg[2])
local rounds = tonumber(arg[3] or "300")
local trace_round = tonumber(arg[4] or "0")
math.randomseed(20261002)
local steps, diffs, retail_errors, retail_failed = 0, 0, 0, nil
local retail_kinds = {}
for round = 1, rounds do
  local jobs = math.random(1, 6)
  local sa, sb = new_smart(A, jobs), new_smart(B, jobs)
  local present, next_id = {}, 1
  A.now, B.now = 0, 0
  for step = 1, 60 do
    local op = math.random(1, 100)
    local desc
    local function both(f)
      local ra, ea = pcall(f, A, sa); local rb, eb = pcall(f, B, sb)
      if not ra then retail_errors = retail_errors + 1; retail_failed = tostring(ea) end
      if ra and not rb then diffs = diffs + 1; print("DIFF patched fails where retail works", round, step, desc, tostring(eb)) end
    end
    if op <= 35 and #present < jobs then            -- never more NPCs than jobs: no overload
      local id = 1000 + 37 * next_id; next_id = next_id + 1; present[#present + 1] = id; desc = "register " .. id
      both(function(env, s)
        local o = { id = id }; function o:name() return "npc" .. id end; function o:clear_smart_terrain() end
        env.db.storage[id] = { object = { id = id } }; env["npc" .. id] = o
        s:register_npc(o)
      end)
    elseif op <= 50 and #present > 0 then
      local id = table.remove(present, math.random(#present)); desc = "unregister " .. id
      both(function(env, s) s:unregister_npc(env["npc" .. id]) end)
    elseif op <= 62 and #present > 0 then
      local id = table.remove(present, math.random(#present)); desc = "death " .. id
      both(function(env, s) s:clear_dead(env["npc" .. id]) end)
    elseif op <= 72 and #present > 0 then
      -- as gulag_general does it: one NPC is told to take over the job that another NPC is doing
      local id = present[math.random(#present)]
      local other = present[math.random(#present)]
      desc = "swap " .. id .. " takes the job of " .. other
      if other ~= id then
        both(function(env, s)
          s.npc_info[id].need_job = s.job_data[s.npc_info[other].job_id].section
          s:switch_to_desired_job({ id = function() return id end })
        end)
      end
    elseif op <= 80 and #present > 0 then
      local id = present[math.random(#present)]; desc = "update one " .. id
      both(function(env, s) s:update_jobs(env["npc" .. id]) end)
    elseif op <= 90 then
      desc = "time"
      both(function(env, s)
        env.now = env.now + 400
        local t = env.game.get_game_time()
        for k, v in pairs(s.dead_time) do if t:diffSec(v) >= 600 then s.dead_time[k] = nil end end
      end)
    else
      desc = "update all"
      both(function(env, s) s:update_jobs() end)
    end
    steps = steps + 1
    if retail_failed then      -- the game would have stopped here: nothing after it can be compared
      local kind = desc:match("^%a+") .. ": " .. retail_failed:gsub("^.-:%d+: ", ""):sub(1, 60)
      retail_kinds[kind] = (retail_kinds[kind] or 0) + 1
      retail_failed = nil
      break
    end
    local x, y = state(A, sa, jobs), state(B, sb, jobs)
    if round == trace_round then print(step, desc); print("  retail : " .. x); print("  patched: " .. y) end
    if x ~= y then diffs = diffs + 1; print("DIFF state", round, step, desc); print("  retail : " .. x); print("  patched: " .. y); break end
  end
end
print(string.format("%d steps in %d rounds, %d difference(s); retail stopped %d time(s):", steps, rounds, diffs, retail_errors))
for kind, n in pairs(retail_kinds) do print("  " .. n .. "x " .. kind) end
os.exit(diffs == 0 and 0 or 1)
