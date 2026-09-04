import {Component, Inject, OnDestroy, ViewEncapsulation} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject} from 'rxjs';
import {takeUntil} from 'rxjs/operators';

/**
 * i18n keys held as CONSTANTS rather than written inline in the template.
 *
 * Angular's interpolation parser terminates a `{{ … }}` block at the first
 * `}}` it meets, so a key literal such as `'Image {{index}} of {{count}}'`
 * written inside an interpolation is a template PARSE error — it closes the
 * binding halfway through the string. Every parameterised key this component
 * uses is therefore exposed as a property and referenced by name.
 */
const KEY_TITLE_MANY = 'Images for case {{caseId}}';
const KEY_TITLE_ONE = '1 image for case {{caseId}}';
const KEY_CAPTION = 'Image {{index}} of {{count}}';
const KEY_ALT_WITH_CASE = 'Image {{index}} of {{count}} for case {{caseId}}';
const KEY_THUMB = 'Image {{index}}';

export interface CalendarImageLightboxData {
  /** SDK uploaded-data file names, rendered via template-files/get-image. */
  images: string[];
  /**
   * OPTIONAL parallel list of SERVER-EMITTED `_300_` thumbnail file names, one
   * per entry of `images` and in the same order. Used for the 64x64 thumbnail
   * strip only; the stage always renders `images`.
   *
   * SUPPLYING IT IS WHAT TURNS THE STRIP ON. A caller that omits it (the
   * calendar's task card does) gets no strip at all — see `showThumbs`. A
   * partially-filled list is fine: an entry that is missing, `null` or empty
   * falls back to the corresponding `images` entry, so a case whose one
   * derivative failed still shows a complete strip. The names are NEVER
   * composed here — see the note in the constructor.
   */
  thumbnails?: (string | null)[] | null;
  startIndex: number;
  /**
   * The three fields below are OPTIONAL and drive the HEADER only.
   *
   * The historical task card (task-preview-modal) opens this lightbox with
   * `images` + `startIndex` and nothing else; the standalone Compliance page's
   * Rapport view (#1168) supplies all three. When `caseId` is absent the whole
   * header — title and meta line — is not rendered, which is the ONLY thing
   * these three fields gate. They do not, and are not meant to, hold the rest
   * of the lightbox at its pre-#1168 shape; see the class comment for what the
   * calendar caller's lightbox actually gained.
   */
  caseId?: number | null;
  /** The task title. First half of the meta line. */
  caseTitle?: string | null;
  /** The property name. Second half of the meta line. */
  propertyName?: string | null;
}

/**
 * Centered dark lightbox for a case's images.
 *
 * Used by TWO callers: the calendar's historical task card and the standalone
 * Compliance page's Rapport view. It lives in `CalendarModule` and is EXPORTED
 * from it; `ComplianceReportModule` already imports that module (for the
 * completion modal), so nothing had to move. It belongs to the task card, not
 * to the calendar's compliance view mode — #1170 must not delete it.
 *
 * ‹ › wrap around. With exactly one image the previous/next buttons, the
 * counter and the thumbnail strip are all ABSENT FROM THE DOM, not merely
 * invisible.
 *
 * THE THUMBNAIL STRIP HAS A SECOND CONDITION: it renders only when the opener
 * also supplied `thumbnails`, i.e. real server-emitted `_300_` names. Without
 * that gate the strip would fall back element-wise to the FULL-SIZE urls and
 * the calendar's task card — which passes none — would pay for n extra
 * authenticated downloads of full-resolution images (and n more base64 strings
 * held in memory) to fill 64x64 boxes, on top of the n it already fetched for
 * the card itself. Its `images` are the reply element's raw
 * `uploadedDataObj.fileName`, the ORIGINAL upload rather than even the `_700_`
 * derivative, so those downloads are the largest files in play.
 *
 * WHAT THE CALENDAR CALLER'S LIGHTBOX ACTUALLY LOOKS LIKE NOW. The strip is the
 * one thing that was RESTORED to its pre-#1168 behaviour for that caller: it is
 * suppressed, exactly as before, because it did not exist before. Everything
 * else on this component is a DELIBERATE, SHARED improvement that the task card
 * gets too — it does not keep the DOM it had. Against `stable`, seven things
 * changed for it:
 *
 *   1. a `<figcaption>` caption is NEW, and it is gated on `count > 0`, NOT on
 *      `hasMultiple` — so it renders for EVERY caller, including at n = 1;
 *   2. the counter used to render unconditionally (`stable`'s template has no
 *      `*ngIf` on it); it is now gated on `hasMultiple` and is therefore ABSENT
 *      at n = 1, where it previously said `1 / 1`;
 *   3. the close button's i18n key went from `Close` to `Close gallery`, so its
 *      accessible name changed (`Luk` → `Luk galleri`);
 *   4. the stage `<img alt>` went from a hardcoded `""` to a populated
 *      description (`altKey`), so the image is no longer decorative;
 *   5. every class was renamed `.lightbox*` → `.calendar-lightbox__*`, and the
 *      stylesheet moved from colour literals to theme custom properties;
 *   6. the stage image's cap went from `max-height: 78vh` to `70vh` (the
 *      caption now shares the column);
 *   7. both openers now pass `panelClass: 'calendar-lightbox-panel'`, which
 *      brings the below-720px full-bleed layout the task card never had.
 *
 * Only the header (title + meta line) is genuinely caller-specific, and only
 * because it needs a `caseId` the task card has no reason to pass. The counter,
 * the caption and the two nav buttons cost no downloads, which is why they are
 * gated on content alone rather than on the opener.
 *
 * Esc, the backdrop and ArrowLeft/ArrowRight are handled here or by MatDialog.
 * The prototype's `cancel`-event interception and its `src`-attribute removal
 * on close are deliberately NOT ported: MatDialog already closes on Escape and
 * on a backdrop click (the openers pass `disableClose: false`), and this
 * component is constructed fresh on every open and destroyed on close — there
 * is no residual index and no stale `src` to clear.
 */
@Component({
  standalone: false,
  selector: 'app-calendar-image-lightbox',
  templateUrl: './calendar-image-lightbox.component.html',
  styleUrls: ['./calendar-image-lightbox.component.scss'],
  // The overlay pane (`.calendar-lightbox-panel`, set via
  // MatDialogConfig.panelClass by both callers) and the dialog container it
  // wraps live OUTSIDE this component's own DOM subtree, so the below-720px
  // full-bleed rules need global scope to reach them. Same reason, and the same
  // shape, as AdhocTaskDrawerComponent. Every class in the stylesheet is
  // `calendar-lightbox`-prefixed precisely because those rules are global.
  encapsulation: ViewEncapsulation.None,
})
export class CalendarImageLightboxComponent implements OnDestroy {
  currentIndex: number;

  /**
   * The image URLs, built ONCE.
   *
   * NOT because a getter would re-download: `authImage` is a PURE pipe, and
   * Angular's purity check is `===`, which for a string is VALUE equality — a
   * getter returning an equal URL string re-runs nothing, which is why the
   * pre-existing `imageSrc(f) | authImage` binding in the task card
   * (task-preview-modal.component.html:120) has always been safe.
   *
   * Precomputing is still the right shape: it allocates each URL once instead
   * of on every change-detection pass, and — the part that does bite, over on
   * `thumbSrcs` — it keeps the ARRAY reference `*ngFor` iterates stable. An
   * array is compared by reference, not by value, so a getter returning a
   * freshly mapped one would hand `*ngFor` a new collection every cycle and
   * re-create the <img> elements, which WOULD re-fetch.
   */
  readonly srcs: string[];

  /**
   * The 64x64 strip's URLs, built ONCE alongside `srcs` and for the same
   * reason (stable array identity for `*ngFor`).
   *
   * Index-aligned with `srcs`; each entry is the caller's `_300_` name when it
   * supplied one and the full-size URL from `srcs` otherwise. That fallback
   * exists for a PARTIALLY-filled list only — a caller that supplies no
   * thumbnails at all gets no strip, see `showThumbs`.
   */
  readonly thumbSrcs: string[];

  /**
   * `true` when the opener supplied at least one usable `_300_` name. The
   * strip's second gate — see the class comment for why the full-size fallback
   * must not be allowed to fill it on its own.
   */
  private readonly hasThumbnailNames: boolean;

  /** `true` once the header's data is present — see CalendarImageLightboxData. */
  readonly showHeader: boolean;
  /** `{task title} · {property name}`, either half omitted when empty. */
  readonly metaLine: string;
  readonly titleKey: string;
  readonly titleParams: {caseId: number | null};

  readonly captionKey = KEY_CAPTION;
  readonly thumbKey = KEY_THUMB;

  private destroy$ = new Subject<void>();

  constructor(
    private dialogRef: MatDialogRef<CalendarImageLightboxComponent>,
    @Inject(MAT_DIALOG_DATA) public data: CalendarImageLightboxData,
  ) {
    const images = data?.images ?? [];
    // SERVER-EMITTED names only. The `_700_` / `_300_` derivative names are
    // built server-side (ReportEformGroupModel.ImageNames /
    // ComplianceReportImageModel.fileName, and the reply element's
    // uploadedDataObj.fileName for the task card); this client never composes
    // one, so a change to that shape cannot silently break here.
    this.srcs = images.map((name) => `/api/template-files/get-image/${name}`);
    // Same rule for the thumbnails: the `_300_` names are server-emitted
    // (ComplianceReportImageModel.thumbnailFileName) and only SELECTED here.
    // A missing/blank entry falls back to the full-size URL already computed
    // above, which is why this maps over `srcs` rather than over `images`.
    const thumbnails = data?.thumbnails ?? [];
    this.hasThumbnailNames = thumbnails.some((name) => !!name);
    this.thumbSrcs = this.srcs.map((src, index) => {
      const name = thumbnails[index];
      return name ? `/api/template-files/get-image/${name}` : src;
    });
    const count = this.srcs.length;
    this.currentIndex = count > 0
      ? Math.min(Math.max(data.startIndex ?? 0, 0), count - 1)
      : 0;

    this.showHeader = data?.caseId != null;
    this.metaLine = [data?.caseTitle, data?.propertyName]
      .map((part) => (part ?? '').trim())
      // Both halves optional, joined by the separator only when both are
      // present — no leading or trailing ' · '.
      .filter((part) => part.length > 0)
      .join(' · ');
    this.titleKey = count === 1 ? KEY_TITLE_ONE : KEY_TITLE_MANY;
    this.titleParams = {caseId: data?.caseId ?? null};

    // ArrowLeft / ArrowRight while the dialog is open.
    //
    // `keydownEvents()` is NOT a DOM listener on this component's subtree: CDK
    // routes it through OverlayKeyboardDispatcher, which listens once on
    // `body` (`overlay-module.mjs`: `_renderer.listen('body', 'keydown', …)`,
    // attached lazily when the first overlay is added) and hands the event to
    // the TOPMOST overlay that has subscribers. What that buys over a `@HostListener('document:keydown')`
    // is therefore precise: only one overlay reacts, so a dialog stacked above
    // this one takes the arrows instead of both firing; and because this is a
    // MODAL dialog, its backdrop and focus trap keep the pointer and focus off
    // the page behind it in the first place. The subscription also dies with
    // the dialog ref, so nothing survives close.
    //
    // Escape is NOT handled here: MatDialog already closes on it.
    this.dialogRef
      .keydownEvents()
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => this.onKeydown(event));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get count(): number {
    return this.srcs.length;
  }

  /**
   * The switch behind every "hidden with one image" rule: previous, next, the
   * counter and the thumbnail strip. Each is `*ngIf`-ed on it, so with one
   * image none of the four is in the DOM.
   */
  get hasMultiple(): boolean {
    return this.count > 1;
  }

  /**
   * The thumbnail strip's gate: `hasMultiple` AND real `_300_` names from the
   * opener. Necessarily false whenever `hasMultiple` is, so the one-image rule
   * above still holds for all four controls.
   */
  get showThumbs(): boolean {
    return this.hasMultiple && this.hasThumbnailNames;
  }

  get currentSrc(): string {
    return this.srcs[this.currentIndex] ?? '';
  }

  /** `{i, n}` for the caption and the counter. */
  get captionParams(): {index: number; count: number} {
    return {index: this.currentIndex + 1, count: this.count};
  }

  /**
   * `Billede {i} af {n} til sag {sagsNr}` when the case id is known, and the
   * plain caption otherwise — the calendar caller passes no case id.
   */
  get altKey(): string {
    return this.showHeader ? KEY_ALT_WITH_CASE : KEY_CAPTION;
  }

  get altParams(): {index: number; count: number; caseId: number | null} {
    return {
      index: this.currentIndex + 1,
      count: this.count,
      caseId: this.data?.caseId ?? null,
    };
  }

  thumbParams(index: number): {index: number} {
    return {index: index + 1};
  }

  prev() {
    const count = this.count;
    if (count === 0) {
      return;
    }
    this.currentIndex = (this.currentIndex - 1 + count) % count;
  }

  next() {
    const count = this.count;
    if (count === 0) {
      return;
    }
    this.currentIndex = (this.currentIndex + 1) % count;
  }

  /** Thumbnail activation. Out-of-range indices are ignored rather than clamped. */
  select(index: number) {
    if (index < 0 || index >= this.count) {
      return;
    }
    this.currentIndex = index;
  }

  close() {
    this.dialogRef.close();
  }

  trackByIndex(index: number): number {
    return index;
  }

  private onKeydown(event: KeyboardEvent): void {
    if (!this.hasMultiple) {
      return;
    }
    // Guard: a nested TEXT-ENTRY control owns its own caret movement. The
    // thumbnail buttons are deliberately NOT excluded — arrowing between images
    // while a thumb has focus is the point.
    if (this.isTextEntry(event.target)) {
      return;
    }
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.prev();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.next();
    }
  }

  private isTextEntry(target: EventTarget | null): boolean {
    const element = target as HTMLElement | null;
    if (!element || !element.tagName) {
      return false;
    }
    const tag = element.tagName.toUpperCase();
    return (
      tag === 'INPUT' ||
      tag === 'TEXTAREA' ||
      tag === 'SELECT' ||
      element.isContentEditable === true
    );
  }
}
