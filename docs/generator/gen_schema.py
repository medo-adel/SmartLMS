"""Generates docs/schema.sql, docs/ERD.md and docs/DATA_DICTIONARY.md from spec_schema.py."""
import re
import sys
from collections import defaultdict
from pathlib import Path

import spec_schema as S

OUT = Path(__file__).resolve().parent.parent
TABLES = {t["name"]: t for t in S.TABLES}

COMPOSITE_PK = {"AspNetUserRoles": ["UserId", "RoleId"], "AspNetUserLogins": ["LoginProvider", "ProviderKey"], "AspNetUserTokens": ["UserId", "LoginProvider", "Name"]}
INT_IDENTITY_PK = {"AspNetUserClaims", "AspNetRoleClaims"}

CHECKS = {
    "Submissions": ["(([UserId] IS NOT NULL AND [AssignmentGroupId] IS NULL) OR ([UserId] IS NULL AND [AssignmentGroupId] IS NOT NULL))"],
    "DiscussionVotes": ["(([ThreadId] IS NOT NULL AND [PostId] IS NULL) OR ([ThreadId] IS NULL AND [PostId] IS NOT NULL))"],
    "Certificates": ["(([CourseId] IS NOT NULL AND [LearningPathId] IS NULL) OR ([CourseId] IS NULL AND [LearningPathId] IS NOT NULL))"],
    "CartItems": ["((CASE WHEN [CourseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LessonId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BundleId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PlanId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LearningPathId] IS NULL THEN 0 ELSE 1 END) = 1)"],
    "OrderItems": ["((CASE WHEN [CourseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LessonId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BundleId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PlanId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LearningPathId] IS NULL THEN 0 ELSE 1 END) = CASE WHEN [ItemType] = 'WalletTopUp' THEN 0 ELSE 1 END)"],
    "Questions": [],
    "Wallets": ["([Balance] >= 0)"],
    "Coupons": ["([Value] > 0)"],
    "ExamQuestionRules": ["(([QuestionId] IS NOT NULL AND [QuestionBankId] IS NULL) OR ([QuestionId] IS NULL AND [QuestionBankId] IS NOT NULL AND [PickCount] > 0))"],
    "Tenants": ["([MaxMobileDevices] >= 0 AND [MaxWebDevices] >= 0)"],
}
EXTRA_SQL = {
    "Submissions": [
        "CREATE UNIQUE INDEX [UX_Submissions_Assignment_User_Version] ON [dbo].[Submissions] ([AssignmentId],[UserId],[Version]) WHERE [UserId] IS NOT NULL;",
        "CREATE UNIQUE INDEX [UX_Submissions_Assignment_Group_Version] ON [dbo].[Submissions] ([AssignmentId],[AssignmentGroupId],[Version]) WHERE [AssignmentGroupId] IS NOT NULL;",
    ],
    "DiscussionVotes": [
        "CREATE UNIQUE INDEX [UX_DiscussionVotes_User_Thread] ON [dbo].[DiscussionVotes] ([UserId],[ThreadId]) WHERE [ThreadId] IS NOT NULL;",
        "CREATE UNIQUE INDEX [UX_DiscussionVotes_User_Post] ON [dbo].[DiscussionVotes] ([UserId],[PostId]) WHERE [PostId] IS NOT NULL;",
    ],
}


def parse_col(spec):
    tok = spec.split()
    name, typ = tok[0], tok[1]
    c = dict(name=name, nullable=typ.endswith("?"), unique=False, fk=None, default=None, enum=None, plain=False)
    typ = typ.rstrip("?")
    for t in tok[2:]:
        if t == "!":
            c["unique"] = True
        elif t.startswith(">>"):
            c["fk"] = t[2:]
            c["plain"] = True
        elif t.startswith(">"):
            c["fk"] = t[1:]
        elif t.startswith("="):
            c["default"] = t[1:]
    # "Name type? !" style: allow '!' glued to type
    if typ.endswith("!"):
        c["unique"] = True
        typ = typ.rstrip("!")
    if typ.endswith("?"):
        c["nullable"] = True
        typ = typ.rstrip("?")
    c["type"] = typ
    if typ.startswith("e:"):
        c["enum"] = typ[2:].split("|")
        c["sql"] = "nvarchar(40)"
    elif typ.startswith("s:"):
        c["sql"] = f"nvarchar({typ[2:]})"
    else:
        c["sql"] = {"g": "uniqueidentifier", "t": "nvarchar(max)", "i": "int", "bi": "bigint", "b": "bit", "m": "decimal(18,2)",
                    "d": "datetime2(7)", "o": "datetimeoffset(7)", "dt": "date", "f": "float", "j": "nvarchar(max)", "rv": "rowversion"}[typ]
    return c


def full_cols(t):
    cols = []
    fl = t["flags"]
    for s in t["cols"]:
        # glued unique marker handling: "Slug s:100!" -> type token has '!'
        cols.append(parse_col(s))
    pre = []
    if t["name"] in COMPOSITE_PK:
        pass
    elif t["name"] in INT_IDENTITY_PK:
        pre.append(dict(name="Id", type="i", sql="int", nullable=False, unique=False, fk=None, default=None, enum=None, identity=True, pk=True))
    else:
        pre.append(dict(name="Id", type="g", sql="uniqueidentifier", nullable=False, unique=False, fk=None, default="NEWSEQUENTIALID()", enum=None, pk=True))
    if "T" in fl or "N" in fl:
        pre.append(dict(name="TenantId", type="g", sql="uniqueidentifier", nullable="N" in fl, unique=False, fk="Tenants", default=None, enum=None))
    post = []
    if "A" in fl:
        post += [
            dict(name="CreatedAtUtc", type="d", sql="datetime2(7)", nullable=False, unique=False, fk=None, default="SYSUTCDATETIME()", enum=None),
            dict(name="CreatedBy", type="g", sql="uniqueidentifier", nullable=True, unique=False, fk=None, default=None, enum=None),
            dict(name="UpdatedAtUtc", type="d", sql="datetime2(7)", nullable=True, unique=False, fk=None, default=None, enum=None),
            dict(name="UpdatedBy", type="g", sql="uniqueidentifier", nullable=True, unique=False, fk=None, default=None, enum=None),
        ]
    if "S" in fl:
        post += [
            dict(name="IsDeleted", type="b", sql="bit", nullable=False, unique=False, fk=None, default="0", enum=None),
            dict(name="DeletedAtUtc", type="d", sql="datetime2(7)", nullable=True, unique=False, fk=None, default=None, enum=None),
            dict(name="DeletedBy", type="g", sql="uniqueidentifier", nullable=True, unique=False, fk=None, default=None, enum=None),
        ]
    return pre + cols + post


def default_sql(c):
    d = c["default"]
    if d is None:
        return ""
    if c["enum"]:
        d = "N'" + d.strip("'") + "'"
    elif d.startswith("'"):
        d = "N" + d
    return f" DEFAULT {d}"


def q(n):
    return f"[{n}]"


def fk_plan(t, c):
    """Return (kind, child_cols, parent_table, parent_cols). kind: plain | composite."""
    flags = t["flags"]
    p = c["fk"]
    if c["name"] == "TenantId" or c.get("plain"):
        return "plain", [c["name"]], p, ["Id"]
    if "T" in flags:
        if p == "AspNetUsers":
            return "composite", ["TenantId", c["name"]], "UserTenantMemberships", ["TenantId", "UserId"]
        if p == "UserDevices":
            return "composite", ["UserId", c["name"]], "UserDevices", ["UserId", "Id"]
        if "T" in TABLES[p]["flags"]:
            return "composite", ["TenantId", c["name"]], p, ["TenantId", "Id"]
        raise SystemExit(f"unhandled tenant FK {t['name']}.{c['name']} -> {p}")
    return "plain", [c["name"]], p, ["Id"]


def validate():
    errs = []
    for t in S.TABLES:
        names = [c["name"] for c in full_cols(t)]
        if len(set(names)) != len(names):
            errs.append(f"duplicate columns in {t['name']}")
        for c in full_cols(t):
            if c["fk"] and c["fk"] not in TABLES:
                errs.append(f"{t['name']}.{c['name']} -> unknown table {c['fk']}")
        for u in t["uniq"] + t["idx"]:
            for n in u:
                if n not in names:
                    errs.append(f"{t['name']} index references unknown column {n}")
    if len(TABLES) != len(S.TABLES):
        errs.append("duplicate table names")
    return errs


def gen_sql():
    out = []
    w = out.append
    w("-- SmartLMS reference schema (SQL Server). GENERATED by docs/generator/gen_schema.py. Do not edit by hand.")
    w("-- Purpose: design review and DBA reading. The authoritative schema is produced by EF Core migrations in Phase 2;")
    w("-- these two must be reconciled (names, types, indexes) when the first migration is generated.")
    w("-- Hangfire creates its own [HangFire] schema at first start; it is not listed here.")
    w("-- Foreign keys use NO ACTION. Deletion is soft (IsDeleted) for most business tables, which also avoids cascade-path conflicts.")
    w("")
    w("IF DB_ID(N'SmartLMSDb') IS NULL CREATE DATABASE [SmartLMSDb];")
    w("GO")
    w("USE [SmartLMSDb];")
    w("GO")
    w("SET QUOTED_IDENTIFIER ON;")
    w("GO")
    w("")
    fks, idxs, targets = [], [], set()
    for t in S.TABLES:
        cols = full_cols(t)
        soft = "S" in t["flags"]
        lines = []
        for c in cols:
            nn = "NULL" if c["nullable"] else "NOT NULL"
            if c["type"] == "rv":
                nn = "NOT NULL"
            ident = " IDENTITY(1,1)" if c.get("identity") else ""
            lines.append(f"    {q(c['name'])} {c['sql']}{ident} {nn}{default_sql(c)}")
        n = t["name"]
        if n in COMPOSITE_PK:
            lines.append(f"    CONSTRAINT [PK_{n}] PRIMARY KEY ({', '.join(q(x) for x in COMPOSITE_PK[n])})")
        else:
            lines.append(f"    CONSTRAINT [PK_{n}] PRIMARY KEY ([Id])")
        for c in cols:
            if c["enum"]:
                vals = ",".join(f"N'{v}'" for v in c["enum"])
                lines.append(f"    CONSTRAINT [CK_{n}_{c['name']}] CHECK ({q(c['name'])} IN ({vals}))")
            if c["type"] == "j":
                lines.append(f"    CONSTRAINT [CK_{n}_{c['name']}_Json] CHECK (ISJSON({q(c['name'])}) = 1)")
        for i, ck in enumerate(CHECKS.get(n, [])):
            lines.append(f"    CONSTRAINT [CK_{n}_Rule{i + 1}] CHECK {ck}")
        w(f"CREATE TABLE [dbo].[{n}] (")
        w(",\n".join(lines))
        w(");")
        w("GO")
        # FKs
        for c in cols:
            if c["fk"]:
                kind, ccols, ptab, pcols = fk_plan(t, c)
                fks.append(f"ALTER TABLE [dbo].[{n}] ADD CONSTRAINT [FK_{n}_{c['name']}] FOREIGN KEY ({', '.join(q(x) for x in ccols)}) REFERENCES [dbo].[{ptab}] ({', '.join(q(x) for x in pcols)});")
                if kind == "composite":
                    targets.add((ptab, tuple(pcols)))
        # indexes
        covered = set()
        uniq_defs = []
        for c in cols:
            if c["unique"]:
                uniq_defs.append(([c["name"]], c["nullable"]))
        for u in t["uniq"]:
            uniq_defs.append((list(u), any(next(cc for cc in cols if cc["name"] == x)["nullable"] for x in u) and False))
        for u, nullable in uniq_defs:
            where = []
            if len(u) == 1 and next(cc for cc in cols if cc["name"] == u[0])["nullable"]:
                where.append(f"{q(u[0])} IS NOT NULL")
            if soft:
                where.append("[IsDeleted] = 0")
            w_sql = f" WHERE {' AND '.join(where)}" if where else ""
            idxs.append(f"CREATE UNIQUE INDEX [UX_{n}_{'_'.join(u)}] ON [dbo].[{n}] ({', '.join(q(x) for x in u)}){w_sql};")
            covered.add(u[0])
        for u in t["idx"]:
            idxs.append(f"CREATE INDEX [IX_{n}_{'_'.join(u)}] ON [dbo].[{n}] ({', '.join(q(x) for x in u)});")
            covered.add(u[0])
        for c in cols:
            if c["fk"] and fk_plan(t, c)[0] == "composite" and c["name"] not in covered and not c["unique"]:
                idxs.append(f"CREATE INDEX [IX_{n}_{c['name']}_{'_'.join(x for x in fk_plan(t, c)[1] if x != c['name'])}] ON [dbo].[{n}] ({', '.join(q(x) for x in [c['name']] + [y for y in fk_plan(t, c)[1] if y != c['name']])});")
                covered.add(c["name"])
                continue
            if (c["fk"] or c["name"] == "TenantId") and c["name"] not in covered and not c["unique"]:
                if n in COMPOSITE_PK and c["name"] == COMPOSITE_PK[n][0]:
                    continue
                idxs.append(f"CREATE INDEX [IX_{n}_{c['name']}] ON [dbo].[{n}] ({q(c['name'])});")
        idxs.extend(EXTRA_SQL.get(n, []))
    w("")
    w("-- Alternate keys that tenant-scoped (composite) foreign keys point at")
    uqs = [f"ALTER TABLE [dbo].[{pt}] ADD CONSTRAINT [UQ_{pt}_{'_'.join(pc)}] UNIQUE ({', '.join(q(x) for x in pc)});" for pt, pc in sorted(targets)]
    out.extend(uqs)
    w("GO")
    w("")
    w("-- Foreign keys. Composite (TenantId, X) keys make cross-tenant references impossible at the database level.")
    out.extend(fks)
    w("GO")
    w("")
    w("-- Indexes (foreign keys, TenantId, search and uniqueness)")
    out.extend(idxs)
    w("GO")
    return "\n".join(out) + "\n", len(fks), len(idxs)


def gen_dictionary():
    out = ["# Data dictionary", "", "Generated from `docs/generator/spec_schema.py`. Columns shown include the standard ones each table gets from its flags.", "",
           "Flags: **T** tenant-owned (TenantId, global query filter), **N** nullable TenantId, **A** auditable, **S** soft-deletable.", ""]
    groups = defaultdict(list)
    for t in S.TABLES:
        groups[t["group"]].append(t)
    for g, ts in groups.items():
        out.append(f"## {g}")
        out.append("")
        for t in ts:
            out.append(f"### {t['name']}")
            out.append("")
            out.append(f"Flags: `{t['flags'] or '-'}`" + (f". {t['note']}" if t["note"] else ""))
            out.append("")
            out.append("| Column | Type | Null | Key / rule |")
            out.append("|---|---|---|---|")
            for c in full_cols(t):
                rule = []
                if c.get("pk"):
                    rule.append("PK")
                if c["fk"]:
                    kd, cc, pt, pc = fk_plan(t, c)
                    rule.append(f"FK ({', '.join(cc)}) -> {pt}({', '.join(pc)})" + (" tenant-scoped" if kd == "composite" else ""))
                if c["unique"]:
                    rule.append("unique")
                if c["enum"]:
                    rule.append("one of: " + ", ".join(c["enum"]))
                if c["default"] is not None:
                    rule.append(f"default {c['default']}")
                out.append(f"| {c['name']} | {c['sql']} | {'yes' if c['nullable'] else 'no'} | {'; '.join(rule)} |")
            extras = [f"unique ({', '.join(u)})" for u in t["uniq"]] + [f"index ({', '.join(u)})" for u in t["idx"]]
            if extras:
                out.append("")
                out.append("Indexes: " + "; ".join(extras) + ".")
            out.append("")
    return "\n".join(out) + "\n"


ERD_SPLIT = {
    "Bundles": "Commerce: pricing and cart", "BundleCourses": "Commerce: pricing and cart", "SubscriptionPlans": "Commerce: pricing and cart",
    "SubscriptionPlanCourses": "Commerce: pricing and cart", "UserSubscriptions": "Commerce: pricing and cart", "CartItems": "Commerce: pricing and cart",
    "Coupons": "Commerce: pricing and cart",
    "Orders": "Commerce: orders and payments", "OrderItems": "Commerce: orders and payments", "CouponRedemptions": "Commerce: orders and payments",
    "Payments": "Commerce: orders and payments", "ManualPaymentSubmissions": "Commerce: orders and payments", "Invoices": "Commerce: orders and payments",
}
ERD_SPLIT_DEFAULT = "Commerce: vouchers, wallet and affiliates"


def erd_group(t):
    if t["group"] != "Commerce":
        return t["group"]
    return ERD_SPLIT.get(t["name"], ERD_SPLIT_DEFAULT)


def gen_erd():
    groups = defaultdict(list)
    for t in S.TABLES:
        groups[erd_group(t)].append(t)
    out = ["# Entity relationship diagrams", "",
           "Generated from `docs/generator/spec_schema.py`. One diagram per area. Tables from another area that are referenced here appear as stubs (key only).", "",
           "Every tenant-owned table also has `TenantId -> Tenants.Id`, left out of the drawings. Relationships labelled `(tenant-scoped)` are composite foreign keys `(TenantId, X)`, so the database rejects a reference to a row of another tenant. References to users go to `UserTenantMemberships` (a user is a member of a tenant), not directly to the global `AspNetUsers`.", "",
           "Legend: `||--o{` one to many (required), `|o--o{` one to many (optional FK). Column lists show keys and unique columns only; see DATA_DICTIONARY.md for all columns.", ""]
    for g, ts in groups.items():
        names = {t["name"] for t in ts}
        rels, stubs = [], set()
        body = []
        for t in ts:
            attrs = []
            for c in full_cols(t):
                if c["name"] == "TenantId":
                    continue
                keys = []
                if c.get("pk"):
                    keys.append("PK")
                if c["fk"]:
                    keys.append("FK")
                if c["unique"]:
                    keys.append("UK")
                if keys:
                    ty = c["sql"].split("(")[0]
                    attrs.append(f"        {ty} {c['name']} {','.join(keys)}")
                if c["fk"] and c["name"] != "TenantId":
                    kd, cc, pt, pc = fk_plan(t, c)
                    if pt not in names:
                        stubs.add(pt)
                    card = "|o--o{" if c["nullable"] else "||--o{"
                    lab = c["name"] + (" (tenant-scoped)" if kd == "composite" else "")
                    rels.append(f"    {pt} {card} {t['name']} : \"{lab}\"")
            body.append(f"    {t['name']} {{\n" + "\n".join(attrs) + "\n    }")
        for s in sorted(stubs):
            body.append(f"    {s} {{\n        uniqueidentifier Id PK\n    }}")
        out.append(f"## {g}")
        out.append("")
        notes = [f"- **{t['name']}**: {t['note']}" for t in ts if t["note"]]
        out.append("```mermaid")
        out.append("erDiagram")
        out.extend(body)
        out.extend(sorted(set(rels)))
        out.append("```")
        out.append("")
        out.extend(notes)
        out.append("")
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    errs = validate()
    if errs:
        print("\n".join(errs))
        sys.exit(1)
    sql, nfk, nidx = gen_sql()
    (OUT / "schema.sql").write_text(sql)
    (OUT / "DATA_DICTIONARY.md").write_text(gen_dictionary())
    (OUT / "ERD.md").write_text(gen_erd())
    print(f"tables={len(S.TABLES)} fks={nfk} indexes={nidx}")


# ---------------------------------------------------------------- audit of SQL Server rules a parser cannot check
def _bytes(c):
    s = c["sql"]
    if s.startswith("nvarchar("):
        return 2 * int(s[9:-1]) if s[9:-1] != "max" else 99999
    return {"uniqueidentifier": 16, "int": 4, "bigint": 8, "bit": 1, "decimal(18,2)": 9, "datetime2(7)": 8, "datetimeoffset(7)": 10, "date": 3, "float": 8}.get(s, 8)


def audit():
    problems, notes = [], []
    sql, _, _ = gen_sql()
    if re.search(r"ON DELETE CASCADE|ON UPDATE CASCADE|SET NULL", sql, re.I):
        problems.append("cascade action present")
    notes.append("Cascade paths: no FK declares ON DELETE/UPDATE CASCADE or SET NULL, so SQL Server error 1785 (multiple cascade paths) cannot occur.")
    names = re.findall(r"CONSTRAINT \[([^\]]+)\]|ALTER TABLE \[dbo\]\.\[\w+\] ADD CONSTRAINT \[([^\]]+)\]", sql)
    flat = [a or b for a, b in names]
    dup = {x for x in flat if flat.count(x) > 1}
    if dup:
        problems.append(f"duplicate constraint names {sorted(dup)[:5]}")
    long_ = [x for x in flat if len(x) > 128]
    if long_:
        problems.append(f"names over 128 chars {long_[:3]}")
    idx_names = re.findall(r"CREATE (?:UNIQUE )?INDEX \[([^\]]+)\] ON \[dbo\]\.\[(\w+)\]", sql)
    pair_dup = {p for p in idx_names if idx_names.count(p) > 1}
    if pair_dup:
        problems.append(f"duplicate index names {sorted(pair_dup)[:3]}")
    for n, _t in idx_names:
        if len(n) > 128:
            problems.append(f"index name too long {n}")
    # index key sizes: clustered PK 900 bytes, nonclustered 1700 bytes
    for t in S.TABLES:
        cols = {c["name"]: c for c in full_cols(t)}
        for u in t["uniq"] + t["idx"] + [[c["name"]] for c in cols.values() if c["unique"]]:
            size = sum(_bytes(cols[x]) for x in u)
            if size > 1700:
                problems.append(f"{t['name']} index {u} key {size} bytes > 1700")
    notes.append("Index key sizes: every index key is at most 1700 bytes (nonclustered limit); PKs are 16-byte GUIDs (clustered limit 900).")
    for t in S.TABLES:
        for c in full_cols(t):
            if c["enum"] and max(len(v) for v in c["enum"]) > 40:
                problems.append(f"enum too long {t['name']}.{c['name']}")
    # residual relationships the database does NOT scope by tenant (plain FKs between tenant-aware tables)
    tenant_fks = []
    for t in S.TABLES:
        if not ("T" in t["flags"] or "N" in t["flags"]):
            continue
        for c in full_cols(t):
            if c["fk"] and c["name"] != "TenantId":
                kd, _cc, pt, _pc = fk_plan(t, c)
                p = TABLES[pt]
                if kd == "plain" and ("T" in p["flags"] or "N" in p["flags"]):
                    tenant_fks.append((t["name"], c["name"], c["fk"], c["nullable"]))
    # every FK from a T table must be composite unless it is an explicit global-identity reference
    composite = sum(1 for t in S.TABLES if "T" in t["flags"] for c in full_cols(t) if c["fk"] and c["name"] != "TenantId" and fk_plan(t, c)[0] == "composite")
    notes.append(f"Tenant scoping: {composite} foreign keys are composite (TenantId, X), so the database rejects cross-tenant references. Residual plain relationships: {len(tenant_fks)} (listed in tenant_consistency_check.sql).")
    sqlt = sql
    for pt, pc in {(fk_plan(t, c)[2], tuple(fk_plan(t, c)[3])) for t in S.TABLES for c in full_cols(t) if c["fk"] and fk_plan(t, c)[0] == "composite"}:
        if f"[UQ_{pt}_{'_'.join(pc)}]" not in sqlt:
            problems.append(f"missing alternate key for composite FK target {pt}{pc}")
    return problems, notes, tenant_fks


def gen_tenant_check(tenant_fks):
    out = ["-- Tenant consistency check. GENERATED by docs/generator/gen_schema.py.",
           "-- Tenant-owned rows reference each other through composite (TenantId, X) foreign keys, so the database blocks cross-tenant references.",
           "-- This script covers only the RESIDUAL plain foreign keys: rows with a nullable TenantId (platform-level, e.g. AuditLogs, RefreshTokens,",
           "-- UserDevices) that point at tenant-owned rows. Those are not scoped by the database. Every query must return 0 rows.",
           "SET NOCOUNT ON;", ""]
    for ch, col, par, _nullable in tenant_fks:
        out.append(f"SELECT '{ch}.{col} -> {par}' AS [Relationship], COUNT(*) AS [Mismatches] FROM [dbo].[{ch}] c JOIN [dbo].[{par}] p ON p.[Id] = c.[{col}] WHERE c.[TenantId] IS NOT NULL AND p.[TenantId] IS NOT NULL AND c.[TenantId] <> p.[TenantId] HAVING COUNT(*) > 0;")
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    problems, notes, tfks = audit()
    (OUT / "tenant_consistency_check.sql").write_text(gen_tenant_check(tfks))
    print("audit problems:", problems)
    print("\n".join(notes))
    print("tenant-to-tenant FKs:", len(tfks))
