# Command-line use

EAC Enhancements adds these arguments:

```text
--eace-metadata=d1.<payload>
--eace-100-log
--eace-drive=J:
--eace-dest=C:\Rips\%albumartist% - %albumtitle% (((%year%)))
```

`--eace-metadata` loads disc and track metadata into EAC. `--eace-100-log`
starts the usual EAC Enhancements workflow after that metadata has been loaded.
Metadata is required; the rip will not start without it.
`--eace-drive` selects the requested optical drive before the disc is validated
or metadata is loaded. It accepts a drive letter or a portion of the drive name.
`--eace-dest` optionally supplies the fully qualified album-folder destination.
It may contain the same metadata tokens and conditional delimiters as the
regular EAC Enhancements folder template.

When `--eace-dest` is present, the path is the folder that directly receives the
audio, CUE, playlist, and log - not its parent. This means the default standard
extraction directory and folder template settings are overrided.

After a command-line request completes, EAC should close automatically.
Any log issues found afterward are written to stderr.

## The `d1.` payload

Create the payload by encoding a JSON document as UTF-8, compressing it as a
raw DEFLATE stream (RFC 1951), and encoding the result as unpadded Base64url
(RFC 4648 section 5). Prefix the result with `d1.`.

An example of the JSON format follows:

```json
{
  "disc": {
    "trackCount": 2,
    "cddbId": "89ABCDEF",
    "leadoutPosition": 12345,
    "trackStartPositions": [150, 6000],
    "albumArtist": "Example Artist",
    "albumTitle": "Example Album",
    "year": 2026,
    "mp3V2Type": "Rock",
    "label": "Example Records",
    "barcode": "012345678905",
    "catalogNumber": "ABC-123"
  },
  "tracks": [
    { "number": 1, "title": "First Track", "artist": "Example Artist" },
    { "number": 2, "title": "Second Track", "artist": "Example Artist" }
  ]
}
```

`disc.trackCount` and one entry in `tracks` for every disc track are required.
Track numbers are one-based. `cddbId`, `leadoutPosition`, track positions, and
the physical flags are optional identity checks; if supplied, EAC Enhancements
refuses to apply the metadata or start a rip when the inserted disc differs.

Available `disc` fields are:

```text
trackCount, cddbId, leadoutPosition, trackStartPositions,
albumArtist, albumTitle, cddbMusicType, year, revision, mp3Type,
extendedDiscInformation, mp3V2Type, firstTrackNumber, albumInterpret,
cdNumber, totalNumberOfCds, albumComposer, label, barcode, catalogNumber,
coverImageUrl, coverImageBase64
```

Available track fields are:

```text
number, title, extendedInformation, artist, composer, lyrics,
startPosition, endPosition, preemphasis, dataTrack, fourChannels
```

Omitted text fields are loaded as empty strings. The defaults for `year`,
`revision`, `cddbMusicType`, and `mp3Type` are `-1`; number fields otherwise
use EAC's normal single-disc defaults. Unknown fields are rejected so a typo
cannot silently produce incorrectly tagged files.

Only one instance of each argument may be supplied. Metadata is validated
before EAC starts the workflow, and the metadata provider and normal
existing-metadata confirmation behavior are restored immediately afterward.
