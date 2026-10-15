const EMAIL = /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/;

/** A digest recipient as stored (Plan A: lower-case, comma-separated). null = not an e-mail address. */
export function normalizeRecipient(text: string): string | null {
  const value = (text ?? '').trim().toLowerCase();
  return EMAIL.test(value) ? value : null;
}

/** Adds every address in a typed or pasted text; returns what could not be added. */
export function addRecipients(current: string[], input: string): {recipients: string[]; invalid: string[]} {
  const recipients = [...current];
  const invalid: string[] = [];
  for (const part of (input ?? '').split(/[\s,;]+/).filter((p) => p.length)) {
    const email = normalizeRecipient(part);
    if (email === null) {
      invalid.push(part);
    } else if (!recipients.includes(email)) {
      recipients.push(email);
    }
  }
  return {recipients, invalid};
}
