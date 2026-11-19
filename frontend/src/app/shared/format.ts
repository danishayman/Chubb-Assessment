import { ClaimStatus } from '../core/models';

export const STATUS_LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  AwaitingInformation: 'Awaiting information',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Settled: 'Settled',
};

export const ALL_STATUSES = Object.keys(STATUS_LABELS) as ClaimStatus[];
export const MARKETS = ['SG', 'MY', 'HK', 'TH', 'ID', 'AU'] as const;

export function money(amount: number | null | undefined, currency: string): string {
  if (amount === null || amount === undefined) return '—';
  return `${currency} ${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}
