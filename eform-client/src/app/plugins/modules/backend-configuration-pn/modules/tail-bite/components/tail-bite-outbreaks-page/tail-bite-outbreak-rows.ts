import {TailBiteLocationNode, TailBiteOutbreakSummary} from '../../../../models';
import {parseServerUtc} from '../../shared/tail-bite-dates';
import {TailBiteOutbreakStatus, outbreakStatus} from '../../shared/tail-bite-outbreak-status';
import {locationTitle, nodeMap} from '../../shared/tail-bite-tree';

export interface TailBiteOutbreakListRow {
  id: number;
  title: string;
  openedAt: Date | null;
  status: TailBiteOutbreakStatus;
  openActions: number;
}

/** Rows for the outbreaks table. An outbreak summed at a location that has since been deleted is still named from the tree. */
export function outbreakRows(outbreaks: TailBiteOutbreakSummary[], nodes: TailBiteLocationNode[]): TailBiteOutbreakListRow[] {
  const byId = nodeMap(nodes);
  return outbreaks.map((o) => ({
    id: o.id,
    title: byId.has(o.locationId) ? locationTitle(o.locationId, byId) : `#${o.locationId}`,
    openedAt: parseServerUtc(o.openedAt),
    status: outbreakStatus(o),
    openActions: o.openActions,
  }));
}
