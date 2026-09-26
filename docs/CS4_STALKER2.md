# CS-4: S.T.A.L.K.E.R. 2 read-only reader

`Stalker2SaveReader` accepts the container layout used by the Python oracle:

```text
u32 LE unpacked size
Kraken/Oodle stream
u32 LE CRC32(container bytes before this field)
```

It checks the CRC before decompressing, bounds the declared output size, and
requires exactly one confirmed wallet anchor before returning an S2 save. The
reader then parses the count-prefixed owned-handle and grid arrays, the
source-backed object-record fields, and any save-local name tables. The
`Stalker2StashReader` independently parses only the recognized stash header
that follows those player arrays.

Name-table entries are display metadata. The three-byte key remains an
observed serialization key; the reader does not expose it as a public SID or
use it to construct records. Unknown kinds, malformed grid references,
ambiguous object records, and unresolved names remain diagnostic/read-only.
The stash reader refuses missing, repeated, truncated, or inconsistent
headers instead of guessing another layout.

The committed S2 container and stash fixtures are synthetic outputs from the
Python repository. Tests compare S2 inventory fields, integrity, warnings,
character defaults, and time defaults against the Python golden vector. They
also cover name-table resolution, stash arrays, CRC failures, original and EE
X-Ray inputs, and malformed data. No personal save, game installation, or live
game write was used; structural parity does not establish in-game acceptance.
