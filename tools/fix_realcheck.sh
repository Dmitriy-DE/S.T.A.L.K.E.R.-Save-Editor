#!/bin/bash
# fix_realcheck.sh: installs the Recommended preset and then every other fix of every retail game into a hard-linked pristine copy of the real
# install (archives are never written), syntax-checks the written scripts, removes everything and checks nothing is left.
set -u
S=~/.local/share/Steam/steamapps; T=~/.cache/claude-pytest/steamlib/steamapps
C="$HOME/.dotnet/dotnet $HOME/Projects/save-editor-review-claude/src/StalkerSaveEditor.Cli/bin/Release/net10.0/StalkerSaveEditor.Cli.dll"
check() { # target appid folder
  rm -rf ~/.cache/claude-pytest/steamlib; mkdir -p "$T/common/$3"; cp "$S/appmanifest_$2.acf" "$T/"
  ( cd "$S/common/$3" && for x in *; do case "$x" in gamedata|_appdata_|.save-editor*) ;; *) cp -al "$x" "$T/common/$3/$x";; esac; done )
  D="$T/common/$3"
  $C fixes apply-preset recommended $1 "$D" 2>&1 | tail -1
  # every other catalogued fix (Community, Experimental) one by one: a wrong file hash only shows here
  for id in $($C fixes list --game $1 --json | python3 -c "import json,sys; print(' '.join(x['id'] for x in json.load(sys.stdin)))"); do
    r=$($C fixes install $id "$D" 2>&1 | tail -1); case "$r" in Installed:*|*already*) ;; *) echo "NOT INSTALLED $id: $r";; esac
  done
  bad=0; while IFS= read -r -d '' f; do luac5.1 -p "$f" >/dev/null 2>&1 || { echo "SYNTAX: $f"; bad=1; }; done < <(find "$D/gamedata" -name '*.script' -print0)
  echo "$1: scripts written $(find "$D/gamedata" -name '*.script' | wc -l), syntax errors: $bad"
  for id in $($C fixes list --game $1 --json | python3 -c "import json,sys; print(' '.join(x['id'] for x in reversed(json.load(sys.stdin))))"); do $C fixes remove $id "$D" >/dev/null 2>&1; done
  echo "$1: left after removal: $(find "$D/gamedata" -type f 2>/dev/null | wc -l) file(s); $($C fixes status $1 "$D" | tail -1)"
  rm -rf ~/.cache/claude-pytest/steamlib
}
check soc 4500 "STALKER Shadow of Chernobyl"
check cs 20510 "STALKER Clear Sky"
check cop 41700 "Stalker Call of Pripyat"
check soc-ee 2427410 "STALKER Shadow of Chornobyl - EE"
check cs-ee 2427420 "STALKER Clear Sky - EE"
check cop-ee 2427430 "STALKER Call of Prypiat - EE"
