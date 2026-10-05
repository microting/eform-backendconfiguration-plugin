/** Mirrors Microting.EformBackendConfigurationBase InboxDocumentStatus. */
export enum InboxDocumentStatus {
  Preparing = 0,
  SenderPending = 1,
  Ready = 2,
  Failed = 3,
  Filed = 4,
  Rejected = 5,
}

/** Mirrors Microting.EformBackendConfigurationBase InboxSuggestionKind. */
export enum InboxSuggestionKind {
  Property = 0,
  Tag = 1,
}

/** Mirrors Microting.EformBackendConfigurationBase InboxSenderRuleKind. */
export enum InboxSenderRuleKind {
  Allow = 0,
  Block = 1,
}

export type InboxUnknownSenderPolicy = 'hold' | 'refuse';

export interface InboxSuggestionModel {
  id: number;
  kind: InboxSuggestionKind;
  targetId: number;
  targetName: string;
  source: number;
  confidence: number;
  evidence: string | null;
  page: number | null;
  reason: string | null;
}

export interface InboxListItemModel {
  id: number;
  fileName: string;
  subject: string | null;
  fromAddress: string;
  /** UTC, ISO 8601 with a `Z` (the service adds it; the API sends no offset). */
  receivedAt: string;
  /** UTC, ISO 8601 with a `Z`, or null. */
  readyBy: string | null;
  status: InboxDocumentStatus;
  failureReason: string | null;
  reviewedByMicroting: boolean;
  suggestions: InboxSuggestionModel[];
}

export interface FileInboxDocumentModel {
  name: string;
  propertyIds: number[];
  tagIds: number[];
}

export interface InboxSenderRuleModel {
  id?: number | null;
  pattern: string;
  kind: InboxSenderRuleKind;
}

export interface InboxSettingsModel {
  address: string | null;
  unknownSenderPolicy: InboxUnknownSenderPolicy;
  senderRules: InboxSenderRuleModel[];
}

/** PUT settings body: a full replace of the policy and the rules. */
export interface InboxSettingsUpdateModel {
  unknownSenderPolicy: InboxUnknownSenderPolicy;
  senderRules: { pattern: string; kind: InboxSenderRuleKind }[];
}
