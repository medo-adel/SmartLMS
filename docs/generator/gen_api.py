"""Generates docs/API_CONTRACT.md and docs/api-contract.json from spec_api.py."""
import json
from pathlib import Path

import spec_api as A

OUT = Path(__file__).resolve().parent.parent
ROLE_OK = {"Public", "Auth", "SA", "TA", "IN", "TAS", "ST", "PA"}


def main():
    seen = set()
    total = 0
    by_phase = {}
    md = ["# API contract", "",
          "Generated from `docs/generator/spec_api.py`. This is the agreed contract. Only `GET /health` exists in Phase 1; the **Phase** column says when each endpoint is built.", ""]
    md.append(A.CONVENTIONS.strip())
    md.append("")
    for m in A.MODULES:
        md.append(f"## {m['title']}")
        md.append("")
        md.append(m["intro"])
        md.append("")
        md.append("| Method | Path | Roles | Purpose | Phase |")
        md.append("|---|---|---|---|---|")
        for method, path, roles, summary, phase in m["rows"]:
            for r in roles.split(","):
                assert r in A.__dict__.get("ROLE_OK", ROLE_OK), (path, r)
            key = (method, path)
            assert key not in seen, key
            seen.add(key)
            total += 1
            by_phase[phase] = by_phase.get(phase, 0) + 1
            md.append(f"| {method} | `{path}` | {roles} | {summary} | {phase} |")
        md.append("")
    md.append(A.AI_SERVICE.strip())
    md.append("")
    md.append("## Endpoint count by phase")
    md.append("")
    md.append("| Phase | Endpoints |")
    md.append("|---|---|")
    for p in sorted(by_phase):
        md.append(f"| {p} | {by_phase[p]} |")
    md.append(f"| Total | {total} |")
    md.append("")
    (OUT / "API_CONTRACT.md").write_text("\n".join(md))
    js = [dict(module=m["title"], method=r[0], path="/api/v1" + r[1] if r[0] != "WS" else r[1], roles=r[2].split(","), summary=r[3], phase=r[4])
          for m in A.MODULES for r in m["rows"]]
    (OUT / "api-contract.json").write_text(json.dumps(js, indent=2))
    print("endpoints", total, by_phase)


main()
