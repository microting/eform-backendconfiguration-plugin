import {Overlay} from '@angular/cdk/overlay';
import {DatePipe, NgFor, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatDialog} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {ActivatedRoute} from '@angular/router';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {Subject, Subscription, catchError, debounceTime, finalize, forkJoin, map, of, switchMap, tap} from 'rxjs';
import {OperationDataResult} from 'src/app/common/models';
import {TailBiteLocationNode, TailBiteLocationTree, TailBiteRule, TailBiteRulePreview} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {openConfirm} from '../../shared/tail-bite-confirm';
import {parseServerUtc} from '../../shared/tail-bite-dates';
import {propertyIdParam} from '../../shared/tail-bite-route';
import {TailBiteRuleDraft, describeRule, draftProblem, ruleInputFromDraft} from '../../shared/tail-bite-rule-text';
import {TailBiteLocationOption, levelOptions, liveNodes, locationTitle, nodeMap, orderedNodes, pathOptions} from '../../shared/tail-bite-tree';
import {memoize} from '../../shared/tail-bite-memo';

export interface TailBiteRuleListItem {
  rule: TailBiteRule;
  location: string;
  isRoot: boolean;
  text: string;
  changedAt: Date | null;
}

/** One line of the version history, with its date and sentence worked out once when the history arrives. */
export interface TailBiteRuleHistoryRow {
  version: number;
  changedAt: Date | null;
  text: string;
}

/** A dry-run request tagged with the page state it was made for, so a late answer can be recognised as stale. */
interface PreviewRequest {
  generation: number;
  draft: TailBiteRuleDraft;
}

/**
 * "Udbrudsregler": the rules of the property, an editor with the 90-day dry run, and each rule's version
 * history. A saved change is a new version; outbreaks already open keep the version that opened them.
 * The root's rule cannot be deleted here, so a property always keeps a rule.
 */
@Component({
  selector: 'app-tail-bite-rules-page',
  templateUrl: './tail-bite-rules-page.component.html',
  imports: [DatePipe, NgFor, NgIf, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MtxSelectModule, TranslateModule],
})
export class TailBiteRulesPageComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private route = inject(ActivatedRoute);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);

  nodes: TailBiteLocationNode[] = [];
  items: TailBiteRuleListItem[] = [];
  draft: TailBiteRuleDraft | null = null;
  preview: TailBiteRulePreview | null = null;
  previewError: string | null = null;
  history: TailBiteRuleHistoryRow[] = [];
  previewLines: {location: string; count: number}[] = [];
  busy = false;
  private propertyId: number | null = null;
  /** Bumped whenever the draft is replaced or the property changes; a dry run answering for an older value is dropped. */
  private previewGeneration = 0;
  private preview$ = new Subject<PreviewRequest>();
  private subs = new Subscription();
  private request?: Subscription;
  private requestSeq = 0;

  ngOnInit(): void {
    this.subs.add(
      propertyIdParam(this.route).subscribe((id) => {
        this.propertyId = id;
        this.clearPage();
        this.load();
      }),
    );
    // One dry run after the user pauses typing; a newer request cancels an older one that is still in flight.
    this.subs.add(
      this.preview$
        .pipe(
          debounceTime(400),
          switchMap(({generation, draft}) => {
            const input = ruleInputFromDraft(draft);
            const call = input ? this.service.previewRule(input) : of(null);
            return call.pipe(
              catchError(() => of({success: false, message: this.translate.instant('Could not calculate the preview')})),
              map((res) => ({generation, res})),
            );
          }),
        )
        .subscribe(({generation, res}) => {
          if (generation === this.previewGeneration) {
            this.preview = res?.success ? (res as OperationDataResult<TailBiteRulePreview>).model : null;
            this.previewError = res && !res.success ? res.message || this.translate.instant('Could not calculate the preview') : null;
            this.previewLines = this.linesOf(this.preview);
          }
        }),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    this.request?.unsubscribe();
  }

  /** Forgets everything shown for the previous property so nothing stale is visible while the next one loads. */
  private clearPage(): void {
    this.nodes = [];
    this.items = [];
    this.closeEditor();
  }

  private closeEditor(): void {
    this.previewGeneration++;
    this.draft = null;
    this.history = [];
    this.preview = null;
    this.previewError = null;
    this.previewLines = [];
  }

  /**
   * Refreshes the list. The editor is left alone, so a refused or failed save never discards the user's edits;
   * only `selectRuleId` (the saved or created rule) reloads it from the server, and it closes when the edited rule is gone.
   */
  load(selectRuleId?: number): void {
    const propertyId = this.propertyId;
    if (propertyId === null) {
      return;
    }
    const seq = ++this.requestSeq;
    this.request?.unsubscribe();
    this.request = forkJoin({tree: this.service.getTree(propertyId), rules: this.service.getRules(propertyId)}).subscribe({
      next: ({tree, rules}) => {
        // A refused refresh keeps what is shown (and the editor); only a first load, which starts empty, stays empty.
        if (seq === this.requestSeq && tree?.success && rules?.success) {
          this.refresh(tree.model, rules.model);
          this.reselect(selectRuleId);
        }
      },
      error: () => undefined,
    });
  }

  private refresh(tree: TailBiteLocationTree | null, rules: TailBiteRule[]): void {
    this.nodes = orderedNodes(liveNodes(tree));
    const byId = nodeMap(this.nodes);
    const order = new Map(this.nodes.map((n, i) => [n.id, i]));
    this.items = rules
      .filter((r) => byId.has(r.locationId))
      .sort((a, b) => order.get(a.locationId)! - order.get(b.locationId)!)
      .map((rule) => ({
        rule,
        location: locationTitle(rule.locationId, byId),
        isRoot: byId.get(rule.locationId)!.parentId === null,
        text: describeRule(this.translate, rule),
        changedAt: parseServerUtc(rule.updatedAt),
      }));
    this.previewLines = this.linesOf(this.preview);
  }

  private reselect(selectRuleId?: number): void {
    const target = selectRuleId === undefined ? undefined : this.items.find((i) => i.rule.id === selectRuleId);
    if (target) {
      this.edit(target.rule);
    } else if (this.draft?.ruleId != null && !this.editedItem) {
      this.closeEditor();
    }
  }

  edit(rule: TailBiteRule): void {
    this.closeEditor();
    this.draft = {
      ruleId: rule.id, locationId: rule.locationId, minBittenPigs: rule.minBittenPigs, minSevere: rule.minSevere,
      windowDays: rule.windowDays, countDepth: rule.countDepth, version: rule.version,
    };
    const generation = this.previewGeneration;
    this.service.getRuleHistory(rule.id).subscribe({
      next: (res) => {
        if (generation === this.previewGeneration) {
          this.history = (res?.success ? res.model : []).map((v) => ({
            version: v.version, changedAt: parseServerUtc(v.changedAt), text: describeRule(this.translate, v),
          }));
        }
      },
      error: () => undefined,
    });
    this.requestPreview();
  }

  // Lists bound to mtx-selects keep their reference until their inputs change (see memoize).
  private readonly freeLocationsOf = memoize((items: TailBiteRuleListItem[], nodes: TailBiteLocationNode[]) => {
    const taken = new Set(items.map((i) => i.rule.locationId));
    return pathOptions(nodes.filter((n) => !taken.has(n.id)), nodes);
  });
  private readonly levelsOf = memoize((nodes: TailBiteLocationNode[], locationId: number | null) =>
    levelOptions(nodes, nodes.find((n) => n.id === locationId)?.depth ?? 0));

  newRule(): void {
    this.closeEditor();
    this.draft = {ruleId: null, locationId: null, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1, version: 0};
  }

  /** Locations that do not have a rule yet, labelled with full paths; a new rule is attached to one of them. */
  get freeLocations(): TailBiteLocationOption[] {
    return this.freeLocationsOf(this.items, this.nodes);
  }

  /** Summing levels at or below the rule's own location (a rule never sums above it). */
  get levels(): {depth: number; example: string}[] {
    return this.levelsOf(this.nodes, this.draft?.locationId ?? null);
  }

  chooseLocation(locationId: number | null): void {
    if (!this.draft) {
      return;
    }
    this.draft.locationId = locationId;
    const depth = this.nodes.find((n) => n.id === locationId)?.depth ?? 0;
    this.draft.countDepth = Math.max(this.draft.countDepth, depth);
    this.requestPreview();
  }

  /** Asks for a dry run of the draft; the request waits until the user pauses, so typing does not call the server. */
  requestPreview(): void {
    if (this.draft) {
      this.preview$.next({generation: this.previewGeneration, draft: {...this.draft}});
    }
  }

  get draftText(): string {
    const input = this.draft ? ruleInputFromDraft(this.draft) : null;
    return input ? describeRule(this.translate, input) : '';
  }

  get canSave(): boolean {
    return !!this.draft && ruleInputFromDraft(this.draft) !== null && !this.busy;
  }

  get saveLabel(): string {
    if (!this.draft || this.draft.ruleId === null) {
      return this.translate.instant('Create');
    }
    return this.translate.instant('Save as version {{version}}', {version: this.draft.version + 1});
  }

  /** The rule being edited, once it exists on the server. */
  get editedItem(): TailBiteRuleListItem | null {
    return this.items.find((i) => i.rule.id === this.draft?.ruleId) ?? null;
  }

  /** Why Save is disabled, or an empty string when the draft is fine. */
  get saveHint(): string {
    const problem = this.draft ? draftProblem(this.draft) : null;
    switch (problem) {
      case 'location':
        return this.translate.instant('Choose a location.');
      case 'threshold-missing':
        return this.translate.instant('Set at least one threshold.');
      case 'threshold-invalid':
        return this.translate.instant('A threshold must be a whole number of at least 1.');
      case 'window':
        return this.translate.instant('The number of days must be a whole number from 1 to 90.');
      default:
        return '';
    }
  }

  private linesOf(preview: TailBiteRulePreview | null): {location: string; count: number}[] {
    const byId = nodeMap(this.nodes);
    return Object.entries(preview?.perSummingLocation ?? {})
      .map(([id, count]) => ({location: byId.has(Number(id)) ? locationTitle(Number(id), byId) : `#${id}`, count}))
      .sort((a, b) => b.count - a.count);
  }

  /** Saves the draft as a new rule or the next version; reloads whatever the outcome, so a refusal never leaves stale data. */
  save(): void {
    const input = this.draft ? ruleInputFromDraft(this.draft) : null;
    if (!input || !this.draft || this.busy) {
      return;
    }
    this.busy = true;
    const ruleId = this.draft.ruleId;
    const call = ruleId === null ? this.service.createRule(input) : this.service.updateRule(ruleId, input);
    call.pipe(finalize(() => (this.busy = false))).subscribe({
      next: (res) => {
        if (res?.success) {
          this.load(ruleId ?? (res as OperationDataResult<number>).model);
        } else {
          this.load();
        }
      },
      error: () => this.load(),
    });
  }

  remove(item: TailBiteRuleListItem): void {
    if (this.busy || item.isRoot) {
      return;
    }
    openConfirm(this.dialog, this.overlay, {
      headerText: this.translate.instant('Delete outbreak rule'), itemLabel: this.translate.instant('Location'), itemName: item.location,
      confirmText: this.translate.instant('Delete'), confirmId: 'tailBiteRuleDeleteConfirm',
    }, () => {
      this.busy = true;
      return this.service.deleteRule(item.rule.id).pipe(
        tap({next: (res) => (res?.success ? this.closeEditor() : this.load()), error: () => this.load()}),
        finalize(() => (this.busy = false)),
      );
    // A refusal or an error refreshes the list (tap) and leaves the dialog and the editor as they are.
    }).subscribe({next: () => this.load(), error: () => undefined});
  }
}
