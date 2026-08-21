#!/usr/bin/env python3
"""
Verify that every WPF resource key referenced actually exists, and that the two
colour palettes are interchangeable.

WPF resolves StaticResource and DynamicResource at runtime, not at compile time,
so a mistyped key builds perfectly and then throws (or silently renders nothing)
the moment the page is shown. That failure mode is invisible to the compiler and
to the Windows build, and it cannot be caught on a machine with no Windows to
run the app on — hence this check.

The palette comparison matters just as much: a key defined in the light palette
but missing from the dark one works fine until someone flips the theme, and then
every control bound to it breaks at once.

Run from the repository root. Exits non-zero on any problem.
"""

import glob
import os
import re
import sys


def read(path):
    with open(path, encoding='utf-8') as handle:
        return handle.read()


def keys_in(path):
    return set(re.findall(r'x:Key="([^"]+)"', read(path)))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.chdir(root)

    xamls = sorted(glob.glob('src/**/*.xaml', recursive=True))
    if not xamls:
        print("No XAML found - run this from the repository root.")
        return 1

    palettes = [p for p in xamls if os.path.basename(p).startswith('Palette.')]
    themes = [p for p in xamls if os.path.basename(p) == 'Theme.xaml']

    problems = []

    # ---- palettes must be interchangeable --------------------------------
    if len(palettes) >= 2:
        sets = {p: keys_in(p) for p in palettes}
        shared = set.intersection(*sets.values())
        for path, keys in sets.items():
            for missing in sorted(shared.symmetric_difference(keys) & keys):
                # A key this palette has that at least one other lacks.
                if any(missing not in other for p2, other in sets.items() if p2 != path):
                    problems.append((path, missing, "not defined in every palette"))

    # Application.Resources merges the palette and the theme, so both are global.
    global_keys = set()
    for path in palettes + themes:
        global_keys |= keys_in(path)

    local_keys = {p: keys_in(p) for p in xamls if p not in palettes and p not in themes}

    # ---- every reference must resolve ------------------------------------
    ref = re.compile(r'\{(?:Static|Dynamic)Resource\s+([^}\s]+)\s*\}')
    for path in xamls:
        text = read(path)
        available = global_keys | local_keys.get(path, set())
        if path in palettes:
            # A palette resolves only its own keys, at load time.
            available = keys_in(path)
        for key in sorted(set(ref.findall(text))):
            if key not in available:
                problems.append((path, key, "unresolved"))

    # ---- code-behind reaches for the same dictionary by string -----------
    patterns = [
        r'FindResource\("([^"]+)"\)',
        r'Application\.Current\.Resources\["([^"]+)"\]',
        r'\bRes\("([^"]+)"\)',
    ]
    for path in sorted(glob.glob('src/**/*.cs', recursive=True)):
        text = read(path)
        for pattern in patterns:
            for key in sorted(set(re.findall(pattern, text))):
                if key not in global_keys:
                    problems.append((path, key, "unresolved from code"))

    print("XAML files scanned  : %d" % len(xamls))
    print("Palettes            : %d" % len(palettes))
    print("Global keys defined : %d" % len(global_keys))

    if problems:
        print("\nProblems:")
        for path, key, why in problems:
            print("  %-30s %-22s %s" % (path, key, why))
        print("\n%d problem(s). These would fail at runtime." % len(problems))
        return 1

    print("\nAll resource keys resolve, and every palette defines the same keys.")
    return 0


if __name__ == '__main__':
    sys.exit(main())
