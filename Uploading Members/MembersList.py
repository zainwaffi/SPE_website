"""
Turn a 'Product Purchasers Report' export from AUSA Database into a clean
membership lookup table for the SPE UoA site, and upsert it into Supabase.

Run directly: python MembersList.py
"""
import json
import re
from pathlib import Path
from urllib.parse import quote

import pandas as pd
import psycopg


def tier_from_product(product_name: str) -> str:
    """
    Map a shop product line to a membership tier.

    Order matters: "Non-Student Annual Membership" contains "student", so it has
    to be tested before the plain student tier or every non-student is misfiled.
    (The student product is also misspelled "Memebership" in the export — match
    on the tier word, not the whole product name, so a fixed typo doesn't break
    this.)
    """
    name = str(product_name).casefold()
    if "committee" in name:
        return "Committee"
    if "student" in name:
        return "Student"
    raise ValueError(f"Unrecognised membership product: {product_name!r}")


def clean(raw_path: str) -> pd.DataFrame:
    df = pd.read_csv(raw_path, skiprows=3)

    df["card_number"] = df["card_number"].astype(str).str.strip()
    df["purchaser"] = df["purchaser"].astype(str).str.strip()

    # "SURNAME, Forename" -> "Forename SURNAME"
    parts = df["purchaser"].str.split(",", n=1, expand=True)
    df["surname"] = parts[0].str.strip().str.title()
    df["forename"] = parts[1].fillna("").str.strip().str.title()
    df["full_name"] = (df["forename"] + " " + df["surname"]).str.strip()

    # "Mon 31 Aug 2026 14:08" -> drop the weekday, then parse
    df["purchased_at"] = pd.to_datetime(
        df["purchase_date"].str.split(" ", n=1).str[1],
        format="%d %b %Y %H:%M",
    )

    df["membership_type"] = df["product_name"].apply(tier_from_product)

    # sort by latest purchase.
    df = (
        df.sort_values(["purchased_at"], ascending=False)
        .drop_duplicates(subset=["card_number"])
        .sort_values(["full_name"])
    )

    out = df[
        [
            "card_number",
            "full_name",
            "membership_type",
            "purchased_at",
        ]
    ].copy()

    # Left as a full timestamp, not a display string — the site checks membership
    # age against this value, so it needs day/time precision and to round-trip
    # through Postgres's timestamptz cleanly.
    out["purchased_at"] = out["purchased_at"].dt.strftime("%Y-%m-%d %H:%M:%S")
    return out


# appsettings.json sits at the repo root, one level up from this folder. It's the same
# file the .NET app reads its own ConnectionStrings from, and it's gitignored — never
# committed — so this key can safely hold the real, live Supabase URL rather than
# reading it back out of a shell environment variable.
APPSETTINGS_PATH = Path(__file__).parent.parent / "appsettings.json"


def db_conninfo(config_key: str = "MembersImport") -> str:
    """
    Read a Postgres URL from ConnectionStrings:<config_key> in appsettings.json and
    percent-encode its credentials.

    Supabase passwords routinely contain "@" and "#", which libpq splits on
    before it reaches the host — an unencoded password silently turns the tail
    of itself into the hostname. Split the userinfo off at the *last* "@"
    (a host never contains one) and re-encode both halves.

    Existing %XX escapes are left alone, so a URL that was already encoded
    correctly passes through unchanged. The one case this gets wrong is a
    password containing a literal "%" followed by two hex digits.
    """
    settings = json.loads(APPSETTINGS_PATH.read_text(encoding="utf-8-sig"))
    raw = settings.get("ConnectionStrings", {}).get(config_key)
    if not raw:
        raise ValueError(
            f'ConnectionStrings:{config_key} is missing or empty in {APPSETTINGS_PATH}. '
            f'Add the Supabase connection string there (postgresql://... form) — see README.md.'
        )

    scheme, sep, rest = raw.partition("://")
    if not sep:
        raise ValueError(f"ConnectionStrings:{config_key} is not a postgresql:// URL")

    userinfo, sep, hostpart = rest.rpartition("@")
    if not sep:
        return raw  # no credentials in the URL, nothing to encode

    user, _, password = userinfo.partition(":")
    creds = _encode(user)
    if password:
        creds += ":" + _encode(password)

    return f"{scheme}://{creds}@{hostpart}"


_ESCAPED = re.compile(r"%[0-9A-Fa-f]{2}")


def _encode(part: str) -> str:
    """Percent-encode a userinfo field, leaving valid %XX escapes intact."""
    out, pos = [], 0
    for m in _ESCAPED.finditer(part):
        out.append(quote(part[pos : m.start()], safe=""))
        out.append(m.group())
        pos = m.end()
    out.append(quote(part[pos:], safe=""))
    return "".join(out)


# Lives in `public` so it shows up in the Supabase table editor and is reachable
# through the REST API without adding a schema to the exposed list. RLS is on with
# no policies, which in `public` means deny-all to the anon and authenticated keys —
# only the service role (and the direct Postgres connection below) can read it.
SCHEMA_SQL = """
create table if not exists public.members (
    card_number      text primary key,
    full_name        text not null,
    membership_type  text not null,
    purchased_at     text,
    is_active        boolean not null default true,
    first_seen       timestamptz not null default now(),
    last_seen_import timestamptz not null default now()
);

alter table public.members enable row level security;
"""

MIN_SIZE_RATIO = 0.70


def upload(members: pd.DataFrame, db_url: str, dry_run: bool = False) -> None:
    """
    Merge a cleaned members frame into Supabase.

    The export is a full snapshot, so anyone missing from it is marked
    inactive rather than deleted — the row stays, login just stops
    accepting it. Everything runs in one transaction.
    """
    with psycopg.connect(db_url, autocommit=False) as conn, conn.cursor() as cur:
        cur.execute(SCHEMA_SQL)

        cur.execute("select count(*) from public.members")
        live = cur.fetchone()[0] # type: ignore

        # A short file is a truncated export far more often than it's a mass
        # exodus, and truncate-then-insert on a bad one locks out the chapter.
        if live and len(members) < live * MIN_SIZE_RATIO:
            raise SystemExit(
                f"Refusing to upload: {len(members)} rows against {live} live. "
                f"Looks like a partial export."
            )

        cur.execute(
            """
            create temporary table members_import (
                card_number     text primary key,
                full_name       text not null,
                membership_type text not null,
                purchased_at    text
            ) on commit drop
            """
        )

        cols = ["card_number", "full_name", "membership_type", "purchased_at"]
        with cur.copy(f"copy members_import ({', '.join(cols)}) from stdin") as copy: # type: ignore
            for row in members[cols].itertuples(index=False, name=None):
                copy.write_row(row)

        cur.execute(
            """
            insert into public.members
                (card_number, full_name, membership_type, purchased_at,
                 is_active, last_seen_import)
            select card_number, full_name, membership_type, purchased_at, true, now()
            from members_import
            on conflict (card_number) do update set
                full_name        = excluded.full_name,
                membership_type  = excluded.membership_type,
                purchased_at     = excluded.purchased_at,
                is_active        = true,
                last_seen_import = now()
            """
        )
        upserted = cur.rowcount

        cur.execute(
            """
            update public.members m
               set is_active = false
             where m.is_active
               and not exists (
                   select 1 from members_import i
                   where i.card_number = m.card_number
               )
            """
        )
        lapsed = cur.rowcount

        if dry_run:
            conn.rollback()
            print(f"Dry run: would upsert {upserted}, lapse {lapsed}. Rolled back.")
            return

        conn.commit()
        print(f"Upserted {upserted}, marked {lapsed} inactive.")


if __name__ == "__main__":
    data_dir = Path(__file__).parent
    src = data_dir / "PurchaseReport.csv"
    dst = data_dir / "members.csv"

    members = clean(src) # type: ignore
    members.to_csv(dst, index=False)
    # Dry run first on purpose — flip to False only once the printed upsert/lapse
    # counts look right. See README.md in this folder for appsettings.json setup.
    upload(members, db_conninfo(), dry_run=False)
