import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {ActivatedRoute, NavigationEnd, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {filter} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {chemicalsSelectProperty, selectChemicalsPropertyId} from '../../../../state';
import {confirmDiscardOf} from '../../chemicals-unsaved-changes';

const DEFAULT_TITLE_KEY = 'Chemicals: Title';

/**
 * Kemi page frame: the active screen's title, the property picker every screen shares
 * and the router outlet. The selected property lives in the chemicalsState slice AND in
 * the `?propertyId=` query parameter, so a reload (which empties the store) restores it.
 * A switch the user starts (picker, a ?propertyId= from Back/Forward) first asks the
 * active screen, so its unsaved edits are never dropped silently.
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
  /** Mirrors the store (chemicalsState.propertyId); only the store writes it. */
  propertyId: number | null = null;
  /** What the picker shows: follows the store, and briefly the user's pick while the screen is asked. */
  pickerValue: number | null = null;
  isFirstUser = false;
  loaded = false;
  loadFailed = false;
  titleKey = DEFAULT_TITLE_KEY;
  private started = false;
  /** The routed screen, asked before a property switch drops its edits. */
  private activeScreen: unknown = null;

  ngOnInit(): void {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.onNavigationEnd());
    this.refreshTitle();

    this.store.select(selectCurrentUserIsFirstUser)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((isFirstUser) => {
        this.isFirstUser = !!isFirstUser;
        if (isFirstUser && !this.started) {
          this.started = true;
          this.loadProperties();
        }
      });
  }

  /** The picker's choice. On "keep editing" the property stays and the picker is put back. */
  onPropertyChange(propertyId: number | null): void {
    this.pickerValue = propertyId;
    if (propertyId === this.propertyId) {
      this.syncUrl();
      return;
    }
    this.askScreen((discard) => {
      if (discard) {
        this.applyProperty(propertyId);
      } else {
        this.pickerValue = this.propertyId;
      }
    });
  }

  onScreenActivate(screen: unknown): void {
    this.activeScreen = screen;
  }

  onScreenDeactivate(): void {
    this.activeScreen = null;
  }

  private askScreen(then: (discard: boolean) => void): void {
    confirmDiscardOf(this.activeScreen).subscribe(then);
  }

  private applyProperty(propertyId: number | null): void {
    if (propertyId !== this.propertyId) {
      this.store.dispatch(chemicalsSelectProperty({payload: propertyId}));
    }
    this.syncUrl();
  }

  private loadProperties(): void {
    this.store.select(selectChemicalsPropertyId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((id) => {
        this.propertyId = id;
        this.pickerValue = id;
      });

    this.propertiesService.getAllPropertiesDictionary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          // The flag was withdrawn while the request was in flight: nothing may reach the store.
          if (!this.isFirstUser) {
            return;
          }
          this.loaded = true;
          if (!res || !res.success) {
            // The host already toasts the error; show the error state, not "no properties".
            this.loadFailed = true;
            return;
          }
          this.properties = res.model || [];
          this.applyProperty(this.initialPropertyId());
        },
        error: () => {
          this.loaded = true;
          this.loadFailed = true;
        },
      });
  }

  /** The URL wins (a reload empties the store), then the remembered property, then the first one. */
  private initialPropertyId(): number | null {
    const fromUrl = this.propertyIdFromUrl();
    if (this.hasProperty(fromUrl)) {
      return fromUrl;
    }
    if (this.hasProperty(this.propertyId)) {
      return this.propertyId;
    }
    return this.properties.length ? this.properties[0].id : null;
  }

  /**
   * A later navigation carrying another known ?propertyId= (screen links, Back/Forward,
   * a deeplink) is adopted once the active screen agrees; anything else, or "keep
   * editing", gets the URL repaired. No loop: adopting makes the store equal to the
   * URL, so the repair that follows is a no-op.
   */
  private onNavigationEnd(): void {
    this.refreshTitle();
    if (!this.isFirstUser || !this.loaded || this.loadFailed) {
      return;
    }
    const fromUrl = this.propertyIdFromUrl();
    if (this.hasProperty(fromUrl) && fromUrl !== this.propertyId) {
      this.askScreen((discard) => (discard ? this.applyProperty(fromUrl) : this.syncUrl()));
    } else {
      this.syncUrl();
    }
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
