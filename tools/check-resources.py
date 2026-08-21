#!/usr/bin/env python3
"""
Verify that every WPF resource key referenced actually exists.

WPF resolves StaticResource at runtime, not at compile time, so a mistyped key
builds perfectly and then throws the moment the page is shown. That failure mode
is invisible to the compiler and to CI's Windows build, and it cannot be caught
on a machine that has no Windows to run the app on — hence this check.

Run from the repository root. Exits non-zero on the first unresolved key.
"""

import glob
import os
import re
import sys


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.chdir(root)

    xamls = sorted(glob.glob('src/**/*.xaml', recursive=True))
    if not xamls:
        print("No XAML found - run this from the repository root.")
        return 1

    # Theme.xaml is merged into Application.Resources, so its keys are global.
    # Anything else is only visible inside the file that declares it.
    theme_keys = set()
    local_keys = {}
    for path in xamls:
        text = read(path)
        keys = set(re.findall(r'x:Key="([^"]+)"', text))
        if os.path.basename(path) == 'Theme.xaml':
            theme_keys |= keys
        else:
            local_keys[path] = keys

    missing = []

    for path in xamls:
        text = read(path)
        available = theme_keys | local_keys.get(path, set())
        for key in sorted(set(re.findall(r'\{StaticResource\s+([^}\s]+)\s*\}', text))):
            if key not in available:
                missing.append((path, key))

    # Code-behind reaches for the same dictionary by string.
    patterns = [
        r'FindResource\("([^"]+)"\)',
        r'Application\.Current\.Resources\["([^"]+)"\]',
        r'\bRes\("([^"]+)"\)',
    ]
    for path in sorted(glob.glob('src/**/*.cs', recursive=True)):
        text = read(path)
        for pattern in patterns:
            for key in sorted(set(re.findall(pattern, text))):
                if key not in theme_keys:
                    missing.append((path, key))

    print("XAML files scanned : %d" % len(xamls))
    print("Theme keys defined : %d" % len(theme_keys))

    if missing:
        print("\nUnresolved resource keys:")
        for path, key in missing:
            print("  %-34s %s" % (path, key))
        print("\n%d unresolved key(s). These would throw at runtime." % len(missing))
        return 1

    print("\nAll StaticResource and FindResource keys resolve.")
    return 0


def read(path):
    with open(path, encoding='utf-8') as handle:
        return handle.read()


if __name__ == '__main__':
    sys.exit(main())
