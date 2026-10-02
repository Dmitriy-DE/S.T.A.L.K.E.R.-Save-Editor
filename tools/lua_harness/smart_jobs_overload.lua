-- Random camp life with more NPCs than jobs, on the patched smart_terrain.script only (retail stops at the first
-- overload). Checks after every step: nothing raised an error, every NPC has a real job, and a job's owner is a
-- present NPC that does this very job. usage: lua5.1 smart_jobs_overload.lua PATCHED.script [ROUNDS]
function printf() end
function print_table() end
function abort(fmt, ...) error("ABORT: " .. string.format(fmt, ...), 0) end
cse_alife_smart_zone = {}
function class(name) local c = {}; c.__index = c; _G[name] = c; return function(base) setmetatable(c, { __index = base }) end end
function super() end
now = 0
game = { get_game_time = function() return { t = now, diffSec = function(self, other) return self.t - other.t end } end }
modules = { stype_mobile = 1, stype_stalker = 2 }
xr_logic = { switch_to_section = function() end }
db = { storage = {} }
assert(loadfile(arg[1]))()
local rounds = tonumber(arg[2] or "2000")
local trace_round = tonumber(arg[3] or "0")
math.randomseed(77)
local steps, problems, shared_steps = 0, 0, 0
for round = 1, rounds do
  local jobs = math.random(1, 4)
  local s = setmetatable({}, se_smart_terrain)
  s.npc_info, s.dead_time, s.npc_by_job_section, s.job_data, s.jobs = {}, {}, {}, {}, {}
  s.b_registred, s.npc_to_register, s.id, s.ltx = true, {}, 1000, "ltx"
  s.board = { squads = {}, smarts = {} }
  function s:name() return "test_smart" end
  function s:distance_to_job_location() return 1000000 end
  function s:setup_logic() end
  function s:fill_npc_info(obj) return { se_obj = obj, is_monster = false, need_job = "nil", job_prior = -1, job_id = -1, begin_job = false, stype = 2 } end
  local nodes = {}
  local clusters = { { _prior = 20, jobs = {} }, { _prior = 10, jobs = {} } }
  for i = 1, jobs do
    s.job_data[i] = { section = "logic@job" .. i }
    local c = clusters[i % 2 + 1]
    nodes[i] = { job_id = i, _prior = c._prior }
    c.jobs[#c.jobs + 1] = nodes[i]
  end
  s.jobs = clusters
  local present, objects, next_id = {}, {}, 1
  now = 0
  for step = 1, 80 do
    local op, desc = math.random(1, 100)
    local function run(f) local ok, err = pcall(f); if not ok then problems = problems + 1; print("ERROR", round, step, desc, err) end; return ok end
    local ok = true
    if op <= 40 and #present < jobs + 4 then
      local id = 1000 + 37 * next_id; next_id = next_id + 1; present[#present + 1] = id; desc = "register " .. id
      local o = { id = id }; function o:name() return "npc" .. id end; function o:clear_smart_terrain() end
      objects[id] = o; db.storage[id] = { object = { id = id } }
      ok = run(function() s:register_npc(o) end)
    elseif op <= 52 and #present > 0 then
      local id = table.remove(present, math.random(#present)); desc = "unregister " .. id
      ok = run(function() s:unregister_npc(objects[id]) end)
    elseif op <= 64 and #present > 0 then
      local id = table.remove(present, math.random(#present)); desc = "death " .. id
      ok = run(function() s:clear_dead(objects[id]) end)
    elseif op <= 74 and #present > 1 then
      local id, other = present[math.random(#present)], present[math.random(#present)]
      desc = "swap " .. id .. " takes the job of " .. other
      if other ~= id then
        ok = run(function()
          s.npc_info[id].need_job = s.job_data[s.npc_info[other].job_id].section
          s:switch_to_desired_job({ id = function() return id end })
        end)
      end
    elseif op <= 82 and #present > 0 then
      local id = present[math.random(#present)]; desc = "update one " .. id
      ok = run(function() s:update_jobs(objects[id]) end)
    elseif op <= 90 then
      desc = "time"; now = now + 400
      local t = game.get_game_time()
      for k, v in pairs(s.dead_time) do if t:diffSec(v) >= 600 then s.dead_time[k] = nil end end
    else
      desc = "update all"; ok = run(function() s:update_jobs() end)
    end
    steps = steps + 1
    if round == trace_round then
      local parts = {}
      for _, id in ipairs(present) do local info = s.npc_info[id]; parts[#parts + 1] = id .. ":" .. tostring(info and info.job_id) .. (info and info.job_link and (info.job_link.npc_id == id and "*" or "") or "?") end
      local own = {}
      for i, node in ipairs(nodes) do own[#own + 1] = "job" .. i .. "=" .. tostring(node.npc_id) .. "/" .. tostring(s.npc_by_job_section[s.job_data[i].section]) end
      print(step, desc or "-", "|", table.concat(parts, " "), "|", table.concat(own, " "))
    end
    if not ok then break end
    local on_job = {}
    for _, id in ipairs(present) do
      local info = s.npc_info[id]
      if info == nil or s.job_data[info.job_id] == nil or info.job_link == nil then
        problems = problems + 1; print("NO JOB", round, step, desc, id); ok = false
      else
        on_job[info.job_id] = (on_job[info.job_id] or 0) + 1
      end
    end
    for i, node in ipairs(nodes) do
      if (on_job[i] or 0) > 1 then shared_steps = shared_steps + 1 end
      local owner = node.npc_id
      if owner ~= nil then
        local info = s.npc_info[owner]
        if info == nil or info.job_id ~= i then
          problems = problems + 1; print("STALE OWNER", round, step, desc, "job" .. i, owner); ok = false
        elseif s.npc_by_job_section[s.job_data[i].section] ~= owner then
          problems = problems + 1; print("MAP != OWNER", round, step, desc, "job" .. i, owner, tostring(s.npc_by_job_section[s.job_data[i].section])); ok = false
        end
      end
    end
    if not ok then break end
  end
end
print(string.format("%d steps in %d rounds, %d with a shared job, %d problem(s)", steps, rounds, shared_steps, problems))
os.exit(problems == 0 and 0 or 1)
