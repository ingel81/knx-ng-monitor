# Plan: resolve communication-object flags from the manufacturer catalog

Status: **not started**. Written 2026-07-27 after the flag badges on the group-address page
turned out to have no data to show.

## The defect

`CommunicationObject.Flags` is empty for every ETS 5 / 6 project. On a real installation:

```
CommunicationObjects total : 1528
of those with flags        : 0
```

`BaseProjectLoader.BuildComObjectFlags` reads flag information **off the
`<ComObjectInstanceRef>` element in the project file**:

1. `<Send>` / `<Receive>` child connectors → `"Send,Receive"` (ETS 4)
2. otherwise the attributes `ReadFlag`, `WriteFlag`, `CommunicationFlag`, `TransmitFlag`,
   `UpdateFlag`, `ReadOnInitFlag` when they equal `Enabled`

In an actual ETS 6.4 project the instance refs carry none of that:

```xml
<ComObjectInstanceRef RefId="MD-2_M-1_MI-1_O-2-0_R-1" ChannelId="MD-2_M-1_MI-1_CH-1" Links="GA-1" />
```

ETS only writes flag attributes onto the instance ref when they **differ from the catalog
default**. The values themselves live in the manufacturer data, on the `<ComObject>` the ref
ultimately points at:

```xml
<ComObject Id="M-0083_A-000B-23-25DE_O-96" Name="globalSwitch" Number="96"
           ReadFlag="Disabled" WriteFlag="Enabled" CommunicationFlag="Enabled"
           TransmitFlag="Disabled" UpdateFlag="Disabled" ReadOnInitFlag="Disabled"
           DatapointType="DPST-1-1" />
```

The existing integration test (`Flags == "Send,Receive"`) passes because its sample is an ETS 4
project, where the connectors really are in the project file. ETS 5 / 6 were never covered.

## What to build

Resolve the reference chain and let the more specific level win:

```
ComObjectInstanceRef  (project, M-XXXX-less RefId)
  └─ ComObjectRef     (manufacturer catalog, may override single attributes)
       └─ ComObject    (manufacturer catalog, carries the defaults)
```

Rules:

- Start from the `<ComObject>` attributes, overlay any attribute the `<ComObjectRef>` sets,
  overlay any attribute the `<ComObjectInstanceRef>` sets. Only the most specific wins per
  attribute — not per element.
- Keep the ETS 4 `Send` / `Receive` path exactly as it is; it is correct for that schema.
- The manufacturer files are already in the `ProjectFileMap` (`M-XXXX/*.xml`), so no new I/O —
  see the ZipHandler merge fixed in 0.8.1, which is why they are present even for
  password-protected projects.

**Failsafe is a requirement.** Flags are decoration, not core data:

- Any failure to resolve a ref leaves `Flags = null` and must not fail the import.
- Wrap the resolution so a malformed or missing manufacturer file is logged and skipped.
- Never let this path throw into `ParseProjectFileAsync`.

## Re-import: already solved, do not redo

Verified 2026-07-27:

- `ProjectImportService.cs:265` derives a stable ETS project id and calls
  `GetByEtsProjectIdAsync`; on a hit it takes the **update** path via `MergeReimportAsync`,
  which keeps telegram history and replaces com objects, locations and group ranges.
- `ProjectFeatureDetector.cs:32` sets that id from `P-XXXX.zip` for password-protected
  projects, and `:58` from the `P-XXXX/` folder for plain ones — so both are matched and
  neither creates a duplicate project.

So a re-import after this change picks the flags up in place. Nothing to build here; only
mention in the release notes that a re-import is needed for existing projects.

## Tests

- Unit test the three-level merge directly: instance overrides ref overrides object.
- Integration test against an ETS 6 sample asserting a non-empty flag set — this is the case
  that has no coverage today and let the bug through.
- Keep the existing ETS 4 `"Send,Receive"` assertion green.

## Open question

The UI currently normalises `Send`→`Transmit` and `Receive`→`Write`
(`group-addresses.component.ts`, `FLAG_ALIASES`). Once ETS 5 / 6 deliver real flags, check
whether that mapping should stay for ETS 4 projects or whether the badges should mark
interpreted values differently. The raw project value is already in the badge tooltip.
