#!/usr/bin/env python3
"""
Check that every text colour is actually readable on the surface behind it, in
both palettes.

This exists because the first dark theme shipped with text that could not be
read, and nothing in the build could see it. Contrast is arithmetic, so it does
not need eyes — it needs the WCAG relative-luminance formula and the list of
pairings the UI actually uses.

Thresholds: 4.5:1 for body text, 3.0:1 for large or secondary text and for
non-text indicators, per WCAG 2.1 AA.

Run from the repository root. Exits non-zero if any pairing fails.
"""

import os
import re
import sys

BODY_MIN = 4.5
LARGE_MIN = 3.0


def srgb_to_linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4


def luminance(rgb):
    r, g, b = (srgb_to_linear(v) for v in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(fg, bg):
    a, b = luminance(fg), luminance(bg)
    lighter, darker = max(a, b), min(a, b)
    return (lighter + 0.05) / (darker + 0.05)


def parse_hex(text):
    text = text.lstrip('#')
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4))


def composite(fg, bg, alpha):
    """Flatten a translucent tint onto its backdrop."""
    return tuple(round(f * alpha + b * (1 - alpha)) for f, b in zip(fg, bg))


def read_palette(path):
    text = open(path, encoding='utf-8').read()
    return {k: parse_hex(v)
            for k, v in re.findall(r'<Color x:Key="([^"]+)">(#[0-9A-Fa-f]{6})</Color>', text)}


# (foreground token, background token, minimum, what it is)
PAIRS = [
    ('C.Text',           'C.Surface',    BODY_MIN,  'body text on cards'),
    ('C.Text',           'C.Background', BODY_MIN,  'body text on the window'),
    ('C.Heading',        'C.Surface',    LARGE_MIN, 'headings'),
    ('C.TextSecondary',  'C.Surface',    BODY_MIN,  'secondary text'),
    ('C.TextDim',        'C.Surface',    LARGE_MIN, 'dim text'),
    ('C.TextFaint',      'C.Surface',    LARGE_MIN, 'column headers'),
    ('C.TextFaint',      'C.Background', LARGE_MIN, 'column headers on the window'),
    ('C.Accent',         'C.Surface',    LARGE_MIN, 'primary buttons and links'),
    ('C.Accent2',        'C.Surface',    LARGE_MIN, 'secondary accent'),
    ('C.Good',           'C.Surface',    LARGE_MIN, 'good ratings'),
    ('C.WarnText',       'C.Surface',    LARGE_MIN, 'warnings'),
    ('C.WarnRail',       'C.Surface',    LARGE_MIN, 'middling ratings'),
    ('C.Bad',            'C.Surface',    LARGE_MIN, 'bad ratings and errors'),
    ('C.Band6',          'C.Surface',    LARGE_MIN, '6 GHz band label'),
    ('C.Band5',          'C.Surface',    LARGE_MIN, '5 GHz band label'),
    ('C.Band24',         'C.Surface',    LARGE_MIN, '2.4 GHz band label'),
]

# Band labels sit on a tinted pill, not on the bare surface.
TINTED = [
    ('C.Band6', 'C.Band6', 'C.Surface', LARGE_MIN, '6 GHz pill'),
    ('C.Band5', 'C.Band5', 'C.Surface', LARGE_MIN, '5 GHz pill'),
    ('C.Band24', 'C.Band24', 'C.Surface', LARGE_MIN, '2.4 GHz pill'),
]


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.chdir(root)

    palettes = sorted(f for f in os.listdir('src/Ui') if f.startswith('Palette.'))
    if not palettes:
        print("No palettes found - run this from the repository root.")
        return 1

    failures = 0
    checks = 0

    for name in palettes:
        colours = read_palette(os.path.join('src/Ui', name))
        label = name.replace('Palette.', '').replace('.xaml', '')
        print("\n%s palette" % label)
        print("-" * (len(label) + 8))

        for fg, bg, minimum, what in PAIRS:
            if fg not in colours or bg not in colours:
                continue
            ratio = contrast(colours[fg], colours[bg])
            checks += 1
            ok = ratio >= minimum
            if not ok:
                failures += 1
            print("  [%s] %-34s %5.2f:1  (need %.1f)" %
                  ("ok" if ok else "FAIL", what, ratio, minimum))

        for fg, tint, base, minimum, what in TINTED:
            if fg not in colours or base not in colours:
                continue
            # The tint opacity differs per palette; both are read from the file.
            opacity = 0.16 if label == 'Dark' else 0.10
            backdrop = composite(colours[tint], colours[base], opacity)
            ratio = contrast(colours[fg], backdrop)
            checks += 1
            ok = ratio >= minimum
            if not ok:
                failures += 1
            print("  [%s] %-34s %5.2f:1  (need %.1f)" %
                  ("ok" if ok else "FAIL", what, ratio, minimum))

    print("\n%d pairings checked." % checks)
    if failures:
        print("%d FAILED - that text would be hard or impossible to read." % failures)
        return 1
    print("Every text colour is readable on the surface behind it.")
    return 0


if __name__ == '__main__':
    sys.exit(main())
