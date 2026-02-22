#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# AIDE-Lite Build Bridge — Mac → Parallels Windows 11
# ============================================================
# Orchestrates build/deploy via prlctl exec into the Parallels VM.
# Usage: ./build.sh [all|build|rebuild|quick|close|deploy|launch|status]

VM_NAME="Windows 11"
HELPER_SCRIPT='C:\Users\tomekgolembiewski\ClaudeWinProjects\AIDE-Lite\build-helper.ps1'

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# ============================================================
# Run a step inside the Parallels VM
# ============================================================
run_step() {
    local step="$1"
    local label="$2"

    echo -e "${CYAN}>>> ${label}${NC}"

    prlctl exec "$VM_NAME" --current-user \
        powershell -ExecutionPolicy Bypass \
        -Command "& '$HELPER_SCRIPT' -Step '$step'; exit \$LASTEXITCODE"

    local rc=$?
    if [ $rc -ne 0 ]; then
        echo -e "${RED}>>> FAILED: ${label} (exit code ${rc})${NC}"
        exit $rc
    fi
    echo ""
}

# ============================================================
# Subcommands
# ============================================================
cmd_all() {
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN}  AIDE-Lite: Full Build & Deploy Cycle  ${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
    run_step "close"  "Close Studio Pro"
    run_step "build"  "Build AIDE-Lite"
    run_step "deploy" "Deploy to Extensions"
    run_step "launch" "Launch Studio Pro"
    echo -e "${GREEN}>>> All done!${NC}"
}

cmd_build() {
    run_step "build" "Build AIDE-Lite"
}

cmd_rebuild() {
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN}  AIDE-Lite: Rebuild (no launch)        ${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
    run_step "close"  "Close Studio Pro"
    run_step "build"  "Build AIDE-Lite"
    run_step "deploy" "Deploy to Extensions"
    echo -e "${GREEN}>>> Rebuild complete. Run './build.sh launch' when ready.${NC}"
}

cmd_quick() {
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN}  AIDE-Lite: Quick Deploy               ${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo -e "${YELLOW}>>> Assumes Studio Pro is already closed${NC}"
    echo ""
    run_step "build"  "Build AIDE-Lite"
    run_step "deploy" "Deploy to Extensions"
    echo -e "${GREEN}>>> Quick deploy complete. Run './build.sh launch' when ready.${NC}"
}

cmd_close() {
    run_step "close" "Close Studio Pro"
}

cmd_deploy() {
    run_step "deploy" "Deploy to Extensions"
}

cmd_launch() {
    run_step "launch" "Launch Studio Pro"
}

cmd_status() {
    run_step "status" "AIDE-Lite Status"
}

# ============================================================
# Usage
# ============================================================
usage() {
    echo -e "${CYAN}AIDE-Lite Build Bridge${NC} — Mac → Parallels"
    echo ""
    echo "Usage: ./build.sh <command>"
    echo ""
    echo "Commands:"
    echo "  all      Full workflow: close → build → deploy → launch"
    echo "  build    Build only (check for compile errors)"
    echo "  rebuild  Close → build → deploy (no launch)"
    echo "  quick    Build → deploy (assumes Studio Pro already closed)"
    echo "  close    Close Studio Pro + remove lock file"
    echo "  deploy   Copy build output to Extensions folder"
    echo "  launch   Launch Studio Pro with project"
    echo "  status   Check Studio Pro state and deployment info"
    echo ""
}

# ============================================================
# Dispatch
# ============================================================
if [ $# -eq 0 ]; then
    usage
    exit 0
fi

case "$1" in
    all)     cmd_all ;;
    build)   cmd_build ;;
    rebuild) cmd_rebuild ;;
    quick)   cmd_quick ;;
    close)   cmd_close ;;
    deploy)  cmd_deploy ;;
    launch)  cmd_launch ;;
    status)  cmd_status ;;
    -h|--help|help)
        usage ;;
    *)
        echo -e "${RED}Unknown command: $1${NC}"
        echo ""
        usage
        exit 1
        ;;
esac
