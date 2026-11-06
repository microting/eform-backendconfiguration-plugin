import {Directive, HostBinding, Input} from '@angular/core';

/** The longest property name, in characters, a property dropdown grows to show in full. */
export const PROPERTY_SELECT_MAX_CHARS = 80;

/**
 * Sizes the `mat-form-field` of a property `mtx-select` to its longest
 * property name, capped at PROPERTY_SELECT_MAX_CHARS, so the picked name and
 * the open list (ng-select makes the panel exactly as wide as the select) show
 * the whole name instead of cutting it off with an ellipsis.
 *
 * The width is set as inline style on purpose: it has to outrank the component
 * rules that pin filter fields to a fixed width (190px on Compliance, 180-200px
 * elsewhere) and the theme's 320px cap on mtx-select fields, without a
 * selector-specificity race in every component. `flex: 0 0 auto` keeps a flex
 * row from shrinking the field (the filter rows wrap instead), and
 * `grid-column: 1 / -1` gives it a row of its own in the grid filter layouts,
 * where a fixed column would be too narrow. Both are no-ops elsewhere.
 *
 * `ch` is the width of a digit; names mix capitals, digits and punctuation,
 * so each character is given 1.1ch, plus room for the field's padding, the
 * clear and arrow icons and, in a multi-select, the chip's remove icon.
 */
@Directive({
  selector: 'mat-form-field[appPropertySelectWidth]',
})
export class PropertySelectWidthDirective {
  @Input('appPropertySelectWidth') properties: ReadonlyArray<{name?: string | null}> | null | undefined;

  @HostBinding('style.min-width') readonly minWidth = '200px';
  @HostBinding('style.max-width') readonly maxWidth = '100%';
  @HostBinding('style.flex') readonly flex = '0 0 auto';
  @HostBinding('style.grid-column') readonly gridColumn = '1 / -1';

  @HostBinding('style.width')
  get width(): string {
    let chars = 0;
    for (const property of this.properties ?? []) {
      chars = Math.max(chars, property?.name?.length ?? 0);
    }
    return `calc(${(Math.min(chars, PROPERTY_SELECT_MAX_CHARS) * 1.1).toFixed(1)}ch + 120px)`;
  }
}
