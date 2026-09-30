#!/usr/bin/env python3
"""Find references to logic sections that do not exist in the same .ltx (crash: "section ... not found"). Research tool for our own fixes (docs/roadmap/FIX-PACKS.md).
usage: logic_refs.py FILE..."""
import re, sys
SCHEMES = ('sr_idle','sr_timer','sr_cutscene','sr_teleport','sr_light','sr_particle','sr_sound','sr_postprocess','sr_psy_antenna',
           'sr_no_weapon','sr_deimos','walker','remark','camper','sleeper','animpoint','mob_home','mob_walker','mob_combat','mob_remark',
           'mob_jump','mob_death','mob_trader','combat','combat_ignore','hit','death','meet','wounded','ph_idle','ph_door','ph_button',
           'ph_hit','ph_on_hit','ph_code','ph_sound','ph_force','ph_minigun','ph_car','heli_move','kamp','patrol','companion','smartcover',
           'cover','sr_monster','sr_robbery','sr_bloodsucker','sr_crow_spawner','sr_squad_guider','dialog','sr_silence','invulnerable')
ref = re.compile(r'(?<![\w@])((?:%s)(?:@[\w\.]+)?)(?=[\s,%%}\r\n]|$)' % '|'.join(SCHEMES))
for path in sys.argv[1:]:
    text = open(path, 'rb').read().decode('cp1251', 'replace')
    lines = text.splitlines()
    sections = set(re.findall(r'^\s*\[([^\]]+)\]', text, re.M))
    for n, line in enumerate(lines, 1):
        body = line.split(';', 1)[0]
        if '=' not in body or body.strip().startswith('['): continue
        key, value = body.split('=', 1)
        key = key.strip()
        if not (key.startswith('on_') or key in ('active','combat_ignore','on_hit','on_death','meet','wounded','dialog','on_info','on_signal','on_timer','on_game_timer','on_actor_inside','on_actor_outside')): continue
        for r in ref.findall(value):
            if '@' in r and r not in sections: print(f'{path}:{n}: missing section [{r}]  <- {line.strip()[:140]}')
