#!/usr/bin/env python3
"""Minimal markdown -> styled HTML for the audit report (no third-party deps)."""
import html
import re
import sys

SRC, OUT = sys.argv[1], sys.argv[2]

CSS = """
@page { size: A4; margin: 14mm 12mm 14mm 12mm; }
* { box-sizing: border-box; }
body {
  font-family: "Segoe UI", "Inter", system-ui, sans-serif;
  font-size: 9.4pt; line-height: 1.5; color: #1b1f27; margin: 0;
  -webkit-print-color-adjust: exact; print-color-adjust: exact;
}
h1 { font-size: 21pt; margin: 0 0 2mm; color: #10131a; letter-spacing: -0.4px; line-height: 1.2; }
h2 { font-size: 13.5pt; margin: 9mm 0 3mm; padding: 2mm 0 1.6mm; color: #10131a;
     border-bottom: 2px solid #2f6fa8; letter-spacing: -0.2px; page-break-after: avoid; }
h3 { font-size: 10.8pt; margin: 6mm 0 2mm; color: #24405c; page-break-after: avoid; }
h4 { font-size: 9.8pt; margin: 4mm 0 1.5mm; color: #3a4657; page-break-after: avoid; }
p { margin: 0 0 2.4mm; }
ul, ol { margin: 0 0 2.6mm; padding-left: 5.5mm; }
li { margin-bottom: 1.1mm; }
strong { color: #0d1117; font-weight: 650; }
hr { border: 0; border-top: 1px solid #d8dee6; margin: 7mm 0; }
code { font-family: "Cascadia Mono", Consolas, monospace; font-size: 8.4pt;
       background: #eef1f5; padding: 0.4mm 1.1mm; border-radius: 2px; color: #1e3a5f; }
pre { background: #f4f6f9; border: 1px solid #dde3ea; border-left: 3px solid #2f6fa8;
      padding: 2.5mm 3mm; border-radius: 3px; overflow-x: auto; margin: 0 0 3mm; }
pre code { background: none; padding: 0; font-size: 8.2pt; color: #14304d; }
blockquote { margin: 0 0 3mm; padding: 2.2mm 3mm; background: #fff6e8;
             border-left: 3px solid #d98324; border-radius: 3px; font-weight: 600; color: #6b3d09; }
table { border-collapse: collapse; width: 100%; margin: 0 0 4mm; font-size: 8.3pt;
        page-break-inside: auto; }
tr { page-break-inside: avoid; }
th { background: #2f4a63; color: #fff; text-align: left; padding: 1.6mm 2mm;
     font-weight: 600; font-size: 8.1pt; border: 1px solid #24394d; }
td { padding: 1.5mm 2mm; border: 1px solid #d8dee6; vertical-align: top; }
tbody tr:nth-child(even) td { background: #f6f8fa; }
.cover { border-bottom: 3px solid #2f6fa8; padding-bottom: 4mm; margin-bottom: 6mm; }
.sub { color: #5a6675; font-size: 9pt; margin-top: 1.5mm; }
"""

# --- inline ---------------------------------------------------------------
def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r'`([^`]+)`', lambda m: '<code>%s</code>' % m.group(1), t)
    t = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'(?<![\w*])\*([^*\n]+)\*(?![\w*])', r'<em>\1</em>', t)
    return t

lines = open(SRC, encoding='utf-8').read().split('\n')
out, i, n = [], 0, len(lines)

def close_lists(stack):
    while stack:
        out.append('</%s>' % stack.pop())

liststack = []
while i < n:
    ln = lines[i]

    if ln.startswith('```'):
        close_lists(liststack)
        i += 1
        buf = []
        while i < n and not lines[i].startswith('```'):
            buf.append(html.escape(lines[i])); i += 1
        i += 1
        out.append('<pre><code>%s</code></pre>' % '\n'.join(buf))
        continue

    # table
    if ln.startswith('|') and i + 1 < n and re.match(r'^\|[\s:|-]+\|$', lines[i + 1].strip()):
        close_lists(liststack)
        cells = [c.strip() for c in ln.strip().strip('|').split('|')]
        out.append('<table><thead><tr>' + ''.join('<th>%s</th>' % inline(c) for c in cells) + '</tr></thead><tbody>')
        i += 2
        while i < n and lines[i].startswith('|'):
            cs = [c.strip() for c in lines[i].strip().strip('|').split('|')]
            out.append('<tr>' + ''.join('<td>%s</td>' % inline(c) for c in cs) + '</tr>')
            i += 1
        out.append('</tbody></table>')
        continue

    if ln.strip() == '---':
        close_lists(liststack); out.append('<hr>'); i += 1; continue

    m = re.match(r'^(#{1,4})\s+(.*)$', ln)
    if m:
        close_lists(liststack)
        lvl = len(m.group(1))
        out.append('<h%d>%s</h%d>' % (lvl, inline(m.group(2)), lvl))
        i += 1; continue

    if ln.startswith('> '):
        close_lists(liststack)
        buf = []
        while i < n and lines[i].startswith('> '):
            buf.append(inline(lines[i][2:])); i += 1
        out.append('<blockquote>%s</blockquote>' % ' '.join(buf))
        continue

    m = re.match(r'^(\s*)([-*]|\d+\.)\s+(.*)$', ln)
    if m:
        indent = len(m.group(1))
        tag = 'ol' if m.group(2)[0].isdigit() else 'ul'
        depth = indent // 2 + 1
        while len(liststack) > depth:
            out.append('</%s>' % liststack.pop())
        if len(liststack) < depth:
            out.append('<%s>' % tag); liststack.append(tag)
        out.append('<li>%s</li>' % inline(m.group(3)))
        i += 1; continue

    if not ln.strip():
        close_lists(liststack); i += 1; continue

    close_lists(liststack)
    buf = []
    while i < n and lines[i].strip() and not re.match(r'^(#|\||>|```|---|\s*([-*]|\d+\.)\s)', lines[i]):
        buf.append(lines[i]); i += 1
    out.append('<p>%s</p>' % inline(' '.join(buf)))

close_lists(liststack)
body = '\n'.join(out)

# promote the leading h1 block into a cover header
body = body.replace('<h1>', '<div class="cover"><h1>', 1)
body = body.replace('</h1>', '</h1>', 1)
body = re.sub(r'(</h1>)(.*?)(<hr>)', r'\1\2</div>\3', body, count=1, flags=re.S)

open(OUT, 'w', encoding='utf-8').write(
    '<!DOCTYPE html><html><head><meta charset="utf-8">'
    '<title>RJW Sexual Harassment - Quad-Pass Audit</title>'
    '<style>%s</style></head><body>%s</body></html>' % (CSS, body))
print('wrote', OUT)
