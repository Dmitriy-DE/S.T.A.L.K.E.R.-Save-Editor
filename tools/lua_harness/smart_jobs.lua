-- Runs se_smart_terrain job assignment from a Clear Sky smart_terrain.script outside the game.
-- usage: lua5.1 smart_jobs.lua PATH/smart_terrain.script
-- The engine is replaced by stubs; only the job bookkeeping (pure Lua in the script) runs for real.
local path = arg[1]
local aborted
function printf() end
function abort(fmt, ...) aborted = string.format(fmt, ...); error("ABORT: " .. aborted, 0) end
function print_table() end
cse_alife_smart_zone = {}
function class(name)
  local c = {}; c.__index = c
  _G[name] = c
  return function(base) setmetatable(c, { __index = base }) end
end
function super() end
local now = 0
game = { get_game_time = function() return { t = now, diffSec = function(self, other) return self.t - other.t end } end }
function time_global() return now * 1000 end
modules = { stype_mobile = 1, stype_stalker = 2 }
local switched = {}
xr_logic = { switch_to_section = function(obj, ltx, section) switched[#switched + 1] = obj.id end }
db = { storage = {}, actor = nil }
local chunk = assert(loadfile(path))
chunk()

local function new_smart(job_count)
  local s = setmetatable({}, se_smart_terrain)
  s.npc_info, s.dead_time, s.npc_by_job_section, s.job_data, s.jobs = {}, {}, {}, {}, {}
  s.b_registred, s.npc_to_register, s.id, s.ltx = true, {}, 1000, "ltx"
  s.board = { squads = {}, smarts = {} }
  function s:name() return "test_smart" end
  function s:distance_to_job_location() return 1000000 end   -- nobody has walked to the job yet
  function s:fill_npc_info(obj)
    return { se_obj = obj, is_monster = false, need_job = "nil", job_prior = -1, job_id = -1, begin_job = false, stype = modules.stype_stalker }
  end
  local cluster = { _prior = 10, jobs = {} }
  for i = 1, job_count do
    s.job_data[i] = { section = "logic@job" .. i }
    cluster.jobs[i] = { job_id = i, _prior = 10 }
  end
  s.jobs[1] = cluster
  return s
end
local function npc(id)
  local o = { id = id }
  function o:name() return "npc" .. id end
  function o:clear_smart_terrain() self.m_smart_terrain_id = nil end
  db.storage[id] = { object = { id = id } }
  return o
end
local function try(label, f)
  aborted = nil
  local ok, err = pcall(f)
  print(string.format("  %-58s %s", label, ok and "ok" or ("FAILED: " .. tostring(err))))
  return ok
end
local function state(s)
  local parts = {}
  for id, info in pairs(s.npc_info) do
    if type(info) == "table" and info.se_obj then parts[#parts + 1] = string.format("npc%d->job%s", id, tostring(info.job_id)) end
  end
  table.sort(parts)
  local owners = {}
  for i, j in ipairs(s.jobs[1].jobs) do
    owners[#owners + 1] = string.format("job%d owner=%s map=%s", i, tostring(j.npc_id), tostring(s.npc_by_job_section[s.job_data[i].section]))
  end
  return table.concat(parts, " ") .. " | " .. table.concat(owners, "; ")
end

local failures = 0
local function check(cond, text) if not cond then failures = failures + 1; print("  !! " .. text) end end

print("1. two NPCs, one job (the retail abort)")
local s = new_smart(1)
local a, b = npc(1), npc(2)
try("register npc1", function() s:register_npc(a) end)
local shared = try("register npc2 (no free job)", function() s:register_npc(b) end)
print("  " .. state(s))
if shared then
  check(s.jobs[1].jobs[1].npc_id == 1, "the first NPC must stay the owner of the job")
  try("periodic update", function() s:update_jobs() end)
  print("  " .. state(s))
  check(s.jobs[1].jobs[1].npc_id == 1 and s.npc_by_job_section["logic@job1"] == 1, "owner or mapping lost after the periodic update")

  print("2. the extra NPC leaves: the owner keeps the job")
  try("unregister npc2", function() s:unregister_npc(b) end)
  print("  " .. state(s))
  check(s.jobs[1].jobs[1].npc_id == 1, "the job lost its owner when the extra NPC left")
  check(s.npc_by_job_section["logic@job1"] == 1, "the job-to-NPC mapping lost the owner when the extra NPC left")

  print("3. the extra NPC dies on the shared job, then the owner is re-evaluated")
  local c = npc(3)
  try("register npc3 (shares)", function() s:register_npc(c) end)
  try("npc3 dies", function() s:clear_dead(c) end)
  print("  " .. state(s))
  check(s.jobs[1].jobs[1].npc_id == 1, "the job lost its owner when the extra NPC died")
  try("periodic update", function() s:update_jobs() end)
  try("per-object update for the owner (as sim_squad_generic does)", function() s:update_jobs(a) end)
  print("  " .. state(s))

  print("4. the owner dies, the extra NPC remains, a new NPC arrives after the death timer")
  local d = npc(4)
  s.dead_time = {}
  try("register npc4 (shares)", function() s:register_npc(d) end)
  try("owner npc1 dies", function() s:clear_dead(a) end)
  try("periodic update", function() s:update_jobs() end)
  print("  " .. state(s))
  now = now + 700; s.dead_time = {}
  try("periodic update after the timer", function() s:update_jobs() end)
  local e = npc(5)
  try("register npc5", function() s:register_npc(e) end)
  try("periodic update", function() s:update_jobs() end)
  print("  " .. state(s))
  try("unregister npc4", function() s:unregister_npc(d) end)
  try("unregister npc5", function() s:unregister_npc(e) end)
  print("  " .. state(s))

  print("5. a second job frees up: the extra NPC moves to it and the first owner is untouched")
  local s2 = new_smart(2)
  local p, q, r = npc(11), npc(12), npc(13)
  try("register 3 NPCs on 2 jobs", function() s2:register_npc(p); s2:register_npc(q); s2:register_npc(r) end)
  print("  " .. state(s2))
  try("npc12 leaves", function() s2:unregister_npc(q) end)
  try("periodic update", function() s2:update_jobs() end)
  print("  " .. state(s2))
  local j = s2.jobs[1].jobs
  check(j[1].npc_id ~= nil and j[2].npc_id ~= nil, "after the move both jobs must have an owner")
  check(s2.npc_by_job_section["logic@job1"] == j[1].npc_id and s2.npc_by_job_section["logic@job2"] == j[2].npc_id, "mapping differs from the owners")

  print("6. job swap requested while sharing")
  local s3 = new_smart(1)
  local u, v = npc(21), npc(22)
  s3:register_npc(u); s3:register_npc(v)
  function s3:setup_logic() end
  s3.npc_info[22].need_job = "logic@job1"
  try("npc22 asks for the owner's job", function() s3:switch_to_desired_job({ id = function() return 22 end }) end)
  print("  " .. state(s3))
  try("periodic update", function() s3:update_jobs() end)
  try("unregister both", function() s3:unregister_npc(u); s3:unregister_npc(v) end)
end
print(failures == 0 and "RESULT: consistent" or ("RESULT: " .. failures .. " inconsistency(ies)"))
os.exit(failures == 0 and 0 or 1)
