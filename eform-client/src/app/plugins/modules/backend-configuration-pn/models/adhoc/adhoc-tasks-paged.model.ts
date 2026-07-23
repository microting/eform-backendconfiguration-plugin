import {Paged} from 'src/app/common/models';
import {AdhocTaskModel} from './adhoc-task.model';

/**
 * `POST adhoc/index`'s response - mirrors C# `AdhocTaskIndexResultModel`
 * (`Paged<AdhocTaskModel>` plus the three status counts the mockup's
 * status-select tabs render, e.g. "Åbne (12)"). The counts reflect every
 * filter on the request EXCEPT `status` itself, so switching status tabs
 * doesn't change the other tabs' counts.
 */
export interface AdhocTaskIndexResultModel extends Paged<AdhocTaskModel> {
  openCount: number;
  completedCount: number;
  archivedCount: number;
}
