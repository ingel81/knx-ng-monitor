#!/bin/bash

# KNX-NG-Monitor Start Script

echo "Starting KNX-NG-Monitor in separate terminals..."

# Get the project root directory
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Detect available terminal emulator
detect_terminal() {
    if command -v gnome-terminal &> /dev/null; then
        echo "gnome-terminal"
    elif command -v konsole &> /dev/null; then
        echo "konsole"
    elif command -v xfce4-terminal &> /dev/null; then
        echo "xfce4-terminal"
    elif command -v alacritty &> /dev/null; then
        echo "alacritty"
    elif command -v kitty &> /dev/null; then
        echo "kitty"
    elif command -v xterm &> /dev/null; then
        echo "xterm"
    else
        echo "none"
    fi
}

TERMINAL=$(detect_terminal)

if [ "$TERMINAL" = "none" ]; then
    echo "Error: No supported terminal emulator found!"
    echo "Please install one of: gnome-terminal, konsole, xfce4-terminal, alacritty, kitty, xterm"
    exit 1
fi

echo "Using terminal: $TERMINAL"

# Start Backend in new terminal
case "$TERMINAL" in
    "gnome-terminal")
        gnome-terminal --title="KNX Monitor - Backend" -- bash -c "cd '$PROJECT_ROOT/backend' && dotnet run --project KnxMonitor.Api; exec bash"
        ;;
    "konsole")
        konsole --new-tab --title "KNX Monitor - Backend" -e bash -c "cd '$PROJECT_ROOT/backend' && dotnet run --project KnxMonitor.Api; exec bash" &
        ;;
    "xfce4-terminal")
        xfce4-terminal --title="KNX Monitor - Backend" -e "bash -c 'cd $PROJECT_ROOT/backend && dotnet run --project KnxMonitor.Api; exec bash'" &
        ;;
    "alacritty")
        alacritty -t "KNX Monitor - Backend" -e bash -c "cd '$PROJECT_ROOT/backend' && dotnet run --project KnxMonitor.Api; exec bash" &
        ;;
    "kitty")
        kitty --title "KNX Monitor - Backend" bash -c "cd '$PROJECT_ROOT/backend' && dotnet run --project KnxMonitor.Api; exec bash" &
        ;;
    "xterm")
        xterm -T "KNX Monitor - Backend" -e bash -c "cd '$PROJECT_ROOT/backend' && dotnet run --project KnxMonitor.Api; exec bash" &
        ;;
esac

# Wait a moment for backend to start
sleep 2

# Start Frontend in new terminal
case "$TERMINAL" in
    "gnome-terminal")
        gnome-terminal --title="KNX Monitor - Frontend" -- bash -c "cd '$PROJECT_ROOT/frontend' && npm start; exec bash"
        ;;
    "konsole")
        konsole --new-tab --title "KNX Monitor - Frontend" -e bash -c "cd '$PROJECT_ROOT/frontend' && npm start; exec bash" &
        ;;
    "xfce4-terminal")
        xfce4-terminal --title="KNX Monitor - Frontend" -e "bash -c 'cd $PROJECT_ROOT/frontend && npm start; exec bash'" &
        ;;
    "alacritty")
        alacritty -t "KNX Monitor - Frontend" -e bash -c "cd '$PROJECT_ROOT/frontend' && npm start; exec bash" &
        ;;
    "kitty")
        kitty --title "KNX Monitor - Frontend" bash -c "cd '$PROJECT_ROOT/frontend' && npm start; exec bash" &
        ;;
    "xterm")
        xterm -T "KNX Monitor - Frontend" -e bash -c "cd '$PROJECT_ROOT/frontend' && npm start; exec bash" &
        ;;
esac

echo ""
echo "=========================================="
echo "Services started in separate terminals!"
echo "=========================================="
echo "Backend: http://localhost:5075"
echo "Frontend: http://localhost:4200"
echo "=========================================="
echo ""
