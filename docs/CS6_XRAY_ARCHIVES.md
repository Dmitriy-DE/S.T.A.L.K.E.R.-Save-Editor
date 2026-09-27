# CS-6: read-only X-Ray archive access

`XRayArchiveReader.Open(Stream)` reads `.db`, `.xdb`, and `.xrp` archives from
a readable, seekable stream. The returned `XRayArchive` exposes the complete
file table through `Entries` and reads an entry on demand with `ReadFile(name)`.
Entry lookup ignores path case, accepts either slash style, and does not extract
files to disk. The archive owns the input stream unless `leaveOpen` is `true`.

The reader accepts optional type-666 metadata chunks, ordinary FAT headers,
direct LZ-Huffman headers, and the two regional X-Ray scrambled LZ-Huffman
header variants. File payloads whose stored and unpacked sizes differ use the
managed LZO1X codec. A nonzero FAT CRC32 is checked against the unpacked bytes;
zero retains the Python oracle's "checksum omitted" behavior.

Tests construct synthetic archives in memory, including compressed and
scrambled headers, compressed file payloads, truncated chunks, and CRC errors.
No game archives or personal save files are stored in the repository.
