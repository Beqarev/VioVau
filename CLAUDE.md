@AGENTS.md

## Claude Code extras
- Slash commands in `.claude/commands/`: `/playtest`, `/release-build`, `/new-card`, `/bake-character`, `/review-changes`.
- Hooks (`.claude/settings.json`): when you finish a turn after editing C#, `tools/check.sh` runs and blocks on a broken build or failing tests. The git pre-commit hook (`tools/hooks/pre-commit`) does the same for every commit.
- For big searches or reviews, use a subagent so file dumps stay out of the main context.
