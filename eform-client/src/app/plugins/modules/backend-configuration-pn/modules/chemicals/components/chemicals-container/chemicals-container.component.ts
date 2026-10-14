import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {ActivatedRoute, NavigationEnd, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {filter, take} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {chemicalsSelectProperty, selectChemicalsPropertyId} from '../../../../state';

const DEFAULT_TITLE_KEY = 'Chemicals: Title';

/**
 * Kemi page frame: the active screen's title, the property picker every screen shares
 * and the router outlet. The selected property lives in the chemicalsState slice AND in
 * the `?propertyId=` query parameter, so a reload (which empties the store) restores it.
 *
 * TODO(chemistry-GA): the REST controllers are first-user-only until GA, so anyone else
 * gets a notice and no chemistry call is made. Remove the gate together with the
 * server-side [FirstUserOnly] attributes.
 */
@Component({
  selector: 'app-chemicals-container',
  templateUrl: './chemicals-container.component.html',
  styleUrls: ['./chemicals-container.component.scss'],
  standalone: false,
})
export class ChemicalsContainerComponent implements OnInit {
  private store = inject(Store);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private destroyRef = inject(DestroyRef);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  properties: CommonDictionaryModel[] = [];
  propertyId: number | null = null;
  isFirstUser = false;
  loaded = false;
  titleKey = DEFAULT_TITLE_KEY;

  ngOnInit(): void {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.refreshTitle();
        this.syncUrl();
      });
    this.refreshTitle();

    this.store.select(selectCurrentUserIsFirstUser)
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((isFirstUser) => {
        this.isFirstUser = !!isFirstUser;
        if (this.isFirstUser) {
          this.loadProperties();
        }
      });
  }

  onPropertyChange(propertyId: number | null): void {
    this.propertyId = propertyId;
    this.store.dispatch(chemicalsSelectProperty({payload: propertyId}));
    this.syncUrl();
  }

  private loadProperties(): void {
    this.store.select(selectChemicalsPropertyId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((id) => (this.propertyId = id));

    this.propertiesService.getAllPropertiesDictionary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((res) => {
        this.properties = (res && res.success && res.model) || [];
        this.loaded = true;
        const fromUrl = this.propertyIdFromUrl();
        const target = [fromUrl, this.propertyId].find((id) => this.hasProperty(id))
          ?? (this.properties.length ? this.properties[0].id : null);
        if (target !== this.propertyId) {
          this.onPropertyChange(target);
        } else {
          this.syncUrl();
        }
      });
  }

  private hasProperty(id: number | null): boolean {
    return id !== null && this.properties.some((p) => p.id === id);
  }

  private propertyIdFromUrl(): number | null {
    const raw = this.route.snapshot.queryParamMap.get('propertyId');
    const id = raw ? Number(raw) : NaN;
    return Number.isInteger(id) ? id : null;
  }

  /** Keeps ?propertyId= equal to the selected property (menu clicks drop it). */
  private syncUrl(): void {
    if (!this.isFirstUser || !this.loaded || this.propertyId === null
      || this.propertyIdFromUrl() === this.propertyId) {
      return;
    }
    void this.router.navigate([], {
      queryParams: {propertyId: this.propertyId},
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private refreshTitle(): void {
    this.titleKey = this.route.firstChild?.snapshot.data?.['titleKey'] ?? DEFAULT_TITLE_KEY;
  }
}
