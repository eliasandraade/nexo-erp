#!/usr/bin/env bash
# Installs the Chromium of the end-to-end suites on a CI machine. The browser itself comes from Playwright's download, and the
# system libraries normally come with the machine, so apt is only touched when Chromium really does not open. The Ubuntu mirror
# behind apt has hung for 26 minutes (2026-11-05) and then again several times on 2026-11-06.
#
# A time limit around `install-deps` ends npx, not the apt-get it started as root. That apt-get goes on holding the dpkg lock, and
# every later attempt fails at once with "Could not get lock". So each failed attempt also kills what it left behind.
#
# PLAYWRIGHT_FORCE_DEPS=1 goes through the apt path even if Chromium opens, to exercise it.
set -u

opens() {
  node --input-type=module -e "import { chromium } from '@playwright/test'; const browser = await chromium.launch(); await browser.close();"
}

npx playwright install chromium || exit 1

if [ "${PLAYWRIGHT_FORCE_DEPS:-}" != "1" ] && opens; then
  echo "Chromium opens with what this machine already has: apt not needed."
  exit 0
fi

for attempt in 1 2; do
  echo "Installing the system libraries through apt, attempt $attempt"
  if timeout --kill-after=10 240 npx playwright install-deps chromium && opens; then
    exit 0
  fi
  sudo pkill -9 -x apt-get || true
  sudo pkill -9 -x dpkg || true
  sudo dpkg --configure -a || true
done

echo "Chromium could not be made to open: the system libraries did not install." >&2
exit 1
