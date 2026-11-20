import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationCancel,
  NavigationEnd,
  NavigationError,
  NavigationSkipped,
  NavigationStart,
  Router,
  RouterOutlet,
} from '@angular/router';
import {Store} from '@ngrx/store';
import {CommonDictionaryModel} from 'src/app/common/models';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {chemicalsSelectAllProperties, chemicalsSelectProperty, chemicalsSetProperties, selectChemicalsScope} from '../../../../state';
import {ChemicalUnsavedChangesAware, confirmDiscardOf} from '../../chemicals-unsaved-changes';
import {NgIf} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslatePipe, TranslateService} from '@ngx-translate/core';
import {MatCard} from '@angular/material/card';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MtxSelect} from '@ng-matero/extensions/select';
import {PropertySelectWidthDirective} from '../../../../directives';

const DEFAULT_TITLE_KEY = 'Chemicals: Title';

/** The picker value of "All" (Beholdning, Historik); never a property id, which is a positive integer. */
export const CHEMICALS_ALL_PROPERTIES = 'all' as const;

/** What the picker can hold: one property, or "All". */
export type ChemicalsPickerValue = number | typeof CHEMICALS_ALL_PROPERTIES;

export interface ChemicalsPickerItem {
  id: ChemicalsPickerValue;
  name: string;
}

/**
 * Kemi page frame: the active screen's title, the property picker every screen shares
 * and the router outlet. The selected property lives in the chemicalsState slice AND in
 * the `?propertyId=` query parameter, so a reload (which empties the store) restores it.
 * A switch the user starts (picker, a ?propertyId= from Back/Forward) first asks the
 * active screen, so its unsaved edits are never dropped silently.
 *
 * Beholdning and Historik (route data allProperties) also offer "All": the URL without
 * ?propertyId= is "All" there, whether opened, reached by a menu click or by Back; only a
 * ?propertyId= (the digest deeplink, a picked property) opens one property. Moving on to a
 * screen without "All" selects the first property.
 *
 * TODO(chemistry-GA): the REST controllers are first-user-only until GA, so anyone else
 * gets a notice and no chemistry call is made. Remove the gate together with the
 * server-side [FirstUserOnly] attributes.
 */
@Component({
  selector: 'app-chemicals-container',
  imports: [NgIf, FormsModule, TranslatePipe, RouterOutlet, MatCard, MatFormField, MatLabel, MtxSelect, PropertySelectWidthDirective],
  templateUrl: './chemicals-container.component.html',
  styleUrls: ['./chemicals-container.component.scss'],
})
export class ChemicalsContainerComponent implements OnInit {
  private store = inject(Store);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private destroyRef = inject(DestroyRef);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private translate = inject(TranslateService);

  properties: CommonDictionaryModel[] = [];
  /** What the picker offers: "All" first on the screens that have it, then the properties. */
  pickerItems: ChemicalsPickerItem[] = [];
  /** Mirrors the store (chemicalsState.propertyId); only the store writes it. */
  propertyId: number | null = null;
  /** Mirrors the store (chemicalsState.allProperties). */
  allProperties = false;
  /** What the picker shows: follows the store, and briefly the user's pick while the screen is asked. */
  pickerValue: ChemicalsPickerValue | null = null;
  /** The active screen offers "All" (its route data). */
  screenAllowsAll = false;
  private allLabel = '';
  isFirstUser = false;
  loaded = false;
  loadFailed = false;
  titleKey = DEFAULT_TITLE_KEY;
  private started = false;
  /** The routed screen, asked before a property switch drops its edits. */
  private activeScreen: unknown = null;
  /**
   * A router navigation is between its start and its end (R-2). The ?propertyId= write is a
   * navigation of its own and would replace (cancel) it, e.g. the user's first menu click
   * while the properties are still loading, so the write waits for the navigation to end.
   */
  private navigating = false;
  /**
   * A selection made while a navigation was in flight has not reached the URL yet: that navigation's
   * end must write it, not read the missing ?propertyId= as "All".
   */
  private urlBehind = false;
  private pendingSync: ReturnType<typeof setTimeout> | undefined;

  ngOnInit(): void {
    // The container is created by a navigation that may not have ended yet.
    this.navigating = this.router.currentNavigation() !== null;
    // A write scheduled after a cancelled navigation must not touch the URL of a page we left.
    this.destroyRef.onDestroy(() => clearTimeout(this.pendingSync));
    this.router.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => this.onRouterEvent(event));
    this.refreshScreen();
    this.translate.stream('Chemicals: All')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((label: string) => {
        this.allLabel = label;
        this.refreshPickerItems();
      });

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

  /** The picker's choice. On "keep editing" the selection stays and the picker is put back. */
  onPropertyChange(value: ChemicalsPickerValue | null): void {
    this.pickerValue = value;
    if (value === this.selection) {
      this.syncUrl();
      return;
    }
    this.askScreen((discard) => {
      if (discard) {
        this.apply(value);
      } else {
        // Put back after the picker has been rendered with the pick: a synchronous revert
        // leaves ngModel's binding unchanged, and the select would keep showing the pick.
        setTimeout(() => (this.pickerValue = this.selection));
      }
    });
  }

  /** The store's selection as a picker value. */
  private get selection(): ChemicalsPickerValue | null {
    return this.allProperties ? CHEMICALS_ALL_PROPERTIES : this.propertyId;
  }

  /** The active screen has an uncancellable request in flight; switching now would race it. */
  get screenBusy(): boolean {
    return (this.activeScreen as Partial<ChemicalUnsavedChangesAware> | null)?.busy === true;
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

  private apply(value: ChemicalsPickerValue | null): void {
    if (value !== this.selection) {
      this.store.dispatch(value === CHEMICALS_ALL_PROPERTIES
        ? chemicalsSelectAllProperties()
        : chemicalsSelectProperty({payload: value}));
    }
    this.syncUrl();
  }

  private loadProperties(): void {
    this.store.select(selectChemicalsScope)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((scope) => {
        this.propertyId = scope.propertyId;
        this.allProperties = scope.allProperties;
        this.pickerValue = this.selection;
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
          this.store.dispatch(chemicalsSetProperties({payload: this.properties}));
          this.refreshScreen();
          this.apply(this.initialSelection());
        },
        error: () => {
          this.loaded = true;
          this.loadFailed = true;
        },
      });
  }

  /**
   * The URL wins (a reload empties the store), then "All" on a screen that has it, then the remembered
   * property, then the first one. No properties: nothing ("All" of nothing would hide "no properties").
   */
  private initialSelection(): ChemicalsPickerValue | null {
    const fromUrl = this.propertyIdFromUrl();
    if (this.hasProperty(fromUrl)) {
      return fromUrl;
    }
    if (this.screenAllowsAll && this.properties.length) {
      return CHEMICALS_ALL_PROPERTIES;
    }
    if (this.hasProperty(this.propertyId)) {
      return this.propertyId;
    }
    return this.properties.length ? this.properties[0].id : null;
  }

  private onRouterEvent(event: unknown): void {
    if (event instanceof NavigationStart) {
      this.navigating = true;
    } else if (event instanceof NavigationEnd) {
      this.navigating = false;
      this.onNavigationEnd();
    } else if (event instanceof NavigationCancel || event instanceof NavigationError || event instanceof NavigationSkipped) {
      this.navigating = false;
      // A cancel is also how the router reports a navigation replaced by a newer one, whose start
      // follows in the same tick: write after that tick, and only if nothing is navigating then.
      clearTimeout(this.pendingSync);
      this.pendingSync = setTimeout(() => this.syncUrl());
    }
  }

  /**
   * A later navigation carrying another known ?propertyId= (screen links, Back/Forward,
   * a deeplink) is adopted once the active screen agrees; anything else, or "keep
   * editing", gets the URL repaired. No loop: adopting makes the store equal to the
   * URL, so the repair that follows is a no-op.
   */
  private onNavigationEnd(): void {
    this.refreshScreen();
    if (!this.isFirstUser || !this.loaded || this.loadFailed) {
      return;
    }
    const wanted = this.selectionFromUrl();
    if (wanted !== null && wanted !== this.selection) {
      this.askScreen((discard) => (discard ? this.apply(wanted) : this.syncUrl()));
    } else if (this.allProperties && !this.screenAllowsAll) {
      // This screen shows one property: the screen that had "All" is already left.
      this.apply(this.properties.length ? this.properties[0].id : null);
    } else {
      this.syncUrl();
    }
  }

  /**
   * What the URL of a finished navigation selects: a known ?propertyId=, or on a screen with "All" no parameter
   * (unless a selection made during the navigation is still to be written). Null: keep the selection.
   */
  private selectionFromUrl(): ChemicalsPickerValue | null {
    const urlBehind = this.urlBehind;
    this.urlBehind = false;
    const fromUrl = this.propertyIdFromUrl();
    if (this.hasProperty(fromUrl)) {
      return fromUrl;
    }
    return this.screenAllowsAll && fromUrl === null && !urlBehind && this.properties.length ? CHEMICALS_ALL_PROPERTIES : null;
  }

  private hasProperty(id: number | null): boolean {
    return id !== null && this.properties.some((p) => p.id === id);
  }

  private propertyIdFromUrl(): number | null {
    const raw = this.route.snapshot.queryParamMap.get('propertyId');
    const id = raw ? Number(raw) : NaN;
    return Number.isInteger(id) ? id : null;
  }

  /**
   * Keeps ?propertyId= equal to the selected property (menu clicks drop it), and drops it under
   * "All". Never while a navigation is in flight (R-2): its NavigationEnd calls back here.
   */
  private syncUrl(): void {
    if (this.navigating) {
      this.urlBehind = true;
      return;
    }
    this.urlBehind = false;
    if (!this.isFirstUser || !this.loaded) {
      return;
    }
    // Nothing selected (no properties): no parameter to write.
    if (!this.allProperties && this.propertyId === null) {
      return;
    }
    // "All" is the URL without the parameter; a property is its id.
    const wanted = this.allProperties ? null : this.propertyId;
    if (this.propertyIdFromUrl() === wanted) {
      return;
    }
    void this.router.navigate([], {
      queryParams: {propertyId: wanted},
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** The active screen's title and whether it offers "All". */
  private refreshScreen(): void {
    const data = this.route.firstChild?.snapshot.data;
    this.titleKey = data?.['titleKey'] ?? DEFAULT_TITLE_KEY;
    this.screenAllowsAll = data?.['allProperties'] === true;
    this.refreshPickerItems();
  }

  /** A new array only when something changed: the select re-reads its items on every new array. */
  private refreshPickerItems(): void {
    const items: ChemicalsPickerItem[] = [
      ...(this.screenAllowsAll ? [{id: CHEMICALS_ALL_PROPERTIES, name: this.allLabel} as ChemicalsPickerItem] : []),
      ...this.properties.map((p) => ({id: p.id, name: p.name})),
    ];
    const same = items.length === this.pickerItems.length
      && items.every((item, i) => item.id === this.pickerItems[i].id && item.name === this.pickerItems[i].name);
    if (!same) {
      this.pickerItems = items;
    }
  }
}
