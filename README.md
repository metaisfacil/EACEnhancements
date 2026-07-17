# EAC Enhancements

> [!IMPORTANT]
> This is experimental and proof-of-concept software that relies on memory hacking.
> It has not yet been extensively battle-tested. Proceed at your own risk.

EAC Enhancements is an experimental/proof-of-concept third-party plugin for
Exact Audio Copy that adds a number of quality-of-life features:

- Automated 100% log + cue test & copy workflow
- Read-only verification of EAC's required 100% log settings
- Optional alerts after rips highlighting errors
- Auto-generated album folders with dynamic naming
- Improved responsiveness of progress window during secure mode rips

Exact Audio Copy 1.6 and 1.8 are supported. No patches or launcher needed.

The plugin can also begin the 100% log workflow automatically with metadata.
See [Command-line use](COMMAND-LINE.md) for details.

## Repository layout

- `src/EACEnhancements` contains the production C# sources.
- `Tests` contains the standalone C# test programs.
- `Scripts` contains build, installation, and diagnostic-launch tooling.
- `Artifacts` receives generated build output and is ignored by Git.

## Installation

1. Ensure Exact Audio Copy is not running.
2. If you downloaded a release, place EACEnhancements.dll next to EAC.exe.
If you're building from source, run `Scripts\Install EAC Enhancements.cmd`.
3. On first run, start EAC normally so the options file can be created.
Alternatively, you can manually create your own EACEnhancements.ini based
on the example provided in this repo and place it next to EAC.exe.

The installer locates registered EAC installations automatically. 
You can also run `.\Scripts\Install.ps1 -EacDirectory "path to EAC"`.

## Building

Run `.\Scripts\Build.ps1`. The compiled `EACEnhancements.dll` is written to
the `Artifacts` folder. For a portable or unregistered EAC installation, pass
its location with `-EacDirectory "path to EAC"`.

Run `.\Scripts\Test.ps1` to compile and execute all standalone test programs.
Their generated executables are written under `Artifacts\Tests`.

## Using the 100% log workflow

Select the tracks to rip, then choose **Action > Test & Copy + Cue (100% Log)**.
EAC Enhancements will ask for a destination, if applicable, before beginning
the preparation and extraction process.

The destination works as follows:

- **Use this directory:** A new album folder is always created inside EAC's
  configured extraction directory.
- **Ask every time**, with **Create new folders...** enabled: Choose the parent
  directory in which the new album folder should be created.
- **Ask every time**, with **Create new folders...** disabled: Choose the folder
  that should receive the rip files directly.

## EAC Enhancements options

Open **Action > EAC Enhancements Options...** to choose the extraction root,
folder template, new-folder behavior, rip-error alerts, and diagnostic logging.

The **Check 100% Log Setup...** button reports any required EAC settings that
are incorrect and tells you what they should be. It does not change any
settings or complain about optional recommendations.

### Conditional folder components

Use EAC's usual percent-style tokens in folder templates. For example:

```text
%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%comment%}}}
```

Triple parentheses include the year and its parentheses only when a year is
present. Triple curly braces do the same for the comment and its braces.

With both values present:

```text
Unknown Artist - Unknown Title (2004) [FLAC] {ABCD-001}
```

With both values empty:

```text
Unknown Artist - Unknown Title [FLAC]
```

## Rip-error alerts

When enabled, an alert appears after a rip only if EAC reported a problem. The
alert identifies affected track numbers when the error is track-specific.

## Troubleshooting

Diagnostic logging is off by default. Enable it in EAC Enhancements Options
before reproducing a problem, then check `EACEnhancements.log` in the Exact
Audio Copy folder. Developers can instead use
`Scripts\Launch EAC with Logging.cmd` for a single logging-enabled session
without changing the saved option.

If installation reports that the DLL is in use, close every EAC window and run
the installer again.

If EAC Enhancements reports that it cannot create or update
`EACEnhancements.ini`, the current Windows account probably cannot write to the
EAC installation folder. Close EAC and have an administrator grant that account
Modify permission to the folder, or install EAC somewhere the account can write
to. The plugin continues with existing settings or built-in defaults, but it
cannot persist option changes until write access is corrected.
