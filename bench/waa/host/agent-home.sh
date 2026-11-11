#!/bin/bash
# Make the agents' own Claude Code folder, $WAA_HOME/claude-home: only the API settings from this user's
# ~/.claude/settings.json (base URL and key, for the lab's proxy) and no auto-update. run.py's agents get it through
# CLAUDE_CONFIG_DIR, so no CLAUDE.md, skills, plugins, hooks or effort setting of the machine's user reach them.
# Prints the setting names it kept, never their values.
set -eo pipefail
D=${WAA_HOME:-/root}/claude-home
mkdir -p "$D"
chmod 700 "$D"
umask 077
jq '{env: ((.env // {}) | with_entries(select(.key | test("^ANTHROPIC_(BASE_URL|AUTH_TOKEN|API_KEY|CUSTOM_HEADERS)$")))
     + {DISABLE_AUTOUPDATER: "1"})}' ~/.claude/settings.json > "$D/settings.json"
jq -r '"kept: " + (.env | keys | join(" "))' "$D/settings.json"
