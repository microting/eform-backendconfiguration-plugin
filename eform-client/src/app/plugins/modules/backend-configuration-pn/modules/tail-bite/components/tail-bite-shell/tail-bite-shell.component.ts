import {NgFor, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatTabsModule} from '@angular/material/tabs';
import {NavigationEnd, Router, RouterModule} from '@angular/router';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {Subscription, filter} from 'rxjs';
import {TailBitePropertyStatus} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TAIL_BITE_BASE, parseTailBiteUrl} from '../../shared/tail-bite-route';

/**
 * The tail-bite area: property picker, the four tabs and the page below them. The property lives in the URL
 * (`tail-bite/<propertyId>/<tab>`); the picker navigates, the pages read the route. The bare `tail-bite` URL
 * opens the first property with tail biting enabled.
 */
@Component({
  selector: 'app-tail-bite-shell',
  templateUrl: './tail-bite-shell.component.html',
  imports: [NgFor, NgIf, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatTabsModule, MtxSelectModule, TranslateModule],
})
export class TailBiteShellComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private router = inject(Router);

  readonly base = TAIL_BITE_BASE;
  readonly links = [
    {path: 'outbreaks', id: 'tailBiteTabOutbreaks', label: 'Outbreaks'},
    {path: 'locations', id: 'tailBiteTabLocations', label: 'Locations and QR'},
    {path: 'rules', id: 'tailBiteTabRules', label: 'Outbreak rules'},
    {path: 'action-types', id: 'tailBiteTabActionTypes', label: 'Action types'},
  ];
  /** null until loaded; then the properties with tail biting enabled, possibly none. */
  properties: TailBitePropertyStatus[] | null = null;
  propertyId: number | null = null;
  private sub?: Subscription;

  ngOnInit(): void {
    this.sub = this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.syncFromUrl());
    this.syncFromUrl();
    this.service.getProperties().subscribe((res) => {
      this.properties = res?.success ? res.model.filter((p) => p.enabled) : [];
      this.syncFromUrl();
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  select(propertyId: number | null): void {
    if (propertyId === null || propertyId === this.propertyId) {
      return;
    }
    // A detail page (outbreaks/:id) belongs to its property: switching goes to the new property's list.
    this.router.navigate([this.base, propertyId, parseTailBiteUrl(this.router.url).tab ?? 'outbreaks']);
  }

  private syncFromUrl(): void {
    const fromUrl = parseTailBiteUrl(this.router.url);
    this.propertyId = fromUrl.propertyId;
    if (!this.properties) {
      return;
    }
    if (this.properties.length === 0) {
      // Nothing to show: no tabs and no page, only the empty state.
      this.propertyId = null;
    } else if (this.propertyId === null || !this.properties.some((p) => p.propertyId === this.propertyId)) {
      // Bare URL or a property that is not offered (disabled, unknown): open the first one on the same tab.
      this.router.navigate([this.base, this.properties[0].propertyId, fromUrl.tab ?? 'outbreaks'], {replaceUrl: true});
    }
  }
}
