#!/usr/bin/env python3
"""generate_place_names.py DUMPS_DIR: adds level and stash names from the games' own string tables to catalog_names.json.

DUMPS_DIR holds the Enhanced Edition trees written by `fixes extract` (socee-all, csee-all, copee-all): they carry the
text of every language. Written kinds, per release family:
  levels   level id as a save spells it, lower case (l01_escape, marsh, zaton) -> name
  stashes  Clear Sky only: name of the box object a treasure section targets -> the treasure's name
Only names are taken; nothing else from the game files is stored."""
import glob, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CATALOG = os.path.join(HERE, '..', 'src', 'StalkerSaveEditor.Core', 'Catalogs', 'Data', 'catalog_names.json')
FOLDERS = {'cze': 'cs', 'ger': 'de', 'eng': 'en', 'spa': 'es', 'fra': 'fr', 'ita': 'it', 'jpn': 'ja', 'kor': 'ko',
           'pol': 'pl', 'rus': 'ru', 'ukr': 'uk', 'zh_cn': 'zh_CN', 'zh_tw': 'zh_TW'}
LEGACY = {'cze': 'cp1250', 'pol': 'cp1250', 'rus': 'cp1251', 'ukr': 'cp1251'}
TREES = {'soc': ('socee-all', 'config'), 'clear_sky': ('csee-all', 'configs'), 'cop': ('copee-all', 'configs')}
STRING = re.compile(r'<string\s+id="([^"]+)"\s*>\s*<text>(.*?)</text>', re.S)


def texts(folder, lang):
    """id (lower case) -> text of one language folder."""
    out = {}
    for path in sorted(glob.glob(os.path.join(folder, '*.xml'))):
        raw = open(path, 'rb').read()
        try:
            body = raw.decode('utf-8-sig')
        except UnicodeDecodeError:
            body = raw.decode(LEGACY.get(lang, 'cp1252'), errors='replace')
        for key, text in STRING.findall(body):
            text = re.sub(r'\s+', ' ', text.replace('\\n', ' ')).strip()
            if text and '�' not in text:
                out.setdefault(key.lower(), text)
    return out


def level_ids(root, cfg):
    """Level names of game_levels.ltx / game_maps_single.ltx: the ids a level changer stores."""
    ids = set()
    for name in ('game_levels.ltx', 'game_maps_single.ltx'):
        path = os.path.join(root, cfg, name)
        if os.path.exists(path):
            body = open(path, 'rb').read().decode('latin-1')
            ids |= {m.lower() for m in re.findall(r'^\s*name\s*=\s*(\w+)', body, re.M)}
            ids |= {m.lower() for m in re.findall(r'^\[(\w+)\]', body, re.M)}
    return ids


def treasures(root, cfg):
    """box object name -> string id of the treasure name (Clear Sky spells the target as the object's name)."""
    out = {}
    for path in glob.glob(os.path.join(root, cfg, 'misc', 'treasure_*.ltx')):
        body = open(path, 'rb').read().decode('latin-1')
        for section in re.split(r'^\[', body, flags=re.M)[1:]:
            target = re.search(r'^target\s*=\s*([A-Za-z_]\w*)\s*$', section, re.M)
            name = re.search(r'^name\s*=\s*(\w+)', section, re.M)
            if target and name:
                out[target.group(1).lower()] = name.group(1).lower()
    return out


def main():
    dumps = os.path.abspath(sys.argv[1])
    catalog = json.load(open(CATALOG, encoding='utf-8'))
    for family, (tree, cfg) in TREES.items():
        root = os.path.join(dumps, tree)
        table = {code: texts(os.path.join(root, cfg, 'text', folder), folder) for folder, code in FOLDERS.items()}
        levels = {}
        for level in sorted(level_ids(root, cfg)):
            names = {code: strings[level] for code, strings in table.items() if level in strings}
            if 'en' in names:
                levels[level] = dict(sorted(names.items()))
        stashes = {}
        for box, string_id in sorted(treasures(root, cfg).items()):
            names = {code: strings[string_id] for code, strings in table.items() if string_id in strings}
            if 'en' in names:
                stashes[box] = dict(sorted(names.items()))
        kinds = catalog['releases'][family]
        kinds['levels'] = levels
        if stashes:
            kinds['stashes'] = stashes
        print(f'{family}: {len(levels)} levels, {len(stashes)} stashes')
    with open(CATALOG, 'w', encoding='utf-8') as out:
        json.dump(catalog, out, ensure_ascii=False, indent=1, sort_keys=True)
        out.write('\n')


if __name__ == '__main__':
    main()
