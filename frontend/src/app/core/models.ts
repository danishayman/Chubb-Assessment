// Mirrors the DTOs in backend/src/Claims.Api/Features. The backend owns the contract (see /openapi/v1.json).

export type Role = 'Claimant' | 'ClaimsOfficer' | 'Manager';
export type ClaimType = 'Motor' | 'Property';
export type Market = 'SG' | 'MY' | 'HK' | 'TH' | 'ID' | 'AU';
export type ClaimStatus = 'Submitted' | 'UnderReview' | 'AwaitingInformation' | 'Approved' | 'Rejected' | 'Settled';

export interface User { id: string; name: string; email: string; role: Role; }
export interface LoginResponse { token: string; expiresAt: string; user: User; }

export interface Policy {
  id: string; policyNumber: string; type: ClaimType; market: Market; currency: string;
  coverageLimit: number; excess: number; startDate: string; endDate: string;
}

export interface ClaimSummary {
  id: string; claimNumber: string; type: ClaimType; market: Market; currency: string; status: ClaimStatus;
  claimedAmount: number; reserveAmount: number | null; approvedAmount: number | null;
  incidentDate: string; submittedAt: string; updatedAt: string; ageDays: number;
  assignedOfficerId: string | null; assignedOfficerName: string | null; claimantName: string; openInfoRequests: number;
}

export interface InfoRequest {
  id: string; question: string; requestedAt: string; response: string | null; respondedAt: string | null;
  status: 'Open' | 'Answered';
}
export interface Note { id: string; authorName: string; text: string; createdAt: string; }
export interface Activity { at: string; actor: string; action: string; detail: string; }

export interface ClaimDetail {
  summary: ClaimSummary;
  policyNumber: string; location: string; description: string;
  settledAmount: number | null; decisionReason: string | null; closedAt: string | null;
  coverageLimit: number; excess: number; maxPayable: number | null;
  allowedNextStatuses: ClaimStatus[];
  informationRequests: InfoRequest[];
  notes: Note[] | null;
  timeline: Activity[];
}

export interface SubmitClaim {
  policyId: string; incidentDate: string; location: string; description: string; claimedAmount: number;
}

export interface Staff { id: string; name: string; role: Role; }

export interface AppNotification { id: string; claimId: string; message: string; createdAt: string; read: boolean; }
export interface NotificationList { unreadCount: number; items: AppNotification[]; }

export interface Workload {
  openClaims: number; unassigned: number; oldestUnassignedDays: number;
  byStatus: { status: ClaimStatus; count: number }[];
  ageing: { label: string; count: number }[];
  officers: {
    officerId: string; name: string; open: number; awaitingInformation: number; oldestOpenDays: number;
    closedLast30Days: number; avgDaysToClose: number | null;
  }[];
}

export interface ExposureLine {
  market: Market; currency: string; type: ClaimType; openClaims: number; reserved: number; claimed: number;
}
export interface Exposure {
  lines: ExposureLine[];
  largestOpenByMarket: { id: string; claimNumber: string; market: Market; currency: string; status: ClaimStatus; reserveAmount: number }[];
}

export interface Problem { title?: string; detail?: string; status?: number; }
