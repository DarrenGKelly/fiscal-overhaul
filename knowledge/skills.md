---
type: Convention
title: Skills
description: The project's Claude Code skills and agents — which exist and what each is for, which come from the user's global skills, how to write one, and the checks they must pass.
resource:
  - .claude/skills
  - tools/check-skills.py
---

# Skills

## What exists

| Skill | Where | For |
|---|---|---|
| [/build](../.claude/skills/build/SKILL.md) | project | Build and deploy the mod, or the compile check |
| [/playtest](../.claude/skills/playtest/SKILL.md) | project | Deploy, launch the game, open the Economy panel, screenshot and judge it (computer use) |
| [/plan-feature](../.claude/skills/plan-feature/SKILL.md) | project | A feature issue into a reviewed plan and spelled-out slice issues |
| [/promote](../.claude/skills/promote/SKILL.md) | project | The `autodev` → `master` promotion PR and the sync back |
| `/autodev`, `/orchestrate` | global (`~/.claude/skills/`) | One autonomous task, or a batch of briefs. Generic: they read this project's specifics from [git-workflow.md](git-workflow.md), [testing.md](testing.md), [orchestration.md](orchestration.md) and [issues.md](issues.md). Don't copy them into the project |

The agents a batch spawns (`batch-child`, `researcher`, `test-agent`) are in `.claude/agents/`
([orchestration.md § Agent types](orchestration.md#agent-types)).

## Writing one

- A skill is `.claude/skills/<name>/SKILL.md` with `name` (= the folder) and `description` frontmatter. The description
  is 100–450 characters, says what the skill does and when to use it, and names `/<name>`: it is what loads every
  session, so it is the trigger.
- The body is the procedure, with the mistakes that cost the most up front. Link the knowledge doc for reference
  detail rather than copying it: a copied rule drifts ([knowledge-format.md](knowledge-format.md#indexes-are-generated)).
- Name scripts as `./tools/<name>.ps1 -Flag` or `python tools/<name>.py`: the checker verifies each exists, and each
  PowerShell flag is a real parameter.
- An agent is `.claude/agents/<name>.md` with `name`, `description` and a `model` (`sonnet`, `opus`, `haiku`, `fable`
  or `inherit`); without a model it runs on the spawning session's.

## Checks

`python tools/check-skills.py` checks every skill and agent: the frontmatter parses, `name` matches, the description's
length and `/<name>`, links resolve, the size budget (25 KB a skill), and every `tools/` script and flag named exists.
`python tools/check-doc-links.py` checks their links too. The pre-commit hook runs both when a skill or agent is staged.
