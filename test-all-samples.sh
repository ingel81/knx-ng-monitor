#!/bin/bash
# Test all KNX Project Parser samples with passwords
# Run from repository root

XKNX_SAMPLES="docs/samples/xknxproject"
OWN_SAMPLES="docs/samples/own"
PARSER_TOOL_PATH="backend/KnxMonitor.ParserTool"

echo ""
echo "========================================"
echo "KNX Project Parser - Sample Test Suite"
echo "========================================"
echo ""

# Counters
total=0
success_detect=0
success_parse=0
failed_detect=0
failed_parse=0

# Function to test a project
test_project() {
    local name=$1
    local file_path=$2
    local password=$3

    ((total++))

    echo ""
    echo "----------------------------------------"
    echo "[$total] $name"
    echo "----------------------------------------"

    if [ ! -f "$file_path" ]; then
        echo "[SKIP] missing fixture: $file_path"
        return
    fi

    # Detect
    echo ""
    echo "[DETECT]"
    if dotnet run --project "$PARSER_TOOL_PATH" -- detect "$file_path" 2>&1; then
        ((success_detect++))
        echo "✓ Detect OK"
    else
        ((failed_detect++))
        echo "✗ Detect FAILED"
    fi

    # Parse
    echo ""
    echo "[PARSE]"
    if [ -z "$password" ]; then
        if dotnet run --project "$PARSER_TOOL_PATH" -- parse "$file_path" 2>&1; then
            ((success_parse++))
            echo "✓ Parse OK"
        else
            ((failed_parse++))
            echo "✗ Parse FAILED"
        fi
    else
        if dotnet run --project "$PARSER_TOOL_PATH" -- parse "$file_path" --password "$password" 2>&1; then
            ((success_parse++))
            echo "✓ Parse OK (password: $password)"
        else
            ((failed_parse++))
            echo "✗ Parse FAILED (password: $password)"
        fi
    fi
}

# Public xknxproject samples (always tracked)
test_project "ETS4 - No Password" \
             "$XKNX_SAMPLES/test_project-ets4-no_password.knxproj"

test_project "ETS4 - Password Protected" \
             "$XKNX_SAMPLES/test_project-ets4.knxproj" \
             "test"

test_project "ETS6 - Free Addressing" \
             "$XKNX_SAMPLES/ets6_free.knxproj"

test_project "ETS6 - Two Level Addressing" \
             "$XKNX_SAMPLES/ets6_two_level.knxproj"

test_project "ETS6 - Password Protected (xknxproject-style)" \
             "$XKNX_SAMPLES/testprojekt-ets6.knxproj" \
             "test"

# Local-only samples (skipped when not present)
test_project "ETS5 - Large Project (own)" \
             "$OWN_SAMPLES/myProject_ets_v5.7.7.knxproj"

test_project "ETS5 - KNX Secure (own)" \
             "$OWN_SAMPLES/TestMitSecure_ets_v5.7.7_secure.knxproj" \
             "affe"

echo ""
echo "========================================"
echo "SUMMARY"
echo "========================================"
echo "Total samples tested: $total"
echo "Detect: $success_detect OK, $failed_detect FAILED"
echo "Parse:  $success_parse OK, $failed_parse FAILED"
echo "========================================"
echo ""
