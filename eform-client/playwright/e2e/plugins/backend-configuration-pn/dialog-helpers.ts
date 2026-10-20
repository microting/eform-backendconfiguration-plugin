/**
 * Typing into Angular Material dialogs without racing their opening.
 *
 * A MatDialog's DOM is attached before it is usable for typing, and a test that
 * types too early fails at random:
 *  - The content's first change detection runs a frame after the DOM exists
 *    (the host coalesces events, `provideZoneChangeDetection({ eventCoalescing: true })`).
 *    In that pass ngModel / formControlName write their initial value and wipe
 *    anything typed before it.
 *  - MatDialog moves focus to its first tabbable field (or to the container) only
 *    after the open animation has finished, which can be after the title has text.
 *    `fill()` focuses its target and then inserts the text into whatever has focus,
 *    so a late autofocus sends the text into a different field. One example is a
 *    property name that ended up appended to the CVR number while the required name
 *    stayed empty and Save stayed disabled.
 *
 * {@link dialogReady} waits until neither can happen any more, and
 * {@link fillInDialog} also checks that the value actually landed in the field.
 * Neither helper retries: a failure points at the field and the dialog state.
 */
import { errors, expect, Locator, Page } from '@playwright/test';
import { UI_TIMEOUT } from './wait-helpers';

/**
 * Waits until the MatDialog that contains `field` has rendered and settled:
 *  - its `[mat-dialog-title]` (when it has one) has text, so the first change detection has run;
 *  - the open animation is over (`mdc-dialog--open` set, `mdc-dialog--opening` cleared);
 *  - focus is inside the dialog, or inside an overlay opened from it such as a select panel.
 *    Every autoFocus mode ends with focus there, so this means the deferred autofocus has
 *    already happened. The one exception: once the dialog has been open for more than
 *    {@link AUTOFOCUS_WINDOW_FRAMES} frames, focus on <body> is accepted too. That is a dialog
 *    open for a while whose focused element was re-rendered away, so focus fell back to <body>.
 *    Autofocus runs only once, at open, so nothing will move focus back into the dialog;
 *  - the focused element has stayed the same for two more animation frames.
 * Once a dialog has settled it is marked, and every later call for the same dialog returns at once
 * without waiting again. That is safe because the race only exists while the dialog opens. A field
 * rendered later (a tab, an inline editor) still has fillInDialog's toHaveValue check behind it.
 */
/**
 * How long after the open animation the deferred autofocus may still land. CDK moves focus in the
 * render after the animation ends, so this is generous.
 */
const AUTOFOCUS_WINDOW_FRAMES = 10;

export async function dialogReady(page: Page, field: Locator): Promise<void> {
  await field.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const handle = await field.elementHandle({ timeout: UI_TIMEOUT });
  if (!handle) {
    throw new Error(`dialogReady: ${field} is not attached`);
  }
  try {
    await page.waitForFunction(
      // The handle is typed `unknown`: typing it as Element makes waitForFunction's overloads recurse too deep (TS2589).
      ([arg, autofocusWindow]: [unknown, number]) => {
        const el = arg as Element;
        type Settle = { frames: number; openFrames: number; focused: Element | null };
        const dialog = el.closest('mat-dialog-container') as (HTMLElement & { __pwSettle?: Settle }) | null;
        if (!dialog) {
          throw new Error('dialogReady: the field is not inside a mat-dialog-container');
        }
        if (dialog.dataset.pwSettled) {
          return true;
        }
        const title = dialog.querySelector('[mat-dialog-title], [matDialogTitle], .mat-mdc-dialog-title');
        const rendered = !title || (title.textContent ?? '').trim() !== '';
        const opened = dialog.classList.contains('mdc-dialog--open') && !dialog.classList.contains('mdc-dialog--opening');
        const focused = document.activeElement;
        let focusInside = !!focused && dialog.contains(focused);
        if (!focusInside && focused) {
          const panes = Array.from(document.querySelectorAll('.cdk-overlay-pane'));
          const focusedPane = focused.closest('.cdk-overlay-pane');
          focusInside = !!focusedPane && panes.indexOf(focusedPane) > panes.indexOf(dialog.closest('.cdk-overlay-pane') as Element);
        }
        const state = dialog.__pwSettle ?? (dialog.__pwSettle = { frames: 0, openFrames: 0, focused: null });
        state.openFrames = rendered && opened ? state.openFrames + 1 : 0;
        // After the autofocus window, focus on <body> means it fell out of a long-open dialog, not that autofocus is pending.
        const onBody = !focused || focused === document.body || focused === document.documentElement;
        const focusSettled = focusInside || (onBody && state.openFrames > autofocusWindow);
        state.frames = rendered && opened && focusSettled ? (state.focused === focused ? state.frames + 1 : 1) : 0;
        state.focused = focused;
        if (state.frames >= 3) {
          dialog.dataset.pwSettled = 'true';
          return true;
        }
        return false;
      },
      [handle, AUTOFOCUS_WINDOW_FRAMES] as [unknown, number],
      { polling: 'raf', timeout: UI_TIMEOUT }
    );
  } catch (error) {
    if (!(error instanceof errors.TimeoutError)) {
      throw error;
    }
    const state = await handle
      .evaluate((el: Element) => {
        const dialog = el.closest('mat-dialog-container');
        const title = dialog?.querySelector('[mat-dialog-title], [matDialogTitle], .mat-mdc-dialog-title');
        const focused = document.activeElement;
        return `classes="${dialog?.className}", title="${title?.textContent?.trim() ?? '(none)'}", ` +
          `focused=<${focused?.tagName.toLowerCase()} id="${focused?.id}">`;
      })
      .catch(() => '(dialog gone)');
    throw new Error(`The dialog holding ${field} did not settle within ${UI_TIMEOUT}ms: ${state}`);
  } finally {
    await handle.dispose();
  }
}

/**
 * {@link dialogReady}, then one `fill()`, then a check that the field holds exactly `value`.
 * If the text went into another field, or the field was wiped, this fails here and names
 * the field, rather than later as a disabled Save or a request that was never sent.
 */
export async function fillInDialog(page: Page, field: Locator, value: string): Promise<void> {
  await dialogReady(page, field);
  await field.fill(value, { timeout: UI_TIMEOUT });
  await expect(field).toHaveValue(value, { timeout: UI_TIMEOUT });
}
