"""CocoIndex app: incremental index of the ChangeSharp documentation.

Walks docs/ for Markdown files, splits each into sections, and writes a
searchable consolidated index under out/. Only changed files are reprocessed.
"""
import hashlib
import pathlib
import re
from typing import Iterator

import cocoindex as coco
from cocoindex.connectors import localfs
from cocoindex.resources.file import PatternFilePathMatcher


@coco.lifespan
def coco_lifespan(builder: coco.EnvironmentBuilder) -> Iterator[None]:
    """Configure the CocoIndex environment."""
    builder.settings.db_path = pathlib.Path("./cocoindex.db")
    yield


@coco.fn(memo=True)
async def split_into_sections(file: localfs.File) -> list[str]:
    """Split a Markdown file into sections on level-1/2 headings."""
    text = await file.read_text()
    parts = re.split(r"\n(?=#\s)", text)
    sections = [p.strip() for p in parts if p.strip()]
    return sections or [text.strip()]


@coco.fn(memo=True)
def write_section(section: str, outdir: pathlib.Path, doc_stem: str) -> None:
    """Write one section to a dedicated file under outdir."""
    safe = re.sub(r"[^A-Za-z0-9._-]+", "_", doc_stem)
    digest = hashlib.md5(section.encode("utf-8")).hexdigest()[:8]
    outname = f"{safe}.{digest}.md"
    localfs.declare_file(outdir / outname, section, create_parent_dirs=True)


@coco.fn
async def app_main(sourcedir: pathlib.Path, outdir: pathlib.Path) -> None:
    """Index all Markdown docs into section files."""
    files = localfs.walk_dir(
        sourcedir,
        recursive=True,
        path_matcher=PatternFilePathMatcher(included_patterns=["**/*.md"]),
    )

    await coco.use_mount(
        coco.component_subpath("setup"),
        localfs.declare_dir_target,
        outdir,
    )

    async for _, f in files.items():
        sections = await split_into_sections(f)
        stem = f.file_path.path.stem
        await coco.mount_each(
            write_section,
            [(f"{stem}#{i}", s) for i, s in enumerate(sections)],
            outdir,
            stem,
        )


app = coco.App(
    coco.AppConfig(name="docs-index"),
    app_main,
    sourcedir=pathlib.Path("../docs"),
    outdir=pathlib.Path("./out"),
)