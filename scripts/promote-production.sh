#!/usr/bin/env bash
# Moves the `production` branch to a commit of master (docs/engineering/staging-e-promocao.md). Railway deploys production
# from that branch. The lock on GitHub refuses a commit without a successful `master-gate` and refuses a force push; it does
# not know WHICH workflow produced the check, nor whether the workflows themselves changed. This script answers both before
# trying: the gate must come from the master workflow, run by the push of this very commit to master, and a change to what
# runs the gate must have been looked at by whoever promotes.
#
#   scripts/promote-production.sh <sha>                          promote
#   scripts/promote-production.sh <sha> --check                  every verification, no push
#   scripts/promote-production.sh <sha> --ci-changes-reviewed    promote although the workflows or this script changed
set -euo pipefail

repo="eliasandraade/nexo-erp"
gate_name="master-gate"
gate_workflow=".github/workflows/master.yml"
actions_app=15368                                   # GitHub Actions
guarded_paths=(.github scripts/promote-production.sh global.json)

sha="${1:-}"
check_only=false
ci_reviewed=false
for arg in "${@:2}"; do
  case "$arg" in
    --check) check_only=true ;;
    --ci-changes-reviewed) ci_reviewed=true ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done
if ! [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Usage: scripts/promote-production.sh <full 40-character SHA of a master commit> [--check] [--ci-changes-reviewed]" >&2
  exit 2
fi

git fetch --quiet origin master production

if ! git merge-base --is-ancestor "$sha" origin/master; then
  echo "Refused: $sha is not on master." >&2
  exit 1
fi
if ! git merge-base --is-ancestor origin/production "$sha"; then
  echo "Refused: $sha is not ahead of production ($(git rev-parse --short origin/production)). To go back, see the rollback section of the document." >&2
  exit 1
fi

# The run that counts: the master workflow, started by the push of this commit to master, and green. A job called
# `master-gate` in any other workflow, event or branch is not it.
suite=$(gh api "repos/$repo/actions/runs?head_sha=$sha&event=push&branch=master&per_page=100" \
  --jq "[.workflow_runs[] | select(.path == \"$gate_workflow\" and .head_sha == \"$sha\" and .event == \"push\" and .head_branch == \"master\" and .conclusion == \"success\")] | sort_by(.run_started_at) | last | .check_suite_id // empty")
if [[ -z "$suite" ]]; then
  echo "Refused: no successful run of $gate_workflow for the push of $sha to master." >&2
  exit 1
fi
gate=$(gh api "repos/$repo/commits/$sha/check-runs?check_name=$gate_name&per_page=100" \
  --jq "[.check_runs[] | select(.app.id == $actions_app and .check_suite.id == $suite)] | last | .conclusion // \"absent\"")
if [[ "$gate" != "success" ]]; then
  echo "Refused: $gate_name of the master workflow on $sha is '$gate'." >&2
  exit 1
fi

# What runs the gate is part of what is being promoted. A change there is shown, and promoting it is a decision of its own.
ci_changes=$(git diff --name-status origin/production "$sha" -- "${guarded_paths[@]}")
if [[ -n "$ci_changes" ]]; then
  echo "Workflows or promotion files changed between production and $sha:"
  echo "$ci_changes" | sed 's/^/  /'
  echo "Read them with: git diff origin/production $sha -- ${guarded_paths[*]}"
  if ! $ci_reviewed; then
    echo "Refused: run again with --ci-changes-reviewed after reading those changes." >&2
    exit 1
  fi
fi

echo "Promoting $(git log -1 --format='%h %s' "$sha")"
echo "Commits entering production:"
git log --oneline "origin/production..$sha"
if $check_only; then
  echo "Check only: every verification passed, nothing was pushed."
  exit 0
fi
git push origin "$sha:refs/heads/production"
