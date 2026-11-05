#!/usr/bin/env bash
# Moves the `production` branch to a commit of master (docs/engineering/staging-e-promocao.md). Railway deploys production
# from that branch. The real lock is on GitHub: the branch protection refuses a commit without a successful `master-gate`
# and refuses a force push. These checks only give a clear answer before trying.
set -euo pipefail

repo="eliasandraade/nexo-erp"
sha="${1:-}"
if ! [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Usage: scripts/promote-production.sh <full 40-character SHA of a master commit>" >&2
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

gate=$(gh api "repos/$repo/commits/$sha/check-runs?check_name=master-gate&per_page=1" --jq '.check_runs[0].conclusion // "absent"')
if [[ "$gate" != "success" ]]; then
  echo "Refused: master-gate on $sha is '$gate'." >&2
  exit 1
fi

echo "Promoting $(git log -1 --format='%h %s' "$sha")"
echo "Commits entering production:"
git log --oneline "origin/production..$sha"
git push origin "$sha:refs/heads/production"
