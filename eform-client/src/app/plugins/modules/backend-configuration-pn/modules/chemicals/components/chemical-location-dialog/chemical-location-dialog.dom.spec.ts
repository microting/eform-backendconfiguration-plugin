import {NO_ERRORS_SCHEMA, Pipe, PipeTransform} from '@angular/core';
import {ComponentFixture, TestBed, fakeAsync, tick} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {discardConfirmHarness} from '../../helpers/chemical-dialogs.testing';
import {ChemicalLocationDialogComponent} from './chemical-location-dialog.component';

/** The host's authImage pipe needs the auth store and HttpClient; the template only needs a URL back. */
@Pipe({name: 'authImage', standalone: false})
class FakeAuthImagePipe implements PipeTransform {
  transform(src: string): Promise<string> {
    return Promise.resolve(src);
  }
}

/** Template-level checks for the W3 location dialog (M7). */
describe('ChemicalLocationDialogComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalLocationDialogComponent>;
  let component: ChemicalLocationDialogComponent;
  const find = <T extends HTMLElement = HTMLElement>(selector: string): T | null => fixture.nativeElement.querySelector(selector);

  function render(row: unknown, photoUrl: string | null = null): void {
    TestBed.configureTestingModule({
      declarations: [ChemicalLocationDialogComponent, FakeAuthImagePipe],
      imports: [FormsModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          createLocation: jest.fn().mockReturnValue(of({success: true, model: {id: 44}})),
          uploadLocationPhoto: jest.fn().mockReturnValue(of({success: false, message: 'Nej'})),
        }},
        {provide: MatDialogRef, useValue: {close: jest.fn()}},
        ...discardConfirmHarness({translate: false}).providers,
        {provide: MAT_DIALOG_DATA, useValue: {propertyId: 3, row, photoUrl}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalLocationDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    let next = 0;
    URL.createObjectURL = jest.fn().mockImplementation(() => `blob:preview-${++next}`);
    URL.revokeObjectURL = jest.fn();
  });

  afterEach(() => fixture?.destroy());

  const existing = {location: {id: 2, name: 'Kemirum', description: ''}, openCount: 2, actionCount: 0};

  it('disables Save until a name is typed', async () => {
    render(null);
    expect(find<HTMLButtonElement>('#chemicalLocationSaveBtn')?.disabled).toBe(true);

    component.name = 'Lade';
    fixture.detectChanges();
    await fixture.whenStable();

    expect(find<HTMLButtonElement>('#chemicalLocationSaveBtn')?.disabled).toBe(false);
  });

  it('shows a refused photo as an alert and keeps Save disabled', () => {
    render(null);
    component.name = 'Lade';
    component.onFile(new File(['x'], 'a.gif', {type: 'image/gif'}));
    fixture.detectChanges();

    expect(find('#chemicalLocationPhotoError')?.getAttribute('role')).toBe('alert');
    expect(find('#chemicalLocationPhotoError')?.textContent).toContain('Chemicals: Photo wrong type');
    expect(find<HTMLButtonElement>('#chemicalLocationSaveBtn')?.disabled).toBe(true);
  });

  it('says why a location with placements cannot be archived yet, and only then', () => {
    render(existing);
    expect(find('#chemicalLocationArchiveInfo')?.textContent).toContain('Chemicals: Archive blocked');

    fixture.destroy();
    TestBed.resetTestingModule();
    render(null);
    expect(find('#chemicalLocationArchiveInfo')).toBeNull();
  });

  it('opens the file picker from the photo button', () => {
    render(null);
    const input = find<HTMLInputElement>('#chemicalLocationPhotoInput');
    const click = jest.spyOn(input as HTMLInputElement, 'click').mockImplementation(() => undefined);

    find<HTMLButtonElement>('#chemicalLocationPhotoBtn')?.click();

    expect(click).toHaveBeenCalled();
  });

  it('takes the picked file from the input and clears it (M6)', () => {
    render(null);
    const input = find<HTMLInputElement>('#chemicalLocationPhotoInput') as HTMLInputElement;
    const file = new File(['x'], 'a.jpg', {type: 'image/jpeg'});
    Object.defineProperty(input, 'files', {value: [file], configurable: true});

    input.dispatchEvent(new Event('change'));

    expect(component.photo).toBe(file);
    expect(input.value).toBe('');
  });

  it('previews the picked photo instead of the stored one (M1)', fakeAsync(() => {
    render(existing, '/photo/2?v=p.jpg');
    tick();
    fixture.detectChanges();
    expect(find<HTMLImageElement>('#chemicalLocationPhoto')?.getAttribute('src')).toBe('/photo/2?v=p.jpg');

    component.onFile(new File(['x'], 'a.jpg', {type: 'image/jpeg'}));
    fixture.detectChanges();

    expect(find<HTMLImageElement>('#chemicalLocationPhoto')?.getAttribute('src')).toBe('blob:preview-1');
  }));

  it('switches the title to edit once a create is saved (M5)', () => {
    render(null);
    expect(find('#chemicalLocationTitle')?.textContent).toContain('Chemicals: New location');
    component.name = 'Lade';
    component.onFile(new File(['x'], 'a.jpg', {type: 'image/jpeg'}));

    component.save();
    fixture.detectChanges();

    expect(find('#chemicalLocationTitle')?.textContent).toContain('Chemicals: Edit location');
  });
});
