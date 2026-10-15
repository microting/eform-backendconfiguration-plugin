/**
 * What the server accepts (ChemicalInventoryService.NormalizeRecipients: MailAddress.TryCreate with an exact
 * match): no whitespace and none of @ , ; < > ( ) [ ] " : \ in a part, no empty, leading or trailing dot
 * label and at least one dot in the domain.
 */
const PART = '[^\\s@,;<>()\\[\\]":\\\\.]+';
const EMAIL = new RegExp(`^${PART}(\\.${PART})*@${PART}(\\.${PART})+$`);

/**
 * "Name <address>" (a paste from a mail client) becomes the bare address; other text is left as it is.
 * The unquoted name may swallow plain addresses that precede it ("first@example.com Name <second@example.com>"): those are kept.
 */
const NAMED_ADDRESS = /("[^"]*"|[^,;<>"]*)\s*<([^<>]*)>/g;

/** A digest recipient as stored (Plan A: lower-case, comma-separated). null = not an e-mail address. */
export function normalizeRecipient(text: string): string | null {
  const value = (text ?? '').trim().toLowerCase();
  return EMAIL.test(value) ? value : null;
}

/** Adds every address in a typed or pasted text; returns what could not be added. */
export function addRecipients(current: string[], input: string): {recipients: string[]; invalid: string[]} {
  const recipients = [...current];
  const invalid: string[] = [];
  const unwrapped = (input ?? '').replace(NAMED_ADDRESS, (_match, name: string, address: string) => {
    const swallowed = name.startsWith('"') ? [] : name.split(/\s+/).filter((token) => token.includes('@'));
    return ` ${swallowed.join(' ')} ${address} `;
  });
  for (const part of unwrapped.split(/[\s,;]+/).filter((p) => p.length)) {
    const email = normalizeRecipient(part);
    if (email === null) {
      invalid.push(part);
    } else if (!recipients.includes(email)) {
      recipients.push(email);
    }
  }
  return {recipients, invalid};
}
