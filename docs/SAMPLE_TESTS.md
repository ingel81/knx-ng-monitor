# KNX Project Parser — Sample Test Recipes

Manual / CLI test commands for every sample we keep around. Run from the
repository root.

The CLI tool lives in `backend/KnxMonitor.ParserTool` and reads its input
directly from disk — no need to copy files anywhere.

## Quick start (all samples in one go)

```bash
bash test-all-samples.sh           # Linux / macOS / Git Bash on Windows
./test-all-samples.ps1             # PowerShell
```

Both scripts iterate over the public xknxproject samples and the proprietary
samples in `docs/samples/own/`. Missing files are reported as `[SKIP]` and do
not fail the run.

## CLI options

```
parse <file>                                  # plain
      --password <pw>                         # for password-protected projects
      --keyring <file.knxkeys>                # KNX Secure: read tool keys / GA keys
      --keyring-password <kpw>                # password for the .knxkeys file
      --format console | json | csv           # output format (default: console)
      --verbose                               # progress + debug logging on stderr

detect <file>                                  # fast pre-scan: ETS version / password / Secure / addressing style
       --format console | json | csv
```

## Public xknxproject samples (always tracked)

These live under `docs/samples/xknxproject/` and are committed to the repo
under their original MIT licence (mirrored from
[XKNX/xknxproject](https://github.com/XKNX/xknxproject)).

| # | Sample | Password | What it exercises |
|---|---|---|---|
| 1 | `test_project-ets4-no_password.knxproj` | – | ETS 4 plain |
| 2 | `test_project-ets4.knxproj` | `test` | ETS 4 + password (standard ZIP encryption) |
| 3 | `ets6_free.knxproj` | – | ETS 6 + Free addressing (16-bit raw integers) |
| 4 | `ets6_two_level.knxproj` | – | ETS 6 + Two-level addressing (`main/sub`) |
| 5 | `testprojekt-ets6.knxproj` | `test` | **ETS 6 + password** — exercises the PBKDF2(UTF-16-LE)/Base64 → AES wrapping |

Example commands:

```bash
SAMPLES=docs/samples/xknxproject

dotnet run --project backend/KnxMonitor.ParserTool -- parse  "$SAMPLES/ets6_free.knxproj"
dotnet run --project backend/KnxMonitor.ParserTool -- parse  "$SAMPLES/test_project-ets4.knxproj" --password test
dotnet run --project backend/KnxMonitor.ParserTool -- parse  "$SAMPLES/testprojekt-ets6.knxproj" --password test
dotnet run --project backend/KnxMonitor.ParserTool -- detect "$SAMPLES/testprojekt-ets6.knxproj"
```

## Proprietary samples (gitignored, expected under `docs/samples/own/`)

These are typically your own ETS exports. They are listed here as the contract
for what the test suite and `test-all-samples` scripts look for; the files
themselves are not tracked.

| # | Sample | Password | Keyring | Keyring PW | Notes |
|---|---|---|---|---|---|
| 6 | `myProject_ets_v5.7.7.knxproj` | – | – | – | Large ETS 5 project, used by the integration tests for line counts (841 GAs / 94 devices) |
| 7 | `TestMitSecure_ets_v5.7.7_secure.knxproj` | `affe` | `TestMitSecure_ets_v5.7.7_secure.knxkeys` | `affe` | KNX Secure end-to-end — exercises both the project-password unlock and the keyring decryption |

KNX Secure CLI run:

```bash
OWN=docs/samples/own

dotnet run --project backend/KnxMonitor.ParserTool -- parse \
  "$OWN/TestMitSecure_ets_v5.7.7_secure.knxproj" \
  --password affe \
  --keyring "$OWN/TestMitSecure_ets_v5.7.7_secure.knxkeys" \
  --keyring-password affe
```

Expected tail of the output:

```
Keyring: 1 device(s), 0 GA key(s), backbone=yes
```

## UI walkthrough

End-to-end through the Angular wizard at `http://localhost:8080`:

| Sample | Wizard prompts | Notes |
|---|---|---|
| `myProject_ets_v5.7.7.knxproj` | none | Auto-activated if first project |
| `test_project-ets4.knxproj` (`test`) | password | |
| `ets6_free` / `ets6_two_level` | none | |
| `testprojekt-ets6.knxproj` (`test`) | password → optional keyring (Skip works) | `<Security>` blocks present, but no Data-Secure GA |
| `TestMitSecure_..._secure.knxproj` (`affe`) | password → optional keyring | If keyring + `affe` provided, tool keys are decrypted and stored |

## Adding more samples

1. Drop the file into `docs/samples/own/` (private) or — if licence allows —
   `docs/samples/xknxproject/` (committed).
2. Add a new row in `test-all-samples.sh` and `test-all-samples.ps1`.
3. If you also want it covered by integration tests, add a `[SkippableFact]`
   in `backend/KnxMonitor.ProjectParser.Tests/Integration/`. Use
   `Skip.IfNot(TestSamples.Exists("filename"))` to keep CI happy when the
   file is missing.

## Why the test project no longer has a `TestData/` folder in git

`backend/KnxMonitor.ProjectParser.Tests/TestData/` is a build sink: the
project's `.csproj` `<None Include>` entries copy the sample files from
`docs/samples/{xknxproject,own}/` into that folder at build time so the test
binaries can find them next to the assembly. The directory is **gitignored**
to avoid duplicating sample bytes between `docs/samples/` and `TestData/`.
