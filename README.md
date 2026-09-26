# S.T.A.L.K.E.R. Save Editor — Next (C#)

Work-in-progress .NET rewrite of [S.T.A.L.K.E.R. Save Editor](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save_Editor).
The Python editor stays the reference implementation until this port reaches parity.

The read-only X-Ray readers currently recognize original Shadow of Chernobyl,
Clear Sky, and Call of Pripyat saves plus their Enhanced Editions. See
[the current X-Ray reader scope](docs/CS4_XRAY_TRILOGY.md).

X-Ray item removal is limited to actor-owned registry leaves that are not
equipped. See the [CS-5 removal scope](docs/CS5_XRAY_DELETE.md).

The read-only S2 reader parses the CRC/Kraken container, confirmed player
inventory layout, save-local item name tables, and recognized stash layout.
See the [CS-4 S2 reader scope](docs/CS4_STALKER2.md).

The `StalkerSaveEditor.Steam` library exposes Steam RemoteStorage list and read
operations through a separate worker process with a 15-second timeout. It does
not expose write operations. Automated tests use fake worker and native-storage
interfaces; they do not initialize a live Steam session.

Start with [ARCHITECTURE.md](ARCHITECTURE.md) and [AGENTS.md](AGENTS.md).
