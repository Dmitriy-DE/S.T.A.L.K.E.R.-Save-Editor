#!/usr/bin/env python3
"""fix_regress.py DUMPS_DIR OUT_DIR: re-checks every catalogued text patch against game dumps, without the editor's code.

DUMPS_DIR holds the trees written by `fixes extract` (soc-all, socee-all, cs-all, cs-scripts, csee-all, cop-all,
copee-all). For the retail build and for the Enhanced Edition of each game it
  1. checks each patch: file present, SHA-256 of the original, anchor found exactly once at the moment it is applied,
     line endings of the replacement match the file, text fits the code page;
  2. writes the patched tree to OUT_DIR/<name>;
  3. compiles every changed script (luac5.1 -p) and lists globals a changed script newly reads or assigns;
  4. runs the static checkers on the original and on the patched tree and prints what the patches added or removed.
Exit code 1 when a patch fails or the patched tree has a finding the original does not have."""
import collections, hashlib, json, os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CATALOG = os.path.join(HERE, '..', 'src', 'StalkerSaveEditor.Core', 'Patching', 'Data', 'game-fixes.json')
# game -> (retail dump, EE dump, config folder inside gamedata as the catalogue spells it)
TREES = {
    'ShadowOfChernobyl': ('soc-all', 'socee-all'),
    'ClearSky': ('cs-all', 'csee-all'),
    'CallOfPripyat': ('cop-all', 'copee-all'),
}
EE_GAME = {'ShadowOfChernobylEnhancedEdition': 'ShadowOfChernobyl', 'ClearSkyEnhancedEdition': 'ClearSky',
           'CallOfPripyatEnhancedEdition': 'CallOfPripyat'}
# Lua's own library: a patch may use a name no shipped script uses, so the undefined-global check must not flag it
LUA_BASE = {'rawequal', 'rawget', 'rawset', 'select', 'unpack', 'next', 'pcall', 'xpcall', 'tostring', 'tonumber',
            'type', 'pairs', 'ipairs', 'assert', 'error', 'setmetatable', 'getmetatable'}
CODEPAGE = {1251: 'cp1251', 1252: 'cp1252', 28591: 'latin-1', 65001: 'utf-8'}


def source(dumps, tree, rel):
    """Path of a gamedata-relative file inside a dump; the retail Clear Sky scripts live in their own folder."""
    rel = rel.replace('\\', '/')
    rel = rel[len('gamedata/'):] if rel.startswith('gamedata/') else rel
    path = os.path.join(dumps, tree, rel)
    if not os.path.exists(path) and tree == 'cs-all' and rel.startswith('scripts/'):
        path = os.path.join(dumps, 'cs-scripts', rel[len('scripts/'):])
    return rel, path


def build(dumps, out, name, tree, jobs):
    """jobs: [(fix id, patch, expected sha)] in catalogue order. Returns (errors, changed files)."""
    errors, state, origin = [], {}, {}
    for fid, patch, sha in jobs:
        rel, path = source(dumps, tree, patch['relativePath'])
        enc = CODEPAGE.get(patch.get('codePage'), 'latin-1')
        if rel not in state:
            if not os.path.exists(path):
                errors.append(f'{fid}: {rel} is not in {tree}'); continue
            raw = open(path, 'rb').read()
            origin[rel] = raw
            state[rel] = raw
        if hashlib.sha256(origin[rel]).hexdigest() != sha:
            errors.append(f'{fid}: {rel} original hash differs from the catalogue'); continue
        try:
            old, new = patch['expectedText'].encode(enc), patch['replacementText'].encode(enc)
        except UnicodeEncodeError as e:
            errors.append(f'{fid}: {rel} text does not fit {enc}: {e}'); continue
        count = state[rel].count(old)
        if count != 1:
            errors.append(f'{fid}: {rel} anchor found {count} times when applied'); continue
        if old == new:
            errors.append(f'{fid}: {rel} replacement equals the anchor')
        crlf = b'\r\n' in origin[rel]
        bare = re.search(rb'(?<!\r)\n', new) is not None
        if crlf and bare and re.search(rb'(?<!\r)\n', old) is None:
            errors.append(f'{fid}: {rel} replacement has a bare LF in a CRLF file')
        if not crlf and b'\r\n' in new:
            errors.append(f'{fid}: {rel} replacement has CRLF in an LF file')
        state[rel] = state[rel].replace(old, new)
    dest = os.path.join(out, name)
    shutil.rmtree(dest, ignore_errors=True)
    for rel, raw in state.items():
        for base, data in (('patched', raw), ('original', origin[rel])):
            p = os.path.join(dest, base, rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            open(p, 'wb').write(data)
    return errors, sorted(state)


def lua_globals(path):
    """(read, assigned) global names of one script, from the luac listing; None when it does not compile."""
    r = subprocess.run(['luac5.1', '-l', '-p', path], capture_output=True, text=True, errors='replace')
    if r.returncode:
        return None, r.stderr.strip()
    get = set(re.findall(r'GETGLOBAL\s+\S+ \S+\s+; (\S+)', r.stdout))
    put = set(re.findall(r'SETGLOBAL\s+\S+ \S+\s+; (\S+)', r.stdout))
    return get, put


def scripts_check(dest, changed):
    out = []
    for rel in changed:
        if not rel.endswith('.script'):
            continue
        g0, p0 = lua_globals(os.path.join(dest, 'original', rel))
        g1, p1 = lua_globals(os.path.join(dest, 'patched', rel))
        if g1 is None:
            out.append(f'SYNTAX {rel}: {p1}'); continue
        if g0 is None:
            continue  # the original does not compile with stock Lua 5.1 (engine extensions); nothing to compare
        if g1 - g0:
            out.append(f'new global read in {rel}: {", ".join(sorted(g1 - g0))}')
        if p1 - p0:
            out.append(f'new global assigned in {rel}: {", ".join(sorted(p1 - p0))}')
    return out


def overlay(dumps, tree, dest, changed):
    """A full copy of the dump (hard links) with the patched files written over it; returns its path."""
    full = dest + '-full'
    shutil.rmtree(full, ignore_errors=True)
    roots = [tree] + (['cs-scripts'] if tree == 'cs-all' else [])
    for root in roots:
        src = os.path.join(dumps, root)
        dst = os.path.join(full, 'scripts') if root == 'cs-scripts' else full
        shutil.copytree(src, dst, copy_function=os.link, dirs_exist_ok=True)
    for rel in changed:
        p = os.path.join(full, rel)
        os.remove(p)
        shutil.copyfile(os.path.join(dest, 'patched', rel), p)
    return full


def run_checkers(root, base_roots):
    """Findings of every checker as a set of lines with the tree prefix removed."""
    cfg = 'config' if os.path.isdir(os.path.join(root, 'config')) else 'configs'
    ltx = [os.path.join(d, f) for d, _, fs in os.walk(os.path.join(root, cfg)) for f in fs if f.endswith('.ltx')]
    xml = [os.path.join(d, f) for d, _, fs in os.walk(os.path.join(root, cfg, 'gameplay')) for f in fs
           if f.startswith('dialogs') and f.endswith('.xml')]
    runs = [('check_condlists.py', ltx), ('check_logic_refs.py', ltx), ('check_dialogs.py', xml),
            ('lua_globals.py', [os.path.join(root, 'scripts')]), ('check_infos.py', [root, cfg]),
            ('check_condfuncs.py', [root, cfg]), ('check_module_calls.py', [root, cfg]),
            ('check_trade_items.py', [os.path.join(root, cfg)])]
    found = set()
    for tool, args in runs:
        if not args:
            continue
        r = subprocess.run([sys.executable, os.path.join(HERE, tool)] + args, capture_output=True, text=True,
                           errors='replace')
        for line in (r.stdout + r.stderr).splitlines():
            line = line.strip()
            for b in base_roots:
                line = line.replace(b + os.sep, '').replace(b, '')
            if line:
                found.add(f'{tool[:-3]}: ' + re.sub(r':\d+:', ':', line))  # patches move lines
    return found


def main():
    dumps, out = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
    cat = json.load(open(CATALOG, encoding='utf-8'))
    ee_sha = cat['enhancedEditionSha256']
    bad = False
    for game, (retail, ee) in TREES.items():
        retail_jobs, ee_jobs = [], []
        for d in cat['definitions']:
            if d['implementation'] != 'ExactTextReplacement':
                continue
            if d['game'] == game:
                retail_jobs += [(d['id'], p, p['expectedFileSha256']) for p in d['textPatches']]
                shared = [p for p in d['textPatches'] if not p.get('retailOnly')]
                hashes = ee_sha.get(d['id'])
                if hashes:
                    if len(hashes) != len(shared):
                        print(f'{d["id"]}: {len(hashes)} EE hashes for {len(shared)} patches'); bad = True
                    ee_jobs += [(d['id'], p, h) for p, h in zip(shared, hashes)]
            elif EE_GAME.get(d['game']) == game:
                ee_jobs += [(d['id'], p, p['expectedFileSha256']) for p in d['textPatches']]
        for name, tree, jobs in ((game, retail, retail_jobs), (game + 'EE', ee, ee_jobs)):
            errors, changed = build(dumps, out, name, tree, jobs)
            dest = os.path.join(out, name)
            notes = scripts_check(dest, changed)
            full = overlay(dumps, tree, dest, changed)
            base_roots = [full, os.path.join(dumps, tree), os.path.join(dumps, 'cs-scripts')]
            orig_full = overlay(dumps, tree, dest + '-orig', [])
            before = run_checkers(orig_full, [orig_full])
            after = run_checkers(full, base_roots)
            shutil.rmtree(orig_full, ignore_errors=True)
            added = sorted(a for a in after - before
                           if not (a.startswith('lua_globals: ') and a.split(': ')[2] in LUA_BASE))
            removed = sorted(before - after)
            print(f'== {name}: {len(jobs)} patches, {len(changed)} files, {len(errors)} patch errors, '
                  f'{len(notes)} script notes, checkers +{len(added)} -{len(removed)}')
            for line in errors + notes + ['+ ' + a for a in added] + ['- ' + r for r in removed]:
                print('  ' + line)
            bad = bad or bool(errors) or bool(added) or any(n.startswith('SYNTAX') for n in notes)
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
