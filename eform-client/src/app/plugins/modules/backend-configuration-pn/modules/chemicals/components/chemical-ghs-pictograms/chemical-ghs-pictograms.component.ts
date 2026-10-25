import {ChangeDetectionStrategy, Component, Input} from '@angular/core';
import {NgFor, NgIf} from '@angular/common';
import {ghsPictogramSrc} from '../../helpers';

/** One pictogram: its code and the image to draw, or null for a code without artwork. */
export interface ChemicalGhsPictogramItem {
  code: string;
  src: string | null;
}

/**
 * The GHS pictograms of a product (W1 table, W2 side panel): the official diamonds, the code as the text
 * alternative and tooltip. A code without official artwork stays a text chip.
 */
@Component({
  selector: 'app-chemical-ghs-pictograms',
  imports: [NgFor, NgIf],
  templateUrl: './chemical-ghs-pictograms.component.html',
  styleUrls: ['./chemical-ghs-pictograms.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChemicalGhsPictogramsComponent {
  items: ChemicalGhsPictogramItem[] = [];

  /** Diamond edge in px: 28 in the table, larger in the side panel. */
  @Input() size = 28;

  @Input() set codes(codes: string[] | null | undefined) {
    this.items = (codes ?? []).map((code) => ({code, src: ghsPictogramSrc(code)}));
  }
}
