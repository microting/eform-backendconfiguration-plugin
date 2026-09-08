import {parseContentDispositionFileName} from './backend-configuration-pn-compliance-report.service';

/**
 * The `Content-Disposition` parser behind the Compliance export (#1169/#1189).
 *
 * It is the only thing standing between the server's file name and the name the
 * browser saves, and every one of its branches is a SILENT degradation: a wrong
 * branch does not throw, it just saves `Milj_tilsyn.csv`, or the caller's
 * client-built placeholder, and nobody notices until a customer asks why their
 * download is mis-named. The one Playwright test that touches the download at
 * all exercises the happy path once, in a slow shard, and asserts nothing about
 * the fallbacks.
 *
 * ORDER is the whole contract, and it is the order asserted below:
 *   1. RFC 5987 `filename*=UTF-8''<percent-encoded>` — the only lossless form;
 *   2. the quoted `filename="<ascii>"` — LOSSY, because the server's
 *      `MakeAsciiFallback` maps every non-ASCII character to `_`;
 *   3. a bare unquoted `filename=<value>`;
 *   4. the caller's own fallback.
 *
 * A pure function with no Angular surface, so no TestBed — the same shape as
 * `compliance-report-sections.spec.ts` and the helper specs next door.
 */
describe('parseContentDispositionFileName', () => {
  /** What the caller builds client-side when the header tells it nothing. */
  const FALLBACK = 'compliance-rapport.csv';

  describe('the RFC 5987 form wins', () => {
    it('decodes filename* and ignores the lossy ASCII half beside it', () => {
      // Exactly what `ComplianceExportFileNaming.BuildContentDisposition`
      // emits: both halves, the ASCII one with ø flattened to _.
      const header =
        `attachment; filename="Milj_tilsyn.csv"; filename*=UTF-8''Milj%C3%B8tilsyn.csv`;

      // The DECODED name, not `Milj_tilsyn.csv`. Reversing the two branches
      // would still produce a plausible-looking .csv, which is why the
      // assertion names the ø.
      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('Miljøtilsyn.csv');
    });

    it('wins even when it is written BEFORE the ASCII half', () => {
      // Header parameter order is not guaranteed; preference must come from
      // the branch order in the parser, not from position in the string.
      const header =
        `attachment; filename*=UTF-8''Milj%C3%B8tilsyn.csv; filename="Milj_tilsyn.csv"`;

      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('Miljøtilsyn.csv');
    });

    it('matches the token case-insensitively', () => {
      const header = `attachment; FILENAME*=utf-8''rapport.pdf`;

      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('rapport.pdf');
    });
  });

  describe('malformed percent-encoding degrades, it does not throw', () => {
    // `decodeURIComponent('%E2')` throws a URIError — `%E2` is the first byte
    // of a three-byte UTF-8 sequence with nothing after it. The parser catches
    // it and falls through. This is the branch that would take the whole
    // download subscription down if the try/catch were dropped.
    const TRUNCATED = `filename*=UTF-8''%E2`;

    it('does not throw', () => {
      expect(() => parseContentDispositionFileName(`attachment; ${TRUNCATED}`, FALLBACK)).not.toThrow();
    });

    it('falls through to the ASCII half when there is one', () => {
      const header = `attachment; filename="Milj_tilsyn.csv"; ${TRUNCATED}`;

      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('Milj_tilsyn.csv');
    });

    it('falls all the way through to the caller fallback when there is not', () => {
      // Note this is NOT the same as "returns the raw %E2": an undecodable
      // extended form must not reach the save dialog verbatim either.
      expect(parseContentDispositionFileName(`attachment; ${TRUNCATED}`, FALLBACK)).toBe(FALLBACK);
    });
  });

  describe('the quoted ASCII form', () => {
    it('unescapes a backslash-escaped quote inside the value', () => {
      // The raw header bytes are: attachment; filename="a\"b.csv"
      const header = 'attachment; filename="a\\"b.csv"';

      // The regex allows `\\.` inside the quoted run precisely so the value is
      // not truncated at the inner quote; a naive `[^"]*` would yield `a\`.
      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('a"b.csv');
    });

    it('is not confused by a semicolon inside the quoted value', () => {
      const header = 'attachment; filename="rapport; endelig.csv"';

      expect(parseContentDispositionFileName(header, FALLBACK)).toBe('rapport; endelig.csv');
    });

    it('trims surrounding whitespace', () => {
      expect(parseContentDispositionFileName('attachment; filename="  a.csv  "', FALLBACK)).toBe('a.csv');
    });

    /**
     * CURRENT behaviour, pinned as-is rather than as the behaviour one would
     * want. An EMPTY quoted value (`filename=""`) does correctly fall out of
     * the quoted branch — but it then reaches the BARE branch, whose
     * `[^;]+` happily matches the two quote characters themselves, so the
     * caller's fallback is never reached and the saved name is the literal
     * `""`. Reported, not fixed: the server never emits an empty
     * `filename=""`, so nothing on this path can reach it today.
     */
    it('lets an EMPTY quoted value leak the raw quotes through the bare branch', () => {
      expect(parseContentDispositionFileName('attachment; filename=""', FALLBACK)).toBe('""');
    });
  });

  describe('the bare unquoted form', () => {
    it('reads a value with no quotes at all', () => {
      expect(parseContentDispositionFileName('attachment; filename=x.csv', FALLBACK)).toBe('x.csv');
    });

    it('stops at the next parameter', () => {
      expect(
        parseContentDispositionFileName('attachment; filename=x.csv; charset=utf-8', FALLBACK),
      ).toBe('x.csv');
    });

    it('trims the surrounding whitespace a header may carry', () => {
      expect(parseContentDispositionFileName('attachment; filename = x.csv ', FALLBACK)).toBe('x.csv');
    });
  });

  describe('no usable header at all', () => {
    it('returns the caller fallback for a null header', () => {
      // The real case: a proxy strips the header, or CORS does not expose it.
      expect(parseContentDispositionFileName(null, FALLBACK)).toBe(FALLBACK);
    });

    it('returns the caller fallback for an empty header', () => {
      expect(parseContentDispositionFileName('', FALLBACK)).toBe(FALLBACK);
    });

    it('returns the caller fallback for a header carrying no filename', () => {
      expect(parseContentDispositionFileName('attachment', FALLBACK)).toBe(FALLBACK);
    });
  });
});
