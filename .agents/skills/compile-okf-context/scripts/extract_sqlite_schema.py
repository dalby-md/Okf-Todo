#!/usr/bin/env python3
"""Extract deterministic SQLite structure without reading application rows."""

from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path


def quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def rows(connection: sqlite3.Connection, statement: str) -> list[dict[str, object]]:
    return [dict(row) for row in connection.execute(statement).fetchall()]


def extract(database: Path) -> dict[str, object]:
    uri = database.resolve().as_uri() + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    connection.row_factory = sqlite3.Row
    try:
        objects = rows(
            connection,
            """
            SELECT type, name, tbl_name, sql
            FROM sqlite_schema
            WHERE name NOT LIKE 'sqlite_%'
            ORDER BY type, name
            """,
        )
        tables = []
        for item in (entry for entry in objects if entry["type"] == "table"):
            name = str(item["name"])
            quoted = quote_identifier(name)
            indexes = rows(connection, f"PRAGMA index_list({quoted})")
            for index in indexes:
                index_name = quote_identifier(str(index["name"]))
                index["columns"] = rows(connection, f"PRAGMA index_info({index_name})")
                sql_row = connection.execute(
                    "SELECT sql FROM sqlite_schema WHERE type = 'index' AND name = ?", (index["name"],)
                ).fetchone()
                index["sql"] = sql_row["sql"] if sql_row else None
            tables.append(
                {
                    "name": name,
                    "sql": item["sql"],
                    "columns": rows(connection, f"PRAGMA table_xinfo({quoted})"),
                    "foreign_keys": rows(connection, f"PRAGMA foreign_key_list({quoted})"),
                    "indexes": indexes,
                }
            )
        return {
            "database": str(database.resolve()),
            "tables": tables,
            "views": [entry for entry in objects if entry["type"] == "view"],
            "triggers": [entry for entry in objects if entry["type"] == "trigger"],
        }
    finally:
        connection.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if not args.database.is_file():
        parser.error(f"database does not exist: {args.database}")
    payload = json.dumps(extract(args.database), indent=2, sort_keys=True) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(payload, encoding="utf-8")
    else:
        print(payload, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
