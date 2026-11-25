"""Structural test of the tenant-scoped composite foreign keys.

Builds the same keys and foreign keys (from spec_schema.py via gen_schema.fk_plan) in an in-memory SQLite database, then tries
cross-tenant inserts and expects them to fail. This proves the key design, NOT SQL Server behavior. SQLite and SQL Server both
use MATCH SIMPLE semantics for composite FKs with NULLs, but only a run on SQL Server verifies schema.sql itself.
"""
import sqlite3
import sys

import gen_schema as G
import spec_schema as S


def build():
    db = sqlite3.connect(":memory:")
    db.execute("PRAGMA foreign_keys=ON")
    targets = {}
    plans = {}
    for t in S.TABLES:
        for c in G.full_cols(t):
            if c["fk"]:
                kind, cc, pt, pc = G.fk_plan(t, c)
                plans.setdefault(t["name"], []).append((cc, pt, pc))
                if kind == "composite":
                    targets.setdefault(pt, set()).add(tuple(pc))
    for t in S.TABLES:
        cols = G.full_cols(t)
        parts = [f'"{c["name"]}"' for c in cols]
        n = t["name"]
        if n in G.COMPOSITE_PK:
            parts.append(f'PRIMARY KEY ({", ".join(chr(34) + x + chr(34) for x in G.COMPOSITE_PK[n])})')
        else:
            parts.append('PRIMARY KEY ("Id")')
        for pc in sorted(targets.get(n, [])):
            if pc != ("Id",):
                parts.append(f'UNIQUE ({", ".join(chr(34) + x + chr(34) for x in pc)})')
        for cc, pt, pc in plans.get(n, []):
            parts.append(f'FOREIGN KEY ({", ".join(chr(34) + x + chr(34) for x in cc)}) REFERENCES "{pt}" ({", ".join(chr(34) + x + chr(34) for x in pc)})')
        db.execute(f'CREATE TABLE "{n}" ({", ".join(parts)})')
    return db


def ins(db, table, **vals):
    cols = ", ".join(f'"{k}"' for k in vals)
    db.execute(f'INSERT INTO "{table}" ({cols}) VALUES ({", ".join("?" * len(vals))})', list(vals.values()))


def must_fail(db, label, fn):
    try:
        fn()
    except sqlite3.IntegrityError:
        print("blocked  :", label)
        return True
    print("NOT BLOCKED:", label)
    return False


def main():
    db = build()
    ok = True
    ins(db, "Tenants", Id="A")
    ins(db, "Tenants", Id="B")
    ins(db, "AspNetUsers", Id="uA", TenantId="A")
    ins(db, "AspNetUsers", Id="uB", TenantId="B")
    ins(db, "UserTenantMemberships", Id="mA", TenantId="A", UserId="uA")
    ins(db, "UserTenantMemberships", Id="mB", TenantId="B", UserId="uB")
    ins(db, "Courses", Id="cA", TenantId="A", OwnerInstructorId="uA")
    ins(db, "Courses", Id="cB", TenantId="B", OwnerInstructorId="uB")
    ins(db, "Enrollments", Id="e1", TenantId="A", UserId="uA", CourseId="cA")
    print("allowed  : same-tenant enrollment")
    ok &= must_fail(db, "tenant A enrollment of tenant B course", lambda: ins(db, "Enrollments", Id="e2", TenantId="A", UserId="uA", CourseId="cB"))
    ok &= must_fail(db, "tenant A enrollment of a user who is a member of B only", lambda: ins(db, "Enrollments", Id="e3", TenantId="A", UserId="uB", CourseId="cA"))
    ok &= must_fail(db, "tenant A course owned by instructor of tenant B", lambda: ins(db, "Courses", Id="c3", TenantId="A", OwnerInstructorId="uB"))
    # SuperAdmin acting in tenant A needs an explicit membership row
    ins(db, "AspNetUsers", Id="sa", TenantId=None)
    ok &= must_fail(db, "SuperAdmin without SupportAccess membership", lambda: ins(db, "Enrollments", Id="e4", TenantId="A", UserId="sa", CourseId="cA"))
    ins(db, "UserTenantMemberships", Id="mSA", TenantId="A", UserId="sa", Kind="SupportAccess")
    ins(db, "Enrollments", Id="e5", TenantId="A", UserId="sa", CourseId="cA")
    print("allowed  : SuperAdmin with explicit SupportAccess membership")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
