import {addRecipients, normalizeRecipient} from './chemical-recipients.helper';

describe('digest recipients helper', () => {
  it('trims and lower-cases a valid address', () => {
    expect(normalizeRecipient('  First@Example.COM ')).toBe('first@example.com');
  });

  it.each(['', '   ', 'mette', 'mette@', '@example.com', 'a b@example.com', 'a@x'])('rejects %p', (text) => {
    expect(normalizeRecipient(text)).toBeNull();
  });

  it('adds pasted lists split on comma, semicolon and whitespace, de-duplicated', () => {
    const result = addRecipients(['first@example.com'], ' Second@Example.COM; ,bad, second@example.com\nFIRST@example.com third@example.com');

    expect(result.recipients).toEqual(['first@example.com', 'second@example.com', 'third@example.com']);
    expect(result.invalid).toEqual(['bad']);
  });

  it('unwraps "Name <address>" from a pasted mail-client list', () => {
    const result = addRecipients([], 'Mette Hansen <First@Example.com>; "Jens, Hansen" <second@example.com>,Third <third@example.com>');

    expect(result.recipients).toEqual(['first@example.com', 'second@example.com', 'third@example.com']);
    expect(result.invalid).toEqual([]);
  });

  it('keeps a plain address that precedes a "Name <address>" in a mixed paste', () => {
    const result = addRecipients([], 'first@example.com Second <second@example.com>');

    expect(result.recipients).toEqual(['first@example.com', 'second@example.com']);
    expect(result.invalid).toEqual([]);
  });

  it('keeps plain addresses around several named ones in a mixed paste', () => {
    const result = addRecipients([], 'a@example.com, Mette Hansen <b@example.com> c@example.com "D, E" <d@example.com>');

    expect(result.recipients).toEqual(['a@example.com', 'b@example.com', 'c@example.com', 'd@example.com']);
    expect(result.invalid).toEqual([]);
  });

  it('imports a display name that contains a bare @ without reporting it', () => {
    const result = addRecipients([], 'Support @ Acme <s@example.com>, @mette <m@example.com>');

    expect(result.recipients).toEqual(['s@example.com', 'm@example.com']);
    expect(result.invalid).toEqual([]);
  });

  it('reports bracketed and quoted leftovers the server would reject', () => {
    const result = addRecipients([], '"a"@example.com a@example.com. a(b)@example.com');

    expect(result.recipients).toEqual([]);
    expect(result.invalid).toEqual(['"a"@example.com', 'a@example.com.', 'a(b)@example.com']);
  });

  it('keeps the list unchanged for empty input', () => {
    expect(addRecipients(['second@example.com'], '  ')).toEqual({recipients: ['second@example.com'], invalid: []});
  });
});
