import {ChangeDetectionStrategy, Component, Input} from '@angular/core';
import {NgFor} from '@angular/common';

/** One pictogram: its code and the image to draw, or null for a code without artwork. */
export interface ChemicalGhsPictogramItem {
  code: string;
  src: string | null;
}

/** The GHS pictograms of a product (W1 table, W2 side panel). Stub: text chips only. */
@Component({
  selector: 'app-chemical-ghs-pictograms',
  imports: [NgFor],
  templateUrl: './chemical-ghs-pictograms.component.html',
  styleUrls: ['./chemical-ghs-pictograms.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChemicalGhsPictogramsComponent {
  items: ChemicalGhsPictogramItem[] = [];

  @Input() set codes(codes: string[] | null | undefined) {
    this.items = (codes ?? []).map((code) => ({code, src: null}));
  }
}
