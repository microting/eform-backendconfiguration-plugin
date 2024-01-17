import {AfterContentInit, Component, OnInit} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {translates} from './../i18n/translates';
import {selectCurrentUserLocale} from 'src/app/state';
import {Store} from '@ngrx/store';

@Component({
  selector: 'app-backend-configuration-pn-layout',
  template: `
    <router-outlet></router-outlet>`,
})
export class BackendConfigurationPnLayoutComponent
  implements AfterContentInit, OnInit {
  private selectCurrentUserLocale$ = this.store.select(selectCurrentUserLocale);

  constructor(
    private translateService: TranslateService,
    private store: Store
  ) {
  }

  ngOnInit() {
  }

  ngAfterContentInit() {
    this.selectCurrentUserLocale$.subscribe((locale) => {
      const i18n = translates[locale];
      this.translateService.setTranslation(locale, i18n, true);
    });
  }
}
