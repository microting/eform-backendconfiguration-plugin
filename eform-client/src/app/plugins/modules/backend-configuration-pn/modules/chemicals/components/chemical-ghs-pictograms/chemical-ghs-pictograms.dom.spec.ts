import {describe, it, expect, afterEach} from 'vitest';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {ChemicalGhsPictogramsComponent} from './chemical-ghs-pictograms.component';
import {detectChanges} from '../../../../testing/detect-changes';

/** 855 check G1: the GHS codes were red text chips; they are the official diamonds, the code kept as the text alternative. */
describe('ChemicalGhsPictogramsComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalGhsPictogramsComponent>;
  const all = (selector: string): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll(selector));

  function render(codes: string[] | null): void {
    TestBed.configureTestingModule({imports: [ChemicalGhsPictogramsComponent]});
    fixture = TestBed.createComponent(ChemicalGhsPictogramsComponent);
    fixture.componentRef.setInput('codes', codes);
    detectChanges(fixture);
  }

  afterEach(() => fixture?.destroy());

  it('draws each known code as its diamond, named by the code', () => {
    render(['GHS07', 'GHS09']);

    const images = all('img') as HTMLImageElement[];
    expect(images.map((i) => i.getAttribute('alt'))).toEqual(['GHS07', 'GHS09']);
    expect(images.map((i) => i.getAttribute('title'))).toEqual(['GHS07', 'GHS09']);
    expect(images.every((i) => i.getAttribute('src')?.startsWith('data:image/svg+xml;base64,'))).toBe(true);
    expect(all('.ghs-chip')).toHaveLength(0);
  });

  it('keeps a code without official artwork as a text chip', () => {
    render(['GHS07', 'GHS99']);

    expect((all('img') as HTMLImageElement[]).map((i) => i.alt)).toEqual(['GHS07']);
    expect(all('.ghs-chip').map((c) => c.textContent?.trim())).toEqual(['GHS99']);
  });

  it('renders nothing for a product without pictograms', () => {
    render(null);

    expect(fixture.nativeElement.textContent.trim()).toBe('');
    expect(all('img')).toHaveLength(0);
  });
});
