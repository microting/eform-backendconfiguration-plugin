#!/usr/bin/env python3
"""Guard against translated ngx-translate interpolation placeholders.

Issue #1181: a `translateTsFiles.py` run translated the placeholder *names*
inside `{{...}}` (e.g. `{{count}}` became `{{Anzahl}}`), so ngx-translate found
no matching interpolation parameter and rendered the braces literally in the
UI -- 28 broken entries spread across 12 locales.

This script asserts that, for every key, the multiset of `{{...}}` tokens in a
locale's value is exactly the multiset in the `enUS.ts` value for that key.
Token names are opaque: they are compared literally, because they are code, not
prose. It exits non-zero on any mismatch, naming the locale, key, line and the
missing/extra tokens.

Usage:  python3 check_i18n_tokens.py <i18n_dir> [<i18n_dir> ...]

The parser is deliberately strict: anything in a locale file it cannot account
for is reported as a parse error and fails the run, rather than being silently
skipped. A guard that quietly ignores a line shape is not a guard.
"""

import collections
import os
import re
import sys

TOKEN = re.compile(r'\{\{.*?\}\}')
IDENT = re.compile(r'[A-Za-z_$][\w$]*')
NUMBER = re.compile(r'-?\d[\w.]*')

SKIP_FILES = {'translates.ts'}
REFERENCE = 'enUS.ts'


class ParseError(Exception):
    pass


class _Scanner:
    """Brace/quote-aware scanner for a `export const xx = { ... };` locale file.

    Line-based parsing is not sufficient here: these files contain entries whose
    value sits on the line after the key, and at least one nested object
    (`dayOfWeeks`). A character scanner handles both, and can hard-fail on
    anything unexpected instead of skipping it.
    """

    def __init__(self, text, path):
        self.s = text
        self.i = 0
        self.path = path

    def line(self, pos=None):
        return self.s.count('\n', 0, self.i if pos is None else pos) + 1

    def fail(self, msg):
        raise ParseError(f'{self.path}:{self.line()}: {msg}')

    def skip_trivia(self):
        while self.i < len(self.s):
            c = self.s[self.i]
            if c in ' \t\r\n':
                self.i += 1
            elif self.s.startswith('//', self.i):
                nl = self.s.find('\n', self.i)
                self.i = len(self.s) if nl == -1 else nl + 1
            elif self.s.startswith('/*', self.i):
                end = self.s.find('*/', self.i + 2)
                if end == -1:
                    self.fail('unterminated block comment')
                self.i = end + 2
            else:
                return

    def read_string(self):
        """Read a quoted string, resolving backslash escapes.

        `translateTsFiles.py` leaves `\\'` inside single-quoted values, so
        unescaping here is what makes the guard safe on files it has touched.
        """
        q = self.s[self.i]
        self.i += 1
        buf = []
        while self.i < len(self.s):
            c = self.s[self.i]
            if c == '\\':
                if self.i + 1 >= len(self.s):
                    self.fail('trailing backslash in string')
                nxt = self.s[self.i + 1]
                buf.append({'n': '\n', 't': '\t', 'r': '\r'}.get(nxt, nxt))
                self.i += 2
                continue
            if c == q:
                self.i += 1
                return ''.join(buf)
            if c == '\n' and q != '`':
                self.fail('newline inside a non-template string')
            buf.append(c)
            self.i += 1
        self.fail('unterminated string')

    def read_key(self):
        c = self.s[self.i]
        if c in '"\'`':
            return self.read_string()
        m = IDENT.match(self.s, self.i) or NUMBER.match(self.s, self.i)
        if not m:
            self.fail(f'expected an object key, found {self.s[self.i:self.i + 30]!r}')
        self.i = m.end()
        return m.group(0)

    def parse_object(self, prefix, out):
        # self.s[self.i] is '{'
        self.i += 1
        while True:
            self.skip_trivia()
            if self.i >= len(self.s):
                self.fail('unterminated object')
            if self.s[self.i] == '}':
                self.i += 1
                return
            if self.s[self.i] == ',':
                self.i += 1
                continue
            if self.s[self.i] in '[.':
                self.fail('computed keys / spread are not supported by this guard')
            key = self.read_key()
            full = f'{prefix}{key}'
            self.skip_trivia()
            if self.i >= len(self.s) or self.s[self.i] != ':':
                self.fail(f'expected ":" after key {full!r}')
            self.i += 1
            self.skip_trivia()
            if self.i >= len(self.s):
                self.fail(f'missing value for key {full!r}')
            lineno = self.line()
            c = self.s[self.i]
            if c in '"\'`':
                value = self.read_string()
                if full in out:
                    self.fail(f'duplicate key {full!r} (first seen on line {out[full][0]})')
                out[full] = (lineno, value)
            elif c == '{':
                self.parse_object(full + '.', out)
            else:
                self.fail(f'value of {full!r} is not a string or object '
                          f'({self.s[self.i:self.i + 30]!r}) -- this guard only '
                          f'understands translation maps')


def parse_ts(path):
    """Return {key: (lineno, value)} for a locale file. Raises ParseError."""
    with open(path, encoding='utf-8') as fh:
        text = fh.read()
    sc = _Scanner(text, path)
    sc.skip_trivia()
    brace = text.find('{', sc.i)
    if brace == -1:
        raise ParseError(f'{path}: no object literal found')
    sc.i = brace
    out = {}
    sc.parse_object('', out)
    sc.skip_trivia()
    # Only a trailing `;` (and trivia) may follow the object.
    rest = text[sc.i:].strip()
    if rest not in ('', ';'):
        raise ParseError(f'{path}:{sc.line()}: unexpected trailing content {rest[:40]!r}')
    return out


def check_dir(directory):
    """Returns (mismatch_count, error_count)."""
    ref_path = os.path.join(directory, REFERENCE)
    if not os.path.isfile(ref_path):
        print(f'ERROR: {ref_path} not found', file=sys.stderr)
        return 0, 1

    print(f'== {directory}')
    errors = 0
    try:
        en = parse_ts(ref_path)
    except ParseError as exc:
        print(f'  PARSE ERROR: {exc}', file=sys.stderr)
        return 0, 1

    locales = sorted(f for f in os.listdir(directory)
                     if f.endswith('.ts') and f != REFERENCE and f not in SKIP_FILES)
    mismatches = 0
    for name in locales:
        try:
            loc = parse_ts(os.path.join(directory, name))
        except ParseError as exc:
            print(f'  {name}: PARSE ERROR: {exc}', file=sys.stderr)
            errors += 1
            continue
        problems = []
        for key, (lineno, value) in loc.items():
            if key not in en:
                continue  # locale key with no enUS counterpart -- not our concern
            want = collections.Counter(TOKEN.findall(en[key][1]))
            got = collections.Counter(TOKEN.findall(value))
            if want != got:
                problems.append((lineno, key, en[key][1], value,
                                 sorted((want - got).elements()),
                                 sorted((got - want).elements())))
        status = 'ok' if not problems else f'{len(problems)} MISMATCH(ES)'
        print(f'  {name}: {len(loc)} keys, {status}')
        for lineno, key, en_val, loc_val, missing, extra in sorted(problems):
            mismatches += 1
            print(f'    {name}:{lineno}  key: {key!r}')
            print(f'      enUS  : {en_val!r}')
            print(f'      {name[:-3]:<6}: {loc_val!r}')
            print(f'      missing={missing} extra={extra}')
    return mismatches, errors


def main(argv):
    dirs = argv[1:] or ['.']
    total_mismatches = 0
    total_errors = 0
    for directory in dirs:
        if not os.path.isdir(directory):
            print(f'ERROR: not a directory: {directory}', file=sys.stderr)
            total_errors += 1
            continue
        m, e = check_dir(directory)
        total_mismatches += m
        total_errors += e

    print()
    if total_errors:
        print(f'FAIL: {total_mismatches} token mismatch(es), {total_errors} parse error(s)')
        return 1
    if total_mismatches:
        print(f'FAIL: {total_mismatches} interpolation-token mismatch(es). '
              f'Translated placeholder names render as literal braces in the UI '
              f'(issue #1181) -- restore the enUS token names.')
        return 1
    print('OK: interpolation tokens match enUS in every locale.')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
