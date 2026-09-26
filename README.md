# S.T.A.L.K.E.R. Save Editor — Next (C#)

Work-in-progress .NET rewrite of [S.T.A.L.K.E.R. Save Editor](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor).
The Python editor stays the reference implementation until this port reaches parity.

The read-only X-Ray readers currently recognize original Shadow of Chernobyl,
Clear Sky, and Call of Pripyat saves plus their Enhanced Editions. See
[the current X-Ray reader scope](docs/CS4_XRAY_TRILOGY.md).

The read-only S2 reader parses the CRC/Kraken container, confirmed player
inventory layout, save-local item name tables, and recognized stash layout.
See the [CS-4 S2 reader scope](docs/CS4_STALKER2.md).

Start with [ARCHITECTURE.md](ARCHITECTURE.md) and [AGENTS.md](AGENTS.md).
