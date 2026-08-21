# docs-index

A CocoIndex application that incrementally indexes the ChangeSharp documentation.

## What it does

- Watches `../docs/` for Markdown files.
- Splits each file into sections (on level-1/2 headings).
- Writes each section to a searchable file under `out/` (name = `doc-stem.md5.md`).
- Only reprocesses files that actually changed (state kept in `cocoindex.db`).

## Run

```bash
uv run cocoindex update main.py
```

Re-run applies only pending changes (no-op when nothing changed).
For a full rebuild: `uv run cocoindex update main.py --reset`.

## Project Structure

- `main.py` - Main application file with the CocoIndex app definition
- `pyproject.toml` - Project metadata and dependencies