import {Component, OnInit, inject} from '@angular/core';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnInboxService} from '../../../../services';
import {InboxSenderRuleKind, InboxSettingsModel, InboxUnknownSenderPolicy} from '../../../../models';

/** Splits a free-text list of addresses/domains on commas, semicolons and whitespace. */
function parsePatterns(text: string): string[] {
  return [...new Set(text.split(/[\s,;]+/).map(p => p.trim()).filter(p => p))];
}

/**
 * Indstillinger e-mail: the archive address (copy, rotate), allowed and blocked senders and the
 * unknown-sender policy. When the hub is down or not configured the API answers success=false (its
 * message is shown inline; the GET does not toast) and, when it can, still sends the rules, so the
 * sender settings stay editable without an address. Without a model the sender section is replaced
 * by the reason.
 */
@Component({
  selector: 'app-inbox-settings',
  templateUrl: './inbox-settings.component.html',
  standalone: false,
})
export class InboxSettingsComponent implements OnInit {
  private inboxService = inject(BackendConfigurationPnInboxService);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);

  loaded = false;
  address: string | null = null;
  /** The API's failure message (hub down, not configured), shown above the address. */
  addressMessage: string | null = null;
  unknownSenderPolicy: InboxUnknownSenderPolicy = 'hold';
  allowList = '';
  blockList = '';
  copied = false;
  busy = false;
  /**
   * False until the API has sent the current rules. PUT is a full replace, so saving before that would
   * wipe the existing rules.
   */
  rulesLoaded = false;

  ngOnInit(): void {
    this.inboxService.getSettings().subscribe({
      next: res => {
        this.loaded = true;
        this.addressMessage = res?.success ? null : res?.message ?? null;
        if (res?.model) {
          this.apply(res.model);
        }
      },
      error: () => (this.loaded = true),
    });
  }

  copy(): void {
    if (!this.address) {
      return;
    }
    navigator.clipboard.writeText(this.address).then(
      () => (this.copied = true),
      () => (this.copied = false)
    );
  }

  /**
   * Rotating stops the old address after the grace period, so ask first when there is one
   * (window.confirm, like the plugin's other non-delete confirmations).
   */
  rotate(): void {
    if (this.address && !window.confirm(this.translate.instant('Create a new address? Forwarding to the old address stops after 7 days.'))) {
      return;
    }
    this.busy = true;
    this.inboxService.rotateAddress().subscribe({
      next: res => {
        this.busy = false;
        if (res?.success && res.model) {
          this.addressMessage = null;
          this.copied = false;
          if (this.rulesLoaded) {
            this.address = res.model.address; // keep unsaved edits to the rules
          } else {
            this.apply(res.model);
          }
          this.toastr.success(this.translate.instant('New address created. The old address keeps working for 7 days.'));
        }
      },
      error: () => (this.busy = false),
    });
  }

  save(): void {
    if (!this.rulesLoaded) {
      return;
    }
    this.busy = true;
    const rules = [
      ...parsePatterns(this.allowList).map(pattern => ({pattern, kind: InboxSenderRuleKind.Allow})),
      ...parsePatterns(this.blockList).map(pattern => ({pattern, kind: InboxSenderRuleKind.Block})),
    ];
    this.inboxService.updateSettings({unknownSenderPolicy: this.unknownSenderPolicy, senderRules: rules}).subscribe({
      next: res => {
        this.busy = false;
        if (res?.success) {
          this.toastr.success(this.translate.instant('Saved'));
        }
      },
      error: () => (this.busy = false),
    });
  }

  private apply(model: InboxSettingsModel): void {
    this.address = model.address;
    this.unknownSenderPolicy = model.unknownSenderPolicy ?? 'hold';
    const patterns = (kind: InboxSenderRuleKind) =>
      (model.senderRules ?? []).filter(r => r.kind === kind).map(r => r.pattern).join(', ');
    this.allowList = patterns(InboxSenderRuleKind.Allow);
    this.blockList = patterns(InboxSenderRuleKind.Block);
    this.rulesLoaded = true;
  }
}
