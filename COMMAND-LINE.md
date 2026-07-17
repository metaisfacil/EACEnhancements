# Command-line use

EAC Enhancements adds these arguments:

```text
--eace-metadata=d1.<payload>
--eace-100-log
```

`--eace-metadata` loads disc and track metadata into EAC. `--eace-100-log`
starts the usual EAC Enhancements workflow after that metadata has been loaded.
It requires `--eace-metadata`; the rip will not start with blank metadata.

The destination prompt, gap and ISRC detection, cue sheet, Test & Copy, folder
template, and error alert behave exactly as they do when the workflow is chosen
from EAC's Action menu.

## The `d1.` payload

Create the payload by encoding a JSON document as UTF-8, compressing it as a
raw DEFLATE stream (RFC 1951), and encoding the result as unpadded Base64url
(RFC 4648 section 5). Prefix the result with `d1.`.

The JSON shape is:

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
    "extendedDiscInformation": "Catalog number or other notes"
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
cdNumber, totalNumberOfCds, albumComposer, coverImageUrl, coverImageBase64
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

Only one instance of either argument may be supplied. Metadata is validated
before EAC starts the workflow, and the metadata provider selected in EAC is
restored immediately afterward.
