"""Prints line coverage per assembly (and per Tax Intelligence namespace) from Microsoft Code Coverage
XML reports. Used by CI to write the job summary; the same reports go to SonarCloud.

usage: python coverage_summary.py <report.xml> [<report.xml> ...]

Several reports (unit + integration) are merged by line: a line counts as covered if any run hit it.
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def merge(paths):
    # (module, source file, line) -> covered?
    lines = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for module in root.iter("module"):
            name = module.get("name", "?")
            files = {f.get("id"): f.get("path", "") for f in module.iter("source_file")}
            for fn in module.iter("function"):
                for r in fn.iter("range"):
                    src = files.get(r.get("source_id"), "")
                    covered = r.get("covered") in ("yes", "partial")
                    for ln in range(int(r.get("start_line", 0)), int(r.get("end_line", 0)) + 1):
                        key = (name, src, ln)
                        lines[key] = lines.get(key, False) or covered
    return lines


def main():
    lines = merge(sys.argv[1:])
    by_module = defaultdict(lambda: [0, 0])
    tax = [0, 0]
    for (module, src, _), covered in lines.items():
        by_module[module][0] += covered
        by_module[module][1] += 1
        if "TaxIntelligence" in src.replace("\\", "/"):
            tax[0] += covered
            tax[1] += 1
    total = [sum(v[0] for v in by_module.values()), sum(v[1] for v in by_module.values())]

    def pct(c, t):
        return f"{100.0 * c / t:5.1f}%" if t else "  n/a"

    print("| Assembly | Lines covered | Coverage |")
    print("|---|---:|---:|")
    for module in sorted(by_module):
        c, t = by_module[module]
        print(f"| {module} | {c}/{t} | {pct(c, t)} |")
    print(f"| **Total** | {total[0]}/{total[1]} | {pct(*total)} |")
    print(f"| Tax Intelligence (all layers) | {tax[0]}/{tax[1]} | {pct(*tax)} |")


if __name__ == "__main__":
    main()
