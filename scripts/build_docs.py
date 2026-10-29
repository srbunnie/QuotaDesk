#!/usr/bin/env python3
"""Build the QuotaDesk documentation site (GitHub Pages, docs/ folder).

Markdown is the source of truth:
    docs/docs/md/<slug>.md      (Portuguese)  ->  docs/docs/<slug>.html
    docs/en/docs/md/<slug>.md   (English)     ->  docs/en/docs/<slug>.html

The .md files are published as they are (for "View as Markdown", "Copy for AI" and the
"Ask about this page" links). Every HTML page is fully static: no client-side Markdown
rendering and no CDN scripts. Standard library only; the output is deterministic, so
running the script twice gives identical files.

Usage:  python scripts/build_docs.py
"""

from __future__ import annotations

import html
import re
import sys
import unicodedata
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
SITE = "https://srbunnie.github.io/QuotaDesk/"
REPO = "https://github.com/srbunnie/QuotaDesk"

# Sidebar: (group key, [slugs]). Order here is also the prev/next order.
GROUPS = [
    ("start", ["index", "install"]),
    ("connect", ["connect-codex", "connect-claude", "connect-antigravity",
                 "connect-magnific", "connect-openrouter", "connect-figma"]),
    ("use", ["use", "settings"]),
    ("more", ["privacy", "develop", "add-provider"]),
]
ORDER = [slug for _, slugs in GROUPS for slug in slugs]

LANGS = {
    "pt": {
        "html_lang": "pt-BR",
        "src": DOCS / "docs" / "md",
        "out": DOCS / "docs",
        "url": SITE + "docs/",
        "repo_md": "docs/docs/md/",
        "home": "../",                       # site home for this language
        "assets": "assets/",                 # shared docs CSS/JS
        "site_assets": "../assets/",         # main site images
        "other": "../en/docs/",              # same slug, other language
        "groups": {"start": "Começar", "connect": "Conectar", "use": "Usar", "more": "Mais"},
        "t": {
            "docs": "Documentação",
            "skip": "Pular para o conteúdo",
            "search": "Pesquisar",
            "search_ph": "Pesquisar na documentação",
            "no_results": "Nenhum resultado",
            "menu": "Menu da documentação",
            "sidebar": "Documentação",
            "toc": "Nesta página",
            "breadcrumb": "Trilha",
            "lang_group": "Idioma / Language",
            "gh": "GitHub",
            "new_tab": "(abre em nova aba)",
            "ask": "Perguntar sobre esta página",
            "ask_menu": "Perguntar a uma IA",
            "ask_claude": "Perguntar ao Claude",
            "ask_chatgpt": "Perguntar ao ChatGPT",
            "copy": "Copiar para IA",
            "copied": "Copiado",
            "copy_fail": "Não deu para copiar",
            "view_md": "Ver como Markdown",
            "edit": "Editar no GitHub",
            "actions": "Ações da página",
            "prompt": "Leia a documentação do QuotaDesk em {url} e me ajude com: ",
            "prev": "Anterior",
            "next": "Próxima",
            "pager": "Navegação entre páginas",
            "footer_by": "por",
            "and": "e",
            "footer_note": "Projeto independente. Claude, Codex, Antigravity, Figma, Magnific e OpenRouter são marcas de seus donos; o QuotaDesk não é afiliado a nenhum deles.",
            "title_suffix": "Documentação do QuotaDesk",
            "pt_label": "PT",
            "en_label": "EN",
        },
    },
    "en": {
        "html_lang": "en",
        "src": DOCS / "en" / "docs" / "md",
        "out": DOCS / "en" / "docs",
        "url": SITE + "en/docs/",
        "repo_md": "docs/en/docs/md/",
        "home": "../",
        "assets": "../../docs/assets/",
        "site_assets": "../../assets/",
        "other": "../../docs/",
        "groups": {"start": "Get started", "connect": "Connect", "use": "Use", "more": "More"},
        "t": {
            "docs": "Docs",
            "skip": "Skip to content",
            "search": "Search",
            "search_ph": "Search the docs",
            "no_results": "No results",
            "menu": "Docs menu",
            "sidebar": "Documentation",
            "toc": "On this page",
            "breadcrumb": "Breadcrumb",
            "lang_group": "Language / Idioma",
            "gh": "GitHub",
            "new_tab": "(opens in a new tab)",
            "ask": "Ask about this page",
            "ask_menu": "Ask an AI",
            "ask_claude": "Ask Claude",
            "ask_chatgpt": "Ask ChatGPT",
            "copy": "Copy for AI",
            "copied": "Copied",
            "copy_fail": "Could not copy",
            "view_md": "View as Markdown",
            "edit": "Edit on GitHub",
            "actions": "Page actions",
            "prompt": "Read the QuotaDesk docs at {url} and help me with: ",
            "prev": "Previous",
            "next": "Next",
            "pager": "Page navigation",
            "footer_by": "by",
            "and": "and",
            "footer_note": "Independent project. Claude, Codex, Antigravity, Figma, Magnific and OpenRouter are trademarks of their owners; QuotaDesk isn't affiliated with any of them.",
            "title_suffix": "QuotaDesk Docs",
            "pt_label": "PT",
            "en_label": "EN",
        },
    },
}

# Lucide icons (ISC), inlined so the page needs no icon font.
ICONS = {
    "ask": '<path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z"/><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/><path d="M12 17h.01"/>',
    "copy": '<rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
    "check": '<path d="M20 6 9 17l-5-5"/>',
    "file": '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/>',
    "edit": '<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/>',
    "chevron": '<path d="m6 9 6 6 6-6"/>',
    "search": '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
    "menu": '<path d="M4 6h16"/><path d="M4 12h16"/><path d="M4 18h16"/>',
    "github": '<path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4"/><path d="M9 18c-4.51 2-5-2-7-2"/>',
    "external": '<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h3"/>',
    "left": '<path d="m12 19-7-7 7-7"/><path d="M19 12H5"/>',
    "right": '<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
}


def icon(name: str, size: int = 16) -> str:
    return (f'<svg class="icon" width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" '
            f'stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" '
            f'aria-hidden="true" focusable="false">{ICONS[name]}</svg>')


# --------------------------------------------------------------------------- Markdown

def esc(text: str) -> str:
    """HTML-escape text. Backslashes become &#92; so Windows paths in examples render the
    same but never look like a real user-profile path to the repository leak scanner."""
    return html.escape(text, quote=True).replace("\\", "&#92;")


def slugify(text: str) -> str:
    text = re.sub(r"<[^>]+>", "", text)
    text = unicodedata.normalize("NFKD", text)
    text = "".join(c for c in text if not unicodedata.combining(c))
    text = re.sub(r"[^\w\s-]", "", text.lower())
    return re.sub(r"[\s_]+", "-", text).strip("-") or "secao"


def strip_md(text: str) -> str:
    """Plain text of an inline Markdown fragment (for titles, descriptions and the TOC)."""
    text = re.sub(r"`([^`]*)`", r"\1", text)
    text = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"\*\*(.+?)\*\*", r"\1", text)
    text = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"\1", text)
    return text.strip()


class Renderer:
    def __init__(self, lang: dict):
        self.lang = lang
        self.ids: dict[str, int] = {}
        self.toc: list[tuple[int, str, str]] = []

    # ---- links
    def href(self, url: str) -> str:
        if url.startswith(SITE + "assets/"):
            return self.lang["site_assets"] + url[len(SITE + "assets/"):]
        if "://" not in url and not url.startswith(("#", "mailto:", "/")):
            path, _, frag = url.partition("#")
            if path.endswith(".md"):
                url = path[:-3] + ".html" + ("#" + frag if frag else "")
        return url

    # ---- inline
    def inline(self, text: str, slots: list[str] | None = None) -> str:
        top = slots is None
        if slots is None:
            slots = []

        def keep(fragment: str) -> str:
            slots.append(fragment)
            return f"\x00{len(slots) - 1}\x00"

        # Code spans first: nothing inside them is Markdown.
        text = re.sub(r"`([^`]+)`", lambda m: keep(f"<code>{esc(m.group(1))}</code>"), text)

        def image(m: re.Match) -> str:
            return keep(f'<img src="{esc(self.href(m.group(2)))}" alt="{esc(m.group(1))}" loading="lazy">')

        text = re.sub(r"!\[([^\]]*)\]\(([^)\s]+)\)", image, text)

        def link(m: re.Match) -> str:
            label, url = m.group(1), m.group(2)
            target = self.href(url)
            external = url.startswith("http") and not url.startswith(SITE)
            attrs = ' target="_blank" rel="noopener"' if external else ""
            return keep(f'<a href="{esc(target)}"{attrs}>{self.inline(label, slots)}</a>')

        text = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", link, text)
        text = esc(text)
        text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
        text = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", text)
        while "\x00" in text:
            text = re.sub(r"\x00(\d+)\x00", lambda m: slots[int(m.group(1))], text)
        return text

    def heading_id(self, text: str) -> str:
        base = slugify(strip_md(text))
        n = self.ids.get(base, 0)
        self.ids[base] = n + 1
        return base if n == 0 else f"{base}-{n}"

    # ---- blocks
    def blocks(self, lines: list[str]) -> str:
        out: list[str] = []
        i = 0
        while i < len(lines):
            line = lines[i]
            stripped = line.strip()
            if not stripped:
                i += 1
                continue

            fence = re.match(r"^(\s*)(```+|~~~+)\s*([\w+-]*)", line)
            if fence:
                marker, lang = fence.group(2), fence.group(3)
                body: list[str] = []
                i += 1
                while i < len(lines) and not lines[i].strip().startswith(marker):
                    body.append(lines[i])
                    i += 1
                i += 1
                cls = f' class="language-{esc(lang)}"' if lang else ""
                label = f' data-lang="{esc(lang)}"' if lang else ""
                out.append(f'<pre{label}><code{cls}>{esc(chr(10).join(body))}</code></pre>')
                continue

            m = re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", line)
            if m:
                level, text = len(m.group(1)), m.group(2)
                hid = self.heading_id(text)
                if level in (2, 3):
                    self.toc.append((level, hid, strip_md(text)))
                out.append(f'<h{level} id="{hid}">{self.inline(text)}'
                           f'<a class="anchor" href="#{hid}" aria-hidden="true" tabindex="-1">#</a></h{level}>')
                i += 1
                continue

            if re.match(r"^(-{3,}|\*{3,}|_{3,})$", stripped):
                out.append("<hr>")
                i += 1
                continue

            if stripped.startswith("|") and i + 1 < len(lines) and re.match(r"^\s*\|?\s*:?-{3,}", lines[i + 1]):
                rows = []
                while i < len(lines) and lines[i].strip().startswith("|"):
                    rows.append(lines[i].strip())
                    i += 1
                out.append(self.table(rows))
                continue

            if stripped.startswith(">"):
                quote_lines = []
                while i < len(lines) and lines[i].strip().startswith(">"):
                    quote_lines.append(re.sub(r"^\s*>\s?", "", lines[i]))
                    i += 1
                out.append(f"<blockquote>{self.blocks(quote_lines)}</blockquote>")
                continue

            if re.match(r"^\s*([-*+]|\d+[.)])\s+", line):
                html_list, i = self.list_block(lines, i)
                out.append(html_list)
                continue

            para = []
            while i < len(lines) and lines[i].strip() and not self.starts_block(lines, i):
                para.append(lines[i].strip())
                i += 1
            out.append(f"<p>{self.inline(' '.join(para))}</p>")
        return "\n".join(out)

    @staticmethod
    def starts_block(lines: list[str], i: int) -> bool:
        s = lines[i].strip()
        return bool(re.match(r"^(#{1,6}\s|```|~~~|>|([-*+]|\d+[.)])\s|\|)", s)) or bool(re.match(r"^(-{3,}|\*{3,})$", s))

    def list_block(self, lines: list[str], i: int) -> tuple[str, int]:
        first = re.match(r"^(\s*)([-*+]|\d+[.)])\s+", lines[i])
        indent = len(first.group(1))
        ordered = first.group(2)[0].isdigit()
        start = int(first.group(2)[:-1]) if ordered else 1
        items: list[list[str]] = []
        while i < len(lines):
            line = lines[i]
            m = re.match(r"^(\s*)([-*+]|\d+[.)])\s+(.*)$", line)
            if m and len(m.group(1)) == indent and (m.group(2)[0].isdigit()) == ordered:
                items.append([m.group(3)])
                i += 1
                continue
            if not line.strip():
                # A blank line ends the list unless the next line is indented under the item.
                if i + 1 < len(lines) and lines[i + 1].startswith(" " * (indent + 2)) and lines[i + 1].strip():
                    items[-1].append("")
                    i += 1
                    continue
                break
            if line.startswith(" " * (indent + 2)) or (m and len(m.group(1)) > indent):
                items[-1].append(line[indent + 2:] if line.startswith(" " * (indent + 2)) else line.strip())
                i += 1
                continue
            if m or self.starts_block(lines, i):
                break
            items[-1].append(line.strip())  # lazy continuation
            i += 1
        tag = "ol" if ordered else "ul"
        start_attr = f' start="{start}"' if ordered and start != 1 else ""
        parts = []
        for item in items:
            nested = any(re.match(r"^\s*([-*+]|\d+[.)])\s+", l) for l in item[1:]) or "" in item
            if nested:
                parts.append(f"<li>{self.blocks(item)}</li>")
            else:
                parts.append(f"<li>{self.inline(' '.join(s.strip() for s in item))}</li>")
        return f"<{tag}{start_attr}>" + "".join(parts) + f"</{tag}>", i

    def table(self, rows: list[str]) -> str:
        def cells(row: str) -> list[str]:
            row = row.strip().strip("|")
            parts, cur, in_code = [], "", False
            j = 0
            while j < len(row):
                c = row[j]
                if c == "\\" and j + 1 < len(row) and row[j + 1] == "|":
                    cur += "|"
                    j += 2
                    continue
                if c == "`":
                    in_code = not in_code
                if c == "|" and not in_code:
                    parts.append(cur.strip())
                    cur = ""
                else:
                    cur += c
                j += 1
            parts.append(cur.strip())
            return parts

        head = cells(rows[0])
        aligns = []
        for spec in cells(rows[1]):
            left, right = spec.startswith(":"), spec.endswith(":")
            aligns.append("center" if left and right else "right" if right else "")
        def td(tag, text, k):
            a = aligns[k] if k < len(aligns) else ""
            style = f' style="text-align:{a}"' if a else ""
            scope = ' scope="col"' if tag == "th" else ""
            return f"<{tag}{scope}{style}>{self.inline(text)}</{tag}>"
        thead = "<tr>" + "".join(td("th", c, k) for k, c in enumerate(head)) + "</tr>"
        body = "".join("<tr>" + "".join(td("td", c, k) for k, c in enumerate(cells(r))) + "</tr>" for r in rows[2:])
        return f'<div class="table-scroll" tabindex="0"><table><thead>{thead}</thead><tbody>{body}</tbody></table></div>'


# --------------------------------------------------------------------------- Pages

def read_page(lang_key: str, slug: str) -> dict:
    path = LANGS[lang_key]["src"] / f"{slug}.md"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
    lines = text.split("\n")
    title_line = next(i for i, l in enumerate(lines) if l.startswith("# "))
    title = strip_md(lines[title_line][2:])
    j = title_line + 1
    while j < len(lines) and not lines[j].strip():
        j += 1
    lead_lines = []
    while j < len(lines) and lines[j].strip() and not Renderer.starts_block(lines, j):
        lead_lines.append(lines[j].strip())
        j += 1
    return {"slug": slug, "title": title, "lead": " ".join(lead_lines), "body": lines[j:]}


def build_page(lang_key: str, page: dict, titles: dict[str, str], group_of: dict[str, str]) -> str:
    L = LANGS[lang_key]
    t = L["t"]
    slug = page["slug"]
    r = Renderer(L)
    body_html = r.blocks(page["body"])
    lead_html = r.inline(page["lead"])
    description = strip_md(page["lead"])

    html_url = L["url"] + f"{slug}.html"
    md_url = L["url"] + f"md/{slug}.md"
    other_key = "en" if lang_key == "pt" else "pt"
    other_url = LANGS[other_key]["url"] + f"{slug}.html"
    pt_url = LANGS["pt"]["url"] + f"{slug}.html"
    en_url = LANGS["en"]["url"] + f"{slug}.html"
    prompt = quote(t["prompt"].format(url=md_url), safe="")
    edit_url = f"{REPO}/edit/main/{L['repo_md']}{slug}.md"
    full_title = f"{page['title']} · {t['title_suffix']}" if slug != "index" else t["title_suffix"]

    # Sidebar
    nav = []
    for group, slugs in GROUPS:
        gid = f"grp-{group}"
        items = []
        for s in slugs:
            current = ' aria-current="page"' if s == slug else ""
            items.append(f'<li><a href="{s}.html"{current}>{esc(titles[s])}</a></li>')
        nav.append(f'<div class="nav-group"><h2 class="nav-title" id="{gid}">{esc(L["groups"][group])}</h2>'
                   f'<ul aria-labelledby="{gid}">{"".join(items)}</ul></div>')
    nav_html = "\n".join(nav)

    # Table of contents
    if r.toc:
        toc_items = "".join(
            f'<li class="toc-l{lvl}"><a href="#{hid}">{esc(text)}</a></li>' for lvl, hid, text in r.toc)
        toc_html = (f'<aside class="toc" aria-labelledby="toc-title"><div class="toc-inner">'
                    f'<p class="toc-title" id="toc-title">{esc(t["toc"])}</p><ul>{toc_items}</ul></div></aside>')
    else:
        toc_html = '<aside class="toc" aria-hidden="true"></aside>'

    # Breadcrumb
    crumbs = [f'<li><a href="index.html">{esc(t["docs"])}</a></li>']
    if slug != "index":
        crumbs.append(f'<li>{esc(L["groups"][group_of[slug]])}</li>')
        crumbs.append(f'<li><span aria-current="page">{esc(page["title"])}</span></li>')
    crumb_html = f'<nav class="crumbs" aria-label="{esc(t["breadcrumb"])}"><ol>{"".join(crumbs)}</ol></nav>'

    # Prev / next
    k = ORDER.index(slug)
    pager = []
    if k > 0:
        p = ORDER[k - 1]
        pager.append(f'<a class="pager-prev" href="{p}.html" rel="prev"><span class="pager-label">{icon("left", 14)} {esc(t["prev"])}</span>'
                     f'<span class="pager-title">{esc(titles[p])}</span></a>')
    if k < len(ORDER) - 1:
        n = ORDER[k + 1]
        pager.append(f'<a class="pager-next" href="{n}.html" rel="next"><span class="pager-label">{esc(t["next"])} {icon("right", 14)}</span>'
                     f'<span class="pager-title">{esc(titles[n])}</span></a>')
    pager_html = f'<nav class="pager" aria-label="{esc(t["pager"])}">{"".join(pager)}</nav>'

    nt = f'<span class="sr-only"> {esc(t["new_tab"])}</span>'
    actions = f"""<div class="page-actions" role="group" aria-label="{esc(t['actions'])}">
  <div class="ask">
    <button type="button" class="action" id="ask-btn" aria-haspopup="true" aria-expanded="false" aria-controls="ask-menu">{icon('ask')}<span>{esc(t['ask'])}</span>{icon('chevron', 14)}</button>
    <ul class="ask-menu" id="ask-menu" role="menu" aria-label="{esc(t['ask_menu'])}" hidden>
      <li role="none"><a role="menuitem" href="https://claude.ai/new?q={prompt}" target="_blank" rel="noopener">{esc(t['ask_claude'])}{icon('external', 14)}{nt}</a></li>
      <li role="none"><a role="menuitem" href="https://chatgpt.com/?q={prompt}" target="_blank" rel="noopener">{esc(t['ask_chatgpt'])}{icon('external', 14)}{nt}</a></li>
    </ul>
  </div>
  <button type="button" class="action" id="copy-btn" data-md="md/{slug}.md" data-label="{esc(t['copy'])}" data-done="{esc(t['copied'])}" data-fail="{esc(t['copy_fail'])}">{icon('copy')}<span>{esc(t['copy'])}</span></button>
  <a class="action" href="md/{slug}.md" target="_blank" rel="noopener">{icon('file')}<span>{esc(t['view_md'])}</span>{nt}</a>
  <a class="action" href="{esc(edit_url)}" target="_blank" rel="noopener">{icon('edit')}<span>{esc(t['edit'])}</span>{nt}</a>
  <span class="sr-only" id="copy-status" role="status" aria-live="polite"></span>
</div>"""

    pt_cur = lang_key == "pt"
    if pt_cur:
        lang_switch = (f'<span aria-current="true" lang="pt-BR">PT<span class="sr-only"> (Português)</span></span>'
                       f'<span class="sep" aria-hidden="true">|</span>'
                       f'<a href="{L["other"]}{slug}.html" lang="en" hreflang="en">EN<span class="sr-only"> (English)</span></a>')
    else:
        lang_switch = (f'<a href="{L["other"]}{slug}.html" lang="pt-BR" hreflang="pt-BR">PT<span class="sr-only"> (Português)</span></a>'
                       f'<span class="sep" aria-hidden="true">|</span>'
                       f'<span aria-current="true" lang="en">EN<span class="sr-only"> (English)</span></span>')

    gh_footer = f"{REPO}/blob/main/README.en.md" if lang_key == "en" else REPO

    return f"""<!doctype html>
<html lang="{L['html_lang']}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{esc(full_title)}</title>
<meta name="description" content="{esc(description)}">
<link rel="canonical" href="{html_url}">
<link rel="alternate" hreflang="pt-BR" href="{pt_url}">
<link rel="alternate" hreflang="en" href="{en_url}">
<link rel="alternate" hreflang="x-default" href="{pt_url}">
<link rel="alternate" type="text/markdown" href="md/{slug}.md">
<meta property="og:title" content="{esc(full_title)}">
<meta property="og:description" content="{esc(description)}">
<meta property="og:type" content="article">
<meta property="og:url" content="{html_url}">
<meta property="og:image" content="{SITE}assets/screens/panel-working.png">
<link rel="icon" href="{L['site_assets']}screens/tray.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;600&family=Silkscreen:wght@400;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="{L['assets']}docs.css">
<script src="{L['assets']}docs.js" defer></script>
</head>
<body data-title="{esc(page['title'])}" data-url="{html_url}" data-no-results="{esc(t['no_results'])}">
<a class="skip" href="#conteudo">{esc(t['skip'])}</a>

<header class="topbar">
  <div class="topbar-row">
    <button type="button" class="icon-btn nav-toggle" id="nav-toggle" aria-expanded="false" aria-controls="sidebar">{icon('menu', 20)}<span class="sr-only">{esc(t['menu'])}</span></button>
    <a class="logo" href="{L['home']}">QUOTA<b>DESK</b></a>
    <a class="docs-home" href="index.html">{esc(t['docs'])}</a>
    <div class="search" role="search">
      <label class="sr-only" for="doc-search">{esc(t['search'])}</label>
      {icon('search')}
      <input type="search" id="doc-search" placeholder="{esc(t['search_ph'])}" autocomplete="off" spellcheck="false" aria-controls="sidebar">
    </div>
    <p class="lang" role="group" aria-label="{esc(t['lang_group'])}">{lang_switch}</p>
    <a class="icon-btn gh" href="{REPO}" target="_blank" rel="noopener">{icon('github', 20)}<span class="sr-only">{esc(t['gh'])} {esc(t['new_tab'])}</span></a>
  </div>
</header>

<div class="layout">
  <nav class="sidebar" id="sidebar" aria-label="{esc(t['sidebar'])}">
{nav_html}
    <p class="nav-empty" hidden>{esc(t['no_results'])}</p>
  </nav>

  <main id="conteudo" class="content" tabindex="-1">
    <article>
      {crumb_html}
      <h1>{esc(page['title'])}</h1>
      <p class="lead">{lead_html}</p>
      {actions}
      <div class="prose">
{body_html}
      </div>
      {pager_html}
    </article>
  </main>

  {toc_html}
</div>

<footer class="site-footer">
  <div class="footer-inner">
    <p><strong>QuotaDesk</strong> · {t['footer_by']} <a href="https://github.com/edirsooo">@edirsooo</a> {t['and']} <a href="https://github.com/srbunnie">@srbunnie</a> · <a href="{gh_footer}">GitHub</a> · <a href="{REPO}/blob/main/LICENSE">MIT</a></p>
    <p>{esc(t['footer_note'])}</p>
  </div>
</footer>
</body>
</html>
"""


def write(path: Path, content: str) -> bool:
    data = content.encode("utf-8")
    if path.exists() and path.read_bytes() == data:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return True


def main() -> int:
    changed = 0
    group_of = {s: g for g, slugs in GROUPS for s in slugs}
    for lang_key, L in LANGS.items():
        missing = [s for s in ORDER if not (L["src"] / f"{s}.md").exists()]
        if missing:
            print(f"[{lang_key}] missing Markdown: {', '.join(missing)}", file=sys.stderr)
            return 1
        pages = {s: read_page(lang_key, s) for s in ORDER}
        titles = {s: p["title"] for s, p in pages.items()}
        for slug, page in pages.items():
            out = L["out"] / f"{slug}.html"
            if write(out, build_page(lang_key, page, titles, group_of)):
                changed += 1
                print(f"wrote {out.relative_to(ROOT).as_posix()}")
    print(f"{changed} file(s) changed, {len(ORDER) * len(LANGS)} page(s) total.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
