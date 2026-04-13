# KNX Project Parser - Sample Test Commands

## Quick Start

### Run All Tests (Automated)

**PowerShell (Windows):**
```powershell
.\test-all-samples.ps1
```

**Bash (Linux/WSL):**
```bash
./test-all-samples.sh
```

---

## Individual Test Commands

All commands assume you're in the `backend/KnxMonitor.ParserTool` directory.

### ETS5 Projects

#### 1. ETS5 - Large Project (841 GAs, 94 Devices)
**File:** `myProject_ets_v5.7.7.knxproj` (No Password)

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/myProject_ets_v5.7.7.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/myProject_ets_v5.7.7.knxproj"

# Parse with JSON output
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/myProject_ets_v5.7.7.knxproj" --format json

# Parse with CSV output
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/myProject_ets_v5.7.7.knxproj" --format csv > output.csv
```

#### 2. ETS5 - Password Protected
**File:** `TestMitSecure_ets_v5.7.7_secure.knxproj` (Password: `affe`)

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/TestMitSecure_ets_v5.7.7_secure.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/TestMitSecure_ets_v5.7.7_secure.knxproj" --password affe

# Parse with verbose logging
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/TestMitSecure_ets_v5.7.7_secure.knxproj" --password affe --verbose
```

---

### ETS4 Projects

#### 3. ETS4 - No Password
**File:** `test_project-ets4-no_password.knxproj`

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/test_project-ets4-no_password.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/test_project-ets4-no_password.knxproj"
```

#### 4. ETS4 - Password Protected
**File:** `test_project-ets4.knxproj` (Password: `test`)

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/test_project-ets4.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/test_project-ets4.knxproj" --password test
```

---

### ETS6 Projects

#### 5. ETS6 - Free Addressing
**File:** `ets6_free.knxproj` (No Password)

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/ets6_free.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/ets6_free.knxproj"
```

#### 6. ETS6 - Two Level Addressing
**File:** `ets6_two_level.knxproj` (No Password)

```bash
# Detect
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/ets6_two_level.knxproj"

# Parse
dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/ets6_two_level.knxproj"
```

#### 7. ETS6 - Password Protected ⚠️ SKIP
**File:** `testprojekt-ets6.knxproj` (Password: `test`)

**NOTE:** This file has SharpZipLib compatibility issues. Skip this test.

```bash
# Detect works
dotnet run -- detect "../KnxMonitor.ProjectParser.Tests/TestData/testprojekt-ets6.knxproj"

# Parse FAILS with SharpZipLib error
# dotnet run -- parse "../KnxMonitor.ProjectParser.Tests/TestData/testprojekt-ets6.knxproj" --password test
```

---

## CLI Command Reference

### Detect Command
Quickly detect project features without full parsing.

```bash
dotnet run -- detect <file.knxproj> [--format console|json|csv]
```

**Output includes:**
- Project ID (e.g., P-046B)
- ETS Version (Ets4, Ets5, Ets6)
- Password protection (Yes/No)
- KNX Secure (Yes/No)
- Addressing Style (ThreeLevel, TwoLevel, Free)

### Parse Command
Full project parsing with group addresses and devices.

```bash
dotnet run -- parse <file.knxproj> [options]
```

**Options:**
- `--password <password>` - Password for encrypted projects
- `--format console|json|csv` - Output format (default: console)
- `--verbose` - Enable verbose logging

**Output includes:**
- All features (from detect)
- Group Addresses (raw address, formatted address, name, DPT)
- Devices (physical address, name, manufacturer, product)
- Statistics (duration, counts, warnings)

---

## Expected Results

### ETS5 Large Project
```
Statistics:
  Duration:      ~80-120 ms
  Group Addrs:   841
  Devices:       94
  Addressing:    ThreeLevel (5/3/8)
```

### ETS5 Password Protected
```
Statistics:
  Duration:      ~40-50 ms
  Group Addrs:   1
  Devices:       2
  Password:      Yes (affe)
```

### ETS4 Projects
```
Statistics:
  Duration:      ~50-150 ms
  Group Addrs:   Varies
  Devices:       Varies
  ETS Version:   Ets4
```

### ETS6 Projects
```
Statistics:
  Duration:      ~30-50 ms
  Group Addrs:   4 (ets6_free/two_level)
  Devices:       0 (minimal samples)
  ETS Version:   Ets6

⚠️  WARNING: ETS6 Segment-Tag fallback may log warning if no Segment tags found
```

---

## Troubleshooting

### Issue: "Could not find file"
**Solution:** Run commands from `backend/KnxMonitor.ParserTool` directory or use absolute paths.

### Issue: "Wrong password"
**Solution:** Check password in table above. Case-sensitive!

### Issue: ETS6 SharpZipLib error
**Expected:** Known issue with `testprojekt-ets6.knxproj`. Skip this file.

### Issue: No devices found in ETS6
**Expected:** The `ets6_free.knxproj` and `ets6_two_level.knxproj` samples are minimal and don't contain device definitions.

---

## Performance Benchmarks

| Project | ETS Version | GAs | Devices | Duration |
|---------|-------------|-----|---------|----------|
| myProject | ETS5 | 841 | 94 | ~80ms |
| TestMitSecure | ETS5 | 1 | 2 | ~44ms |
| test_project-ets4 | ETS4 | Varies | Varies | ~100ms |
| ets6_free | ETS6 | 4 | 0 | ~33ms |

**Target:** <500ms for medium projects ✅ **Achieved!**
