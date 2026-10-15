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

  it('keeps the list unchanged for empty input', () => {
    expect(addRecipients(['second@example.com'], '  ')).toEqual({recipients: ['second@example.com'], invalid: []});
  });
});
