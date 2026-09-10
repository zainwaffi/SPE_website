# Membership CSV import

Turns an AUSA "Product Purchasers Report" export into a clean membership lookup table
and upserts it into Supabase, at `public.members`. This is a standalone offline tool —
it is not part of the running site, and nothing in the .NET app writes to this table.
The app only *reads* it (see `Data/Models/Member.cs`), for the student-number login path.

## Setup

Same Supabase Postgres the site already uses, just in `postgresql://` URL form rather
than the ADO.NET keyword format .NET expects (the two ecosystems don't share a
connection-string syntax). It lives at `ConnectionStrings:MembersImport` in
`appsettings.json`, at the repo root — a separate key from the app's own
`DefaultConnection`, since the format differs, but the same file:

```bash
pip install pandas psycopg[binary]
```

```json
"ConnectionStrings": {
  "DefaultConnection": "",
  "MembersImport": "postgresql://user:password@host:6543/postgres"
}
```

`appsettings.json` is gitignored — it never gets committed — so the real Postgres URL,
password included, can live there directly. `db_conninfo()` in `MembersList.py` reads
it and percent-encodes special characters in the password for you, so paste the
password in as-is rather than pre-encoding it.

`MembersList.py` itself never contains the secret — only the path to `appsettings.json`
— so it's safe to copy or hand the `.py` file to someone else on its own; they just need
their own `appsettings.json` (or the key pointed at their own copy) alongside it.

## Running it

1. Drop the new export at `Product Purchasers Report` csv file and rename it EXACTLY to `PurchaseReport.csv` in this folder
   (overwriting the previous one).
2. `python MembersList.py`

It always runs as a dry run first (`dry_run=True` at the bottom of the file) — check the
printed upsert/lapse counts look right, then flip that to `False` and run again to
actually commit the change. It refuses to run at all if the new export has fewer than
70% of the rows currently live, to avoid a truncated export locking out the whole
chapter.

`Test.ipynb` is a thin notebook wrapper around the same script, for stepping through a
run interactively — it has no logic of its own.

## What gets ignored

`*.csv` (the raw export and the cleaned `members.csv` — both contain real names and card
numbers) and `__pycache__/` in this folder are gitignored. Don't add exceptions for a
"just this once" export.
