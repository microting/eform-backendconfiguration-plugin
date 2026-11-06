import {Directive, ElementRef, HostBinding, Input, inject} from '@angular/core';

/** The longest property name, in characters, a property dropdown grows to show in full. */
export const PROPERTY_SELECT_MAX_CHARS = 80;

/** Room beside the name: the field's padding, the clear and arrow icons and, in a multi-select, the chip's remove icon. */
const CONTROL_ROOM_PX = 120;

/** The narrowest a property field gets, so short names keep the old filter width. */
const MIN_WIDTH_PX = 200;

/**
 * Sizes the `mat-form-field` of a property `mtx-select` to its widest
 * property name (the first PROPERTY_SELECT_MAX_CHARS characters of it), so the
 * picked name and the open list (ng-select makes the panel exactly as wide as
 * the select) show the whole name instead of cutting it off with an ellipsis.
 *
 * The names are measured in the field's own font on a canvas: a character
 * count cannot say how wide "WWW" is next to "iii".
 *
 * The width is set as inline style on purpose: it has to outrank the component
 * rules that pin filter fields to a fixed width (190px on Compliance, 180-200px
 * elsewhere) and the theme's 320px cap on mtx-select fields, without a
 * selector-specificity race in every component. `flex: 0 0 auto` keeps a flex
 * row from shrinking the field (the filter rows wrap instead), and
 * `grid-column: 1 / -1` gives it a row of its own in the grid filter layouts,
 * where a fixed column would be too narrow. Both are no-ops elsewhere.
 */
@Directive({
  selector: 'mat-form-field[appPropertySelectWidth]',
})
export class PropertySelectWidthDirective {
  private static canvas?: HTMLCanvasElement;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input('appPropertySelectWidth') properties: ReadonlyArray<{name?: string | null}> | null | undefined;

  @HostBinding('style.min-width') readonly minWidth = `${MIN_WIDTH_PX}px`;
  @HostBinding('style.max-width') readonly maxWidth = '100%';
  @HostBinding('style.flex') readonly flex = '0 0 auto';
  @HostBinding('style.grid-column') readonly gridColumn = '1 / -1';

  private measuredFor: ReadonlyArray<{name?: string | null}> | null | undefined;
  private measuredLength = -1;
  private measuredWidth = `${MIN_WIDTH_PX}px`;

  constructor() {
    // A web font that loads after the first measurement changes the widths:
    // measure again on the next change detection.
    document.fonts?.ready.then(() => (this.measuredLength = -1));
  }

  /** Re-measured only when the list (or its length) changes, not on every change detection. */
  @HostBinding('style.width')
  get width(): string {
    const properties = this.properties ?? [];
    if (!this.host.nativeElement.isConnected) {
      // Not in the page yet, so it has no computed font to measure with.
      return this.measuredWidth;
    }
    if (this.properties !== this.measuredFor || properties.length !== this.measuredLength) {
      this.measuredFor = this.properties;
      this.measuredLength = properties.length;
      this.measuredWidth = `${Math.ceil(this.widestName(properties)) + CONTROL_ROOM_PX}px`;
    }
    return this.measuredWidth;
  }

  private widestName(properties: ReadonlyArray<{name?: string | null}>): number {
    const names = properties.map(p => (p?.name ?? '').slice(0, PROPERTY_SELECT_MAX_CHARS));
    PropertySelectWidthDirective.canvas ??= document.createElement('canvas');
    const context = PropertySelectWidthDirective.canvas.getContext('2d');
    const style = getComputedStyle(this.host.nativeElement);
    const fontSizePx = parseFloat(style.fontSize) || 16;
    if (!context) {
      // No 2D canvas: assume every character is as wide as a capital.
      return Math.max(0, ...names.map(n => n.length)) * fontSizePx * 0.75;
    }
    context.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    if ('letterSpacing' in context && style.letterSpacing !== 'normal') {
      // The form field's tracking adds up over 80 characters.
      context.letterSpacing = style.letterSpacing;
    }
    let widest = 0;
    for (const name of names) {
      widest = Math.max(widest, context.measureText(name).width);
    }
    return widest;
  }
}
