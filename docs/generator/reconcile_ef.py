"""Compares schema.sql (hand-reviewed DDL) with ef-migration.sql (what EF Core actually creates).
Compares tables, columns+nullability and foreign key shapes (table, columns, referenced table, referenced columns). Names and defaults are ignored."""
import re, sys
from pathlib import Path
D = Path(__file__).resolve().parents[1]
def clean(s): return s.replace("[dbo].", "")
def parse(text):
    text = clean(text)
    tables = {}
    for m in re.finditer(r"CREATE TABLE \[(\w+)\] \((.*?)\n\s*\);", text, re.S):
        cols = {}
        for l in m.group(2).splitlines():
            c = re.match(r"\s*\[(\w+)\]\s+[\w\(\),]+(?:\s+IDENTITY(?:\(\d+,\s*\d+\))?)?\s*(NOT NULL|NULL)(?:\s+IDENTITY(?:\(\d+,\s*\d+\))?)?", l)
            if c: cols[c.group(1)] = c.group(2)
        tables[m.group(1)] = cols
    for m in re.finditer(r"ALTER TABLE \[(\w+)\] ADD \[(\w+)\] [\w\(\),]+\s+(NOT NULL|NULL)", text):
        if m.group(1) in tables: tables[m.group(1)][m.group(2)] = m.group(3)
    fks = set()
    for m in re.finditer(r"FOREIGN KEY \(([^)]*)\) REFERENCES \[(\w+)\] \(([^)]*)\)", text):
        # find owning table: nearest preceding CREATE TABLE or ALTER TABLE
        pre = text[:m.start()]
        t = max(re.finditer(r"(?:CREATE TABLE|ALTER TABLE) \[(\w+)\]", pre), key=lambda x: x.start()).group(1)
        f = lambda s: tuple(x.strip(" []") for x in s.split(","))
        fks.add((t, f(m.group(1)), m.group(2), f(m.group(3))))
    cascades = len(re.findall(r"ON DELETE (?:CASCADE|SET NULL|SET DEFAULT)", text))
    return tables, fks, cascades
a_t, a_f, a_c = parse((D / "schema.sql").read_text())
b_t, b_f, b_c = parse((D / "ef-migration.sql").read_text())
b_t.pop("__EFMigrationsHistory", None)
print(f"tables: schema.sql={len(a_t)} ef={len(b_t)}; fks: schema.sql={len(a_f)} ef={len(b_f)}")
bad = 0
for n in sorted(set(a_t) ^ set(b_t)): print("TABLE only in one side:", n); bad += 1
for n in sorted(set(a_t) & set(b_t)):
    for c in sorted(set(a_t[n]) ^ set(b_t[n])): print("COLUMN only in one side:", n, c); bad += 1
    for c in sorted(set(a_t[n]) & set(b_t[n])):
        if a_t[n][c] != b_t[n][c]: print("NULLABILITY differs:", n, c, a_t[n][c], b_t[n][c]); bad += 1
for x in sorted(a_f - b_f): print("FK only in schema.sql:", x); bad += 1
for x in sorted(b_f - a_f): print("FK only in EF:", x); bad += 1
print(f"referential actions other than NO ACTION: schema.sql={a_c} ef={b_c}")
if a_c != b_c: bad += 1
print("DIFFERENCES:", bad)
sys.exit(1 if bad else 0)
